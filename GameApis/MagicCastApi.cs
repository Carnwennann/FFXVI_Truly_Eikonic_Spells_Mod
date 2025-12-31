using System.Runtime.InteropServices;
using System.Numerics;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// Handles magic spell casting and projectile spawning.
/// 
/// The magic system works with two approaches:
/// 
/// 1. SetupMagic + CastMagic flow:
///    - SetupMagic prepares the spell struct with magicId
///    - CastMagic actually spawns the spell using the prepared struct
///    - Used for: general magic spells by ID
/// 
/// 2. FireMagicProjectile flow:
///    - Takes a MagicManager pointer + ProjectileData
///    - MagicManager+0x38 points to MagicInputConfig
///    - MagicInputConfig+0x10 = Shot Type (1=Normal, 2=Charged, 3=Precision, 4=Burst)
///    - Used for: magic projectiles (Dia/Dia-type shots)
/// 
/// Key structures:
/// - MagicManager: Main manager structure (~512 bytes)
///   +0x28 = Timeline pointer
///   +0x38 = MagicInputConfig pointer
/// 
/// - MagicInputConfig: Shot configuration (~256 bytes)
///   +0x10 = Shot Type (1=Normal/Dia, 2=Charged/Diara, 3=Precision Counter, 4=Burst)
///   +0x18 = Burst ID?
///   +0x1C = Charged ID?
/// 
/// - UnkMagicStruct: Magic spell setup structure (264 bytes)
///   +0x00 = ptr1 (pointer to some data)
///   +0x08 = array of 32 longs (256 bytes)
/// </summary>
public unsafe class MagicCastApi
{
    // ============================================================
    // DELEGATES
    // ============================================================
    
    /// <summary>
    /// Sets a property for a magic operation (like Operation 35).
    /// a1: Operation object pointer
    /// a2: Property ID (35=Speed, 38=Homing)
    /// a3: Pointer to property data struct (a3+8 is pointer to value)
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
    /// Fires a magic projectile (Dia/Diara shots).
    /// Offset: 0x56E0F0 (hardcoded, signature scan was matching wrong function)
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long FireMagicProjectileDelegate(long magicManager, long projectileData);
    
    // ============================================================
    // CONSTANTS
    // ============================================================
    
    // Function offsets (relative to base address)
    private const int FIRE_MAGIC_PROJECTILE_OFFSET = 0x56E0F0;
    
    // Signature for Operation_35 Property Setter
    private const string SET_OPERATION_PROPERTY_SIG = "48 8B C4 48 89 58 08 48 89 70 18 57 48 83 EC 40 C5 F8 29 70 E8 48 8B F9 C5 F8 29 78 D8 83 EA 23 0F 84 3F 01 00 00 83 EA 01 0F 84 C5 00 00 00 83 EA 01 0F 84 B1 00 00 00 83 EA 01 0F 84 8B 00 00 00 81 FA 63 05 00 00 0F 85 26 01 00 00 33 DB 89 59 30 49 8B 40 08 8B 08 89 4F 34";
    
    // Universal Magic Signatures
    private const string MAGIC_UNK_EXECUTE_SIG = "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 49 8B F9 41 8B D8 8B F2 41 83 F8 02 75 2F 48 8D 59 10 48 8D B9 10 01 00 00 EB 1B 48 8B 0B 48 85 C9 74 0F 39 71 20 75 0A";
    private const string OPERATION_FACTORY_SIG = "48 89 5C 24 08 57 48 83 EC 20 49 8B F8 81 FA B7 00 00 00 75 65 48 8B 01 4C 8D 4C 24 48 33 DB 48 89 5C 24 48 8D 53 48 44 8D 43 08 FF 50 30 48 8B D0 48 85 C0 74 6A 48 8B 0F 48 8D 05 04 96 E8 00 48 89 02 44 8D 43 01 41 8B C0 87 42 0C 83 4A 20 FF";
    
    // Signatures from 010 Template
    private const string MAGIC_FILE_PROCESS_SIG = "48 8B C4 48 89 58 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D 68 ?? 48 81 EC ?? ?? ?? ?? C5 F8 29 70 ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 45 33 F6 48 89 55";
    private const string MAGIC_FILE_HANDLE_SUB_ENTRY_SIG = "40 55 53 56 57 41 54 41 56 41 57 48 8B EC 48 83 EC ?? 48 8D 59";

    // Shot types for MagicInputConfig+0x10
    public const int SHOT_TYPE_NORMAL = 1;    // Normal shot (Dia)
    public const int SHOT_TYPE_CHARGED = 2;   // Charged shot (Diara)
    public const int SHOT_TYPE_PRECISION = 3; // Precision Counter
    public const int SHOT_TYPE_BURST = 4;     // Magic Burst
    
    // Buffer sizes
    private const int MAGIC_STRUCT_SIZE = 0x108;      // 264 bytes (8 + 32*8)
    private const int MAGIC_MANAGER_SIZE = 0x200;     // 512 bytes
    private const int MAGIC_INPUT_CONFIG_SIZE = 0x100; // 256 bytes
    private const int PROJECTILE_DATA_SIZE = 0x500;   // 1280 bytes
    
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
    private FireMagicProjectileDelegate? _fireMagicProjectileWrapper;
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

    // Temporary overrides for specific casts
    public List<FuzzerEntry>? TemporaryFuzzerEntries { get; set; }

    // Tracker for operation occurrences within a single MagicFileProcess call
    private Dictionary<int, int> _opInstanceTracker = new();
    private Dictionary<long, int> _propInstanceTracker = new(); // Key: (opType << 32) | propId
    private int _lastOpType = -1;
    private List<FuzzerEntry> _pendingInjections = new();
    private bool _isProcessingInjections = false;
    
    // FireMagicProjectile system cache (deep copy)
    private IntPtr _magicManagerCopy = IntPtr.Zero;
    private IntPtr _magicInputConfigCopy = IntPtr.Zero;
    private IntPtr _projectileDataBuffer = IntPtr.Zero;
    private long _cachedMagicManager = 0;  // Original pointer for debugging
    private long _cachedValidVTable = 0;
    private bool _hasCachedProjectileContext = false;
    
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private readonly IStartupScanner _scanner;
    private Config _configuration;
    
    // ============================================================
    // CALLBACKS
    // ============================================================
    
    /// <summary>
    /// Called when a charged shot (Diara) is detected and should be suppressed.
    /// Parameters: (activeEikon, magicManager, projectileData)
    /// Return true to suppress the shot, false to let it through.
    /// </summary>
    public Func<int, long, long, bool>? OnChargedShotDetected { get; set; }
    
    /// <summary>
    /// Called when a normal shot (Dia) is successfully fired.
    /// Can be used to cache context for later use.
    /// </summary>
    public Action<long, long>? OnNormalShotFired { get; set; }
    
    /// <summary>
    /// Called to get the current active Eikon.
    /// </summary>
    public Func<int>? GetActiveEikon { get; set; }
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    /// <summary>
    /// True if we have valid cached SetupMagic/CastMagic context.
    /// </summary>
    public bool HasMagicContext => _hasMagicContext;
    
    /// <summary>
    /// True if we have valid cached FireMagicProjectile context (deep copy).
    /// </summary>
    public bool HasProjectileContext => _hasCachedProjectileContext;
    
    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public MagicCastApi(ILogger logger, IModConfig modConfig, Config configuration, IStartupScanner scanner)
    {
        _logger = logger;
        _modConfig = modConfig;
        _configuration = configuration;
        _scanner = scanner;
        
        // Allocate buffers
        _magicStructBuffer = Marshal.AllocHGlobal(MAGIC_STRUCT_SIZE);
        _magicManagerCopy = Marshal.AllocHGlobal(MAGIC_MANAGER_SIZE);
        _magicInputConfigCopy = Marshal.AllocHGlobal(MAGIC_INPUT_CONFIG_SIZE);
        _projectileDataBuffer = Marshal.AllocHGlobal(PROJECTILE_DATA_SIZE);
        
        // Zero-initialize
        for (int i = 0; i < MAGIC_STRUCT_SIZE; i++) *((byte*)_magicStructBuffer + i) = 0;
        for (int i = 0; i < MAGIC_MANAGER_SIZE; i++) *((byte*)_magicManagerCopy + i) = 0;
        for (int i = 0; i < MAGIC_INPUT_CONFIG_SIZE; i++) *((byte*)_magicInputConfigCopy + i) = 0;
        for (int i = 0; i < PROJECTILE_DATA_SIZE; i++) *((byte*)_projectileDataBuffer + i) = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Allocated buffers: MagicStruct=0x{(long)_magicStructBuffer:X}, MagicManager=0x{(long)_magicManagerCopy:X}, Config=0x{(long)_magicInputConfigCopy:X}, ProjData=0x{(long)_projectileDataBuffer:X}", _logger.ColorGreen);
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
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Hooked SetupMagic at 0x{address:X}", _logger.ColorGreen);
        });
        
        // CastMagic - Actually spawns the spell
        scans.AddScan("48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D", address =>
        {
            _castMagicHook = hooks.CreateHook<CastMagicDelegate>(CastMagicImpl, address).Activate();
            _castMagicWrapper = hooks.CreateWrapper<CastMagicDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Hooked CastMagic at 0x{address:X}", _logger.ColorGreen);
        });
    }
    
    /// <summary>
    /// Initialize FireMagicProjectile hook using hardcoded offset.
    /// Call from main mod after SetupScans.
    /// </summary>
    public void InitializeFireMagicProjectile(IReloadedHooks hooks, long baseAddress)
    {
        var fireMagicAddr = baseAddress + FIRE_MAGIC_PROJECTILE_OFFSET;
        _fireMagicProjectileHook = hooks.CreateHook<FireMagicProjectileDelegate>(FireMagicProjectileImpl, fireMagicAddr).Activate();
        _fireMagicProjectileWrapper = hooks.CreateWrapper<FireMagicProjectileDelegate>(fireMagicAddr, out _);
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Hooked FireMagicProjectile at 0x{fireMagicAddr:X}", _logger.ColorGreen);
    }

    /// <summary>
    /// Initialize Universal Magic hooks using signature scans.
    /// </summary>
    public void InitializeUniversalMagicHooks(IReloadedHooks hooks)
    {
        // MagicUnkExecute - The Universal Property Logger/Fuzzer
        _scanner.AddScan(MAGIC_UNK_EXECUTE_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Hooked MagicUnkExecute at 0x{address:X}", _logger.ColorGreen);
            _magicUnkExecuteHook = hooks.CreateHook<MagicUnkExecuteDelegate>(MagicUnkExecuteImpl, address).Activate();
            _magicUnkExecuteWrapper = hooks.CreateWrapper<MagicUnkExecuteDelegate>(address, out _);
        });

        // OperationFactory - The VTable Mapper
        _scanner.AddScan(OPERATION_FACTORY_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Hooked OperationFactory at 0x{address:X}", _logger.ColorGreen);
            _operationFactoryHook = hooks.CreateHook<OperationFactoryDelegate>(OperationFactoryImpl, address).Activate();
            _operationFactoryWrapper = hooks.CreateWrapper<OperationFactoryDelegate>(address, out _);
        });

        // MagicFile::ProcessUnk
        _scanner.AddScan(MAGIC_FILE_PROCESS_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Hooked MagicFile::ProcessUnk at 0x{address:X}", _logger.ColorGreen);
            _magicFileProcessHook = hooks.CreateHook<GenericMagicDelegate>(MagicFileProcessImpl, address).Activate();
        });

        // MagicFile::HandleSubEntry
        _scanner.AddScan(MAGIC_FILE_HANDLE_SUB_ENTRY_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Hooked MagicFile::HandleSubEntry at 0x{address:X}", _logger.ColorGreen);
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
    
    private void EnqueueModifications(int magicId, List<FuzzerEntry> entries)
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
        _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] === ENTRY === magicId={magicId}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] State: _hasMagicContext={_hasMagicContext}, _magicStructBuffer=0x{(long)_magicStructBuffer:X}, _castMagic_a1=0x{_castMagic_a1:X}", _logger.ColorYellow);
        
        if (!_hasMagicContext)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: No magic context! Fire a normal shot first.", _logger.ColorRed);
            return false;
        }
        
        if (_magicStructBuffer == IntPtr.Zero || _castMagic_a1 == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: Missing buffers or a1!", _logger.ColorRed);
            return false;
        }
        
        if (_setupMagicHook == null || _castMagicHook == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: Hooks not initialized! SetupMagic={_setupMagicHook != null}, CastMagic={_castMagicHook != null}", _logger.ColorRed);
            return false;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] All checks passed, calling SetupMagic...", _logger.ColorGreen);
        _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Params: casterActorRef=0x{_setupMagic_casterActorRef:X}, positionStruct=0x{_setupMagic_positionStruct:X}, commandId={_setupMagic_commandId}, actionID={_setupMagic_actionID}, flag={_setupMagic_flag}", _logger.ColorYellow);
        
        _currentlyCastingMagicId = magicId;
        try
        {
            // Enqueue modifications for the deferred processing
            if (TemporaryFuzzerEntries != null)
            {
                EnqueueModifications(magicId, TemporaryFuzzerEntries);
            }

            // Call SetupMagic with our cached struct and new magicId
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Calling SetupMagic.OriginalFunction...", _logger.ColorYellow);
            var execResult = _setupMagicHook.OriginalFunction(
                (long)_magicStructBuffer, 
                magicId, 
                _setupMagic_casterActorRef, 
                _setupMagic_positionStruct, 
                _setupMagic_commandId, 
                _setupMagic_actionID, 
                _setupMagic_flag
            );
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] SetupMagic returned: 0x{execResult:X}", _logger.ColorGreen);
            
            // Call CastMagic to actually spawn the spell
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Calling CastMagic.OriginalFunction with a1=0x{_castMagic_a1:X}...", _logger.ColorYellow);
            var castResult = _castMagicHook.OriginalFunction(_castMagic_a1, (long)_magicStructBuffer);
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] CastMagic returned: {(int)castResult}", _logger.ColorGreen);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] === SUCCESS === Magic spell {magicId} cast!", _logger.ColorGreen);
            return true;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] === CRASH === {ex.Message}", _logger.ColorRed);
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Stack: {ex.StackTrace}", _logger.ColorRed);
            return false;
        }
        finally
        {
            _currentlyCastingMagicId = 0;
        }
    }
    
    /// <summary>
    /// Fire a magic projectile (Dia-type shot) using deep-copied context.
    /// Requires HasProjectileContext to be true (fire a normal shot first to capture context).
    /// </summary>
    /// <param name="shotType">Shot type: 1=Normal/Dia, 2=Charged/Diara, 3=Precision, 4=Burst</param>
    /// <param name="count">Number of projectiles to fire</param>
    public bool FireMagicProjectiles(int shotType, int count = 1)
    {
        if (!_hasCachedProjectileContext)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Cannot fire projectiles - no cached context! Fire a normal shot first.", _logger.ColorRed);
            return false;
        }
        
        if (_magicManagerCopy == IntPtr.Zero || _magicInputConfigCopy == IntPtr.Zero)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Cannot fire projectiles - buffers not allocated!", _logger.ColorRed);
            return false;
        }
        
        if (_fireMagicProjectileHook == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Cannot fire projectiles - hook not initialized!", _logger.ColorRed);
            return false;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Firing {count} projectiles (shotType={shotType})...", _logger.ColorGreen);
        
        try
        {
            // Patch MagicManager+0x38 to point to our copied config
            long* configPtrLocation = (long*)((long)_magicManagerCopy + 0x38);
            *configPtrLocation = (long)_magicInputConfigCopy;
            
            // Set the shot type
            int* shotTypePtr = (int*)((long)_magicInputConfigCopy + 0x10);
            *shotTypePtr = shotType;
            
            // Fire the projectiles
            int successCount = 0;
            
            // Set currently casting ID to help normalization if MAGIC_PROCESS is triggered
            int magicId = (shotType == SHOT_TYPE_NORMAL) ? 214 : 215;
            _currentlyCastingMagicId = magicId;
            
            try
            {
                for (int i = 0; i < count; i++)
                {
                    // Enqueue modifications for each projectile
                    if (TemporaryFuzzerEntries != null)
                    {
                        EnqueueModifications(magicId, TemporaryFuzzerEntries);
                    }

                    long result = _fireMagicProjectileHook.OriginalFunction((long)_magicManagerCopy, 0);
                    if (result != 0) successCount++;
                }
            }
            finally
            {
                _currentlyCastingMagicId = 0;
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Fired {successCount}/{count} projectiles!", 
                successCount == count ? _logger.ColorGreen : _logger.ColorYellow);
            
            return successCount > 0;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] CRASH firing projectiles: {ex.Message}", _logger.ColorRed);
            return false;
        }
    }
    
    /// <summary>
    /// Fire Normal (Dia) projectiles. Shorthand for FireMagicProjectiles(SHOT_TYPE_NORMAL, count).
    /// </summary>
    public bool FireDiaProjectiles(int count = 1) => FireMagicProjectiles(SHOT_TYPE_NORMAL, count);
    
    /// <summary>
    /// Fire Charged (Diara) projectiles. Shorthand for FireMagicProjectiles(SHOT_TYPE_CHARGED, count).
    /// </summary>
    public bool FireDiaraProjectiles(int count = 1) => FireMagicProjectiles(SHOT_TYPE_CHARGED, count);
    
    /// <summary>
    /// Cast Dia spell using SetupMagic/CastMagic system (more stable than FireMagicProjectile).
    /// magicId for Dia needs to be discovered - try common values or check logs.
    /// </summary>
    public bool CastSpells(int magicID = 1, int count = 1)
    {   
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Attempting to cast {count} Dia spells via CastMagicSpell...", _logger.ColorGreen);
        
        int successCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (CastMagicSpell(magicID))
                successCount++;
        }
        
        return successCount > 0;
    }
    
    // ============================================================
    // HOOK IMPLEMENTATIONS
    // ============================================================
    
    private long SetupMagicImpl(long battleMagicPtr, int magicId, long casterActorRef, long positionStruct, int commandId, int actionID, byte flag)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] Called! structPtr=0x{battleMagicPtr:X}, magicId={magicId}", _logger.ColorGreen);
        
        // Dump the BattleMagic struct for reverse engineering
        DumpBattleMagic(battleMagicPtr, "SETUP_MAGIC_INPUT");
        
        // Dump the positionStruct - this likely contains the actual aim direction!
        DumpPositionStruct(positionStruct, "POSITION_STRUCT");
        
        // Apply position overrides BEFORE calling original SetupMagic
        if (_configuration.EnablePositionOverrides && positionStruct != 0)
        {
            ApplyPositionStructOverrides(positionStruct);
        }
        
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
        
        // Cache parameters with descriptive names
        _setupMagic_casterActorRef = casterActorRef;   // ActorReference* of caster (from GetTargetDataMaybe)
        _setupMagic_positionStruct = positionStruct;   // Position from BattleBehaviorEntityEntry::GetPositionStructMaybe
        _setupMagic_commandId = commandId;             // Command ID (101 for Dia)
        _setupMagic_actionID = actionID;               // Action ID (218-219)
        _setupMagic_flag = flag;                       // Flag byte
        
        // Debug: Dump casterActorRef structure to see if it matches ActorReference
        if (casterActorRef != 0)
        {
            try
            {
                // ActorReference structure (from FunctionHooks):
                // +0x00 = vtable
                // +0x08 = ActorId (uint)
                // +0x0C = EntityId (uint)
                long vtable = *(long*)casterActorRef;
                uint actorId = *(uint*)(casterActorRef + 0x08);
                uint entityId = *(uint*)(casterActorRef + 0x0C);
                _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] casterActorRef dump: vtable=0x{vtable:X}, ActorId={actorId}, EntityId={entityId}", _logger.ColorBlue);
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] Failed to read casterActorRef: {ex.Message}", _logger.ColorRed);
            }
        }

        // Apply experimental overrides from config
        _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] Config values: commandId_experiment={_configuration.a5_experiment}, actionID_experiment={_configuration.a6_experiment}", _logger.ColorYellow);

        if (_configuration.a5_experiment != -999)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] Overriding commandId: {commandId} -> {_configuration.a5_experiment}", _logger.ColorYellow);
            commandId = _configuration.a5_experiment;
        }
        if (_configuration.a6_experiment != -999)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] Overriding actionID: {actionID} -> {_configuration.a6_experiment}", _logger.ColorYellow);
            actionID = _configuration.a6_experiment;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] casterActorRef=0x{casterActorRef:X}, positionStruct=0x{positionStruct:X}, commandId={commandId}, actionID={actionID}, flag={flag}", _logger.ColorGreen);
        
        // Call original to fill the struct
        var result = _setupMagicHook!.OriginalFunction(battleMagicPtr, magicId, casterActorRef, positionStruct, commandId, actionID, flag);
        
        // Apply config overrides AFTER the struct is filled (before CastMagic reads it)
        if (_configuration.EnableMagicStructOverrides && battleMagicPtr != 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [SETUP_MAGIC] Applying struct overrides post-fill...", _logger.ColorYellow);
            ApplyMagicStructOverrides(battleMagicPtr);
            
            // Dump again to verify changes
            DumpBattleMagic(battleMagicPtr, "SETUP_MAGIC_AFTER_OVERRIDE");
        }
        
        return result;
    }
    
    private char CastMagicImpl(long a1, long unkMagicStructPtr)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [CAST_MAGIC] Called! a1=0x{a1:X}, structPtr=0x{unkMagicStructPtr:X}", _logger.ColorGreen);
        
        // Dump the filled BattleMagic struct (after SetupMagic has populated it)
        DumpBattleMagic(unkMagicStructPtr, "CAST_MAGIC_FILLED");
        
        // Note: Struct overrides are now applied in SetupMagic after the struct is filled
        // Keeping this here as backup in case SetupMagic isn't always called first
        if (_configuration.EnableMagicStructOverrides && unkMagicStructPtr != 0)
        {
            ApplyMagicStructOverrides(unkMagicStructPtr);
        }
        
        _castMagic_a1 = a1;
        _hasMagicContext = true;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [CAST_MAGIC] === MAGIC CONTEXT READY! ===", _logger.ColorGreen);
        
        return _castMagicHook!.OriginalFunction(a1, unkMagicStructPtr);
    }
    
    private long FireMagicProjectileImpl(long magicManager, long projectileData)
    {
        // Get shot type
        int shotType = 0;
        long ptr38 = *(long*)(magicManager + 0x38);
        if (ptr38 != 0)
        {
            shotType = *(int*)(ptr38 + 0x10);
        }
        
        // Check for Charged Shot suppression
        if (shotType == SHOT_TYPE_CHARGED && OnChargedShotDetected != null)
        {
            int activeEikon = GetActiveEikon?.Invoke() ?? 0;
            if (OnChargedShotDetected(activeEikon, magicManager, projectileData))
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FIRE_MAGIC] Charged shot SUPPRESSED by callback", _logger.ColorYellow);
                return 0; // Suppress
            }
        }
        
        // Call original
        long result = _fireMagicProjectileHook!.OriginalFunction(magicManager, projectileData);
        
        // Cache on successful NORMAL shot
        if (result != 0 && shotType == SHOT_TYPE_NORMAL)
        {
            CacheProjectileContext(magicManager, projectileData);
            OnNormalShotFired?.Invoke(magicManager, projectileData);
        }
        
        return result;
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

    private HashSet<int> _scannedMagicIds = new();

    private void ScanMagicFile(long ptr, int magicId)
    {
        // Disable scanning by default now that we have the offsets
        return;
    }

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

    private void CacheProjectileContext(long magicManager, long projectileData)
    {
        try
        {
            _cachedMagicManager = magicManager;
            
            // Deep copy MagicManager
            Buffer.MemoryCopy((void*)magicManager, (void*)_magicManagerCopy, MAGIC_MANAGER_SIZE, MAGIC_MANAGER_SIZE);
            
            // Deep copy MagicInputConfig
            long configPtr = *(long*)(magicManager + 0x38);
            if (configPtr != 0)
            {
                Buffer.MemoryCopy((void*)configPtr, (void*)_magicInputConfigCopy, MAGIC_INPUT_CONFIG_SIZE, MAGIC_INPUT_CONFIG_SIZE);
            }
            
            // Copy projectile data
            if (projectileData != 0)
            {
                Buffer.MemoryCopy((void*)projectileData, (void*)_projectileDataBuffer, PROJECTILE_DATA_SIZE, PROJECTILE_DATA_SIZE);
            }
            
            // Cache VTable
            long timelinePtr = *(long*)(magicManager + 0x28);
            if (timelinePtr != 0)
            {
                _cachedValidVTable = *(long*)timelinePtr;
            }
            
            _hasCachedProjectileContext = true;
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Context cached from 0x{magicManager:X}", _logger.ColorBlue);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Cache failed: {ex.Message}", _logger.ColorRed);
        }
    }
    
    /// <summary>
    /// Apply configuration overrides to the BattleMagic struct before casting.
    /// </summary>
    private void ApplyMagicStructOverrides(long ptr)
    {
        try
        {
            bool anyApplied = false;
            
            // Override Timing (+0x18)
            if (_configuration.MagicTimingOverride > -998.0f)
            {
                float* timingPtr = (float*)(ptr + 0x18);
                float oldValue = *timingPtr;
                *timingPtr = _configuration.MagicTimingOverride;
                _logger.WriteLine($"[{_modConfig.ModId}] [STRUCT_OVERRIDE] +0x18 Timing: {oldValue:F4} -> {_configuration.MagicTimingOverride:F4}", _logger.ColorYellow);
                anyApplied = true;
            }
            
            // Override Scale (+0x90)
            if (_configuration.MagicScaleOverride > -998.0f)
            {
                float* scalePtr = (float*)(ptr + 0x90);
                float oldValue = *scalePtr;
                *scalePtr = _configuration.MagicScaleOverride;
                _logger.WriteLine($"[{_modConfig.ModId}] [STRUCT_OVERRIDE] +0x90 Scale: {oldValue:F4} -> {_configuration.MagicScaleOverride:F4}", _logger.ColorYellow);
                anyApplied = true;
            }
            
            // Override AimQuat (+0xD8) - Convert angle to unit quaternion (Y, W)
            // Quaternion for Y-axis rotation: Y = sin(angle/2), W = cos(angle/2)
            if (_configuration.MagicAimAngleOverride > -998.0f)
            {
                float* xPtr = (float*)(ptr + 0xD8);
                float* yPtr = (float*)(ptr + 0xDC);
                float* zPtr = (float*)(ptr + 0xE0);
                float* wPtr = (float*)(ptr + 0xE4);
                
                float oldY = *yPtr;
                float oldW = *wPtr;
                
                // Convert degrees to radians and calculate quaternion components
                float angleRad = _configuration.MagicAimAngleOverride * (float)Math.PI / 180.0f;
                float halfAngle = angleRad / 2.0f;
                float newY = (float)Math.Sin(halfAngle);
                float newW = (float)Math.Cos(halfAngle);
                
                // Set X and Z to 0 (horizontal rotation only), Y and W from quaternion
                *xPtr = 0.0f;
                *yPtr = newY;
                *zPtr = 0.0f;
                *wPtr = newW;
                
                // Verify normalization
                float magnitude = (float)Math.Sqrt(newY * newY + newW * newW);
                
                _logger.WriteLine($"[{_modConfig.ModId}] [STRUCT_OVERRIDE] +0xD8 AimQuat: (Y={oldY:F4}, W={oldW:F4}) -> (Y={newY:F4}, W={newW:F4}) [angle={_configuration.MagicAimAngleOverride:F1}°, |YW|={magnitude:F4}]", _logger.ColorYellow);
                anyApplied = true;
            }
            
            if (anyApplied)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [STRUCT_OVERRIDE] === Overrides applied! ===", _logger.ColorGreen);
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [STRUCT_OVERRIDE] FAILED: {ex.Message}", _logger.ColorRed);
        }
    }
    
    /// <summary>
    /// Apply configuration overrides to the PositionStruct before SetupMagic.
    /// Based on reverse engineering: +0x30/34/38 = Position (X, Y, Z)
    /// </summary>
    private void ApplyPositionStructOverrides(long ptr)
    {
        try
        {
            bool anyApplied = false;
            
            // Override Position X (+0x30)
            if (_configuration.PositionXOffset > -998.0f)
            {
                float* xPtr = (float*)(ptr + 0x30);
                float oldValue = *xPtr;
                *xPtr = oldValue + _configuration.PositionXOffset;
                _logger.WriteLine($"[{_modConfig.ModId}] [POS_OVERRIDE] +0x30 X: {oldValue:F4} + {_configuration.PositionXOffset:F4} = {*xPtr:F4}", _logger.ColorYellow);
                anyApplied = true;
            }
            
            // Override Position Y (+0x34) - height
            if (_configuration.PositionYOffset > -998.0f)
            {
                float* yPtr = (float*)(ptr + 0x34);
                float oldValue = *yPtr;
                *yPtr = oldValue + _configuration.PositionYOffset;
                _logger.WriteLine($"[{_modConfig.ModId}] [POS_OVERRIDE] +0x34 Y: {oldValue:F4} + {_configuration.PositionYOffset:F4} = {*yPtr:F4}", _logger.ColorYellow);
                anyApplied = true;
            }
            
            // Override Position Z (+0x38)
            if (_configuration.PositionZOffset > -998.0f)
            {
                float* zPtr = (float*)(ptr + 0x38);
                float oldValue = *zPtr;
                *zPtr = oldValue + _configuration.PositionZOffset;
                _logger.WriteLine($"[{_modConfig.ModId}] [POS_OVERRIDE] +0x38 Z: {oldValue:F4} + {_configuration.PositionZOffset:F4} = {*zPtr:F4}", _logger.ColorYellow);
                anyApplied = true;
            }
            
            if (anyApplied)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [POS_OVERRIDE] === Position overrides applied! ===", _logger.ColorGreen);
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [POS_OVERRIDE] FAILED: {ex.Message}", _logger.ColorRed);
        }
    }
    
    // ============================================================
    // DEBUG HELPERS
    // ============================================================
    
    /// <summary>
    /// Dumps the BattleMagic struct contents for reverse engineering.
    /// Struct size: 0x108 (264 bytes)
    /// </summary>
    private void DumpBattleMagic(long ptr, string label = "BattleMagic")
    {
        if (ptr == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] NULL pointer", _logger.ColorRed);
            return;
        }
        
        try
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] === DUMP START (0x{ptr:X}) ===", _logger.ColorYellow);
            
            // Core fields - interpret +0x18 as float (timing/delay?)
            long vtable = *(long*)ptr;
            long qword8 = *(long*)(ptr + 0x08);
            double double10 = *(double*)(ptr + 0x10);
            float float18 = *(float*)(ptr + 0x18);  // Looks like a float ~0.4
            int dword1C = *(int*)(ptr + 0x1C);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x00 vtable:   0x{vtable:X}", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x08 qword8:   0x{qword8:X}", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x10 double10: {double10}", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x18 Timing?:  {float18:F4}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x1C dword1C:  {dword1C} (0x{dword1C:X})", _logger.ColorBlue);
            
            // BattleMagicSub0x20 (qword20 sub-structure)
            long qword20_ptr = ptr + 0x20;
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x20 BattleMagicSub: at 0x{qword20_ptr:X}", _logger.ColorBlue);
            
            // gap78
            long gap78 = *(long*)(ptr + 0x78);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x78 gap78:    0x{gap78:X}", _logger.ColorBlue);
            
            // Fields 0x80-0x9C - interpret 0x90 as float (scale/speed multiplier)
            for (int i = 0; i < 8; i++)
            {
                int offset = 0x80 + i * 4;
                int value = *(int*)(ptr + offset);
                
                // Special handling for 0x90 - interpret as float
                if (offset == 0x90)
                {
                    float floatValue = *(float*)(ptr + offset);
                    _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x{offset:X2} Scale?:    {floatValue:F4} (raw: 0x{value:X})", _logger.ColorGreen);
                }
                else
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x{offset:X2} field_{offset:X2}: {value} (0x{value:X})", _logger.ColorBlue);
                }
            }
            
            // Reference fields
            long fieldA0 = *(long*)(ptr + 0xA0);
            long fieldA8 = *(long*)(ptr + 0xA8);
            long fieldB0 = *(long*)(ptr + 0xB0);
            long fieldB8 = *(long*)(ptr + 0xB8);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xA0 Field_a0: 0x{fieldA0:X}", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xA8 field_A8: 0x{fieldA8:X}", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xB0 field_B0: 0x{fieldB0:X}", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xB8 field_B8: 0x{fieldB8:X}", _logger.ColorBlue);
            
            // Short/byte fields
            short fieldC0 = *(short*)(ptr + 0xC0);
            byte fieldC2 = *(byte*)(ptr + 0xC2);
            int fieldC4 = *(int*)(ptr + 0xC4);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xC0 field_C0: {fieldC0}", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xC2 field_C2: {fieldC2}", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xC4 field_C4: {fieldC4} (0x{fieldC4:X})", _logger.ColorBlue);
            
            // Important pointers
            long magicFileResource = *(long*)(ptr + 0xC8);
            long qwordD0 = *(long*)(ptr + 0xD0);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xC8 MagicFileResource: 0x{magicFileResource:X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xD0 qwordD0:  0x{qwordD0:X}", _logger.ColorBlue);
            
            // Decode gapD8 as aim direction (quaternion Y,W components - horizontal rotation only)
            // Format: (0, Y, 0, W) where Y² + W² = 1.0 (unit quaternion)
            float dir0 = *(float*)(ptr + 0xD8);  // Always 0 (X)
            float dir1 = *(float*)(ptr + 0xDC);  // Y component
            float dir2 = *(float*)(ptr + 0xE0);  // Always 0 (Z)
            float dir3 = *(float*)(ptr + 0xE4);  // W component
            float magnitude = (float)Math.Sqrt(dir1 * dir1 + dir3 * dir3);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xD8 AimQuat: (X={dir0:F2}, Y={dir1:F4}, Z={dir2:F2}, W={dir3:F4}) |YW|={magnitude:F4}", _logger.ColorGreen);
            
            // Key entity/magic fields - NOW WITH CORRECT NAMES
            uint target = *(uint*)(ptr + 0xE8);
            int unkEntityId = *(int*)(ptr + 0xEC);
            int commandId = *(int*)(ptr + 0xF0);  // 101=Dia, 102=Diara, 307=Impulse
            int actionId = *(int*)(ptr + 0xF4);   // 219=Dia, 227=Diara, 824=Impulse
            int magicId = *(int*)(ptr + 0xF8);    // 214=Dia, 1570=Diara, 818=Impulse
            int fieldFC = *(int*)(ptr + 0xFC);
            int field100 = *(int*)(ptr + 0x100);
            int field104 = *(int*)(ptr + 0x104);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xE8 Target:      {target} (0x{target:X})", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xEC UnkEntityId: {unkEntityId} (0x{unkEntityId:X})", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xF0 CommandId:   {commandId} (Dia=101, Diara=102, Impulse=307)", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xF4 ActionId:    {actionId} (Dia=219, Diara=227, Impulse=824)", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xF8 MagicId:     {magicId} (Dia=214, Diara=1570, Impulse=818)", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0xFC field_FC:    {fieldFC} (0x{fieldFC:X})", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x100 field_100:  {field100} (0x{field100:X})", _logger.ColorBlue);
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x104 field_104:  {field104} (0x{field104:X})", _logger.ColorBlue);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] === DUMP END ===", _logger.ColorYellow);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] DUMP FAILED: {ex.Message}", _logger.ColorRed);
        }
    }
    
    /// <summary>
    /// Dumps the PositionStruct passed to SetupMagic.
    /// This likely contains the player position and aim direction.
    /// Structure is unknown - dumping first 256 bytes as floats to identify patterns.
    /// </summary>
    private void DumpPositionStruct(long ptr, string label = "PositionStruct")
    {
        if (ptr == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] NULL pointer", _logger.ColorRed);
            return;
        }
        
        try
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] === DUMP START (0x{ptr:X}) ===", _logger.ColorYellow);
            
            // Dump as floats - positions/directions are usually floats
            // Look for patterns like (X, Y, Z) positions or quaternions
            for (int i = 0; i < 64; i++)  // 64 floats = 256 bytes
            {
                int offset = i * 4;
                float floatVal = *(float*)(ptr + offset);
                int intVal = *(int*)(ptr + offset);
                
                // Highlight values that look like positions (reasonable world coords) or quaternions (-1 to 1)
                string highlight = "";
                if (floatVal >= -1.0f && floatVal <= 1.0f && floatVal != 0.0f)
                    highlight = " [QUAT?]";
                else if (Math.Abs(floatVal) > 1.0f && Math.Abs(floatVal) < 10000.0f && floatVal != 0.0f)
                    highlight = " [POS?]";
                
                // Only log non-zero or interesting values to reduce spam
                if (floatVal != 0.0f || intVal != 0)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [{label}] +0x{offset:X2}: float={floatVal:F4}, int={intVal} (0x{intVal:X}){highlight}", _logger.ColorBlue);
                }
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] === DUMP END ===", _logger.ColorYellow);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [{label}] DUMP FAILED: {ex.Message}", _logger.ColorRed);
        }
    }
    
    /// <summary>
    /// Helper to dump bytes as hex string.
    /// </summary>
    private string DumpBytes(long ptr, int count)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
        {
            byte b = *(byte*)(ptr + i);
            sb.Append($"{b:X2} ");
        }
        return sb.ToString().TrimEnd();
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
        _hasCachedProjectileContext = false;
        _castMagic_a1 = 0;
        _cachedMagicManager = 0;
        _cachedValidVTable = 0;
        _setupMagic_casterActorRef = 0;
        _setupMagic_positionStruct = 0;
        _setupMagic_commandId = 0;
        _setupMagic_actionID = 0;
        _setupMagic_flag = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Reset", _logger.ColorYellow);
    }
    
    /// <summary>
    /// Update configuration reference.
    /// </summary>
    public void UpdateConfiguration(Config configuration)
    {
        _configuration = configuration;
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastApi] Configuration updated! a5_experiment={_configuration.a5_experiment}, actionID_experiment={_configuration.a6_experiment}", _logger.ColorGreen);
    }
    
    /// <summary>
    /// Clean up allocated buffers.
    /// </summary>
    public void Dispose()
    {
        if (_magicStructBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_magicStructBuffer);
        if (_magicManagerCopy != IntPtr.Zero) Marshal.FreeHGlobal(_magicManagerCopy);
        if (_magicInputConfigCopy != IntPtr.Zero) Marshal.FreeHGlobal(_magicInputConfigCopy);
        if (_projectileDataBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_projectileDataBuffer);
        
        _magicStructBuffer = IntPtr.Zero;
        _magicManagerCopy = IntPtr.Zero;
        _magicInputConfigCopy = IntPtr.Zero;
        _projectileDataBuffer = IntPtr.Zero;
    }
}
