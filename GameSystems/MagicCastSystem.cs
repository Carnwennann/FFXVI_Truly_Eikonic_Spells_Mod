using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells;

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
///    - Used for: magic projectiles (Dia/Diara type shots)
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
public unsafe class MagicCastSystem
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
    
    private SetupMagicDelegate? _setupMagicWrapper;
    private CastMagicDelegate? _castMagicWrapper;
    private FireMagicProjectileDelegate? _fireMagicProjectileWrapper;
    private MagicUnkExecuteDelegate? _magicUnkExecuteWrapper;
    private OperationFactoryDelegate? _operationFactoryWrapper;
    
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
    
    public MagicCastSystem(ILogger logger, IModConfig modConfig, Config configuration, IStartupScanner scanner)
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
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Allocated buffers: MagicStruct=0x{(long)_magicStructBuffer:X}, MagicManager=0x{(long)_magicManagerCopy:X}, Config=0x{(long)_magicInputConfigCopy:X}, ProjData=0x{(long)_projectileDataBuffer:X}", _logger.ColorGreen);
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
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Hooked SetupMagic at 0x{address:X}", _logger.ColorGreen);
        });
        
        // CastMagic - Actually spawns the spell
        scans.AddScan("48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D", address =>
        {
            _castMagicHook = hooks.CreateHook<CastMagicDelegate>(CastMagicImpl, address).Activate();
            _castMagicWrapper = hooks.CreateWrapper<CastMagicDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Hooked CastMagic at 0x{address:X}", _logger.ColorGreen);
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
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Hooked FireMagicProjectile at 0x{fireMagicAddr:X}", _logger.ColorGreen);
    }

    /// <summary>
    /// Initialize Universal Magic hooks using signature scans.
    /// </summary>
    public void InitializeUniversalMagicHooks(IReloadedHooks hooks)
    {
        // MagicUnkExecute - The Universal Property Logger/Fuzzer
        _scanner.AddScan(MAGIC_UNK_EXECUTE_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Hooked MagicUnkExecute at 0x{address:X}", _logger.ColorGreen);
            _magicUnkExecuteHook = hooks.CreateHook<MagicUnkExecuteDelegate>(MagicUnkExecuteImpl, address).Activate();
            _magicUnkExecuteWrapper = hooks.CreateWrapper<MagicUnkExecuteDelegate>(address, out _);
        });

        // OperationFactory - The VTable Mapper
        _scanner.AddScan(OPERATION_FACTORY_SIG, address =>
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Hooked OperationFactory at 0x{address:X}", _logger.ColorGreen);
            _operationFactoryHook = hooks.CreateHook<OperationFactoryDelegate>(OperationFactoryImpl, address).Activate();
            _operationFactoryWrapper = hooks.CreateWrapper<OperationFactoryDelegate>(address, out _);
        });
    }
    
    // ============================================================
    // PUBLIC API - CAST MAGIC
    // ============================================================
    
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
        
        try
        {
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
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Cannot fire projectiles - no cached context! Fire a normal shot first.", _logger.ColorRed);
            return false;
        }
        
        if (_magicManagerCopy == IntPtr.Zero || _magicInputConfigCopy == IntPtr.Zero)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Cannot fire projectiles - buffers not allocated!", _logger.ColorRed);
            return false;
        }
        
        if (_fireMagicProjectileHook == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Cannot fire projectiles - hook not initialized!", _logger.ColorRed);
            return false;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Firing {count} projectiles (shotType={shotType})...", _logger.ColorGreen);
        
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
            for (int i = 0; i < count; i++)
            {
                long result = _fireMagicProjectileHook.OriginalFunction((long)_magicManagerCopy, 0);
                if (result != 0) successCount++;
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Fired {successCount}/{count} projectiles!", 
                successCount == count ? _logger.ColorGreen : _logger.ColorYellow);
            
            return successCount > 0;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] CRASH firing projectiles: {ex.Message}", _logger.ColorRed);
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
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Attempting to cast {count} Dia spells via CastMagicSpell...", _logger.ColorGreen);
        
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
        { 8, "Speed - float" },
        { 31, "Scale projectile body (default=1.0) - float"},
        { 35, "Duration (s) - float" },
        { 42, "Hitbox/Attachment Size? - float" },
        // 1 = enemy, 2 = ???, 3 = clive, 4 = Clive's feet (yeah really)
        { 73, "Location spawn magic ID? - int"},
        // Collection of effects and sound for eg. 1008 shiva projectile impact
        // But also spawns projectiles on some IDs e.g. 1007
        { 89, "Impact ID? - int"}, 
        { 2593, "Op2593 - Y-axis curve strength - float (positive = up, negative = down)"},
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

    private void MagicUnkExecuteImpl(long magicFileInstance, int opType, int propertyId, long dataPtr)
    {
        // Try to find Magic ID. Offset 272 was giving weird values, let's check 280
        int magicId = *(int*)(magicFileInstance + 280);
        if (magicId <= 0 || magicId > 100000) magicId = *(int*)(magicFileInstance + 272); // Fallback
        
        string contextStr = $"[Magic {magicId}]";

        // dataPtr + 8 is the pointer to the actual value
        long valuePtr = *(long*)(dataPtr + 8);

        // UNIVERSAL FUZZER LOGIC
        bool isFuzzed = false;
        float originalFloat = 0;
        int originalInt = 0;
        FuzzerEntry? activeEntry = null;

        if (_configuration.EnableUniversalFuzzer)
        {
            foreach (var entry in _configuration.FuzzerEntries)
            {
                if (entry.Enabled && entry.PropertyId == propertyId && (entry.OpType == -1 || entry.OpType == opType))
                {
                    activeEntry = entry;
                    isFuzzed = true;
                    if (entry.UseFloat)
                    {
                        originalFloat = *(float*)valuePtr;
                        *(float*)valuePtr = entry.FloatValue;
                        _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {contextStr} Op {opType} Prop {propertyId} (Float) OVERRIDE: {originalFloat:F4} -> {entry.FloatValue:F4}", _logger.ColorYellow);
                    }
                    else
                    {
                        originalInt = *(int*)valuePtr;
                        *(int*)valuePtr = entry.IntValue;
                        _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {contextStr} Op {opType} Prop {propertyId} (Int) OVERRIDE: {originalInt} -> {entry.IntValue}", _logger.ColorYellow);
                    }
                    break; // Only apply one override per property call
                }
            }
        }

        // LOGGING
        float fVal = *(float*)valuePtr;
        int iVal = *(int*)valuePtr;
        
        // Highlight known properties
        string propName = "";
        if (_propertyNames.TryGetValue(propertyId, out string? name))
        {
            propName = $" ({name})";
        }

        _logger.WriteLine($"[{_modConfig.ModId}] [PROP_LOG] {contextStr} Op {opType} Prop {propertyId}{propName}: float={fVal:F4}, int={iVal} (0x{iVal:X})", _logger.ColorBlue);

        // Execute original function with the (potentially fuzzed) value
        _magicUnkExecuteHook!.OriginalFunction(magicFileInstance, opType, propertyId, dataPtr);

        // RESTORE original value so we don't corrupt the game's memory permanently
        if (isFuzzed && activeEntry != null)
        {
            if (activeEntry.UseFloat)
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
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Context cached from 0x{magicManager:X}", _logger.ColorBlue);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Cache failed: {ex.Message}", _logger.ColorRed);
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
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Reset", _logger.ColorYellow);
    }
    
    /// <summary>
    /// Update configuration reference.
    /// </summary>
    public void UpdateConfiguration(Config configuration)
    {
        _configuration = configuration;
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Configuration updated! a5_experiment={_configuration.a5_experiment}, actionID_experiment={_configuration.a6_experiment}", _logger.ColorGreen);
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
