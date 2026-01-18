using System.Numerics;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.GameStructs;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

/// <summary>
/// Internal engine for casting magic spells.
/// Handles the low-level game hooks and memory management.
/// This is NOT part of the public API.
/// </summary>
internal unsafe class MagicCastingEngine : IDisposable
{
    // ============================================================
    // DELEGATES
    // ============================================================

    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long SetupMagicDelegate(long battleMagicPtr, int magicId, long casterActorRef, long positionStruct, int commandId, int actionID, byte flag);
    
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate char CastMagicDelegate(long a1, long unkMagicStructPtr);

    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate char FireMagicProjectileDelegate(long magicManagerPtr, long projectileDataPtr);
    
    // ============================================================
    // SIGNATURES
    // ============================================================

    private const string SETUP_MAGIC_SIG = "48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8";
    private const string CAST_MAGIC_SIG = "48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D";
    private const string INSERT_NEW_MAGIC_SIG = "40 53 48 83 EC 20 48 8B DA 4C 8B D9 8B 92 EC 00";
    private const string FIRE_MAGIC_PROJECTILE_SIG = "48 89 5C 24 10 48 89 74 24 18 48 89 7C 24 20 55 41 54 41 55 41 56 41 57 48 8d 6C 24 90 48 81 EC 70 01 00 00 48 8B 05 2D 0F 1A 01 48 33 C4 48 89 45 60 48 8B 51 38 4C 8B E1 44 8B 42 10 41 83 E8 01 0F 84 B6 01 00 00";
    
    // ============================================================
    // CONSTANTS
    // ============================================================
    
    private const int DEFAULT_COMMAND_ID = 101;
    private const int DEFAULT_ACTION_ID = 218;
    private const byte DEFAULT_FLAG = 1;
    private const int MAGIC_STRUCT_SIZE = 0x108;
    private const int POSITION_STRUCT_SIZE = 0x40;
    
    // ============================================================
    // HOOKS & FUNCTION POINTERS
    // ============================================================
    
    private IHook<SetupMagicDelegate>? _setupMagicHook;
    private IHook<CastMagicDelegate>? _castMagicHook;
    private IHook<FireMagicProjectileDelegate>? _fireMagicProjectileHook;
    private CastMagicDelegate? _castMagicWrapper;
    
    // ============================================================
    // CACHED CONTEXT
    // ============================================================
    
    private IntPtr _magicStructBuffer = IntPtr.Zero;
    private IntPtr _positionStructBuffer = IntPtr.Zero;
    private long _cachedCasterActorRef = 0;
    private long _cachedTargetActorRef = 0;
    private long _cachedPositionStruct = 0;
    private int _cachedCommandId = 0;
    private int _cachedActionId = 0;
    private byte _cachedFlag = 0;
    private long _cachedExecutorClient = 0;
    private bool _hasMagicContext = false;
    
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly string _modId;
    private readonly MagicProcessor _processor;
    private readonly long _baseAddress;
    
    // External callbacks for getting player info
    public Func<nint>? GetPlayerStaticActorInfo { get; set; }
    public Func<long>? GetPlayerActorRef { get; set; }
    public Func<int>? GetActiveEikon { get; set; }
    public Func<int, long, long, bool>? OnChargedShotDetected { get; set; }
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    public bool IsReady => _hasMagicContext || (*(long*)(_baseAddress + GlobalOffsets.BattleMagicExecutor) != 0);
    
    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public MagicCastingEngine(ILogger logger, string modId, Config configuration, IStartupScanner scanner)
    {
        _logger = logger;
        _modId = modId;
        _baseAddress = System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress;
        
        // Create processor component
        _processor = new MagicProcessor(logger, modId, configuration, scanner);

        // Allocate buffers
        _magicStructBuffer = Marshal.AllocHGlobal(MAGIC_STRUCT_SIZE);
        _positionStructBuffer = Marshal.AllocHGlobal(POSITION_STRUCT_SIZE);
        
        // Zero-initialize
        for (int i = 0; i < MAGIC_STRUCT_SIZE; i++) 
            *((byte*)_magicStructBuffer + i) = 0;
        for (int i = 0; i < POSITION_STRUCT_SIZE; i++) 
            *((byte*)_positionStructBuffer + i) = 0;
        
        _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Initialized", _logger.ColorGreen);
    }
    
    // ============================================================
    // INITIALIZATION
    // ============================================================
    
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        scans.AddScan(SETUP_MAGIC_SIG, address =>
        {
            _setupMagicHook = hooks.CreateHook<SetupMagicDelegate>(SetupMagicImpl, address).Activate();
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Hooked SetupMagic at 0x{address:X}", _logger.ColorGreen);
        });
        
        scans.AddScan(CAST_MAGIC_SIG, address =>
        {
            _castMagicHook = hooks.CreateHook<CastMagicDelegate>(CastMagicImpl, address).Activate();
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Hooked CastMagic at 0x{address:X}", _logger.ColorGreen);
        });

        scans.AddScan(INSERT_NEW_MAGIC_SIG, address =>
        {
            _castMagicWrapper = hooks.CreateWrapper<CastMagicDelegate>(address, out _);
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Resolved InsertNewMagic at 0x{address:X}", _logger.ColorGreen);
        });

        scans.AddScan(FIRE_MAGIC_PROJECTILE_SIG, address =>
        {
            _fireMagicProjectileHook = hooks.CreateHook<FireMagicProjectileDelegate>(FireMagicProjectileImpl, address).Activate();
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Hooked FireMagicProjectile at 0x{address:X}", _logger.ColorGreen);
        });
    }
    
    public void InitializeProcessor(IReloadedHooks hooks)
    {
        _processor.SetupScans(hooks);
    }
    
    // ============================================================
    // PUBLIC API
    // ============================================================
    
    /// <summary>
    /// Gets the currently locked target from the camera system.
    /// </summary>
    public nint GetLockedTarget()
    {
        // TODO: Implement camera lock target retrieval
        // For now, return the last attacked enemy if available
        return nint.Zero;
    }
    
    /// <summary>
    /// Gets the player's actor pointer.
    /// </summary>
    public nint GetPlayerActor()
    {
        return GetPlayerStaticActorInfo?.Invoke() ?? nint.Zero;
    }
    
    /// <summary>
    /// Cast a spell using the provided request configuration.
    /// </summary>
    public bool CastSpell(MagicCastRequest request)
    {
        if (!IsReady)
        {
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Cannot cast: No magic context. Cast any spell in-game first.", _logger.ColorYellow);
            return false;
        }
        
        // Enqueue modifications if any
        if (request.Modifications.Count > 0)
        {
            var modEntries = ConvertToMagicModEntries(request.Modifications);
            _processor.EnqueueModifications(request.MagicId, modEntries);
        }
        
        // Determine source actor - prioritize cache over callbacks for reliability
        long casterActorRef;
        if (request.SourceActor.HasValue && request.SourceActor.Value != nint.Zero)
        {
            // Explicit source actor provided - use it
            var actorInfo = (StaticActorInfo*)request.SourceActor.Value;
            casterActorRef = actorInfo->ActorRef;
        }
        else if (_cachedCasterActorRef != 0)
        {
            // Use cached caster from last spell cast (most reliable)
            casterActorRef = _cachedCasterActorRef;
        }
        else
        {
            // Fallback: try to get player actor ref via callback
            casterActorRef = GetPlayerActorRef?.Invoke() ?? 0;
        }
        
        // Use cached position - prioritize cache over any dynamic lookup
        long positionStruct = _cachedPositionStruct;
        
        // Determine target actor - prioritize cache over callbacks
        long targetActorRef;
        if (request.TargetActor.HasValue && request.TargetActor.Value != nint.Zero)
        {
            // Explicit target provided - use it
            var targetInfo = (StaticActorInfo*)request.TargetActor.Value;
            targetActorRef = targetInfo->ActorRef;
        }
        else if (_cachedTargetActorRef != 0)
        {
            // Use cached target from last spell cast
            targetActorRef = _cachedTargetActorRef;
        }
        else
        {
            // No target available
            targetActorRef = 0;
        }
        
        // Determine command/action IDs
        int commandId = _cachedCommandId != 0 ? _cachedCommandId : DEFAULT_COMMAND_ID;
        int actionId = _cachedActionId != 0 ? _cachedActionId : DEFAULT_ACTION_ID;
        byte flag = _cachedFlag != 0 ? _cachedFlag : DEFAULT_FLAG;
        
        // Validate requirements
        if (casterActorRef == 0 || positionStruct == 0)
        {
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Cannot cast: Missing caster (0x{casterActorRef:X}) or position (0x{positionStruct:X})", _logger.ColorRed);
            return false;
        }
        
        try
        {
            // Setup the magic struct
            _setupMagicHook!.OriginalFunction(
                (long)_magicStructBuffer, 
                request.MagicId, 
                casterActorRef, 
                positionStruct, 
                commandId, 
                actionId, 
                flag
            );
            
            // TODO: If targetActorRef != 0, inject target info into magic struct
            // This requires further reverse engineering of the magic struct
            // targetActorRef is resolved and ready to use: 0x{targetActorRef:X}
            
            // Get executor client
            long executorClient = *(long*)(_baseAddress + GlobalOffsets.BattleMagicExecutor);
            if (executorClient == 0) executorClient = _cachedExecutorClient;
            
            if (executorClient != 0)
            {
                _castMagicWrapper!((long)executorClient, (long)_magicStructBuffer);
                return true;
            }
            
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Cannot cast: No executor client available", _logger.ColorRed);
            return false;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Error casting spell: {ex.Message}", _logger.ColorRed);
            return false;
        }
    }
    
    /// <summary>
    /// Enqueue modifications for a magic ID without casting.
    /// The modifications will be applied when the magic is cast by the game.
    /// </summary>
    public void EnqueueModifications(int magicId, List<MagicModification> modifications)
    {
        var modEntries = ConvertToMagicModEntries(modifications);
        _processor.EnqueueModifications(magicId, modEntries);
    }
    
    // ============================================================
    // HOOK IMPLEMENTATIONS
    // ============================================================
    
    private long SetupMagicImpl(long battleMagicPtr, int magicId, long casterActorRef, long positionStruct, int commandId, int actionID, byte flag)
    {
        // Cache the context from normal game magic casts
        _cachedCasterActorRef = casterActorRef;
        _cachedPositionStruct = positionStruct;
        _cachedCommandId = commandId;
        _cachedActionId = actionID;
        _cachedFlag = flag;
        
        return _setupMagicHook!.OriginalFunction(battleMagicPtr, magicId, casterActorRef, positionStruct, commandId, actionID, flag);
    }
    
    private char CastMagicImpl(long a1, long unkMagicStructPtr)
    {
        if (!_hasMagicContext)
            _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Captured Magic Context", _logger.ColorGreen);
        
        _cachedExecutorClient = a1;
        _hasMagicContext = true;
        
        return _castMagicHook!.OriginalFunction(a1, unkMagicStructPtr);
    }

    private char FireMagicProjectileImpl(long magicManagerPtr, long projectileDataPtr)
    {
        if (magicManagerPtr != 0)
        {
            int shotType = MagicManagerHelper.GetShotTypeFromManager(magicManagerPtr);
            
            if (shotType == (int)MagicShotType.Charged && OnChargedShotDetected != null && GetActiveEikon != null)
            {
                int activeEikon = GetActiveEikon();
                if (OnChargedShotDetected(activeEikon, magicManagerPtr, projectileDataPtr))
                {
                    _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Suppressing Charged Shot for Eikon {activeEikon}", _logger.ColorYellow);
                    return (char)0;
                }
            }
        }

        return _fireMagicProjectileHook!.OriginalFunction(magicManagerPtr, projectileDataPtr);
    }
    
    // ============================================================
    // HELPERS
    // ============================================================
    
    private void SetupPositionStruct(Vector3 position)
    {
        // Position struct layout (simplified):
        // +0x00: X (float)
        // +0x04: Y (float)
        // +0x08: Z (float)
        // ... additional data
        
        float* posPtr = (float*)_positionStructBuffer;
        posPtr[0] = position.X;
        posPtr[1] = position.Y;
        posPtr[2] = position.Z;
    }
    
    private List<MagicModEntry> ConvertToMagicModEntries(List<MagicModification> modifications)
    {
        var entries = new List<MagicModEntry>();
        
        foreach (var mod in modifications)
        {
            // Skip AddOperation entries - they don't need to be injected
            // The individual AddProperty entries contain all the data needed
            if (mod.Type == MagicModificationType.AddOperation)
            {
                continue;
            }
            
            var entry = new MagicModEntry
            {
                Enabled = true,
                OpType = mod.operationId,
                PropertyId = mod.PropertyId,
                TargetOperationGroupId = mod.OperationGroupId,
                InjectAfterOp = mod.InjectAfterOp  // Propagate injection timing
            };
            
            // Set the value based on type
            SetEntryValue(entry, mod.Value);
            
            // Set action-specific flags
            switch (mod.Type)
            {
                case MagicModificationType.SetProperty:
                    entry.IsInjection = false;
                    entry.DisableOp = false;
                    break;
                case MagicModificationType.RemoveProperty:
                    entry.DisableOp = true;
                    entry.IsInjection = false;
                    break;
                case MagicModificationType.AddProperty:
                    entry.IsInjection = true;  // Inject a new property
                    entry.DisableOp = false;
                    break;
                // AddOperation is skipped at the start of the loop
                case MagicModificationType.RemoveOperation:
                    entry.DisableOp = true;
                    entry.PropertyId = -1;  // Block ALL properties of this operation
                    entry.IsInjection = false;
                    break;
            }
            
            entries.Add(entry);
        }
        
        return entries;
    }
    
    private static void SetEntryValue(MagicModEntry entry, object? value)
    {
        if (value is int intVal)
        {
            entry.UseFloat = false;
            entry.IntValue = intVal;
        }
        else if (value is float floatVal)
        {
            entry.UseFloat = true;
            entry.FloatValue = floatVal;
        }
        else if (value is bool boolVal)
        {
            entry.UseFloat = false;
            entry.IntValue = boolVal ? 1 : 0;
        }
        else if (value is System.Numerics.Vector3 vec3Val)
        {
            entry.UseVec3 = true;
            entry.Vec3X = vec3Val.X;
            entry.Vec3Y = vec3Val.Y;
            entry.Vec3Z = vec3Val.Z;
        }
    }
    
    // ============================================================
    // STATE MANAGEMENT
    // ============================================================
    
    public void Reset()
    {
        _hasMagicContext = false;
        _cachedExecutorClient = 0;
        _cachedCasterActorRef = 0;
        _cachedTargetActorRef = 0;
        _cachedPositionStruct = 0;
        _cachedCommandId = 0;
        _cachedActionId = 0;
        _cachedFlag = 0;
        
        _logger.WriteLine($"[{_modId}] [MagicCastingEngine] Reset", _logger.ColorYellow);
    }
    
    public void UpdateConfiguration(Config configuration)
    {
        _processor.UpdateConfiguration(configuration);
    }
    
    public void Dispose()
    {
        if (_magicStructBuffer != IntPtr.Zero) 
        {
            Marshal.FreeHGlobal(_magicStructBuffer);
            _magicStructBuffer = IntPtr.Zero;
        }
        if (_positionStructBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_positionStructBuffer);
            _positionStructBuffer = IntPtr.Zero;
        }
    }
}
