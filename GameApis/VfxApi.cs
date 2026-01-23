using System;
using System.Numerics;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using ff16.gameplay.truly_eikonic_spells.GameApis.Actor;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// API for spawning VFX effects. Captures factory from magic system hooks.
/// </summary>
public static class VfxApi
{
    private static IActorApi? _actorApi;
    public static Action<string, System.Drawing.Color?>? Logger;
    
    // Cached factory from magic hooks
    private static long _cachedFactory = 0;
    private static nint _cachedVTable = 0;

    // --- Scanned Functions ---
    public static UnkVATBDelegate? UnkVatb;
    public static SetPositionDelegate? SetPosition;
    public static SetRotationDelegate? SetRotation;
    public static SetNodeDelegate? SetNode;
    public static ActivateDelegate? Activate;

    /// <summary>
    /// Updates the cached factory from a MagicFileInstance.
    /// Called by MagicProcessor when magic is executed.
    /// </summary>
    public static unsafe void UpdateFactory(long magicFileInstance)
    {
        if (magicFileInstance < 0x10000 || magicFileInstance > 0x00007FFFFFFFFFFF)
            return;
        
        try
        {
            // Validate VTable is in game code range
            long vtable = *(long*)magicFileInstance;
            if (vtable < 0x140000000 || vtable > 0x145000000)
                return;
            
            // Only log if this is a new factory
            if (_cachedFactory != magicFileInstance)
            {
                _cachedFactory = magicFileInstance;
                _cachedVTable = (nint)vtable;
                Logger?.Invoke($"[VFX] Captured factory 0x{magicFileInstance:X} (VTable: 0x{vtable:X})", System.Drawing.Color.Green);
            }
        }
        catch { }
    }
    
    /// <summary>
    /// Returns true if the VFX system has a valid factory.
    /// </summary>
    public static bool IsReady => _cachedFactory != 0 && UnkVatb != null && Activate != null;

    /// <summary>
    /// Stores a reference to ActorApi for actor-based spawning.
    /// </summary>
    public static void SetActorApi(IActorApi actorApi) => _actorApi = actorApi;

    // --- Delegates ---

    [Function(CallingConventions.Microsoft)]
    public delegate long UnkVATBDelegate(long magicFileInstance, long outPtr, int vatbId, int unk);

    [Function(CallingConventions.Microsoft)]
    public delegate void SetPositionDelegate(long vfxInstance, long positionPair);

    [Function(CallingConventions.Microsoft)]
    public delegate void SetRotationDelegate(long vfxInstance, long eulerPtr);

    [Function(CallingConventions.Microsoft)]
    public delegate void SetNodeDelegate(long vfxInstance, long nodePtr);

    [Function(CallingConventions.Microsoft)]
    public delegate void ActivateDelegate(long vfxInstance, int unk);

    // --- Signatures (Patterns) ---
    
    public const string UnkVATB_Signature = "48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 57 48 83 EC 40 48 8B 05 ?? ?? ?? ?? 48 8B F1 4C 8B 15 ?? ?? ?? ?? 48 8B FA 41 8A D9 41 8B E8"; 
    public const string SetPosition_Signature = "48 89 5C 24 08 57 48 83 EC 20 8A 81 F0 00 00 00 48 8B FA D0 E8 48 8B D9 A8 01 74 07";
    public const string SetRotation_Signature = "48 83 EC 28 4C 8B 81 F0 00 00 00 49 8B C0 48 C1 E8 15 A8 01 74 1A C5 FB 10 02 C5 FB 11 81 E4 00 00 00 8B 42 08";
    public const string SetNode_Signature = "48 89 5C 24 08 57 48 83 EC 20 48 8B F9 48 8B 89 F0 00 00 00 48 8B C1 48 C1 E8 15 A8 01 74 20 48 8B 42 08";
    public const string Activate_Signature = "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 3D ?? ?? ?? ?? 8B EA 48 8B D9 8A 97 08 02 00 00 80 E2 01";

    /// <summary>
    /// Sets up signature scans to resolve all VFX function pointers.
    /// Must be called during mod initialization to enable VFX spawning.
    /// </summary>
    public static void SetupScans(Reloaded.Memory.SigScan.ReloadedII.Interfaces.IStartupScanner scanner, Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks hooks)
    {
        scanner.AddScan(UnkVATB_Signature, address =>
        {
            UnkVatb = hooks.CreateWrapper<UnkVATBDelegate>(address, out _);
            Logger?.Invoke($"[VFX] Resolved UnkVatb at 0x{address:X}", System.Drawing.Color.Green);
        });

        scanner.AddScan(SetPosition_Signature, address =>
        {
            SetPosition = hooks.CreateWrapper<SetPositionDelegate>(address, out _);
            Logger?.Invoke($"[VFX] Resolved SetPosition at 0x{address:X}", System.Drawing.Color.Green);
        });

        scanner.AddScan(SetRotation_Signature, address =>
        {
            SetRotation = hooks.CreateWrapper<SetRotationDelegate>(address, out _);
            Logger?.Invoke($"[VFX] Resolved SetRotation at 0x{address:X}", System.Drawing.Color.Green);
        });

        scanner.AddScan(SetNode_Signature, address =>
        {
            SetNode = hooks.CreateWrapper<SetNodeDelegate>(address, out _);
            Logger?.Invoke($"[VFX] Resolved SetNode at 0x{address:X}", System.Drawing.Color.Green);
        });

        scanner.AddScan(Activate_Signature, address =>
        {
            Activate = hooks.CreateWrapper<ActivateDelegate>(address, out _);
            Logger?.Invoke($"[VFX] Resolved Activate at 0x{address:X}", System.Drawing.Color.Green);
        });
    }

    /// <summary>
    /// Spawns a VFX using a TargetStruct for position and optional node attachment.
    /// This is the primary interface for spawning VFX effects.
    /// </summary>
    /// <param name="vfxId">The VFX ID to spawn.</param>
    /// <param name="target">Target containing position, node, and optional direction.</param>
    public static unsafe void SpawnVFX(uint vfxId, TargetStruct target)
    {
        if (UnkVatb == null || Activate == null)
        {
            Logger?.Invoke("[VFX] Functions not resolved. Signature scan failed.", System.Drawing.Color.Red);
            return;
        }
        
        if (_cachedFactory == 0) 
        {
            Logger?.Invoke("[VFX] No factory captured yet. Cast any spell (Fire, etc.) first to capture context.", System.Drawing.Color.Orange);
            return;
        }

        unsafe
        {
            long outPtr = 0;
            long result = UnkVatb(_cachedFactory, (long)(&outPtr), (int)vfxId, 1);
            long vfxInstance = outPtr;
            Logger?.Invoke($"[VFX] UnkVatb returned 0x{result:X}, outPtr=0x{vfxInstance:X}", System.Drawing.Color.Cyan);

            if (vfxInstance > 0x10000 && vfxInstance < 0x00007FFFFFFFFFFF)
            {
                // Create NodePositionPair from TargetStruct
                GameStructs.NodePositionPair pair = new GameStructs.NodePositionPair
                {
                    ParentNode = (nint)target.Node,
                    Position = new Vector3(target.X, target.Y, target.Z)
                };

                if (SetPosition != null)
                {
                    Logger?.Invoke($"[VFX] Calling SetPosition", System.Drawing.Color.DarkGray);
                    SetPosition(vfxInstance, (long)(&pair));
                }
                
                // Apply node attachment if valid
                if (SetNode != null && target.Node > 0x10000 && target.Node < 0x00007FFFFFFFFFFF)
                {
                    Logger?.Invoke($"[VFX] Calling SetNode(0x{target.Node:X})", System.Drawing.Color.DarkGray);
                    SetNode(vfxInstance, target.Node);
                }
                
                // Apply rotation if direction is set
                if (SetRotation != null && (target.DirectionX != 0 || target.DirectionY != 0 || target.DirectionZ != 0))
                {
                    Vector3 rotation = new Vector3(target.DirectionX, target.DirectionY, target.DirectionZ);
                    Logger?.Invoke($"[VFX] Calling SetRotation({rotation})", System.Drawing.Color.DarkGray);
                    SetRotation(vfxInstance, (long)(&rotation));
                }

                Activate(vfxInstance, 1);
            }
        }
    }
    
    /// <summary>
    /// Spawns a VFX at a specific world coordinate (convenience overload).
    /// </summary>
    public static void SpawnVFX(uint vfxId, float x, float y, float z)
    {
        SpawnVFX(vfxId, TargetStruct.FromPosition(new Vector3(x, y, z)));
    }

    /// <summary>
    /// Spawns a VFX on a specific actor using their StaticActorInfo pointer.
    /// Pass 0 for player, or a valid StaticActorInfo pointer for any other actor.
    /// </summary>
    public static unsafe void SpawnVFX(uint vfxId, nint staticActorInfo)
    {
        if (_actorApi == null)
        {
            Logger?.Invoke("[VFX] _actorApi is null", System.Drawing.Color.Red);
            return;
        }
        
        // If 0, use player
        nint actor = staticActorInfo != 0 ? staticActorInfo : _actorApi.GetPlayerStaticActorInfo();
        if (actor == 0)
        {
            Logger?.Invoke("[VFX] Invalid actor pointer", System.Drawing.Color.Orange);
            return;
        }
        
        var target = _actorApi.CreateTargetFromActor(actor);
        if (target.HasValue)
        {
            SpawnVFX(vfxId, target.Value);
        }
        else
        {
            Logger?.Invoke("[VFX] Could not create target from actor", System.Drawing.Color.Orange);
        }
    }

}
