using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

public static class VfxApi
{
    private static FunctionApi? _functionApi;
    public static Action<string, System.Drawing.Color?>? Logger;

    // --- Scanned Functions ---
    public static UnkVATBDelegate? UnkVatb;
    public static SetPositionDelegate? SetPosition;
    public static SetRotationDelegate? SetRotation;
    public static SetNodeDelegate? SetNode;
    public static ActivateDelegate? Activate;

    // --- Context ---
    public static long LastMagicInstance;
    public static Vector3 LastPosition;
    public static long LastNode;
    public static bool HasValidPosition;
    
    // --- Factory Validity Tracking ---
    private static Stopwatch _factoryTimer = Stopwatch.StartNew();
    private const int FACTORY_TIMEOUT_MS = 2000; // Factory expires after 2 seconds of inactivity (game refreshes it during combat)

    /// <summary>
    /// Call this when capturing a new MagicFileInstance to reset the timer.
    /// </summary>
    public static void UpdateFactory(long magicFileInstance)
    {
        if (magicFileInstance > 0x10000)
        {
            LastMagicInstance = magicFileInstance;
            _factoryTimer.Restart();
        }
    }
    
    /// <summary>
    /// Check if the factory is still considered valid (recently used).
    /// </summary>
    public static bool IsFactoryValid()
    {
        return LastMagicInstance > 0x10000 && _factoryTimer.ElapsedMilliseconds < FACTORY_TIMEOUT_MS;
    }

    /// <summary>
    /// Stores a reference to FunctionApi for lazy initialization.
    /// </summary>
    public static void SetFunctionApi(FunctionApi functionApi) => _functionApi = functionApi;

    /// <summary>
    /// Initializes the VFX API using Clive's factory.
    /// The factory is captured automatically by MagicGameSystem hooks when Clive casts any spell.
    /// </summary>
    public static unsafe void Initialize()
    {
        // The factory is captured in MagicGameSystem.MagicUnkExecuteImpl
        // when any magic is cast. We just check if we have it.
        if (LastMagicInstance > 0x10000)
        {
            Logger?.Invoke($"[VFX] Factory already captured: 0x{LastMagicInstance:X}", System.Drawing.Color.Green);
            return;
        }
        
        Logger?.Invoke("[VFX] Factory not yet captured. Cast any spell to initialize.", System.Drawing.Color.Yellow);
    }

    // --- Delegates ---
    // ... (mantenemos los delegados igual)

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
    /// Spawns a VFX at a specific world coordinate.
    /// Creates a temporary NodePositionPair to satisfy the game's SetPosition requirements.
    /// </summary>
    public static unsafe void SpawnVFX(uint vfxId, float x, float y, float z)
    {
        Initialize(); // Ensure pointers are ready (on-demand)
        Logger?.Invoke($"[VFX] SpawnVFX-Coord({vfxId}, {x}, {y}, {z}) called. Factory: 0x{LastMagicInstance:X}", System.Drawing.Color.Gray);
        
        if (UnkVatb == null || Activate == null || LastMagicInstance == 0) 
        {
            Logger?.Invoke("[VFX] Aborting Coord Spawn: Missing factory or delegates", System.Drawing.Color.Orange);
            return;
        }

        unsafe
        {
            long outPtr = 0;
            long result = UnkVatb(LastMagicInstance, (long)(&outPtr), (int)vfxId, 1);
            long vfxInstance = outPtr; // The actual instance is in outPtr
            Logger?.Invoke($"[VFX-Coord] UnkVatb returned 0x{result:X}, outPtr=0x{vfxInstance:X}", System.Drawing.Color.Cyan);

            if (vfxInstance > 0x10000 && vfxInstance < 0x00007FFFFFFFFFFF)
            {
                // Create NodePositionPair for placement
                FunctionApi.NodePositionPair pair = new FunctionApi.NodePositionPair
                {
                    ParentNode = 0, // World space
                    Position = new Vector3(x, y, z)
                };

                if (SetPosition != null)
                {
                    Logger?.Invoke($"[VFX-Coord] Calling SetPosition", System.Drawing.Color.DarkGray);
                    SetPosition(vfxInstance, (long)(&pair));
                }

                Logger?.Invoke($"[VFX-Coord] Calling Activate", System.Drawing.Color.DarkGray);
                Activate(vfxInstance, 1);
            }
        }
    }

    /// <summary>
    /// Spawns a VFX attached to the player or a specific actor ID.
    /// </summary>
    public static unsafe void SpawnVFXOnPlayer(uint vfxId)
    {
        if (_functionApi == null)
        {
            Logger?.Invoke("[VFX-Player] _functionApi is null", System.Drawing.Color.Red);
            return;
        }
        
        StaticActorInfo* cliveInfo = (StaticActorInfo*)_functionApi.GetPlayerStaticActorInfo();
        if (cliveInfo == null) 
        {
            Logger?.Invoke("[VFX-Player] Clive not found", System.Drawing.Color.Orange);
            return;
        }

        Logger?.Invoke($"[VFX-Player] Spawning {vfxId} on Clive", System.Drawing.Color.Gray);
        FunctionApi.NodePositionPair pair = default;
        _functionApi.GetPosition((nint)cliveInfo, &pair);
        SpawnVFX(vfxId, pair.Position.X, pair.Position.Y, pair.Position.Z);
    }
    
    /// <summary>
    /// The easiest way to spawn a VFX using only the ID.
    /// It uses the last known Magic Instance and Position.
    /// </summary>
    public static unsafe void SpawnVFX(int vfxId)
    {
        Logger?.Invoke($"[VFX] SpawnVFX({vfxId}) called. LastMagicInstance: 0x{LastMagicInstance:X}", System.Drawing.Color.Gray);
        
        // Try to initialize every hit if not already found
        if (LastMagicInstance < 0x10000) 
        {
            Logger?.Invoke("[VFX] Factory not ready, initializing...", System.Drawing.Color.Gray);
            Initialize();
        }
        
        // Use time-based validation - factory is only valid for a short time after magic is cast
        // This prevents crashes when attacking with melee after magic ends
        if (!IsFactoryValid() || UnkVatb == null || Activate == null)
        {
            Logger?.Invoke($"[VFX] Factory expired or not ready. Valid={IsFactoryValid()}, Age={_factoryTimer.ElapsedMilliseconds}ms", System.Drawing.Color.Orange);
            return;
        }

        try
        {
            long outPtr = 0;
            Logger?.Invoke($"[VFX] Calling UnkVatb(0x{LastMagicInstance:X}, &outPtr, {vfxId}, 1)", System.Drawing.Color.DarkGray);
            
            // UnkVatb writes the VFX instance to outPtr, return value is status/pointer to outPtr
            long result = UnkVatb(LastMagicInstance, (long)(&outPtr), vfxId, 1);
            
            // The actual VFX instance is in outPtr, not the return value
            long vfxInstance = outPtr;
            Logger?.Invoke($"[VFX] UnkVatb returned 0x{result:X}, outPtr=0x{vfxInstance:X}", System.Drawing.Color.Cyan);

            if (vfxInstance > 0x10000 && vfxInstance < 0x00007FFFFFFFFFFF)
            {
                if (HasValidPosition)
                {
                    Logger?.Invoke($"[VFX] Setting Position: {LastPosition} | Node: 0x{LastNode:X}", System.Drawing.Color.Cyan);
                    FunctionApi.NodePositionPair pair = new FunctionApi.NodePositionPair
                    {
                        ParentNode = (nint)LastNode,
                        Position = LastPosition
                    };
                    
                    if (SetPosition != null)
                    {
                        Logger?.Invoke($"[VFX] Calling SetPosition(0x{vfxInstance:X}, 0x{(long)&pair:X})", System.Drawing.Color.DarkGray);
                        SetPosition(vfxInstance, (long)(&pair));
                    }
                        
                    // Only apply node if it's a valid pointer range
                    if (SetNode != null && LastNode > 0x10000 && LastNode < 0x00007FFFFFFFFFFF)
                    {
                        Logger?.Invoke($"[VFX] Calling SetNode(0x{vfxInstance:X}, 0x{LastNode:X})", System.Drawing.Color.DarkGray);
                        SetNode(vfxInstance, LastNode);
                    }
                    else
                    {
                        Logger?.Invoke($"[VFX] Skipping SetNode (LastNode=0x{LastNode:X})", System.Drawing.Color.Gray);
                    }
                }

                Logger?.Invoke($"[VFX] Calling Activate(0x{vfxInstance:X}, 1)", System.Drawing.Color.DarkGray);
                Activate(vfxInstance, 1);
                Logger?.Invoke($"[VFX] Successfully spawned VFX {vfxId}", System.Drawing.Color.Lime);
            }
            else
            {
                Logger?.Invoke($"[VFX] UnkVatb returned invalid instance: 0x{vfxInstance:X}", System.Drawing.Color.Red);
            }
        }
        catch (Exception ex)
        { 
            Logger?.Invoke($"[VFX] Engine CRASH caught in SpawnVFX: {ex.Message}", System.Drawing.Color.Red);
        }
    }

    /// <summary>
    /// Full version of SpawnVFX for manual control with safe pointer access.
    /// </summary>
    public static unsafe void SpawnVFX(long magicFileInstance, int vfxId, long positionPtr)
    {
        if (UnkVatb == null || Activate == null || magicFileInstance == 0) return;

        long outPtr = 0;
        long result = UnkVatb(magicFileInstance, (long)(&outPtr), vfxId, 1);
        long vfxInstance = outPtr; // The actual instance is in outPtr
        
        if (vfxInstance > 0x10000 && vfxInstance < 0x00007FFFFFFFFFFF)
        {
            if (SetPosition != null && positionPtr != 0)
            {
                SetPosition(vfxInstance, positionPtr);
            }

            Activate(vfxInstance, 1);
        }
    }
}
