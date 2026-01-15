using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using ff16.gameplay.truly_eikonic_spells.GameStructs;

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
    private const int FACTORY_TIMEOUT_MS = 500; // Reduced timeout - factory is only valid during active magic context
    
    // Known valid VTable addresses for MagicFileInstance (cached during successful spawns)
    private static long _knownValidVTable = 0;

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
    /// Check if the factory is still considered valid.
    /// Performs structural validation to prevent crashes on stale pointers.
    /// </summary>
    public static unsafe bool IsFactoryValid()
    {
        if (LastMagicInstance < 0x10000 || _factoryTimer.ElapsedMilliseconds > FACTORY_TIMEOUT_MS)
            return false;
            
        try
        {
            // Validate the VTable pointer - MagicFileInstance has VTable at offset 0
            long vtable = *(long*)LastMagicInstance;
            
            // Basic pointer validation
            if (vtable < 0x140000000 || vtable > 0x145000000) // Game code section range
                return false;
                
            // If we have a known valid VTable, compare against it
            if (_knownValidVTable != 0 && vtable != _knownValidVTable)
            {
                // VTable changed - factory was recycled
                Logger?.Invoke($"[VFX] Factory VTable changed (0x{vtable:X} != 0x{_knownValidVTable:X}), invalidating", System.Drawing.Color.Orange);
                return false;
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Called after a successful VFX spawn to cache the valid VTable.
    /// </summary>
    private static unsafe void CacheValidVTable()
    {
        if (LastMagicInstance > 0x10000)
        {
            try
            {
                _knownValidVTable = *(long*)LastMagicInstance;
            }
            catch { }
        }
    }
    
    /// <summary>
    /// Attempts to get a valid MagicFileInstance from Clive's BattleBehavior.
    /// This is more stable than the factory captured during magic casting.
    /// </summary>
    public static unsafe long GetCliveFactory()
    {
        if (_functionApi == null) return 0;
        
        try
        {
            nint clivePtr = _functionApi.GetPlayerStaticActorInfo();
            if (clivePtr == 0) return 0;
            
            var clive = (GameStructs.StaticActorInfo*)clivePtr;
            if (clive->BattleBehavior < 0x10000) return 0;
            
            var behavior = (GameStructs.BattleBehavior*)clive->BattleBehavior;
            long factory = behavior->MagicFileInstance;
            
            // Validate the factory pointer
            if (factory < 0x10000 || factory > 0x00007FFFFFFFFFFF) return 0;
            
            // Validate VTable is in game code range
            long vtable = *(long*)factory;
            if (vtable < 0x140000000 || vtable > 0x145000000) return 0;
            
            return factory;
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// Gets the best available factory - prefers Clive's stable factory over cached one.
    /// </summary>
    public static unsafe long GetBestFactory()
    {
        // First try Clive's BattleBehavior factory (most stable)
        long cliveFactory = GetCliveFactory();
        if (cliveFactory != 0)
        {
            // Update our cached instance if we got a fresh one
            if (cliveFactory != LastMagicInstance)
            {
                Logger?.Invoke($"[VFX] Using Clive's factory: 0x{cliveFactory:X}", System.Drawing.Color.Cyan);
                LastMagicInstance = cliveFactory;
                _factoryTimer.Restart();
            }
            return cliveFactory;
        }
        
        // Fall back to time-validated cached factory
        if (IsFactoryValid())
        {
            return LastMagicInstance;
        }
        
        return 0;
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
        // Try to get Clive's factory first
        long cliveFactory = GetCliveFactory();
        if (cliveFactory != 0)
        {
            LastMagicInstance = cliveFactory;
            _factoryTimer.Restart();
            Logger?.Invoke($"[VFX] Initialized with Clive's factory: 0x{cliveFactory:X}", System.Drawing.Color.Green);
            return;
        }
        
        // Fall back to previously captured factory
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
    /// Spawns a VFX at a specific world coordinate.
    /// Creates a temporary NodePositionPair to satisfy the game's SetPosition requirements.
    /// </summary>
    public static unsafe void SpawnVFX(uint vfxId, float x, float y, float z)
    {
        Logger?.Invoke($"[VFX] SpawnVFX-Coord({vfxId}, {x}, {y}, {z}) called.", System.Drawing.Color.Gray);
        
        long factory = GetBestFactory();
        if (factory == 0 || UnkVatb == null || Activate == null) 
        {
            Logger?.Invoke("[VFX] Aborting Coord Spawn: No valid factory available", System.Drawing.Color.Orange);
            return;
        }

        unsafe
        {
            long outPtr = 0;
            long result = UnkVatb(factory, (long)(&outPtr), (int)vfxId, 1);
            long vfxInstance = outPtr; // The actual instance is in outPtr
            Logger?.Invoke($"[VFX-Coord] UnkVatb returned 0x{result:X}, outPtr=0x{vfxInstance:X}", System.Drawing.Color.Cyan);

            if (vfxInstance > 0x10000 && vfxInstance < 0x00007FFFFFFFFFFF)
            {
                // Create NodePositionPair for placement
                GameStructs.NodePositionPair pair = new GameStructs.NodePositionPair
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
                
                // Cache the VTable for future validation
                CacheValidVTable();
                Logger?.Invoke($"[VFX] Successfully spawned VFX {vfxId}", System.Drawing.Color.Green);
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
        
        GameStructs.StaticActorInfo* cliveInfo = (GameStructs.StaticActorInfo*)_functionApi.GetPlayerStaticActorInfo();
        if (cliveInfo == null) 
        {
            Logger?.Invoke("[VFX-Player] Clive not found", System.Drawing.Color.Orange);
            return;
        }

        Logger?.Invoke($"[VFX-Player] Spawning {vfxId} on Clive", System.Drawing.Color.Gray);
        GameStructs.NodePositionPair pair = default;
        _functionApi.GetPosition((nint)cliveInfo, &pair);
        SpawnVFX(vfxId, pair.Position.X, pair.Position.Y, pair.Position.Z);
    }
    
    /// <summary>
    /// The easiest way to spawn a VFX using only the ID.
    /// It uses Clive's factory (stable) or falls back to cached magic factory.
    /// </summary>
    public static unsafe void SpawnVFX(int vfxId)
    {
        Logger?.Invoke($"[VFX] SpawnVFX({vfxId}) called. LastMagicInstance: 0x{LastMagicInstance:X}", System.Drawing.Color.Gray);
        
        // Get the best available factory (Clive's or cached)
        long factory = GetBestFactory();
        if (factory == 0 || UnkVatb == null || Activate == null)
        {
            Logger?.Invoke($"[VFX] No valid factory available. CliveFactory={GetCliveFactory():X}, Cached={LastMagicInstance:X}", System.Drawing.Color.Orange);
            return;
        }

        try
        {
            long outPtr = 0;
            Logger?.Invoke($"[VFX] Calling UnkVatb(0x{factory:X}, &outPtr, {vfxId}, 1)", System.Drawing.Color.DarkGray);
            
            // UnkVatb writes the VFX instance to outPtr, return value is status/pointer to outPtr
            long result = UnkVatb(factory, (long)(&outPtr), vfxId, 1);
            
            // The actual VFX instance is in outPtr, not the return value
            long vfxInstance = outPtr;
            Logger?.Invoke($"[VFX] UnkVatb returned 0x{result:X}, outPtr=0x{vfxInstance:X}", System.Drawing.Color.Cyan);

            if (vfxInstance > 0x10000 && vfxInstance < 0x00007FFFFFFFFFFF)
            {
                if (HasValidPosition)
                {
                    Logger?.Invoke($"[VFX] Setting Position: {LastPosition} | Node: 0x{LastNode:X}", System.Drawing.Color.Cyan);
                    GameStructs.NodePositionPair pair = new GameStructs.NodePositionPair
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
                
                // Cache the VTable for future validation
                CacheValidVTable();
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
