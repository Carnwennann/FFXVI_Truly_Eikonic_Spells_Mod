using ff16.gameplay.truly_eikonic_spells.Configuration;
using FF16Framework.Interfaces.Nex;
using FF16Framework.Interfaces.Nex.Structures;
using FF16Tools.Files.Nex;
using FF16Tools.Files.Nex.Entities;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ff16.gameplay.truly_eikonic_spells;

public class TrulyEikonicSpellsMod : ModBase
{
    // ============================================================
    // DEBUG FLAGS - Set to false to disable reverse engineering logs
    // ============================================================
    private const bool DEBUG_ON_HIT = true;          // Log OnHit Action ID
    private const bool DEBUG_MAGIC_HIT = false;      // Log detailed magic hit info + R15 dump
    private const bool DEBUG_BATTLE_TECHNIQUE = false; // Log BattleTechnique calls
    private const bool DEBUG_PERFECT_DODGE = true;   // Log perfect dodge events
    private const bool DEBUG_WINGS_DODGE = false;     // Log Wings of Light dodge handler
    private const bool DEBUG_PLAYER_MODE = false;    // Log player mode changes (spammy)
    private const bool DEBUG_COPY_ATTACK_DATA = false; // Log CopyAttackData calls (projectile creation)
    private const bool DEBUG_PREPARE_TEMPLATE = false; // Log PrepareAttackTemplate calls
    private const bool DEBUG_FIRE_MAGIC = true;      // Log FireMagicProjectile calls (KEY function!)
    // ============================================================
    
    private readonly IModLoader _modLoader;
    private readonly IReloadedHooks?  _hooks;
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    
    // Hooks - same patterns as combo meter
    public unsafe delegate long OnHitDelegate(long* bnpcRow, long R15, long a3, long a4);
    private IHook<OnHitDelegate> _onHit;
    
    public delegate long OnLevelLoad(long a1, double a2, double a3, double a4);
    private IHook<OnLevelLoad> _onLevelLoad;
    
    public unsafe delegate char StartPlayerModeDelegate(long a1, uint playerMode, long a3);
    private IHook<StartPlayerModeDelegate> _startPlayerMode;
    
    // Perfect Dodge hook for Diara system
    public unsafe delegate char OnPerfectDodgeDelegate(long a1, long a2, double a3, double a4);
    private IHook<OnPerfectDodgeDelegate> _onPerfectDodge;
    
    // Wings Perfect Dodge handler - handles Wings of Light dodge effects
    public unsafe delegate long MaybeHandleWingsPerfectDodgeDelegate(long a1, long a2);
    private IHook<MaybeHandleWingsPerfectDodgeDelegate> _maybeHandleWingsPerfectDodge;
    
    // BattleTechnique hook - for logging and potentially spawning attacks
    public delegate char OnBattleTechniqueDelegate(long a1, uint techId, char a3);
    private IHook<OnBattleTechniqueDelegate> _onBattleTechnique;
    private long _battleTechniqueA1; // Store the a1 pointer for potential manual invocation
    
    // CopyAttackData hook - copies attack parameters from template to attack struct
    // This is called when creating projectiles/attacks
    public unsafe delegate void CopyAttackDataDelegate(long destAttackStruct, long srcAttackTemplate);
    private IHook<CopyAttackDataDelegate> _copyAttackData;
    private CopyAttackDataDelegate _copyAttackDataWrapper; // For calling manually
    
    // PrepareAttackTemplate hook - prepares the attack template from stack parameters
    // This is called BEFORE CopyAttackData, sets up the template with ActionId etc.
    // Offset: 0x59FB6E from base (7FF77E9AFB6E in user's session)
    // We need to capture a1 (template) and r8 (appears to have ActionId info)
    public unsafe delegate void PrepareAttackTemplateDelegate(long a1, long a2, long a3, long a4);
    private IHook<PrepareAttackTemplateDelegate> _prepareAttackTemplate;
    
    // FireMagicProjectile - THE function that fires magic shots!
    // Only called for magic shots (normal and charged), not melee or abilities
    // Offset: 0x56E0F0 from base
    // RCX = MagicManager pointer, RDX = ProjectileData pointer
    public unsafe delegate long FireMagicProjectileDelegate(long magicManager, long projectileData);
    private IHook<FireMagicProjectileDelegate> _fireMagicProjectile;
    private FireMagicProjectileDelegate _fireMagicProjectileWrapper; // For calling manually
    
    // GetTimeline - Retrieves the Timeline/Animation object
    // Offset: 0x4692A4
    public unsafe delegate long GetTimelineDelegate(long param_1);
    private IHook<GetTimelineDelegate> _getTimeline;
    
    // === NEW MAGIC SYSTEM (from Discord community) ===
    // MagicExecute - Prepares the magic spell to be cast, setups the magic struct
    // Signature: 48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8
    public unsafe delegate long MagicExecuteDelegate(long unkMagicStructPtr, int magicId, long a3, long a4, int a5, int a6, int a7);
    private IHook<MagicExecuteDelegate> _magicExecute;
    private MagicExecuteDelegate _magicExecuteWrapper;
    
    // CastMagic - Actually spawns the magic spell using the already set-up magic struct
    // Signature: 48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D ?? ?? ?? ?? 48 8D 54 24 30 44 8B 40 08 E8 ?? ?? ?? ?? 33 FF
    public unsafe delegate char CastMagicDelegate(long a1, long unkMagicStructPtr);
    private IHook<CastMagicDelegate> _castMagic;
    private CastMagicDelegate _castMagicWrapper;
    
    public delegate long GetOrCreateEntityDelegate(long entityManager, out long outEntityInfo, long entityIdPtr);
    private GetOrCreateEntityDelegate _getOrCreateEntity;
    
    public delegate long GetBnpcIdFromEntityDelegate(long entity);
    private GetBnpcIdFromEntityDelegate _getBnpcIdFromEntity;
    
    // IsSummonModeActive - checks if a specific summon mode is active
    // bool IsSummonModeActive(long playerState, int summonModeId)
    public delegate byte IsSummonModeActiveDelegate(long playerState, int summonModeId);
    private IsSummonModeActiveDelegate _isSummonModeActive;
    
    // Global pointers (same as combo meter)
    private long _globalEntityManagerPtr;
    private long _globalPlayerStatePtr;  // For reading player state like active Eikon
    
    // Current active Eikon tracking
    private uint _currentEikonMode = 0;
    private long _modeA1 = 0;  // Player mode structure pointer
    
    // Last attacked enemy for Diara system
    private long _lastAttackedEnemyPtr = 0;
    private long _lastAttackR15 = 0;  // Store R15 structure for reference
    
    // Magic template pointer - for spawning our own projectiles
    private long _lastMagicTemplatePtr = 0;
    private long _lastDestStructPtr = 0;  // Destination attack struct (reused)
    private int _lastMagicActionId = 0;   // Store the last created magic ID to identify it in FireMagic
    
    // Pending projectiles to spawn on next magic shot
    private int _pendingDiaProjectiles = 0;
    
    // Cache for projectile data to avoid crashes with delayed shots
    private IntPtr _projectileDataBuffer = IntPtr.Zero;
    private const int PROJECTILE_DATA_SIZE = 0x500; // 1280 bytes, should be enough
    
    // === DEEP COPY CACHE (Opción 3) ===
    // Instead of caching just the pointer (which becomes stale), we deep-copy the entire structure
    private IntPtr _magicManagerCopy = IntPtr.Zero;     // Deep copy of MagicManager
    private IntPtr _magicInputConfigCopy = IntPtr.Zero; // Deep copy of MagicInputConfig (at +0x38)
    private const int MAGIC_MANAGER_SIZE = 0x200;       // 512 bytes for MagicManager
    private const int MAGIC_INPUT_CONFIG_SIZE = 0x100;  // 256 bytes for MagicInputConfig
    private bool _hasCachedMagicContext = false;        // Flag: do we have valid cached data?
    
    // === LEGACY CACHE (for comparison/debugging) ===
    private long _cachedMagicManager = 0;
    private long _cachedValidVTable = 0; // Store ONLY the VTable
    private long _modifiedTimelinePtr = 0; // Track which timeline we modified
    private long _originalVTableBackup = 0; // Backup of the original VTable
    private IntPtr _shadowVTableBuffer = IntPtr.Zero; // Buffer for our constructed VTable
    private const int VTABLE_SIZE = 0x800; // 2048 bytes (256 functions), plenty for a VTable
    private bool _isForceFiringDiara = false; // Flag to activate the hook override
    
    // Cache the param_1 from GetTimeline during normal shots for comparison
    private long _cachedTimelineParam1 = 0;
    private long _cachedTimelinePtr = 0;
    
    // Wings of Light context - store a1 from when it's called normally
    private long _wingsA1Context = 0;
    
    // === NEW MAGIC SYSTEM CACHE ===
    // Cached parameters from MagicExecute and CastMagic for spawning spells on demand
    private IntPtr _magicStructBuffer = IntPtr.Zero;  // Buffer for UnkMagicStruct (ptr1 + 32 longs = 264 bytes)
    private const int MAGIC_STRUCT_SIZE = 0x108;      // 264 bytes (8 + 32*8)
    private long _magicExecute_a3 = 0;
    private long _magicExecute_a4 = 0;
    private int _magicExecute_a5 = 0;
    private int _magicExecute_a6 = 0;
    private int _magicExecute_a7 = 0;
    private long _castMagic_a1 = 0;
    private bool _hasMagicContext = false;  // True after both MagicExecute and CastMagic have run
    
    // Systems
    private DiaSystem _diaSystem;
    private DiaraSystem _diaraSystem;
    private DarkraSystem _darkraSystem;
    
    // NEX
    private WeakReference<INextExcelDBApiManaged> _managedNexApi;
    public WeakReference<INextExcelDBApi> _rawNexApi;
    private readonly NexTableLayout _attackParamLayout;
    
    // Clive IDs (from combo meter)
    private readonly HashSet<uint> _cliveIds = new() { 1, 2, 3, 4, 6, 8, 9, 10 };
    
    // Constructor sin parámetros requerido por Startup
    public TrulyEikonicSpellsMod() { }
    
    public TrulyEikonicSpellsMod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        
#if DEBUG
        Debugger.Launch();
#endif
        
        _logger.WriteLine($"[{_modConfig.ModId}] Initializing Truly Eikonic Spells...", _logger.ColorGreen);
        
        // Load NEX layouts (kept for potential future use)
        _attackParamLayout = TableMappingReader.ReadTableLayout("attackparam", new Version(1, 0, 3));
        
        // Initialize systems
        _diaSystem = new DiaSystem();
        _diaraSystem = new DiaraSystem();
        _darkraSystem = new DarkraSystem();
        
        // Allocate memory for projectile data cache
        _projectileDataBuffer = Marshal.AllocHGlobal(PROJECTILE_DATA_SIZE);
        _shadowVTableBuffer = Marshal.AllocHGlobal(VTABLE_SIZE);
        
        // Allocate memory for deep copy buffers (Opción 3)
        _magicManagerCopy = Marshal.AllocHGlobal(MAGIC_MANAGER_SIZE);
        _magicInputConfigCopy = Marshal.AllocHGlobal(MAGIC_INPUT_CONFIG_SIZE);
        
        // Allocate buffer for new magic system
        _magicStructBuffer = Marshal.AllocHGlobal(MAGIC_STRUCT_SIZE);
        
        // Zero out the buffers
        unsafe
        {
            for (int i = 0; i < MAGIC_MANAGER_SIZE; i++) ((byte*)_magicManagerCopy)[i] = 0;
            for (int i = 0; i < MAGIC_INPUT_CONFIG_SIZE; i++) ((byte*)_magicInputConfigCopy)[i] = 0;
            for (int i = 0; i < MAGIC_STRUCT_SIZE; i++) ((byte*)_magicStructBuffer)[i] = 0;
        }
        
        // Setup Diara logging
        _diaraSystem.Log = (msg) => _logger.WriteLine($"[{_modConfig.ModId}] {msg}", _logger.ColorGreen);
        _diaraSystem.OnBuffActivated += OnDiaraBuffActivated;
        _diaraSystem.OnBuffDeactivated += OnDiaraBuffDeactivated;
        _diaraSystem.OnPerfectDodgeWithBuff += OnDiaraPerfectDodge;
        
        // Get NEX API
        _managedNexApi = _modLoader.GetController<INextExcelDBApiManaged>();
        _rawNexApi = _modLoader.GetController<INextExcelDBApi>();
        
        if (!_managedNexApi.TryGetTarget(out INextExcelDBApiManaged managedApi))
        {
            throw new Exception($"[{_modConfig.ModId}] Could not get INextExcelDBApi!");
        }
        
        managedApi.OnNexLoaded += OnNexLoaded;
        
        // Setup scans
        var scansController = _modLoader.GetController<IStartupScanner>();
        if (!scansController.TryGetTarget(out IStartupScanner scans))
        {
            throw new Exception($"[{_modConfig.ModId}] Unable to get ISharedScans!");
        }
        
        SetupScans(scans);
        
        // Global pointers
        var baseAddress = Process.GetCurrentProcess().MainModule!.BaseAddress;
        _globalEntityManagerPtr = baseAddress + 0x1816CD0;
        _globalPlayerStatePtr = baseAddress + 0x1816608;  // Same as globalUnk in combo_meter
    }
    
    private void OnNexLoaded()
    {
        _logger.WriteLine($"[{_modConfig.ModId}] NEX loaded, setting up tables...", _logger.ColorGreen);
        // Dump layouts to discover fields
        DumpTableLayout("charatimeline");
        DumpTableLayout("charatimelinevariation");
    }
    
    // Dump table columns to find element-related fields
    private void DumpTableLayout(string tableName)
    {
        try 
        {
            var layout = TableMappingReader.ReadTableLayout(tableName, new Version(1, 0, 3));
            _logger.WriteLine($"[{_modConfig.ModId}] === {tableName.ToUpper()} LAYOUT ===", _logger.ColorYellow);
            foreach (var col in layout.Columns)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] Column: {col.Key}, Offset: {col.Value.Offset}, Type: {col.Value.Type}", _logger.ColorYellow);
            }
            _logger.WriteLine($"[{_modConfig.ModId}] === END LAYOUT ===", _logger.ColorYellow);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Error dumping {tableName}: {ex.Message}", _logger.ColorRed);
        }
    }
    
    private unsafe void SetupScans(IStartupScanner scans)
    {
        // OnHit hook - same signature as combo meter
        scans.AddScan("48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 44 8B 82", address =>
        {
            _onHit = _hooks!.CreateHook<OnHitDelegate>(OnHitImpl, address).Activate();
        });
        
        // Level load hook - reset systems
        scans.AddScan("48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 41 54 41 55 41 56 41 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 8B 41", address =>
        {
            _onLevelLoad = _hooks!.CreateHook<OnLevelLoad>(OnLevelLoadImpl, address).Activate();
        });
        
        // StartPlayerMode hook - track Eikon changes
        scans.AddScan("85 D2 0F 84 ?? ?? ?? ?? 48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 48 89 78 ?? 41 55 41 56 41 57 48 83 EC ?? 48 8D 79", address =>
        {
            _startPlayerMode = _hooks!.CreateHook<StartPlayerModeDelegate>(StartPlayerModeImpl, address).Activate();
        });
        
        // Entity helpers (same as combo meter)
        scans.AddScan("48 89 5C 24 ?? 48 89 6C 24 ?? 44 89 44 24 ?? 56 57 41 54 41 56 41 57 48 83 EC ?? 45 33 E4", address =>
        {
            _getOrCreateEntity = _hooks!.CreateWrapper<GetOrCreateEntityDelegate>(address, out _);
        });
        
        scans.AddScan("48 83 EC ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? 8B 40", address =>
        {
            _getBnpcIdFromEntity = _hooks!.CreateWrapper<GetBnpcIdFromEntityDelegate>(address, out _);
        });
        
        // IsSummonModeActive - check if a specific Eikon is active
        scans.AddScan("48 89 5C 24 ?? 57 48 83 EC ?? 8B FA 85 D2", address =>
        {
            _isSummonModeActive = _hooks!.CreateWrapper<IsSummonModeActiveDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] Found IsSummonModeActive at 0x{address:X}", _logger.ColorGreen);
        });
        
        // Perfect Dodge hook - for Diara system
        scans.AddScan("48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 57 41 54 41 55 41 56 41 57 48 83 EC ?? 45 33 ED 4C 8B FA", address =>
        {
            _onPerfectDodge = _hooks!.CreateHook<OnPerfectDodgeDelegate>(OnPerfectDodgeImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked Perfect Dodge at 0x{address:X}", _logger.ColorGreen);
        });
        
        // MaybeHandleWingsPerfectDodge - handles Wings of Light and other special dodge effects
        // This function has checks for wings of light, leviathan dodge, escapement bit dodge
        scans.AddScan("48 89 5C 24 ?? 48 89 74 24 ?? 55 57 41 55 41 56 41 57 48 8B EC 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 48 8B DA", address =>
        {
            _maybeHandleWingsPerfectDodge = _hooks!.CreateHook<MaybeHandleWingsPerfectDodgeDelegate>(MaybeHandleWingsPerfectDodgeImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked MaybeHandleWingsPerfectDodge at 0x{address:X}", _logger.ColorGreen);
        });
        
        // BattleTechnique hook - log special abilities only (not normal attacks)
        scans.AddScan("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 45 8A F8", address =>
        {
            _onBattleTechnique = _hooks!.CreateHook<OnBattleTechniqueDelegate>(OnBattleTechniqueImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked BattleTechnique at 0x{address:X}", _logger.ColorGreen);
        });
        
        // CopyAttackData - copies attack data from template to attack struct
        // Signature: 48 89 5C 24 08 57 48 83 EC 20 48 8D 59 58 48 8B FA
        // Called when creating projectiles/attacks, copies ActionId and other params
        scans.AddScan("48 89 5C 24 ?? 57 48 83 EC 20 48 8D 59 58 48 8B FA", address =>
        {
            _copyAttackData = _hooks!.CreateHook<CopyAttackDataDelegate>(CopyAttackDataImpl, address).Activate();
            _copyAttackDataWrapper = _hooks!.CreateWrapper<CopyAttackDataDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked CopyAttackData at 0x{address:X}", _logger.ColorGreen);
        });
        
        // FireMagicProjectile - THE key function for spawning magic projectiles!
        // Only called for magic shots (normal shot, charged shot), not melee or Eikon abilities
        // Offset: 0x56E0F0 (verified via CheatEngine breakpoint)
        // Verified via CheatEngine: RCX = MagicManager, RDX = ProjectileData (valid pointers)
        // R8 = 0 (not used), R9 = return address (not a parameter)
        // NOTE: Signature scan was matching wrong function at 0x24A168, so using hardcoded offset
        var baseAddr = Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64();
        var fireMagicAddr = baseAddr + 0x56E0F0;
        _fireMagicProjectile = _hooks!.CreateHook<FireMagicProjectileDelegate>(FireMagicProjectileImpl, fireMagicAddr).Activate();
        _fireMagicProjectileWrapper = _hooks!.CreateWrapper<FireMagicProjectileDelegate>(fireMagicAddr, out _);
        _logger.WriteLine($"[{_modConfig.ModId}] Hooked FireMagicProjectile at 0x{fireMagicAddr:X} (base: 0x{baseAddr:X} + 0x56E0F0)", _logger.ColorGreen);
        
        // GetTimeline Hook (0x4692A4)
        var getTimelineAddr = baseAddr + 0x4692A4;
        _getTimeline = _hooks!.CreateHook<GetTimelineDelegate>(GetTimelineImpl, getTimelineAddr).Activate();
        _logger.WriteLine($"[{_modConfig.ModId}] Hooked GetTimeline at 0x{getTimelineAddr:X}", _logger.ColorGreen);
        
        // === NEW MAGIC SYSTEM HOOKS ===
        // MagicExecute - Prepares the magic spell to be cast
        scans.AddScan("48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8", address =>
        {
            _magicExecute = _hooks!.CreateHook<MagicExecuteDelegate>(MagicExecuteImpl, address).Activate();
            _magicExecuteWrapper = _hooks!.CreateWrapper<MagicExecuteDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked MagicExecute at 0x{address:X}", _logger.ColorGreen);
        });
        
        // CastMagic - Actually spawns the magic spell
        scans.AddScan("48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D", address =>
        {
            _castMagic = _hooks!.CreateHook<CastMagicDelegate>(CastMagicImpl, address).Activate();
            _castMagicWrapper = _hooks!.CreateWrapper<CastMagicDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked CastMagic at 0x{address:X}", _logger.ColorGreen);
        });
    }
    
    private long OnLevelLoadImpl(long a1, double a2, double a3, double a4)
    {
        _diaSystem.Reset();
        _diaraSystem.Reset();
        _darkraSystem.Reset();
        _currentEikonMode = 0;
        
        // Reset new magic system cache
        _hasMagicContext = false;
        _castMagic_a1 = 0;
        _magicExecute_a3 = 0;
        _magicExecute_a4 = 0;
        _magicExecute_a5 = 0;
        _magicExecute_a6 = 0;
        _magicExecute_a7 = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] Level loaded, reset Dia/Diara/Darkra systems, Eikon mode, and Magic context", _logger.ColorYellow);
        return _onLevelLoad.OriginalFunction(a1, a2, a3, a4);
    }
    
    // === Perfect Dodge Handler (for Diara system) ===
    private unsafe char OnPerfectDodgeImpl(long a1, long a2, double a3, double a4)
    {
        // === DEBUG: Reverse Engineering - Perfect Dodge ===
        if (DEBUG_PERFECT_DODGE)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DODGE] Perfect Dodge detected! a1=0x{a1:X}, a2=0x{a2:X}", _logger.ColorYellow);
        }
        
        // Update Diara system (check for buff timeout)
        _diaraSystem.Update();
        
        // Process perfect dodge through Diara system
        int diaSpellsToSpawn = _diaraSystem.OnPerfectDodge();
        
        if (diaSpellsToSpawn > 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Should spawn {diaSpellsToSpawn} Dia spells!", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Magic context ready: {_hasMagicContext}", _logger.ColorYellow);
            
            if (_hasMagicContext)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Spawning {diaSpellsToSpawn} Dia spells using new magic system!", _logger.ColorGreen);
                
                // Spawn the Dia spells!
                // Based on logs: magicId=214 was captured, a6=218 seems to be the ActionId
                // Let's try with the captured magicId first (214)
                for (int i = 0; i < diaSpellsToSpawn; i++)
                {
                    TrySpawnMagicSpell(214); // Try with captured magicId
                }
            }
            else
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] No magic context yet - fire a normal shot first!", _logger.ColorRed);
            }
            
            _diaraSystem.ConsumeSpells();
        }
        
        return _onPerfectDodge.OriginalFunction(a1, a2, a3, a4);
    }
    
    /// <summary>
    /// Log timeline from Perfect Dodge context to compare with magic shot timeline
    /// Uses cached param_1 from GetTimeline to read Timeline data
    /// </summary>
    private unsafe void LogTimelineFromContext(long a1, long a2, string context)
    {
        try
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] ===========================================", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Context: {context}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Dodge a1=0x{a1:X}, a2=0x{a2:X}", _logger.ColorYellow);
            
            // Use the cached GetTimeline param_1 to get Timeline during this context
            if (_cachedTimelineParam1 != 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Using cached param_1=0x{_cachedTimelineParam1:X}", _logger.ColorYellow);
                
                // Call GetTimeline with the cached param_1 to get current Timeline
                _currentActionContext = context;
                _shouldLogTimeline = true;
                
                // This will trigger GetTimelineImpl which will log the Timeline
                long timelineResult = _getTimeline.OriginalFunction(_cachedTimelineParam1);
                
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] GetTimeline result during {context}: 0x{timelineResult:X}", 
                    timelineResult != 0 ? _logger.ColorGreen : _logger.ColorRed);
                
                if (timelineResult != 0)
                {
                    // Manually log the Timeline since we called the original directly
                    LogTimelineObject(_cachedTimelineParam1, timelineResult, context);
                }
                else
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Timeline is NULL - vtable[0x48] blocked!", _logger.ColorRed);
                    
                    // Try to read what vtable[0x48] is checking
                    long* vtableEntry = (long*)(_cachedTimelineParam1 + 0x10);
                    _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] param_1+0x10 = 0x{*vtableEntry:X}", _logger.ColorYellow);
                }
                
                _shouldLogTimeline = false;
            }
            else
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] No cached param_1 available - shoot once first!", _logger.ColorRed);
            }
            
            // Also compare with last cached timeline ptr
            if (_cachedTimelinePtr != 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Last cached Timeline ptr=0x{_cachedTimelinePtr:X}", _logger.ColorYellow);
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] ===========================================", _logger.ColorYellow);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Error in LogTimelineFromContext: {ex.Message}", _logger.ColorRed);
        }
    }
    
    /// <summary>
    /// Spawn Dia projectiles during Perfect Dodge using DEEP COPIED context (Opción 3)
    /// 
    /// Problem: The original MagicManager pointer becomes invalid when the player is not in "shooting" state.
    /// Solution: Deep copy the entire MagicManager + MagicInputConfig structures to our own memory.
    /// 
    /// According to reverse engineering:
    /// - FireMagicProjectile IGNORES the second parameter (RDX)
    /// - All data comes from MagicManager (RCX)
    /// - [RCX + 0x38] = MagicInputConfig pointer
    /// - [config + 0x10] = Shot Type: 1=Normal, 2=Charged, 3=Precision Counter
    /// </summary>
    private unsafe void TrySpawnMagicProjectile()
    {
        int spellCount = _diaraSystem.GetPendingSpellCount();
        
        if (spellCount <= 0)
        {
            return;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Attempting to spawn {spellCount} Normal Shots (Dia) using DEEP COPY...", _logger.ColorGreen);
        
        // Check if we have valid deep-copied data
        if (!_hasCachedMagicContext)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] No cached MagicManager copy! Fire a normal shot first.", _logger.ColorRed);
            _diaraSystem.ConsumeSpells();
            return;
        }
        
        // Verify our buffers are allocated
        if (_magicManagerCopy == IntPtr.Zero || _magicInputConfigCopy == IntPtr.Zero)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Deep copy buffers not allocated!", _logger.ColorRed);
            _diaraSystem.ConsumeSpells();
            return;
        }
        
        try
        {
            // IMPORTANT: Fix the internal pointer!
            // The copied MagicManager has [+0x38] pointing to the ORIGINAL config (which is now invalid).
            // We need to patch it to point to OUR copied config.
            long* configPtrLocation = (long*)((long)_magicManagerCopy + 0x38);
            long originalConfigPtr = *configPtrLocation; // Save for logging
            *configPtrLocation = (long)_magicInputConfigCopy; // Point to our copy!
            
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Patched MagicManager+0x38: 0x{originalConfigPtr:X} -> 0x{(long)_magicInputConfigCopy:X}", _logger.ColorYellow);
            
            // Set Shot Type to 1 (Normal Shot = Dia) in our copied config
            int* shotTypePtr = (int*)((long)_magicInputConfigCopy + 0x10);
            int originalShotType = *shotTypePtr;
            *shotTypePtr = 1; // Force Normal Shot
            
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Set ShotType: {originalShotType} -> 1 (Normal/Dia)", _logger.ColorYellow);
            
            // Fire the projectiles using our deep-copied MagicManager!
            for (int i = 0; i < spellCount; i++)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Firing Normal Shot {i + 1}/{spellCount}...", _logger.ColorYellow);
                
                // Call FireMagicProjectile with our COPIED MagicManager
                long result = _fireMagicProjectile.OriginalFunction((long)_magicManagerCopy, 0);
                
                _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Shot {i + 1} result: 0x{result:X}", 
                    result != 0 ? _logger.ColorGreen : _logger.ColorRed);
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Deep copy fire complete!", _logger.ColorGreen);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] CRASH during deep copy fire: {ex.Message}", _logger.ColorRed);
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Stack: {ex.StackTrace}", _logger.ColorRed);
        }
        
        // Consume the spells
        _diaraSystem.ConsumeSpells();
    }
    
    /// <summary>
    /// Spawns Dia projectiles instantly by spoofing the Timeline VTable via Hook
    /// </summary>
    private unsafe void SpawnInstantDiaProjectiles(int count)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Starting Instant Fire Sequence ({count} shots)...", _logger.ColorBlue);
        
        if (_cachedMagicManager == 0 || _cachedValidVTable == 0 || _projectileDataBuffer == IntPtr.Zero)
        {
             _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Invalid cache, aborting.", _logger.ColorRed);
             return;
        }
        
        // Debug: Log cached values
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Cache: MagicMgr=0x{_cachedMagicManager:X}, VTable=0x{_cachedValidVTable:X}, ProjData=0x{(long)_projectileDataBuffer:X}", _logger.ColorYellow);
        
        // Verify MagicManager is still valid by checking its structure
        try
        {
            long configPtr = *(long*)(_cachedMagicManager + 0x38);
            if (configPtr == 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] MagicManager config ptr is NULL! Cache is stale.", _logger.ColorRed);
                return;
            }
            int shotType = *(int*)(configPtr + 0x10);
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Current ShotType at config: {shotType}", _logger.ColorYellow);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Failed to validate MagicManager: {ex.Message}", _logger.ColorRed);
            return;
        }

        try
        {
            // 1. Activate the Hook Override
            _isForceFiringDiara = true;
            _modifiedTimelinePtr = 0; // Reset tracking
            
            // 2. Fire projectiles using cached data
            long safeProjectileData = (long)_projectileDataBuffer;
            
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Calling FireMagicProjectile {count} times...", _logger.ColorYellow);
            
            for (int i = 0; i < count; i++)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Shot {i+1}/{count}...", _logger.ColorYellow);
                _fireMagicProjectile.OriginalFunction(_cachedMagicManager, safeProjectileData);
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Instant fire success!", _logger.ColorGreen);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] CRITICAL FAILURE during instant fire: {ex.Message}", _logger.ColorRed);
        }
        finally
        {
            // 3. Always disable the override
            // NOTE: We do NOT restore the VTable because:
            // 1. The timeline object is temporary and may be destroyed after this call
            // 2. Trying to write to freed memory causes crashes
            // 3. The Shadow VTable is in our own buffer so it's safe even if the object is reused
            _isForceFiringDiara = false;
            _modifiedTimelinePtr = 0;
            _originalVTableBackup = 0;
        }
    }
    
    // Flag to enable timeline logging only during FireMagicProjectile
    private bool _shouldLogTimeline = false;
    private string _currentActionContext = "";
    
    // === GetTimeline Hook - Now just for LOGGING to understand the Timeline object ===
    private unsafe long GetTimelineImpl(long param_1)
    {
        // Call original first
        long result = _getTimeline.OriginalFunction(param_1);
        
        // Cache the param_1 and timeline ptr for later use (during Perfect Dodge comparison)
        if (result != 0)
        {
            _cachedTimelineParam1 = param_1;
            _cachedTimelinePtr = result;
        }
        
        // Only log if we're in a magic projectile context
        if (_shouldLogTimeline)
        {
            LogTimelineObject(param_1, result, _currentActionContext);
            _shouldLogTimeline = false; // Only log once per action
        }
        
        return result;
    }
    
    /// <summary>
    /// Log Timeline object structure to understand what differs between actions
    /// </summary>
    private unsafe void LogTimelineObject(long param_1, long timelineResult, string context)
    {
        try
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] ===========================================", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Context: {context}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] GetTimeline called! param_1=0x{param_1:X}", _logger.ColorYellow);
            
            // Read [param_1 + 0x10] which is the timeline pointer
            long timelinePtr = *(long*)(param_1 + 0x10);
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] [param_1+0x10] (raw timeline ptr) = 0x{timelinePtr:X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Result from GetTimeline = 0x{timelineResult:X}", _logger.ColorYellow);
            
            if (timelinePtr != 0)
            {
                // Read the VTable pointer
                long vtablePtr = *(long*)timelinePtr;
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] VTable ptr = 0x{vtablePtr:X}", _logger.ColorGreen);
                
                // Log key VTable function pointers
                long func48 = *(long*)(vtablePtr + 0x48);
                long func58 = *(long*)(vtablePtr + 0x58);
                long func98 = *(long*)(vtablePtr + 0x98);
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] VTable[0x48] (IsMagicBlocked?) = 0x{func48:X}", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] VTable[0x58] (GetMagicType?)   = 0x{func58:X}", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] VTable[0x98] (IsMagicAllowed?) = 0x{func98:X}", _logger.ColorGreen);
                
                // Dump first 0x100 bytes of timeline object (NOT vtable, the actual object)
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] --- Timeline Object Data (first 0x100 bytes) ---", _logger.ColorYellow);
                for (int i = 0; i < 0x100; i += 0x20)
                {
                    long v0 = *(long*)(timelinePtr + i);
                    long v8 = *(long*)(timelinePtr + i + 0x8);
                    long v10 = *(long*)(timelinePtr + i + 0x10);
                    long v18 = *(long*)(timelinePtr + i + 0x18);
                    _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] +0x{i:X2}: {v0:X16} {v8:X16} {v10:X16} {v18:X16}", _logger.ColorYellow);
                }
            }
            else
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Timeline ptr is NULL!", _logger.ColorRed);
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] ===========================================", _logger.ColorYellow);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [TIMELINE] Error logging: {ex.Message}", _logger.ColorRed);
        }
    }
    
    // === NEW MAGIC SYSTEM HOOKS ===
    
    /// <summary>
    /// MagicExecute hook - captures magic spell setup parameters
    /// Called when preparing a magic spell to be cast
    /// </summary>
    private unsafe long MagicExecuteImpl(long unkMagicStructPtr, int magicId, long a3, long a4, int a5, int a6, int a7)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_EXECUTE] Called! structPtr=0x{unkMagicStructPtr:X}, magicId={magicId}, a3=0x{a3:X}, a4=0x{a4:X}, a5={a5}, a6={a6}, a7={a7}", _logger.ColorGreen);
        
        // Deep copy the magic struct to our buffer
        if (_magicStructBuffer != IntPtr.Zero && unkMagicStructPtr != 0)
        {
            try
            {
                // Copy ptr1 (first 8 bytes - this is a pointer that we need to dereference)
                long ptr1Value = *(long*)unkMagicStructPtr;
                *(long*)_magicStructBuffer = ptr1Value;
                
                // Copy the array of 32 longs (256 bytes starting at offset 0x8)
                for (int i = 0; i < 32; i++)
                {
                    long value = *(long*)(unkMagicStructPtr + 0x8 + 0x8 * i);
                    *(long*)((long)_magicStructBuffer + 0x8 + 0x8 * i) = value;
                }
                
                _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_EXECUTE] Copied magic struct! ptr1=0x{ptr1Value:X}", _logger.ColorBlue);
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_EXECUTE] Failed to copy struct: {ex.Message}", _logger.ColorRed);
            }
        }
        
        // Store the other parameters
        _magicExecute_a3 = a3;
        _magicExecute_a4 = a4;
        _magicExecute_a5 = a5;
        _magicExecute_a6 = a6;
        _magicExecute_a7 = a7;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_EXECUTE] Cached params: a3=0x{a3:X}, a4=0x{a4:X}, a5={a5}, a6={a6}, a7={a7}", _logger.ColorBlue);
        
        return _magicExecute.OriginalFunction(unkMagicStructPtr, magicId, a3, a4, a5, a6, a7);
    }
    
    /// <summary>
    /// CastMagic hook - captures the a1 parameter needed for spawning
    /// Called when actually spawning the magic spell
    /// </summary>
    private unsafe char CastMagicImpl(long a1, long unkMagicStructPtr)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [CAST_MAGIC] Called! a1=0x{a1:X}, structPtr=0x{unkMagicStructPtr:X}", _logger.ColorGreen);
        
        // Store a1 for later use
        _castMagic_a1 = a1;
        
        // Now we have all the context we need!
        _hasMagicContext = true;
        _logger.WriteLine($"[{_modConfig.ModId}] [CAST_MAGIC] === MAGIC CONTEXT READY! Can now spawn spells on demand. ===", _logger.ColorGreen);
        
        return _castMagic.OriginalFunction(a1, unkMagicStructPtr);
    }
    
    /// <summary>
    /// Spawn a magic spell using the new MagicExecute/CastMagic system
    /// WARNING: Only call this after both hooks have executed at least once!
    /// </summary>
    private unsafe void TrySpawnMagicSpell(int magicId)
    {
        if (!_hasMagicContext)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [SPAWN_MAGIC] Cannot spawn - no magic context! Fire a normal shot first.", _logger.ColorRed);
            return;
        }
        
        if (_magicStructBuffer == IntPtr.Zero || _castMagic_a1 == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [SPAWN_MAGIC] Cannot spawn - missing buffers or a1!", _logger.ColorRed);
            return;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [SPAWN_MAGIC] Attempting to spawn magicId={magicId}...", _logger.ColorGreen);
        
        try
        {
            // Call MagicExecute with our cached struct and new magicId
            _logger.WriteLine($"[{_modConfig.ModId}] [SPAWN_MAGIC] Calling MagicExecute...", _logger.ColorYellow);
            _magicExecute.OriginalFunction(
                (long)_magicStructBuffer, 
                magicId, 
                _magicExecute_a3, 
                _magicExecute_a4, 
                _magicExecute_a5, 
                _magicExecute_a6, 
                _magicExecute_a7
            );
            
            // Call CastMagic to actually spawn the spell
            _logger.WriteLine($"[{_modConfig.ModId}] [SPAWN_MAGIC] Calling CastMagic...", _logger.ColorYellow);
            _castMagic.OriginalFunction(_castMagic_a1, (long)_magicStructBuffer);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [SPAWN_MAGIC] SUCCESS! Magic spell {magicId} spawned!", _logger.ColorGreen);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [SPAWN_MAGIC] CRASH: {ex.Message}", _logger.ColorRed);
            _logger.WriteLine($"[{_modConfig.ModId}] [SPAWN_MAGIC] Stack: {ex.StackTrace}", _logger.ColorRed);
        }
    }
    
    /// <summary>
    /// Attempt to trigger Wings of Light visual effect during Diara buff
    /// </summary>
    private unsafe void TryTriggerWingsEffect(long dodgeContext)
    {
        if (_wingsA1Context == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Cannot trigger wings - no wings context captured yet. Enter Wings of Light once to capture.", _logger.ColorYellow);
            return;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Attempting to trigger Wings effect! a1=0x{_wingsA1Context:X}, a2=0x{dodgeContext:X}", _logger.ColorGreen);
        
        try
        {
            // Call the Wings function with captured context
            long result = _maybeHandleWingsPerfectDodge.OriginalFunction(_wingsA1Context, dodgeContext);
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Wings function returned: 0x{result:X}", _logger.ColorGreen);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Wings trigger failed: {ex.Message}", _logger.ColorRed);
        }
    }
    
    // === MaybeHandleWingsPerfectDodge Handler - for Wings of Light effects ===
    private unsafe long MaybeHandleWingsPerfectDodgeImpl(long a1, long a2)
    {
        // Store the a1 context for later use (when we want to trigger wings during Diara)
        _wingsA1Context = a1;
        
        // === DEBUG: Reverse Engineering - Wings Dodge Handler ===
        if (DEBUG_WINGS_DODGE)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [WINGS_DODGE] MaybeHandleWingsPerfectDodge called! a1=0x{a1:X}, a2=0x{a2:X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [WINGS_DODGE] Captured wings context a1=0x{a1:X}", _logger.ColorGreen);
            
            if (_diaraSystem.IsBuffActive)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [WINGS_DODGE] Diara buff active during wings!", _logger.ColorGreen);
            }
        }
        
        return _maybeHandleWingsPerfectDodge.OriginalFunction(a1, a2);
    }
    
    // === Diara Buff Event Handlers ===
    private void OnDiaraBuffActivated()
    {
        // TODO: Trigger Bahamut wings VFX
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Wings should appear now!", _logger.ColorGreen);
    }
    
    private void OnDiaraBuffDeactivated()
    {
        // TODO: Remove Bahamut wings VFX
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Wings should disappear now!", _logger.ColorYellow);
    }
    
    private void OnDiaraPerfectDodge(int spellCount)
    {
        // Log the event
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Perfect Dodge Event: {spellCount} spells requested.", _logger.ColorGreen);
        
        // Note: Actual spawning is handled by OnPerfectDodgeImpl calling TrySpawnMagicProjectile
        // which queues them for the next magic shot to avoid crashes.
    }
    
    /// <summary>
    /// Attempt to spawn Dia spells - currently simulates by applying damage to last target
    /// TODO: Find actual projectile spawning function for visual effect
    /// </summary>
    private unsafe void TrySpawnDiaSpells(int count)
    {
        if (_lastAttackedEnemyPtr == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Cannot spawn - no recent enemy target!", _logger.ColorRed);
            return;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Applying {count} Dia spell damage to last target (0x{_lastAttackedEnemyPtr:X})", _logger.ColorGreen);
        
        // For now, we can only log - actual damage application requires:
        // 1. Finding a function to spawn projectiles/apply damage directly
        // 2. OR calling OnHit with crafted parameters (risky)
        //
        // The modder mentioned: "spawn magic on-demand with function hooking"
        // We need to find the function that creates magic projectiles
        //
        // Potential approaches to investigate:
        // - Look for "CreateProjectile" or similar in game memory
        // - Hook the function that creates Dia projectiles when player shoots
        // - Use NEX magic table to find projectile definitions
        
        for (int i = 0; i < count; i++)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Dia #{i+1} would fire at enemy", _logger.ColorYellow);
        }
    }
    
    // === FireMagicProjectile Handler - THE KEY FUNCTION for magic shots! ===
    private long FireMagicProjectileImpl(long magicManager, long projectileData)
    {
        if (DEBUG_FIRE_MAGIC)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FIRE_MAGIC] Called! RCX=0x{magicManager:X}, RDX=0x{projectileData:X}", _logger.ColorGreen);
        }

        // 1. CHECK FOR SUPPRESSION (Diara Activation)
        // ANALYSIS: Based on Ghidra, ActionID is likely at [RCX + 0x38] + Offset
        // param_1 = RCX (MagicManager)
        // iVar4 = *(int *)(*(longlong *)(param_1 + 0x38) + 0x10); (Normal?)
        // iVar4 = *(int *)(*(longlong *)(param_1 + 0x38) + 0x18); (Burst?)
        // iVar4 = *(int *)(*(longlong *)(param_1 + 0x38) + 0x1c); (Charged?)
        
        int id10 = 0;
        unsafe 
        {
            long ptr38 = *(long*)(magicManager + 0x38);
            if (ptr38 != 0)
            {
                id10 = *(int*)(ptr38 + 0x10);
                int id18 = *(int*)(ptr38 + 0x18);
                int id1C = *(int*)(ptr38 + 0x1c);
                
                if (DEBUG_FIRE_MAGIC)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [ANALYSIS] RCX+0x38: 0x{ptr38:X} | IDs: {id10}, {id18}, {id1C}", _logger.ColorBlue);
                }
                
                // ENABLE TIMELINE LOGGING for this shot
                string shotTypeStr = id10 switch
                {
                    1 => "NORMAL_SHOT",
                    2 => "CHARGED_SHOT",
                    3 => "TYPE_3",
                    4 => "MAGIC_BURST",
                    _ => $"UNKNOWN({id10})"
                };
                _shouldLogTimeline = true;
                _currentActionContext = shotTypeStr;
                
                // SUPPRESSION LOGIC
                // If we see the Charged Shot ID (2) and we are Bahamut, we suppress it.
                if (id10 == 2) // 2 = Charged Shot
                {
                     int activeEikon = GetActiveEikon();
                     if (activeEikon == 8) // Bahamut
                     {
                         _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Bahamut Charged Shot detected (ID=2)! Activating buff and SUPPRESSING projectile.", _logger.ColorGreen);
                         _diaraSystem.OnChargedShotCast(activeEikon);
                         _shouldLogTimeline = false; // Don't log since we're suppressing
                         return 0; // Suppress the original shot!
                     }
                }
            }
        }
        
        // Call original function FIRST
        long result;
        unsafe { result = _fireMagicProjectile.OriginalFunction(magicManager, projectileData); }
        
        // === DEEP COPY CACHE (Opción 3) ===
        // If this was a successful shot (result != 0) AND it was a NORMAL shot (id10 == 1), 
        // deep copy the entire MagicManager + MagicInputConfig structures.
        if (result != 0 && id10 == 1)
        {
            unsafe
            {
                // LEGACY: Keep pointer cache for debugging comparison
                _cachedMagicManager = magicManager;
                
                // === DEEP COPY MagicManager ===
                if (_magicManagerCopy != IntPtr.Zero)
                {
                    Buffer.MemoryCopy((void*)magicManager, (void*)_magicManagerCopy, MAGIC_MANAGER_SIZE, MAGIC_MANAGER_SIZE);
                    _logger.WriteLine($"[{_modConfig.ModId}] [CACHE] Deep copied MagicManager ({MAGIC_MANAGER_SIZE} bytes) from 0x{magicManager:X}", _logger.ColorBlue);
                }
                
                // === DEEP COPY MagicInputConfig (at +0x38) ===
                long configPtr = *(long*)(magicManager + 0x38);
                if (configPtr != 0 && _magicInputConfigCopy != IntPtr.Zero)
                {
                    Buffer.MemoryCopy((void*)configPtr, (void*)_magicInputConfigCopy, MAGIC_INPUT_CONFIG_SIZE, MAGIC_INPUT_CONFIG_SIZE);
                    _logger.WriteLine($"[{_modConfig.ModId}] [CACHE] Deep copied MagicInputConfig ({MAGIC_INPUT_CONFIG_SIZE} bytes) from 0x{configPtr:X}", _logger.ColorBlue);
                    
                    // Log the shot type we captured
                    int capturedShotType = *(int*)((long)_magicInputConfigCopy + 0x10);
                    _logger.WriteLine($"[{_modConfig.ModId}] [CACHE] Captured ShotType = {capturedShotType}", _logger.ColorBlue);
                }
                
                // Mark that we have valid cached data
                _hasCachedMagicContext = true;
                _logger.WriteLine($"[{_modConfig.ModId}] [CACHE] === DEEP COPY COMPLETE - Ready for Diara! ===", _logger.ColorGreen);
                
                // LEGACY: Keep VTable cache for potential future use
                long originalTimelinePtr = *(long*)(magicManager + 0x28);
                if (originalTimelinePtr != 0)
                {
                    _cachedValidVTable = *(long*)originalTimelinePtr;
                }
                
                // Update snapshot buffer with latest valid projectile data
                if (_projectileDataBuffer != IntPtr.Zero && projectileData != 0)
                {
                    Buffer.MemoryCopy((void*)projectileData, (void*)_projectileDataBuffer, PROJECTILE_DATA_SIZE, PROJECTILE_DATA_SIZE);
                }
            }
        }
        
        if (DEBUG_FIRE_MAGIC)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FIRE_MAGIC] Result=0x{result:X}", _logger.ColorGreen);
        }
        
        // === DIARA BONUS PROJECTILES ===
        // Reverted to SYNCHRONOUS spawning to prevent crashes.
        // Async/Task.Run is not thread-safe for gameplay functions.
        if (_pendingDiaProjectiles > 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Spawning {_pendingDiaProjectiles} bonus Dia projectiles (Instant)!", _logger.ColorGreen);
            
            int toSpawn = _pendingDiaProjectiles;
            _pendingDiaProjectiles = 0; // Reset
            
            for (int i = 0; i < toSpawn; i++)
            {
                // Call original function synchronously
                // This works but has no delay and no spread (yet)
                unsafe { _fireMagicProjectile.OriginalFunction(magicManager, projectileData); }
            }
        }
        
        return result;
    }
    
    // === BattleTechnique Handler (for logging special abilities only) ===
    private char OnBattleTechniqueImpl(long a1, uint techId, char a3)
    {
        // Store the a1 pointer for potential manual invocation
        _battleTechniqueA1 = a1;
        
        // === DEBUG: Reverse Engineering - Battle Technique calls ===
        if (DEBUG_BATTLE_TECHNIQUE)
        {
            // Only log non-Ifrit special abilities (BattleTechnique is only called for special moves, not normal attacks)
            // Ifrit moves are 10000-20000 range - skip those as they spam the log
            if (techId < 10000 || techId > 20000)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [TECH] Special Ability techId: {techId}, a1: 0x{a1:X}", _logger.ColorYellow);
            }
        }
        
        return _onBattleTechnique.OriginalFunction(a1, techId, a3);
    }
    
    // === CopyAttackData Handler - Called when creating attacks/projectiles ===
    private unsafe void CopyAttackDataImpl(long destAttackStruct, long srcAttackTemplate)
    {
        // === DEBUG: Reverse Engineering - Attack Data Copy ===
        if (DEBUG_COPY_ATTACK_DATA)
        {
            try
            {
                // Read ActionId from source template (at offset 0x58 from the actual data start)
                // The function does: lea rbx, [rcx+58] then copies from [rdi+58] to [rbx+58]
                // So the ActionId is at srcAttackTemplate + 0x58
                int actionId = *(int*)(srcAttackTemplate + 0x58);
                
                // Get return address from stack to find caller
                // In x64, return address is at RSP when function starts
                // After our hook's prolog, it's offset - let's try reading it
                long* stackPtr = (long*)&destAttackStruct; // Approximate stack location
                long returnAddr = *(stackPtr - 1); // Return address is typically above local vars
                var baseAddr = Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64();
                var callerOffset = returnAddr - baseAddr;
                
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] === Attack Data Copy ===", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Dest (attack struct): 0x{destAttackStruct:X}", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Src (template): 0x{srcAttackTemplate:X}", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] ActionId from template: {actionId}", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Return addr: 0x{returnAddr:X} (offset: 0x{callerOffset:X})", _logger.ColorGreen);
                
                // Check if this is a magic projectile (218, 219, 227)
                if (actionId == 218 || actionId == 219 || actionId == 227)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] >>> MAGIC PROJECTILE DETECTED! <<<", _logger.ColorYellow);
                    
                    // Store the ID for FireMagicProjectile to check
                    _lastMagicActionId = actionId;

                    // Store the template pointer - this is the key to spawning our own projectiles!
                    _lastMagicTemplatePtr = srcAttackTemplate;
                    _lastDestStructPtr = destAttackStruct; // Also store dest for potential reuse
                    _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Stored magic template: 0x{srcAttackTemplate:X}", _logger.ColorYellow);
                    _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Stored dest struct: 0x{destAttackStruct:X}", _logger.ColorYellow);
                    
                    // Dump the template structure
                    DumpMagicTemplate(srcAttackTemplate);
                    
                    // === NEW: Dump the destination structure BEFORE copy ===
                    // This tells us what's already initialized in destAttackStruct
                    DumpDestStructure(destAttackStruct);
                }
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Error reading: {ex.Message}", _logger.ColorRed);
            }
        }
        
        // Call original function
        _copyAttackData.OriginalFunction(destAttackStruct, srcAttackTemplate);
    }
    
    // Dump destination structure to understand what's initialized before copy
    private unsafe void DumpDestStructure(long destStruct)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [DEST_STRUCT] === Destination Structure Before Copy ===", _logger.ColorLightBlue);
        
        // Dump first 0x100 bytes to see what's already there
        for (int i = 0; i < 0x100; i += 0x10)
        {
            long val0 = *(long*)(destStruct + i);
            long val8 = *(long*)(destStruct + i + 8);
            _logger.WriteLine($"[{_modConfig.ModId}] [DEST_STRUCT] +0x{i:X2}: 0x{val0:X16} | 0x{val8:X16}", _logger.ColorLightBlue);
        }
        
        // Check important offsets
        long entityPtr = *(long*)destStruct;
        int destActionId = *(int*)(destStruct + 0x58);
        _logger.WriteLine($"[{_modConfig.ModId}] [DEST_STRUCT] Entity ptr at +0x00: 0x{entityPtr:X}", _logger.ColorLightBlue);
        _logger.WriteLine($"[{_modConfig.ModId}] [DEST_STRUCT] ActionId at +0x58 (before copy): {destActionId}", _logger.ColorLightBlue);
    }
    
    private unsafe char StartPlayerModeImpl(long a1, uint playerMode, long a3)
    {
        // Save the player mode structure pointer
        _modeA1 = a1;
        
        // Track potential Eikon mode changes
        _currentEikonMode = playerMode;
        
        // === DEBUG: Reverse Engineering - Player Mode changes ===
        if (DEBUG_PLAYER_MODE)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MODE] StartPlayerMode called with playerMode: {playerMode}, a1: 0x{a1:X}", _logger.ColorYellow);
        }
        
        return _startPlayerMode.OriginalFunction(a1, playerMode, a3);
    }
    
    private unsafe long OnHitImpl(long* bnpcRow, long R15, long a3, long a4)
    {
        try
        {
            var info = ParseAttackInfo(bnpcRow, R15);
            
            // Only process Clive's attacks against enemies
            if (info.IsCliveAttack && !info.IsCliveTarget && !info.IsHealOrEffect)
            {
                // Get current active Eikon
                int activeEikon = GetActiveEikon();
                
                // Update Diara system timer
                _diaraSystem.Update();
                
                // DEBUG: Log all the action IDs for Clive's attacks
                if (DEBUG_ON_HIT)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [HIT] Hit detected! ActionId={info.ActionId}, Target=0x{info.TargetId:X}", _logger.ColorYellow);
                }
                
                // Store last attacked enemy for Diara system (for magic shots only)
                bool isMagicShot = info.ActionId == 218 || info.ActionId == 219 || info.ActionId == 227 || info.ActionId == 214;
                if (isMagicShot)
                {
                    _lastAttackedEnemyPtr = (long)bnpcRow;
                    _lastAttackR15 = R15;
                    
                    // === DEBUG: Reverse Engineering - Magic Hit Details ===
                    if (DEBUG_MAGIC_HIT)
                    {
                        LogMagicHitDebug(info, R15, bnpcRow, a3, a4);
                    }
                }
                
                // === DIA SYSTEM: Stacking and synergies ===
                var result = _diaSystem.ProcessHit(info.TargetId, info.ActionId, activeEikon, R15);
                
                if (result.WasStackingHit)
                {
                    string bonusText = result.DamageMultiplier > 1.0f ? $" (Damage x{result.DamageMultiplier:F2})" : "";
                    _logger.WriteLine($"[{_modConfig.ModId}] [DIA] +1 stack! Total: {result.CurrentStacks}/50{bonusText}", _logger.ColorGreen);
                }
                else if (result.StacksConsumed > 0)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [DIA] Consumed {result.StacksConsumed} stacks! Damage x{result.DamageMultiplier:F2}", _logger.ColorGreen);
                }
                else if (result.WasSynergyHit)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [DIA] Synergy! Damage x{result.DamageMultiplier:F2} ({result.CurrentStacks} stacks)", _logger.ColorGreen);
                }
                
                // === DARKRA SYSTEM: Shadow debuff from Odin ===
                var darkraResult = _darkraSystem.ProcessHit(info.TargetId, info.ActionId, activeEikon, R15);
                
                if (darkraResult.AppliedDebuff)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [DARKRA] Shadow debuff applied!", _logger.ColorBlue);
                }
                
                if (darkraResult.TriggeredShadowHit)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [DARKRA] Shadow hit! +{darkraResult.ShadowDamage} damage", _logger.ColorBlue);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Error in OnHitImpl: {ex.Message}", _logger.ColorRed);
        }
        
        return _onHit.OriginalFunction(bnpcRow, R15, a3, a4);
    }
    
    private unsafe uint GetAtkSource(long R15)
    {
        long v6 = *(long*)(R15 + 136);
        _getOrCreateEntity(*(long*)_globalEntityManagerPtr, out long entityUnk, v6);
        return (uint)_getBnpcIdFromEntity(entityUnk);
    }
    
    private unsafe AttackInfo ParseAttackInfo(long* bnpcRow, long R15)
    {
        var a = *(long*)((byte*)bnpcRow + 0x20);
        var b = *(long*)((byte*)a + 0x7298);
        uint attackTarget = *(uint*)((byte*)b + 0x38);
        
        int actionId = *(int*)(R15 + 0xB0);
        int rawDmg = *(int*)(R15 + 0x174);
        uint atkSource = GetAtkSource(R15);
        
        return new AttackInfo
        {
            ActionId = actionId,
            Damage = rawDmg,
            TargetId = (long)bnpcRow,
            IsCliveAttack = _cliveIds.Contains(atkSource) || (atkSource == 100 && attackTarget != 1),
            IsCliveTarget = _cliveIds.Contains(attackTarget),
            IsHealOrEffect = attackTarget == 1 && rawDmg <= 0
        };
    }
    
    private struct AttackInfo
    {
        public int ActionId;
        public int Damage;
        public long TargetId;
        public bool IsCliveAttack;
        public bool IsCliveTarget;
        public bool IsHealOrEffect;
    }
    
    // SummonModeIds (corrected from testing):
    // 0=Phoenix/Leviathan/Ultima, 2=Garuda, 3=Titan, 4=Ramuh, 5=Shiva, 7=Odin, 8=Bahamut
    private static readonly int[] KnownEikonIds = { 0, 2, 3, 4, 5, 7, 8 };
    
    // Spell element types for Dia system
    private enum SpellElement { None, Fire, Dia, Dark, Aero, Ice, Thunder, Earth, Water, Ruin }
    
    private unsafe int GetActiveEikon()
    {
        try
        {
            // Formula from another modder - reads memory directly to check active summon mode
            // isSummonModeActive_a1 = [baseAddress + 0x1816608] + 0x4798 + 0x14650
            long basePtr = *(long*)_globalPlayerStatePtr; // baseAddress + 0x1816608 already dereferenced
            if (basePtr == 0) return -2;
            
            long isSummonModeActive_a1 = basePtr + 0x4798 + 0x14650;
            
            // Read the comparison value: [isSummonModeActive_a1 + 0x58 + [isSummonModeActive_a1 + 0x70] * 8]
            long offset70 = *(long*)(isSummonModeActive_a1 + 0x70);
            long compareValue = *(long*)(isSummonModeActive_a1 + 0x58 + offset70 * 8);
            
            // Check each Eikon: [isSummonModeActive_a1 + 8 * summonModeId] == compareValue
            foreach (int eikonId in KnownEikonIds)
            {
                long eikonValue = *(long*)(isSummonModeActive_a1 + 8 * eikonId);
                if (eikonValue == compareValue)
                    return eikonId;
            }
            
            return 0; // No Eikon active
        }
        catch
        {
            return -3; // Error reading memory
        }
    }
    
    private static string GetEikonName(int eikonId)
    {
        return eikonId switch
        {
            0 => "Phoenix",  // Also Leviathan/Ultima (DLC)
            2 => "Garuda",
            3 => "Titan",
            4 => "Ramuh",
            5 => "Shiva",
            7 => "Odin",
            8 => "Bahamut",
            -1 => "No Function",
            -2 => "No PlayerState",
            -3 => "Error",
            _ => $"Unknown({eikonId})"
        };
    }
    
    private static SpellElement GetSpellElement(int eikonId)
    {
        return eikonId switch
        {
            0 => SpellElement.Fire,    // Phoenix = Fire
            8 => SpellElement.Dia,    // Bahamut = Dia
            7 => SpellElement.Dark,   // Odin = Dark
            2 => SpellElement.Aero,   // Garuda = Aero
            5 => SpellElement.Ice,    // Shiva = Ice
            4 => SpellElement.Thunder, // Ramuh = Thunder
            3 => SpellElement.Earth,  // Titan = Earth
            _ => SpellElement.None
        };
    }
    
    // ============================================================
    // DEBUG FUNCTIONS - Reverse Engineering Helpers
    // Set DEBUG_* flags at top of file to enable/disable
    // ============================================================
    #region Debug Functions (Reverse Engineering)
    
    /// <summary>
    /// Log detailed magic hit information for CheatEngine investigation
    /// </summary>
    private unsafe void LogMagicHitDebug(AttackInfo info, long R15, long* bnpcRow, long a3, long a4)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_HIT] === MAGIC PROJECTILE HIT ===", _logger.ColorGreen);
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_HIT] ActionId: {info.ActionId} (218=Air, 219=Ground, 227=Charged)", _logger.ColorGreen);
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_HIT] R15 (attack struct): 0x{R15:X}", _logger.ColorGreen);
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_HIT] bnpcRow (target): 0x{(long)bnpcRow:X}", _logger.ColorGreen);
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_HIT] a3: 0x{a3:X}, a4: 0x{a4:X}", _logger.ColorGreen);
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_HIT] Damage value at R15+0x174: {info.Damage}", _logger.ColorGreen);
        
        // Dump R15 structure for investigation
        DumpR15Structure(R15);
    }
    
    /// <summary>
    /// Dump ProjectileData structure (RDX in FireMagicProjectile)
    /// </summary>
    private unsafe void DumpProjectileData(long ptr)
    {
        try
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [PROJ_DUMP] === Projectile Data Dump (RDX) ===", _logger.ColorYellow);
            
            // Dump first 0x80 bytes
            for (int i = 0; i < 0x80; i += 0x10)
            {
                long v0 = *(long*)(ptr + i);
                long v8 = *(long*)(ptr + i + 8);
                _logger.WriteLine($"[{_modConfig.ModId}] [PROJ_DUMP] +0x{i:X2}: {v0:X16} {v8:X16}", _logger.ColorYellow);
            }
            
            // Check pointers at 0x20 and 0x40
            long ptr20 = *(long*)(ptr + 0x20);
            long ptr40 = *(long*)(ptr + 0x40);
            
            if (ptr20 != 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [PROJ_DUMP] Dereferencing Pointer at 0x20: {ptr20:X}", _logger.ColorYellow);
                for (int i = 0; i < 0x40; i += 0x10)
                {
                    long v0 = *(long*)(ptr20 + i);
                    long v8 = *(long*)(ptr20 + i + 8);
                    _logger.WriteLine($"[{_modConfig.ModId}] [PROJ_DUMP] [0x20]+0x{i:X2}: {v0:X16} {v8:X16}", _logger.ColorYellow);
                }
            }
            
            if (ptr40 != 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [PROJ_DUMP] Dereferencing Pointer at 0x40: {ptr40:X}", _logger.ColorYellow);
                for (int i = 0; i < 0x40; i += 0x10)
                {
                    long v0 = *(long*)(ptr40 + i);
                    long v8 = *(long*)(ptr40 + i + 8);
                    _logger.WriteLine($"[{_modConfig.ModId}] [PROJ_DUMP] [0x40]+0x{i:X2}: {v0:X16} {v8:X16}", _logger.ColorYellow);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [PROJ_DUMP] Error: {ex.Message}", _logger.ColorRed);
        }
    }

    /// <summary>
    /// Dump R15 attack structure for reverse engineering
    /// Use these addresses in CheatEngine to investigate projectile creation
    /// </summary>
    private unsafe void DumpR15Structure(long R15)
    {
        try
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] === R15 Structure Dump ===", _logger.ColorGreen);
            
            // Dump key offsets we know
            int actionId = *(int*)(R15 + 0xB0);
            int damage = *(int*)(R15 + 0x174);
            long ptrAt88 = *(long*)(R15 + 0x88);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x00: 0x{*(long*)R15:X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x08: 0x{*(long*)(R15 + 0x08):X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x10: 0x{*(long*)(R15 + 0x10):X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x18: 0x{*(long*)(R15 + 0x18):X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x20: 0x{*(long*)(R15 + 0x20):X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x28: 0x{*(long*)(R15 + 0x28):X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x30: 0x{*(long*)(R15 + 0x30):X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x88 (entity ptr?): 0x{ptrAt88:X}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0xB0 (ActionId): {actionId}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x174 (Damage): {damage}", _logger.ColorGreen);
            
            // Try to find floats (potential position/direction)
            float f1 = *(float*)(R15 + 0x40);
            float f2 = *(float*)(R15 + 0x44);
            float f3 = *(float*)(R15 + 0x48);
            float f4 = *(float*)(R15 + 0x50);
            float f5 = *(float*)(R15 + 0x54);
            float f6 = *(float*)(R15 + 0x58);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x40-0x48 (floats): {f1:F2}, {f2:F2}, {f3:F2}", _logger.ColorGreen);
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] +0x50-0x58 (floats): {f4:F2}, {f5:F2}, {f6:F2}", _logger.ColorGreen);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] === For CheatEngine: Search for these hex values ===", _logger.ColorYellow);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [R15_DUMP] Error: {ex.Message}", _logger.ColorRed);
        }
    }
    
    /// <summary>
    /// Dump magic template structure for reverse engineering
    /// This template contains the base parameters for magic projectiles
    /// </summary>
    private unsafe void DumpMagicTemplate(long template)
    {
        try
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] === Magic Template Dump ===", _logger.ColorYellow);
            
            // The CopyAttackData function copies from template+0x58 onwards
            // So the relevant data starts at offset 0x58
            int actionId = *(int*)(template + 0x58);
            int val5C = *(int*)(template + 0x5C);
            int val60 = *(int*)(template + 0x60);
            int val64 = *(int*)(template + 0x64);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x00: 0x{*(long*)template:X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x08: 0x{*(long*)(template + 0x08):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x10: 0x{*(long*)(template + 0x10):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x18: 0x{*(long*)(template + 0x18):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x20: 0x{*(long*)(template + 0x20):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x28: 0x{*(long*)(template + 0x28):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x30: 0x{*(long*)(template + 0x30):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x38: 0x{*(long*)(template + 0x38):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x40: 0x{*(long*)(template + 0x40):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x48: 0x{*(long*)(template + 0x48):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x50: 0x{*(long*)(template + 0x50):X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x58 (ActionId): {actionId}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x5C: {val5C}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x60: {val60}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] +0x64: {val64}", _logger.ColorYellow);
            
            // Check if template address looks static (in module range)
            long baseAddr = (long)System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress;
            long moduleEnd = baseAddr + System.Diagnostics.Process.GetCurrentProcess().MainModule!.ModuleMemorySize;
            
            bool isStatic = template >= baseAddr && template < moduleEnd;
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] Template is static: {isStatic}", _logger.ColorYellow);
            if (isStatic)
            {
                long offset = template - baseAddr;
                _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] Static offset: 0x{offset:X}", _logger.ColorYellow);
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [TEMPLATE] Error: {ex.Message}", _logger.ColorRed);
        }
    }
    
    #endregion
}