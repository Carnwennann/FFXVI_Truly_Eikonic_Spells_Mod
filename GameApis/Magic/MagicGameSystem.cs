using System.Runtime.InteropServices;
using System.Numerics;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

/// <summary>
/// Internal system for handling magic spell casting and projectile spawning.
/// Interacts directly with the game's magic system via hooks.
/// </summary>
internal unsafe class MagicGameSystem
{
    // ============================================================
    // DELEGATES
    // ============================================================
    
    /// <summary>
    /// Sets a property for a magic operation (like Operation 35).
    /// a1: Operation object pointer
    /// a2: Property ID (35=Speed, 38=Homing)
    /// a3: Pointer to property data struct (a3+8 is pointer to value)
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate void SetOperationPropertyDelegate(long a1, int a2, long a3);

    /// <summary>
    /// Universal Property Executor. Called for every property in the .magic file.
    /// a1: MagicFileInstance, opType: OperationType, propertyId: PropertyId, dataPtr: DataPtr
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate void MagicUnkExecuteDelegate(long magicFileInstance, int opType, int propertyId, long dataPtr);

    /// <summary>
    /// Operation Factory. Creates operation operations based on type.
    /// a1: Allocator/Manager, opType: OperationType, a3: Pointer to MagicFileInstance
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long OperationFactoryDelegate(long a1, int opType, long a3);

    /// <summary>
    /// Prepares a magic spell to be cast. Sets up the magic struct.
    /// Signature: 48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8
    /// 
    /// Parameters (from reverse engineering):
    /// - a1 (this): BattleMagic* - The magic struct to populate
    /// - a2 (magicId): int - Magic spell ID (214 = Dia)
    /// - a3 (target): ActorReference* - The CASTER's ActorReference (from GetTargetDataMaybe)
    /// - a4 (position): Position* - Position struct from BattleBehaviorEntityEntry::GetPositionStructMaybe
    /// - a5 (commandId): int - Command ID (101 for Dia)
    /// - a6 (actionID): int - Action ID from ActionBase__Unk0x58 (218-219)
    /// - a7 (flag): byte - Flag byte from offset 21 of timeline element
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long SetupMagicDelegate(long battleMagicPtr, int magicId, long casterActorRef, long positionStruct, int commandId, int actionID, byte flag);
    
    /// <summary>
    /// Actually spawns the magic spell using the prepared struct.
    /// Signature: 48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate char CastMagicDelegate(long a1, long unkMagicStructPtr);

    /// <summary>
    /// Fires a magic projectile (Dia, Charged Shot, etc.)
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate char FireMagicProjectileDelegate(long magicManagerPtr, long projectileDataPtr);
    
    // ============================================================
    // CONSTANTS
    // ============================================================
    
    // Signature for Operation_35 Property Setter
    private const string SET_OPERATION_PROPERTY_SIG = "48 8B C4 48 89 58 08 48 89 70 18 57 48 83 EC 40 C5 F8 29 70 E8 48 8B F9 C5 F8 29 78 D8 83 EA 23 0F 84 3F 01 00 00 83 EA 01 0F 84 C5 00 00 00 83 EA 01 0F 84 B1 00 00 00 83 EA 01 0F 84 8B 00 00 00 81 FA 63 05 00 00 0F 85 26 01 00 00 33 DB 89 59 30 49 8B 40 08 8B 08 89 4F 34";
    
    // Universal Magic Signatures
    private const string MAGIC_UNK_EXECUTE_SIG = "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 49 8B F9 41 8B D8 8B F2 41 83 F8 02 75 2F 48 8D 59 10 48 8D B9 10 01 00 00 EB 1B 48 8B 0B 48 85 C9 74 0F 39 71 20 75 0A";
    private const string OPERATION_FACTORY_SIG = "48 89 5C 24 08 57 48 83 EC 20 49 8B F8 81 FA B7 00 00 00 75 65 48 8B 01 4C 8D 4C 24 48 33 DB 48 89 5C 24 48 8D 53 48 44 8D 43 08 FF 50 30 48 8B D0 48 85 C0 74 6A 48 8B 0F 48 8D 05 04 96 E8 00 48 89 02 44 8D 43 01 41 8B C0 87 42 0C 83 4A 20 FF";
    
    // Signatures from 010 Template
    private const string MAGIC_FILE_PROCESS_SIG = "48 8B C4 48 89 58 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D 68 ?? 48 81 EC ?? ?? ?? ?? C5 F8 29 70 ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 45 33 F6 48 89 55";
    private const string MAGIC_FILE_HANDLE_SUB_ENTRY_SIG = "40 55 53 56 57 41 54 41 56 41 57 48 8B EC 48 83 EC ?? 48 8D 59";
    private const string FIRE_MAGIC_PROJECTILE_SIG = "48 89 5C 24 10 48 89 74 24 18 48 89 7C 24 20 55 41 54 41 55 41 56 41 57 48 8d 6C 24 90 48 81 EC 70 01 00 00 48 8B 05 2D 0F 1A 01 48 33 C4 48 89 45 60 48 8B 51 38 4C 8B E1 44 8B 42 10 41 83 E8 01 0F 84 B6 01 00 00";

    // Buffer sizes
    private const int MAGIC_STRUCT_SIZE = 0x108;      // 264 bytes (8 + 32*8)
    
    // ============================================================
    // HOOKS & FUNCTION POINTERS
    // ============================================================
    
    private IHook<SetupMagicDelegate>? _setupMagicHook;
    private IHook<CastMagicDelegate>? _castMagicHook;
    private IHook<FireMagicProjectileDelegate>? _fireMagicProjectileHook;
    private IHook<MagicUnkExecuteDelegate>? _magicUnkExecuteHook;
    private IHook<OperationFactoryDelegate>? _operationFactoryHook;
    private IHook<GenericMagicDelegate>? _magicFileProcessHook;
    private IHook<GenericMagicDelegate>? _magicFileHandleSubEntryHook;
    
    private SetupMagicDelegate? _setupMagicWrapper;
    private CastMagicDelegate? _castMagicWrapper;
    private MagicUnkExecuteDelegate? _magicUnkExecuteWrapper;
    private OperationFactoryDelegate? _operationFactoryWrapper;

    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long GenericMagicDelegate(long a1, long a2, long a3, long a4);
    
    // ============================================================
    // CACHED CONTEXT
    // ============================================================
    
    // SetupMagic/CastMagic system cache
    private IntPtr _magicStructBuffer = IntPtr.Zero;
    private long _setupMagic_casterActorRef = 0;   // ActorReference* of the caster
    private long _setupMagic_positionStruct = 0;   // Position struct pointer
    private int _setupMagic_commandId = 0;         // Command ID (101 for Dia)
    private int _setupMagic_actionID = 0;          // Action ID (218-219)
    private byte _setupMagic_flag = 0;             // Flag byte
    private long _castMagic_a1 = 0;
    private bool _hasMagicContext = false;
    private int _currentlyCastingMagicId = 0;
    
    // Queue for deferred magic processing (CastMagic is often asynchronous)
    // Key: (magicId, operationGroupId)
    private Dictionary<(int magicId, int groupId), Queue<List<FuzzerEntry>>> _groupedQueues = new();
    private List<FuzzerEntry>? _activeInstanceEntries = null;
    private int _activeInstanceMagicId = 0;

    // Tracker for operation occurrences within a single MagicFileProcess call
    private Dictionary<int, int> _opInstanceTracker = new();
    private Dictionary<long, int> _propInstanceTracker = new(); // Key: (opType << 32) | propId
    private int _lastOpType = -1;
    private List<FuzzerEntry> _pendingInjections = new();
    private bool _isProcessingInjections = false;
    
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private readonly IStartupScanner _scanner;
    private Config _configuration;
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    /// <summary>
    /// True if we have valid cached SetupMagic/CastMagic context.
    /// </summary>
    public bool HasMagicContext => _hasMagicContext;

    // External callbacks
    public Func<int>? GetActiveEikon { get; set; }
    public Func<int, long, long, bool>? OnChargedShotDetected { get; set; }
    
    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public MagicGameSystem(ILogger logger, IModConfig modConfig, Config configuration, IStartupScanner scanner)
    {
        _logger = logger;
        _modConfig = modConfig;
        _configuration = configuration;
        _scanner = scanner;
        
        // Allocate buffers
        _magicStructBuffer = Marshal.AllocHGlobal(MAGIC_STRUCT_SIZE);
        
        // Zero-initialize
        for (int i = 0; i < MAGIC_STRUCT_SIZE; i++) *((byte*)_magicStructBuffer + i) = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Initialized", _logger.ColorGreen);
    }
    
    // ============================================================
    // INITIALIZATION
    // ============================================================
    
    /// <summary>
    /// Initialize hooks using signature scanning.
    /// Call from SetupScans in the main mod.
    /// </summary>
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        // SetupMagic - Prepares the magic spell
        scans.AddScan("48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8", address =>
        {
            _setupMagicHook = hooks.CreateHook<SetupMagicDelegate>(SetupMagicImpl, address).Activate();
            _setupMagicWrapper = hooks.CreateWrapper<SetupMagicDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked SetupMagic at 0x{address:X}", _logger.ColorGreen);
        });
        
        // CastMagic - Actually spawns the spell
        scans.AddScan("48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D", address =>
        {
            _castMagicHook = hooks.CreateHook<CastMagicDelegate>(CastMagicImpl, address).Activate();
            _castMagicWrapper = hooks.CreateWrapper<CastMagicDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked CastMagic at 0x{address:X}", _logger.ColorGreen);
        });

        // FireMagicProjectile - For detecting and suppressing charged shots
        scans.AddScan(FIRE_MAGIC_PROJECTILE_SIG, address =>
        {
            _fireMagicProjectileHook = hooks.CreateHook<FireMagicProjectileDelegate>(FireMagicProjectileImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked FireMagicProjectile at 0x{address:X}", _logger.ColorGreen);
        });
    }
    
    /// <summary>
    /// Initialize Universal Magic hooks using signature scans.
    /// </summary>
    public void InitializeUniversalMagicHooks(IReloadedHooks hooks)
    {
        // MagicUnkExecute - The Universal Property Logger/Fuzzer
        _scanner.AddScan(MAGIC_UNK_EXECUTE_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked MagicUnkExecute at 0x{address:X}", _logger.ColorGreen);
            _magicUnkExecuteHook = hooks.CreateHook<MagicUnkExecuteDelegate>(MagicUnkExecuteImpl, address).Activate();
            _magicUnkExecuteWrapper = hooks.CreateWrapper<MagicUnkExecuteDelegate>(address, out _);
        });

        // OperationFactory - The VTable Mapper
        _scanner.AddScan(OPERATION_FACTORY_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked OperationFactory at 0x{address:X}", _logger.ColorGreen);
            _operationFactoryHook = hooks.CreateHook<OperationFactoryDelegate>(OperationFactoryImpl, address).Activate();
            _operationFactoryWrapper = hooks.CreateWrapper<OperationFactoryDelegate>(address, out _);
        });

        // MagicFile::ProcessUnk
        _scanner.AddScan(MAGIC_FILE_PROCESS_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked MagicFile::ProcessUnk at 0x{address:X}", _logger.ColorGreen);
            _magicFileProcessHook = hooks.CreateHook<GenericMagicDelegate>(MagicFileProcessImpl, address).Activate();
        });

        // MagicFile::HandleSubEntry
        _scanner.AddScan(MAGIC_FILE_HANDLE_SUB_ENTRY_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked MagicFile::HandleSubEntry at 0x{address:X}", _logger.ColorGreen);
            _magicFileHandleSubEntryHook = hooks.CreateHook<GenericMagicDelegate>(MagicFileHandleSubEntryImpl, address).Activate();
        });
    }

    private long MagicFileProcessImpl(long a1, long a2, long a3, long a4)
    {
        // Reset trackers for this new process call
        _opInstanceTracker.Clear();
        _propInstanceTracker.Clear();
        _lastOpType = -1;
        _pendingInjections.Clear();
        _activeInstanceEntries = null;
        _activeInstanceMagicId = 0;

        try
        {
            // Ejecutar original primero. 
            // Las IDs se cargarán durante la ejecución y se activarán en MagicUnkExecuteImpl
            long result = _magicFileProcessHook!.OriginalFunction(a1, a2, a3, a4);

            // Process any remaining injections at the end of the group
            if (_pendingInjections.Count > 0)
            {
                _isProcessingInjections = true;
                try
                {
                    foreach (var entry in _pendingInjections)
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] [INJECTOR] Injecting Op {entry.OpType} Prop {entry.PropertyId} AFTER Op {_lastOpType} (End of Group)", _logger.ColorGreen);
                        PerformInjection(a1, entry);
                    }
                    _pendingInjections.Clear();
                }
                finally
                {
                    _isProcessingInjections = false;
                }
            }

            // Inyectar propiedades personalizadas (Opción 2) - Solo las que van al final
            if (_activeInstanceEntries != null)
            {
                foreach (var entry in _activeInstanceEntries)
                {
                    if (entry.Enabled && entry.IsInjection && entry.InjectAfterOp == -1)
                    {
                        // We already know this entry is for this Magic/Group because it was dequeued for it
                        _logger.WriteLine($"[{_modConfig.ModId}] [INJECTOR] Injecting Op {entry.OpType} Prop {entry.PropertyId} at END of Group", _logger.ColorGreen);
                        PerformInjection(a1, entry);
                    }
                }
            }

            return result;
        }
        finally
        {
            _activeInstanceEntries = null;
            _activeInstanceMagicId = 0;
        }
    }

    private void PerformInjection(long magicFileInstance, FuzzerEntry entry)
    {
        if (entry.UseVec3)
            InjectPropertyVec3(magicFileInstance, entry.OpType, entry.PropertyId, entry.Vec3X, entry.Vec3Y, entry.Vec3Z);
        else if (entry.UseFloat)
            InjectPropertyFloat(magicFileInstance, entry.OpType, entry.PropertyId, entry.FloatValue);
        else
            InjectPropertyInt(magicFileInstance, entry.OpType, entry.PropertyId, entry.IntValue);
    }

    private void InjectPropertyFloat(long magicFileInstance, int opType, int propId, float value)
    {
        float* valPtr = stackalloc float[1];
        *valPtr = value;
        
        long* fakeData = stackalloc long[2];
        fakeData[0] = 0; 
        fakeData[1] = (long)valPtr;
        
        // Llamamos a nuestro Impl en lugar de OriginalFunction para ver los logs
        MagicUnkExecuteImpl(magicFileInstance, opType, propId, (long)fakeData);
    }

    private void InjectPropertyInt(long magicFileInstance, int opType, int propId, int value)
    {
        int* valPtr = stackalloc int[1];
        *valPtr = value;
        
        long* fakeData = stackalloc long[2];
        fakeData[0] = 0;
        fakeData[1] = (long)valPtr;
        
        // Llamamos a nuestro Impl en lugar de OriginalFunction para ver los logs
        MagicUnkExecuteImpl(magicFileInstance, opType, propId, (long)fakeData);
    }

    private void InjectPropertyVec3(long magicFileInstance, int opType, int propId, float x, float y, float z)
    {
        Vector3* valPtr = stackalloc Vector3[1];
        *valPtr = new Vector3(x, y, z);
        
        long* fakeData = stackalloc long[2];
        fakeData[0] = 0; 
        fakeData[1] = (long)valPtr;
        
        // Llamamos a nuestro Impl en lugar de OriginalFunction para ver los logs
        MagicUnkExecuteImpl(magicFileInstance, opType, propId, (long)fakeData);
    }

    private long MagicFileHandleSubEntryImpl(long a1, long a2, long a3, long a4)
    {
        int opType = (int)a2;
        
        // Detect Op change (though UnkExecute should have caught it already)
        CheckOpChange(a1, opType);

        long result = _magicFileHandleSubEntryHook!.OriginalFunction(a1, a2, a3, a4);

        return result;
    }

    // ============================================================
    // PUBLIC API - CAST MAGIC
    // ============================================================
    
    public void EnqueueModifications(int magicId, List<FuzzerEntry> entries)
    {
        // Group entries by their target group ID
        var grouped = entries.GroupBy(e => e.TargetOperationGroupId);
        
        foreach (var group in grouped)
        {
            var key = (magicId, group.Key);
            if (!_groupedQueues.TryGetValue(key, out var queue))
            {
                queue = new Queue<List<FuzzerEntry>>();
                _groupedQueues[key] = queue;
            }
            queue.Enqueue(group.ToList());
            _logger.WriteLine($"[{_modConfig.ModId}] [QUEUE] Enqueued {group.Count()} entries for Magic {magicId} Group {group.Key}", _logger.ColorYellow);
        }
    }

    /// <summary>
    /// Spawn a magic spell by ID using the SetupMagic/CastMagic system.
    /// Requires HasMagicContext to be true (fire a normal spell first to capture context).
    /// </summary>
    public bool CastMagicSpell(int magicId)
    {
        if (!_hasMagicContext || _castMagic_a1 == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: No magic context! Fire a normal shot first.", _logger.ColorRed);
            return false;
        }
        
        if (_magicStructBuffer == IntPtr.Zero)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: Missing buffers!", _logger.ColorRed);
            return false;
        }

        if (_setupMagic_casterActorRef == 0 || _setupMagic_positionStruct == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: Incomplete context (Caster=0x{_setupMagic_casterActorRef:X}, Pos=0x{_setupMagic_positionStruct:X})!", _logger.ColorRed);
            return false;
        }
        
        if (_setupMagicHook == null || _castMagicHook == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: Hooks not initialized!", _logger.ColorRed);
            return false;
        }
        
        _currentlyCastingMagicId = magicId;
        try
        {
            // Call SetupMagic with our cached struct and new magicId
            var execResult = _setupMagicHook.OriginalFunction(
                (long)_magicStructBuffer, 
                magicId, 
                _setupMagic_casterActorRef, 
                _setupMagic_positionStruct, 
                _setupMagic_commandId, 
                _setupMagic_actionID, 
                _setupMagic_flag
            );
            
            // Call CastMagic to actually spawn the spell
            var castResult = _castMagicHook.OriginalFunction(_castMagic_a1, (long)_magicStructBuffer);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] === CRASH PREVENTED === {ex.Message}", _logger.ColorRed);
            return false;
        }
        finally
        {
            _currentlyCastingMagicId = 0;
        }
    }
    
    // ============================================================
    // HOOK IMPLEMENTATIONS
    // ============================================================
    
    private long SetupMagicImpl(long battleMagicPtr, int magicId, long casterActorRef, long positionStruct, int commandId, int actionID, byte flag)
    {
        // Cache parameters with descriptive names
        _setupMagic_casterActorRef = casterActorRef;   // ActorReference* of caster (from GetTargetDataMaybe)
        _setupMagic_positionStruct = positionStruct;   // Position from BattleBehaviorEntityEntry::GetPositionStructMaybe
        _setupMagic_commandId = commandId;             // Command ID (101 for Dia)
        _setupMagic_actionID = actionID;               // Action ID (218-219)
        _setupMagic_flag = flag;                       // Flag byte
        
        // Deep copy the magic struct
        if (_magicStructBuffer != IntPtr.Zero && battleMagicPtr != 0)
        {
            try
            {
                long ptr1Value = *(long*)battleMagicPtr;
                *(long*)_magicStructBuffer = ptr1Value;
                
                for (int i = 0; i < 32; i++)
                {
                    long value = *(long*)(battleMagicPtr + 0x8 + 0x8 * i);
                    *(long*)((long)_magicStructBuffer + 0x8 + 0x8 * i) = value;
                }
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] Copy failed: {ex.Message}", _logger.ColorRed);
            }
        }
        
        // Call original to fill the struct
        return _setupMagicHook!.OriginalFunction(battleMagicPtr, magicId, casterActorRef, positionStruct, commandId, actionID, flag);
    }
    
    private char CastMagicImpl(long a1, long unkMagicStructPtr)
    {
        if (!_hasMagicContext)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Captured Magic Context (a1=0x{a1:X})", _logger.ColorGreen);
        }
        
        _castMagic_a1 = a1;
        _hasMagicContext = true;
        
        return _castMagicHook!.OriginalFunction(a1, unkMagicStructPtr);
    }

    private char FireMagicProjectileImpl(long magicManagerPtr, long projectileDataPtr)
    {
        if (magicManagerPtr != 0)
        {
            // magicManagerPtr + 0x38 is the pointer to MagicInputConfig
            long inputConfig = *(long*)(magicManagerPtr + 0x38);
            if (inputConfig != 0)
            {
                // inputConfig + 0x10 is the Shot Type (1=Normal, 2=Charged, 3=Precision, 4=Burst)
                int shotType = *(int*)(inputConfig + 0x10);
                
                // If it's a Charged Shot (2), check with the mod if we should suppress it
                if (shotType == 2 && OnChargedShotDetected != null && GetActiveEikon != null)
                {
                    int activeEikon = GetActiveEikon();
                    // If the callback returns true, we suppress the projectile by returning 0
                    if (OnChargedShotDetected(activeEikon, magicManagerPtr, projectileDataPtr))
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Suppressing Charged Shot projectile for Eikon {activeEikon}", _logger.ColorYellow);
                        return (char)0;
                    }
                }
            }
        }

        return _fireMagicProjectileHook!.OriginalFunction(magicManagerPtr, projectileDataPtr);
    }
    
    // ============================================================
    // INTERNAL HELPERS
    // ============================================================
    
    private readonly Dictionary<long, string> _operationNames = new()
    {
        { 0x7FF6C7A69EA0, "Operation_35 (Duration)" },
        { 0x7FF6C7A695D8, "Operation_25" },
        { 0x7FF6C7A69958, "Operation_94" },
        { 0x7FF6C7A69798, "Operation_87" },
        { 0x7FF6C7A684E8, "Operation_1841" },
        { 0x7FF6C7A69F80, "Operation_101" },
        { 0x7FF6C7A6A060, "Operation_183" },
        { 0x7FF6C7A69B18, "Operation_139" },
        { 0x7FF6C7A6A988, "Operation_4448" },
        { 0x7FF6C7A6A230, "Operation_50" },
        { 0x7FF6C7A6A310, "Operation_108" },
        { 0x7FF6C7A68960, "Operation_1587" },
        { 0x7FF6C7A67FA0, "Operation_2855" },
        { 0x7FF6C79D9860, "Operation_3790" },
        { 0x7FF6C79D9780, "Operation_3771" },
        { 0x7FF6C7A68080, "Operation_3847" },
        { 0x7FF6C7A68320, "Operation_39" },
        { 0x7FF6C79D8178, "Operation_6460" },
        { 0x7FF6C7A67C18, "Operation_4553" },
        { 0x7FF6C7A67DD8, "Operation_4446" },
    };

    private readonly Dictionary<int, string> _propertyNames = new()
    {
        
        { 2, "??? - int"},
        { 8, "Speed - float" },
        // value:  0 = use target, 1 = not use target
        { 13, "Calculate target trajectory - bool" },
        { 14, "Pi value - float" },
        // Positive values = downwards, negative = upwards
        // Only works if Calculate target trajectory is 0
        { 22, "Vertical Angle Degrees offset - float" },
        // value: 0 = no, 1 = yes
        { 30, "Disappear after duration? - bool" },
        { 31, "Scale projectile body (default=1.0) - float"},
        { 35, "Duration (s) - float" },
        // value: 76 = impacts with map geometry, 260 = no longer impacts with map geometry
        { 36, "Hitbox Behaviour ID? - int" },
        { 41, "Hitbox Behaviour Impact ID? - int" },
        { 42, "Hitbox/Attachment Size? - float" },
        // - 0 = ?
        // - 1 = Targeted Actor
        // - 2 = Source Body Part (Eid Id, specified by Prop 81)
        // - 3 = Source Position (Center)
        // - 4 = ?
        // - 5 = Layout Instance ID (specified by Prop 1458)
        // - 6 = ? (prop 84 is used?)
        // - 7 = ? (Targeted Actor but with a twist)
        { 73, "Location spawn type ID - int"},
        // Collection of effects and sound for eg. 1008 shiva projectile impact
        // But also spawns projectiles on some IDs e.g. 1007
        { 89, "VFX ID? - int"},
        // values: 2=parabola, 3= infinite looking orbit around source, 4=stationary at source (or don't work?)
        { 187, "Type of trayectory ID - int"},
        { 2227, "??? - int"},
        { 2430 , "Trajectory variables - vec3"},
        { 2351 , "Unknown variable - float"},
        // value: can be positive or negative float
        { 2593, "Trayectory intensity curve strength - float"},
    };

    private long OperationFactoryImpl(long a1, int opType, long a3)
    {
        long result = _operationFactoryHook!.OriginalFunction(a1, opType, a3);
        
        if (result != 0)
        {
            long vtable = *(long*)result;
            if (!_operationNames.ContainsKey(vtable))
            {
                string name = $"Operation_{opType}";
                _operationNames[vtable] = name;
                _logger.WriteLine($"[{_modConfig.ModId}] [VTABLE_MAP] Mapped {name} to VTable 0x{vtable:X}", _logger.ColorGreen);
            }
        }
        
        return result;
    }

    private unsafe int GetIdFromRuntime(long instance)
    {
        // Extremely strict pointer validation to prevent AccessViolationException
        if (instance < 0x10000 || instance > 0x00007FFFFFFFFFFF || instance % 8 != 0) return 0;
        
        try
        {
            // 1. Check if it's a class instance by looking at the VTable
            long vtable = *(long*)instance;
            if (vtable < 0x10000 || vtable > 0x00007FFFFFFFFFFF || vtable % 8 != 0) return 0;

            // 2. Check the NEW confirmed offsets from scan!
            // Offset +0x200: MagicId (e.g. 214)
            // Offset +0x204: GroupId (e.g. 4338)
            int id200 = *(int*)(instance + 0x200);
            if (id200 > 100 && id200 < 30000) return id200;

            int id204 = *(int*)(instance + 0x204);
            if (id204 > 100 && id204 < 30000) return id204;

            // 3. Check for direct IDs (common for simple wrappers)
            // We use a smaller range to avoid accidentally reading pointers as IDs
            int id8 = *(int*)(instance + 8);
            if (id8 > 100 && id8 < 30000) return id8;
            
            int id12 = *(int*)(instance + 12);
            if (id12 > 100 && id12 < 30000) return id12;

            int id16 = *(int*)(instance + 16);
            if (id16 > 100 && id16 < 30000) return id16;

            // Check common header offsets for IDs
            int id64 = *(int*)(instance + 0x40);
            if (id64 > 100 && id64 < 30000) return id64;

            int id68 = *(int*)(instance + 0x44);
            if (id68 > 100 && id68 < 30000) return id68;

            // 4. Check for data pointer pattern
            long[] potentialOffsets = { 8, 16, 24, 32, 48 };
            foreach (var offset in potentialOffsets)
            {
                long dataPtr = *(long*)(instance + offset);
                if (dataPtr > 0x10000 && dataPtr < 0x00007FFFFFFFFFFF && dataPtr % 8 == 0)
                {
                    int id = *(int*)dataPtr;
                    if (id > 100 && id < 1000000) return id;
                    
                    int idPlus4 = *(int*)(dataPtr + 4);
                    if (idPlus4 > 100 && idPlus4 < 1000000) return idPlus4;
                }
            }
        }
        catch { }
        return 0;
    }

    private unsafe int GetNormalizedMagicId(long magicFileInstance)
    {
        if (magicFileInstance < 0x10000) return 0;

        // Try the "Data Pointer" pattern first (Safe)
        int rawId = GetIdFromRuntime(magicFileInstance);
        
        // If we still don't have an ID, and we are in a controlled cast, we use the cached ID
        if (rawId == 0)
        {
            if (_activeInstanceMagicId != 0) return _activeInstanceMagicId;
            if (_currentlyCastingMagicId != 0) return _currentlyCastingMagicId;
        }
        
        if (rawId == 0) return 0;
        if (rawId == 214) return 214;

        // Check various masks/shifts for 214 (0xD6)
        if ((rawId & 0xFFFF) == 214) return 214;
        if ((rawId >> 16) == 214) return 214;
        if (((rawId >> 16) & 0xFFFF) == 214) return 214;
        if (((rawId >> 24) & 0xFF) == 214) return 214;
        
        return rawId;
    }

    private void CheckOpChange(long magicFileInstance, int opType)
    {
        if (_isProcessingInjections) return;
        if (opType == _lastOpType) return;

        // 1. Resolve IDs immediately using confirmed offsets
        int magicId = 0;
        int groupId = 0;
        if (magicFileInstance > 0x10000 && magicFileInstance % 8 == 0)
        {
            try {
                magicId = *(int*)(magicFileInstance + 0x200);
                groupId = *(int*)(magicFileInstance + 0x204);
            } catch { }
        }

        // Fallbacks
        if (magicId < 100 || magicId > 30000) magicId = GetNormalizedMagicId(magicFileInstance);
        if (groupId < 100 || groupId > 30000) groupId = GetIdFromRuntime(magicFileInstance);
        if (groupId == magicId) groupId = 0;
        
        // 2. Perform pending injections from the PREVIOUS operation
        if (_pendingInjections.Count > 0)
        {
            _isProcessingInjections = true;
            try
            {
                foreach (var entry in _pendingInjections)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [INJECTOR] Injecting Op {entry.OpType} Prop {entry.PropertyId} AFTER Op {_lastOpType} in Magic {magicId} Group {groupId}", _logger.ColorGreen);
                    PerformInjection(magicFileInstance, entry);
                }
                _pendingInjections.Clear();
            }
            finally
            {
                _isProcessingInjections = false;
            }
        }

        // 3. Update state for the NEW operation
        _lastOpType = opType;
        int currentOpOccurrence = _opInstanceTracker.GetValueOrDefault(opType, 0);
        _opInstanceTracker[opType] = currentOpOccurrence + 1;

        // 4. Check for injections that should happen after THIS new operation
        var activeEntries = _activeInstanceEntries ?? _configuration.FuzzerEntries;
        bool fuzzerEnabled = _activeInstanceEntries != null || _configuration.EnableUniversalFuzzer;

        if (fuzzerEnabled)
        {
            foreach (var entry in activeEntries)
            {
                if (entry.Enabled && entry.IsInjection && entry.InjectAfterOp == opType)
                {
                    if (entry.TargetMagicId == -1 || entry.TargetMagicId == magicId)
                    {
                        if (entry.TargetOperationGroupId == -1 || entry.TargetOperationGroupId == groupId)
                        {
                            if (entry.Occurrence == -1 || entry.Occurrence == currentOpOccurrence)
                            {
                                // Queue it for later (when this Op ends)
                                _pendingInjections.Add(entry);
                                _logger.WriteLine($"[{_modConfig.ModId}] [QUEUE_INJECT] Queued Op {entry.OpType} to inject after Op {opType} (Occ {currentOpOccurrence})", _logger.ColorBlue);
                            }
                        }
                    }
                }
            }
        }
    }

    private void MagicUnkExecuteImpl(long magicFileInstance, int opType, int propertyId, long dataPtr)
    {
        // 1. Resolve IDs immediately using confirmed offsets
        int magicId = 0;
        int groupId = 0;
        if (magicFileInstance > 0x10000 && magicFileInstance % 8 == 0)
        {
            try {
                magicId = *(int*)(magicFileInstance + 0x200);
                groupId = *(int*)(magicFileInstance + 0x204);
            } catch { }
        }

        // Fallbacks
        if (magicId < 100 || magicId > 30000) magicId = GetNormalizedMagicId(magicFileInstance);
        if (groupId < 100 || groupId > 30000) groupId = GetIdFromRuntime(magicFileInstance);
        if (groupId == magicId) groupId = 0;

        // 2. ACTIVATE ENTRIES BEFORE CheckOpChange
        // This ensures that when CheckOpChange runs, it already knows which modifications to use
        if (_activeInstanceEntries == null && (magicId != 0 || groupId != 0))
        {
            var key = (magicId, groupId);
            if (_groupedQueues.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                _activeInstanceEntries = queue.Dequeue();
                _activeInstanceMagicId = magicId;
                _logger.WriteLine($"[{_modConfig.ModId}] [ACTIVATE] Linked {_activeInstanceEntries.Count} mods to Magic {magicId} Group {groupId}", _logger.ColorGreen);
            }
        }

        // 3. Detect Op change (Injections/Disables)
        CheckOpChange(magicFileInstance, opType);
        
        if (magicId == 0 && groupId == 0 && _activeInstanceEntries != null)
        {
            // If we are in an active instance but IDs are 0, try to dump the object to see why
            try {
                long vtable = *(long*)magicFileInstance;
                long d8 = *(long*)(magicFileInstance + 8);
                long d16 = *(long*)(magicFileInstance + 16);
                // _logger.WriteLine($"[{_modConfig.ModId}] [ID_DEBUG] Instance 0x{magicFileInstance:X} VTable=0x{vtable:X} +8=0x{d8:X} +16=0x{d16:X}", _logger.ColorYellow);
            } catch { }
        }

        if (_activeInstanceEntries != null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [INJECTION_DEBUG] Magic {magicId} Group {groupId} Op {opType} Prop {propertyId}", _logger.ColorYellow);
        }

        // Track occurrences
        long propKey = ((long)opType << 32) | (uint)propertyId;
        int propOccurrence = _propInstanceTracker.GetValueOrDefault(propKey, 0);
        _propInstanceTracker[propKey] = propOccurrence + 1;

        int opOccurrence = _opInstanceTracker.GetValueOrDefault(opType, 0) - 1;
        if (opOccurrence < 0) opOccurrence = 0;
        
        // Check for DisableOp
        var activeEntries = _activeInstanceEntries ?? _configuration.FuzzerEntries;
        bool fuzzerEnabled = _activeInstanceEntries != null || _configuration.EnableUniversalFuzzer;

        if (fuzzerEnabled)
        {
            foreach (var entry in activeEntries)
            {
                if (entry.Enabled && entry.DisableOp && entry.OpType == opType)
                {
                    if (entry.TargetMagicId == -1 || entry.TargetMagicId == magicId)
                    {
                        if (entry.TargetOperationGroupId == -1 || entry.TargetOperationGroupId == groupId)
                        {
                            // If PropertyId is -1, we use Op Occurrence
                            // If PropertyId is specific, we use Prop Occurrence
                            int targetOcc = (entry.PropertyId == -1) ? opOccurrence : propOccurrence;

                            if (entry.Occurrence == -1 || entry.Occurrence == targetOcc)
                            {
                                if (entry.PropertyId == -1 || entry.PropertyId == propertyId)
                                {
                                    _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {magicId} Group {groupId} Op {opType} Prop {propertyId} DISABLED (Occ {targetOcc})", _logger.ColorRed);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Debug Scan
        // ScanMagicFile(magicFileInstance, magicId);

        string contextStr = $"[Magic {magicId} Group {groupId}]";

        // dataPtr + 8 is the pointer to the actual value
        long valuePtr = *(long*)(dataPtr + 8);

        // UNIVERSAL FUZZER LOGIC
        bool isFuzzed = false;
        float originalFloat = 0;
        int originalInt = 0;
        Vector3 originalVec3 = Vector3.Zero;
        FuzzerEntry? activeEntry = null;

        if (fuzzerEnabled)
        {
            foreach (var entry in activeEntries)
            {
                if (entry.Enabled && !entry.IsInjection && !entry.DisableOp && entry.PropertyId == propertyId && (entry.OpType == -1 || entry.OpType == opType))
                {
                    if (entry.TargetMagicId == -1 || entry.TargetMagicId == magicId)
                    {
                        if (entry.TargetOperationGroupId == -1 || entry.TargetOperationGroupId == groupId)
                        {
                            // Use Prop Occurrence for specific property overrides
                            if (entry.Occurrence == -1 || entry.Occurrence == propOccurrence)
                            {
                                activeEntry = entry;
                                isFuzzed = true;
                                
                                if (entry.UseVec3)
                                {
                                    originalVec3 = *(Vector3*)valuePtr;
                                    *(Vector3*)valuePtr = new Vector3(entry.Vec3X, entry.Vec3Y, entry.Vec3Z);
                                    _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {contextStr} Op {opType} Prop {propertyId} (Vec3) OVERRIDE: {originalVec3} -> {*(Vector3*)valuePtr} (Occ {propOccurrence})", _logger.ColorYellow);
                                }
                                else if (entry.UseFloat)
                                {
                                    originalFloat = *(float*)valuePtr;
                                    *(float*)valuePtr = entry.FloatValue;
                                    _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {contextStr} Op {opType} Prop {propertyId} (Float) OVERRIDE: {originalFloat:F4} -> {entry.FloatValue:F4} (Occ {propOccurrence})", _logger.ColorYellow);
                                }
                                else
                                {
                                    originalInt = *(int*)valuePtr;
                                    *(int*)valuePtr = entry.IntValue;
                                    _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {contextStr} Op {opType} Prop {propertyId} (Int) OVERRIDE: {originalInt} -> {entry.IntValue} (Occ {propOccurrence})", _logger.ColorYellow);
                                }
                                break; // Only apply one override per property call
                            }
                        }
                    }
                }
            }
        }

        // LOGGING
        // Highlight known properties
        string propName = "";
        if (_propertyNames.TryGetValue(propertyId, out string? name))
        {
            propName = $" ({name})";
        }

        if (activeEntry != null && activeEntry.UseVec3)
        {
            Vector3 v = *(Vector3*)valuePtr;
            _logger.WriteLine($"[{_modConfig.ModId}] [PROP_LOG] {contextStr} Op {opType} Prop {propertyId}{propName}: Vec3=({v.X:F4}, {v.Y:F4}, {v.Z:F4})", _logger.ColorBlue);
        }
        else
        {
            float fVal = *(float*)valuePtr;
            int iVal = *(int*)valuePtr;
            _logger.WriteLine($"[{_modConfig.ModId}] [PROP_LOG] {contextStr} Op {opType} Prop {propertyId}{propName}: float={fVal:F4}, int={iVal} (0x{iVal:X})", _logger.ColorBlue);
        }

        // Execute original function with the (potentially fuzzed) value
        _magicUnkExecuteHook!.OriginalFunction(magicFileInstance, opType, propertyId, dataPtr);

        // RESTORE original value so we don't corrupt the game's memory permanently
        if (isFuzzed && activeEntry != null)
        {
            if (activeEntry.UseVec3)
                *(Vector3*)valuePtr = originalVec3;
            else if (activeEntry.UseFloat)
                *(float*)valuePtr = originalFloat;
            else
                *(int*)valuePtr = originalInt;
        }
    }

    // ============================================================
    // STATE MANAGEMENT
    // ============================================================
    
    /// <summary>
    /// Reset all cached context. Call on level load.
    /// </summary>
    public void Reset()
    {
        _hasMagicContext = false;
        _castMagic_a1 = 0;
        _setupMagic_casterActorRef = 0;
        _setupMagic_positionStruct = 0;
        _setupMagic_commandId = 0;
        _setupMagic_actionID = 0;
        _setupMagic_flag = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Reset", _logger.ColorYellow);
    }
    
    /// <summary>
    /// Update configuration reference.
    /// </summary>
    public void UpdateConfiguration(Config configuration)
    {
        _configuration = configuration;
    }
    
    /// <summary>
    /// Clean up allocated buffers.
    /// </summary>
    public void Dispose()
    {
        if (_magicStructBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_magicStructBuffer);
        _magicStructBuffer = IntPtr.Zero;
    }
}
