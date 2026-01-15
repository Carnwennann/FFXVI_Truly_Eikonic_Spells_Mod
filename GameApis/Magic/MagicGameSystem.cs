using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.GameStructs;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

/// <summary>
/// Core system for magic spell casting via game hooks.
/// Handles SetupMagic/CastMagic/FireMagicProjectile hooks and context caching.
/// Delegates file processing to MagicProcessor.
/// </summary>
internal unsafe class MagicGameSystem
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
    // CONSTANTS
    // ============================================================

    private const string SETUP_MAGIC_SIG = "48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8";
    private const string CAST_MAGIC_SIG = "48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D";
    private const string INSERT_NEW_MAGIC_SIG = "40 53 48 83 EC 20 48 8B DA 4C 8B D9 8B 92 EC 00";
    private const string FIRE_MAGIC_PROJECTILE_SIG = "48 89 5C 24 10 48 89 74 24 18 48 89 7C 24 20 55 41 54 41 55 41 56 41 57 48 8d 6C 24 90 48 81 EC 70 01 00 00 48 8B 05 2D 0F 1A 01 48 33 C4 48 89 45 60 48 8B 51 38 4C 8B E1 44 8B 42 10 41 83 E8 01 0F 84 B6 01 00 00";
    
    // Default values for CastMagicSpell
    private const int DEFAULT_COMMAND_ID = 101;
    private const int DEFAULT_ACTION_ID = 218;
    private const byte DEFAULT_FLAG = 1;
    private const int MAGIC_STRUCT_SIZE = 0x108;
    
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
    private long _setupMagic_casterActorRef = 0;
    private long _setupMagic_positionStruct = 0;
    private int _setupMagic_commandId = 0;
    private int _setupMagic_actionID = 0;
    private byte _setupMagic_flag = 0;
    private long _castMagic_a1 = 0;
    private bool _hasMagicContext = false;
    private bool _hasReportedContextFailure = false;
    private int _currentlyCastingMagicId = 0;
    
    // ============================================================
    // DEPENDENCIES & COMPONENTS
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private readonly MagicProcessor _processor;
    private readonly long _baseAddress;
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    public bool HasMagicContext => _hasMagicContext || (*(long*)(_baseAddress + GlobalOffsets.BattleMagicExecutor) != 0);

    // External callbacks
    public Func<int>? GetActiveEikon { get; set; }
    public Func<nint>? GetPlayerStaticActorInfo { get; set; }
    public Func<long>? GetPlayerActorReference { get; set; }
    public Func<int, long, long, bool>? OnChargedShotDetected { get; set; }
    
    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public MagicGameSystem(ILogger logger, IModConfig modConfig, Config configuration, IStartupScanner scanner, FunctionApi functionApi)
    {
        _logger = logger;
        _modConfig = modConfig;
        _baseAddress = System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress;
        
        // Create processor component
        _processor = new MagicProcessor(logger, modConfig, configuration, scanner);

        // Allocate and zero-initialize buffer
        _magicStructBuffer = Marshal.AllocHGlobal(MAGIC_STRUCT_SIZE);
        for (int i = 0; i < MAGIC_STRUCT_SIZE; i++) 
            *((byte*)_magicStructBuffer + i) = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Initialized", _logger.ColorGreen);
    }
    
    // ============================================================
    // INITIALIZATION
    // ============================================================
    
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        scans.AddScan(SETUP_MAGIC_SIG, address =>
        {
            _setupMagicHook = hooks.CreateHook<SetupMagicDelegate>(SetupMagicImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked SetupMagic at 0x{address:X}", _logger.ColorGreen);
        });
        
        // CastMagic - Actually spawns the spell
        scans.AddScan(CAST_MAGIC_SIG, address =>
        {
            _castMagicHook = hooks.CreateHook<CastMagicDelegate>(CastMagicImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked CastMagic at 0x{address:X}", _logger.ColorGreen);
        });

        // InsertNewMagic - Direct call for magic spawning without context
        scans.AddScan(INSERT_NEW_MAGIC_SIG, address =>
        {
            _castMagicWrapper = hooks.CreateWrapper<CastMagicDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Resolved InsertNewMagic at 0x{address:X}", _logger.ColorGreen);
        });

        // FireMagicProjectile - For detecting and suppressing charged shots
        scans.AddScan(FIRE_MAGIC_PROJECTILE_SIG, address =>
        {
            _fireMagicProjectileHook = hooks.CreateHook<FireMagicProjectileDelegate>(FireMagicProjectileImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Hooked FireMagicProjectile at 0x{address:X}", _logger.ColorGreen);
        });
    }
    
    public void InitializeUniversalMagicHooks(IReloadedHooks hooks)
    {
        _processor.SetupScans(hooks);
    }

    // ============================================================
    // PUBLIC API
    // ============================================================
    
    public void EnqueueModifications(int magicId, List<FuzzerEntry> entries)
    {
        _processor.EnqueueModifications(magicId, entries);
    }

    public bool CastMagicSpell(int magicId)
    {
        if (!_hasMagicContext)
        {
            if (!_hasReportedContextFailure)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Magic Context not yet captured. Fire a magic spell once to sync.", _logger.ColorYellow);
                _hasReportedContextFailure = true;
            }
            return false;
        }

        if (_magicStructBuffer == IntPtr.Zero || _setupMagic_casterActorRef == 0 || _setupMagic_positionStruct == 0)
            return false;
        
        _currentlyCastingMagicId = magicId;
        try
        {
            _setupMagicHook!.OriginalFunction(
                (long)_magicStructBuffer, 
                magicId, 
                _setupMagic_casterActorRef, 
                _setupMagic_positionStruct, 
                _setupMagic_commandId != 0 ? _setupMagic_commandId : DEFAULT_COMMAND_ID, 
                _setupMagic_actionID != 0 ? _setupMagic_actionID : DEFAULT_ACTION_ID, 
                _setupMagic_flag != 0 ? _setupMagic_flag : DEFAULT_FLAG
            );
            
            long executorClient = *(long*)(_baseAddress + GlobalOffsets.BattleMagicExecutor);
            if (executorClient == 0) executorClient = _castMagic_a1;
            
            if (executorClient != 0)
            {
                _castMagicWrapper!((long)executorClient, (long)_magicStructBuffer);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Error: {ex.Message}", _logger.ColorRed);
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
        _setupMagic_casterActorRef = casterActorRef;
        _setupMagic_positionStruct = positionStruct;
        _setupMagic_commandId = commandId;
        _setupMagic_actionID = actionID;
        _setupMagic_flag = flag;
        
        // Call original to fill the struct
        return _setupMagicHook!.OriginalFunction(battleMagicPtr, magicId, casterActorRef, positionStruct, commandId, actionID, flag);
    }
    
    private char CastMagicImpl(long a1, long unkMagicStructPtr)
    {
        if (!_hasMagicContext)
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Captured Magic Context (a1=0x{a1:X})", _logger.ColorGreen);
        
        _castMagic_a1 = a1;
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
                    _logger.WriteLine($"[{_modConfig.ModId}] [MagicGameSystem] Suppressing Charged Shot for Eikon {activeEikon}", _logger.ColorYellow);
                    return (char)0;
                }
            }
        }

        return _fireMagicProjectileHook!.OriginalFunction(magicManagerPtr, projectileDataPtr);
    }

    // ============================================================
    // STATE MANAGEMENT
    // ============================================================
    
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
    
    public void UpdateConfiguration(Config configuration)
    {
        _processor.UpdateConfiguration(configuration);
    }
    
    public void Dispose()
    {
        if (_magicStructBuffer != IntPtr.Zero) 
            Marshal.FreeHGlobal(_magicStructBuffer);
        _magicStructBuffer = IntPtr.Zero;
    }
}
