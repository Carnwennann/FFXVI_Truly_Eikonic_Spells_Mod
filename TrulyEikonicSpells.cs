using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.Utils;
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
    private const bool DEBUG_ON_REACTION = true;     // Log OnReaction calls (knockback/stagger)
    
    // STRUCT DUMP FLAGS - Control detailed structure dumps
    private const bool DEBUG_DUMP_TABLE_LAYOUT = false;    // Dump NEX table layouts on load
    private const bool DEBUG_DUMP_TIMELINE = false;        // Dump Timeline object details
    private const bool DEBUG_DUMP_MAGIC_TEMPLATE = false;  // Dump magic attack template structure
    private const bool DEBUG_DUMP_DEST_STRUCTURE = false;  // Dump destination attack structure
    private const bool DEBUG_DUMP_REACTION_DATA = false;   // Dump OnReaction param structures
    private const bool DEBUG_DUMP_MAGIC_HIT = false;       // Dump detailed magic hit info + R15
    private const bool DEBUG_DUMP_PROJECTILE_DATA = false; // Dump projectile data structure
    private const bool DEBUG_DUMP_R15_STRUCTURE = false;   // Dump R15 attack structure
    // ============================================================
    
    private readonly IModLoader _modLoader;
    private readonly IReloadedHooks?  _hooks;
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    
    // Hooks - same patterns as combo meter
    public unsafe delegate long OnHitDelegate(long* bnpcRow, long R15, long a3, long a4);
    private IHook<OnHitDelegate> _onHit;
    
    // OnReaction hook - applies knockback/stagger effects after damage
    // FUN_140592e70: param_1 = battle context, param_2 = R15 (attack struct)
    public unsafe delegate void OnReactionDelegate(long battleContext, long R15);
    private IHook<OnReactionDelegate> _onReaction;
    private long _battleContextForReaction = 0;  // Captured from OnReaction calls
    
    // PhysicsUpdate - now handled by PhysicsSystem class
    // See PhysicsSystem.cs for physics manipulation logic
    
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
    
    // GetTimeline - Retrieves the Timeline/Animation object (kept for debugging)
    // Offset: 0x4692A4
    public unsafe delegate long GetTimelineDelegate(long param_1);
    private IHook<GetTimelineDelegate> _getTimeline;
    
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
    
    // Magic template pointer - for spawning our own projectiles
    private long _lastMagicTemplatePtr = 0;
    private long _lastDestStructPtr = 0;  // Destination attack struct (reused)
    private int _lastMagicActionId = 0;   // Store the last created magic ID to identify it in FireMagic
    
    // Legacy buffers (kept for debugging, may be removed later)
    private IntPtr _projectileDataBuffer = IntPtr.Zero;
    private const int PROJECTILE_DATA_SIZE = 0x500;
    private IntPtr _shadowVTableBuffer = IntPtr.Zero;
    private const int VTABLE_SIZE = 0x800;
    
    // Cache the param_1 from GetTimeline during normal shots for comparison (debugging)
    private long _cachedTimelineParam1 = 0;
    private long _cachedTimelinePtr = 0;
    
    // Wings of Light context - store a1 from when it's called normally
    private long _wingsA1Context = 0;
    
    // Systems
    private DiaSystem _diaSystem;
    private DiaraSystem _diaraSystem;
    private DarkraSystem _darkraSystem;
    private PhysicsSystem _physicsSystem;
    private MagicCastSystem _magicCastSystem;
    
    // NEX
    private WeakReference<INextExcelDBApiManaged> _managedNexApi;
    public WeakReference<INextExcelDBApi> _rawNexApi;
    private readonly NexTableLayout _attackParamLayout;
    
    // Clive IDs (from combo meter)
    private readonly HashSet<uint> _cliveIds = new() { 1, 2, 3, 4, 6, 8, 9, 10 };
    
    // Configuration
    private Config _configuration;
    
    // Constructor sin parámetros requerido por Startup
    public TrulyEikonicSpellsMod() { }
    
    public TrulyEikonicSpellsMod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _configuration = context.Configuration;
        
#if DEBUG
        Debugger.Launch();
#endif
        
        _logger.WriteLine($"[{_modConfig.ModId}] Initializing Truly Eikonic Spells...", _logger.ColorGreen);
        
        // Load NEX layouts (kept for potential future use)
        _attackParamLayout = TableMappingReader.ReadTableLayout("attackparam", new Version(1, 0, 3));
        
        // Initialize systems with configuration
        _diaSystem = new DiaSystem(
            maxStacks: _configuration.MaxDiaStacks,
            damagePerStack: _configuration.DiaDamagePerStack,
            logger: _logger,
            modId: _modConfig.ModId
        );
        _diaSystem.DebugLogging = _configuration.DebugLogging;
        _diaraSystem = new DiaraSystem(
            buffDurationSeconds: _configuration.DiaraBuffDuration,
            diaSpellsPerDodge: _configuration.DiaSpellsPerDodge,
            magicID: _configuration.DiaMagicID,
            logger: _logger,
            modId: _modConfig.ModId
        );
        _diaraSystem.DebugLogging = _configuration.DebugLogging;
        _darkraSystem = new DarkraSystem(
            shadowHitMultiplier: _configuration.ShadowHitMultiplier,
            debuffDuration: _configuration.ShadowDebuffDuration,
            shadowHitDelayMs: _configuration.ShadowHitDelayMs,
            reactionAnimationType: _configuration.ShadowHitReactionType,
            reactionPushDirection: _configuration.ShadowHitReactionIntensity,
            juggleEnabled: _configuration.ShadowHitJuggleEnabled,
            juggleAnimId: _configuration.ShadowHitJuggleAnimId,
            juggleVerticalPush: _configuration.ShadowHitJuggleVerticalPush,
            juggleForwardPush: _configuration.ShadowHitJuggleForwardPush,
            juggleForwardDuration: _configuration.ShadowHitJuggleForwardDuration,
            juggleVerticalInterpolation: _configuration.ShadowHitJuggleVerticalInterpolation,
            logger: _logger,
            modId: _modConfig.ModId
        );
        _darkraSystem.DebugLogging = _configuration.DebugLogging;
        
        _logger.WriteLine($"[{_modConfig.ModId}] Config loaded - Dia: {_configuration.MaxDiaStacks} stacks, Diara: {_configuration.DiaraBuffDuration}s, Darkra: {_configuration.ShadowHitMultiplier * 100}%", _logger.ColorGreen);
        
        // Initialize MagicCastSystem (handles all magic projectile spawning)
        _magicCastSystem = new MagicCastSystem(_logger, _modConfig, _configuration);
        
        // Connect MagicCastSystem to DiaraSystem for direct projectile spawning
        _diaraSystem.SetMagicCastSystem(_magicCastSystem);
        
        // Setup MagicCastSystem callbacks
        _magicCastSystem.GetActiveEikon = GetActiveEikon;
        _magicCastSystem.OnChargedShotDetected = (eikon, mgr, proj) => 
        {
            // Delegate to DiaraSystem for Bahamut charged shot handling
            return _diaraSystem.OnChargedShotCast(eikon);
        };
        
        // Allocate memory for legacy buffers (some may be removed later)
        _projectileDataBuffer = Marshal.AllocHGlobal(PROJECTILE_DATA_SIZE);
        _shadowVTableBuffer = Marshal.AllocHGlobal(VTABLE_SIZE);
        
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
        if (DEBUG_DUMP_TABLE_LAYOUT)
        {
            LogDumpStructs.DumpTableLayout(_logger, _modConfig.ModId, "charatimeline");
            LogDumpStructs.DumpTableLayout(_logger, _modConfig.ModId, "charatimelinevariation");
        }
    }
    
    private unsafe void SetupScans(IStartupScanner scans)
    {
        // OnHit hook - same signature as combo meter
        scans.AddScan("48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 44 8B 82", address =>
        {
            _onHit = _hooks!.CreateHook<OnHitDelegate>(OnHitImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked OnHit at 0x{address:X}", _logger.ColorGreen);
            
            // Also hook OnReaction using hardcoded offset (now that we know hooks work)
            // FUN_140596f44 - processes hit reactions variables, assigns reaction data to target
            var baseAddress = System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64();
            var onReactionAddress = baseAddress + 0x596F44;
            
            try
            {
                _onReaction = _hooks!.CreateHook<OnReactionDelegate>(OnReactionImpl, onReactionAddress).Activate();
                _logger.WriteLine($"[{_modConfig.ModId}] Hooked OnReaction at 0x{onReactionAddress:X} (offset 0x596F44)", _logger.ColorGreen);
                
                // Verify hook was applied by reading first bytes
                byte firstByte = *(byte*)onReactionAddress;
                _logger.WriteLine($"[{_modConfig.ModId}] OnReaction first byte after hook: 0x{firstByte:X2} (should be 0xE9 for JMP)", _logger.ColorYellow);
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] Failed to hook OnReaction: {ex.Message}", _logger.ColorRed);
            }
            
            // Initialize PhysicsSystem - handles knockback physics manipulation
            try
            {
                _physicsSystem = new PhysicsSystem(_logger, _modConfig, _configuration);
                _physicsSystem.Initialize(_hooks!, baseAddress);
                _logger.WriteLine($"[{_modConfig.ModId}] PhysicsSystem initialized", _logger.ColorGreen);
                
                // Set up DarkraSystem hooks now that all hooks are ready
                _darkraSystem.SetHooks(
                    (bnpcRow, R15, a3, a4) => { unsafe { return _onHit.OriginalFunction((long*)bnpcRow, R15, a3, a4); } },
                    (ctx, R15) => _onReaction.OriginalFunction(ctx, R15),
                    () => _battleContextForReaction,
                    _physicsSystem.ApplyShadowHitPhysics
                );
                _logger.WriteLine($"[{_modConfig.ModId}] DarkraSystem hooks configured", _logger.ColorGreen);
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] Failed to initialize PhysicsSystem: {ex.Message}", _logger.ColorRed);
            }
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
        
        // Initialize MagicCastSystem hooks (MagicExecute, CastMagic)
        _magicCastSystem.SetupScans(scans, _hooks!);
        
        // Initialize FireMagicProjectile (uses hardcoded offset)
        var baseAddr = Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64();
        _magicCastSystem.InitializeFireMagicProjectile(_hooks!, baseAddr);
        
        // GetTimeline Hook (0x4692A4) - kept for debugging/logging
        var getTimelineAddr = baseAddr + 0x4692A4;
        _getTimeline = _hooks!.CreateHook<GetTimelineDelegate>(GetTimelineImpl, getTimelineAddr).Activate();
        _logger.WriteLine($"[{_modConfig.ModId}] Hooked GetTimeline at 0x{getTimelineAddr:X}", _logger.ColorGreen);
    }
    
    private long OnLevelLoadImpl(long a1, double a2, double a3, double a4)
    {
        _diaSystem.Reset();
        _diaraSystem.Reset();
        _darkraSystem.Reset();
        _magicCastSystem.Reset();
        _currentEikonMode = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] Level loaded, reset all systems", _logger.ColorYellow);
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
        // DiaraSystem now handles projectile spawning directly via MagicCastSystem
        _diaraSystem.OnPerfectDodge();
        
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
                    if (DEBUG_DUMP_TIMELINE)
                    {
                        LogDumpStructs.LogTimelineObject(_logger, _modConfig.ModId, _cachedTimelineParam1, timelineResult, context);
                    }
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
            if (DEBUG_DUMP_TIMELINE)
            {
                LogDumpStructs.LogTimelineObject(_logger, _modConfig.ModId, param_1, result, _currentActionContext);
            }
            _shouldLogTimeline = false; // Only log once per action
        }
        
        return result;
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
        long lastTarget = _diaraSystem.LastAttackedEnemyPtr;
        
        if (lastTarget == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Cannot spawn - no recent enemy target!", _logger.ColorRed);
            return;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Applying {count} Dia spell damage to last target (0x{lastTarget:X})", _logger.ColorGreen);
        
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
                    if (DEBUG_DUMP_MAGIC_TEMPLATE)
                    {
                        LogDumpStructs.DumpMagicTemplate(_logger, _modConfig.ModId, srcAttackTemplate);
                    }
                    
                    // === NEW: Dump the destination structure BEFORE copy ===
                    // This tells us what's already initialized in destAttackStruct
                    if (DEBUG_DUMP_DEST_STRUCTURE)
                    {
                        LogDumpStructs.DumpDestStructure(_logger, _modConfig.ModId, destAttackStruct);
                    }
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
                int activeEikon = GetActiveEikon();
                
                // === DIA SYSTEM ===
                _diaSystem.OnHit(info.TargetId, info.ActionId, activeEikon, R15, _configuration.EnableDiaSystem);
                
                // === DIARA SYSTEM ===
                _diaraSystem.OnHit(info.TargetId, info.ActionId, R15, _configuration.EnableDiaraSystem);
                
                // === DARKRA SYSTEM (handles shadow hit scheduling internally) ===
                _darkraSystem.OnHit(info.TargetId, info.ActionId, activeEikon, R15, _configuration.EnableDarkraSystem, bnpcRow, a3, a4);
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
    
    // Use shared AttackInfo from EikonUtils.cs
    
    // Delegate to shared EikonUtils for Eikon detection
    private unsafe int GetActiveEikon() => EikonUtils.GetActiveEikon(_globalPlayerStatePtr);
    
    // Delegate to shared EikonUtils
    private static string GetEikonName(int eikonId) => EikonUtils.GetEikonName(eikonId);
    
    // Delegate to shared EikonUtils
    private static EikonUtils.SpellElement GetSpellElement(int eikonId) => EikonUtils.GetSpellElement(eikonId);
    
    /// <summary>
    /// OnReaction implementation - captures battle context and applies knockback/stagger
    /// FUN_140596f44 in Ghidra
    /// 
    /// param_1 (RCX) = Entity/Target pointer structure
    ///   - param_1[0] = Unknown (VTable?)
    ///   - param_1[1] = Entity data pointer (lVar5), has +0x7298 offset for battle data
    ///   - param_1[3] = Previous reaction data (set at end of function)
    ///   - param_1[4] = Some handler with virtual functions
    ///   - param_1[0x1d] & 1 = Skip flag
    /// 
    /// param_2 (RDX) = Attack/Reaction data structure (same as R15 in OnHit)
    ///   - +0x88 = Some ID used for lookups
    ///   - +0x15c = Reaction type ID (uVar12)
    ///   - +0x160 = Secondary reaction value
    ///   - +0x174 = Damage (same as OnHit)
    ///   - +0x184 = Some value reset to 0
    ///   - +0x194 = Flags (bit 12 = special flag, bit 4 = another flag)
    ///   - +0x196 = More flags
    ///   - +0xB0 = Action ID (same as OnHit)
    /// </summary>
    private unsafe void OnReactionImpl(long param1, long param2)
    {
        // Capture the battle context for shadow hits
        _battleContextForReaction = param1;
        
        // === PHYSICS EXPERIMENT: Force PushDirection and Reaction Flags ===
        if (_configuration.EnablePhysicsModification)
        {
            try
            {
                // Read current flags at +0x194
                int flags = *(int*)(param2 + 0x194);
                int originalFlags = flags;
                
                // Check current flag states
                bool has0x2800 = (flags & 0x2800) != 0;
                bool hasBit4 = (flags & 0x10) != 0;
                bool hasBit12 = (flags & 0x1000) != 0;
                
                // Apply flag modifications using TriState enum
                // 0x2800 flag
                if (_configuration.PhysicsFlag0x2800 == TriState.On) flags |= 0x2800;
                else if (_configuration.PhysicsFlag0x2800 == TriState.Off) flags &= ~0x2800;
                
                // bit4 (0x10) flag
                if (_configuration.PhysicsFlagBit4 == TriState.On) flags |= 0x10;
                else if (_configuration.PhysicsFlagBit4 == TriState.Off) flags &= ~0x10;
                
                // bit12 (0x1000) flag
                if (_configuration.PhysicsFlagBit12 == TriState.On) flags |= 0x1000;
                else if (_configuration.PhysicsFlagBit12 == TriState.Off) flags &= ~0x1000;
                
                // Write modified flags if changed
                if (flags != originalFlags)
                {
                    *(int*)(param2 + 0x194) = flags;
                    _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Flags modified: 0x{originalFlags:X8} -> 0x{flags:X8}", _logger.ColorGreen);
                }
                
                _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS-DEBUG] Flags=0x{flags:X8}, bit4={hasBit4}, bit12={hasBit12}, 0x2800={has0x2800}", _logger.ColorYellow);
                
                // Force PushDirection if specified
                if (_configuration.PhysicsForcePushDirection >= 0)
                {
                    int originalPushDir = *(int*)(param2 + 0x160);
                    *(int*)(param2 + 0x160) = _configuration.PhysicsForcePushDirection;
                    _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Forced PushDirection: {originalPushDir} -> {_configuration.PhysicsForcePushDirection}", _logger.ColorGreen);
                }
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Error modifying physics: {ex.Message}", _logger.ColorRed);
            }
        }
        
        if (DEBUG_ON_REACTION && DEBUG_DUMP_REACTION_DATA)
        {
            try
            {
                LogDumpStructs.DumpOnReactionData(_logger, _modConfig.ModId, param1, param2);
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [REACTION] Error dumping: {ex.Message}", _logger.ColorRed);
            }
        }
        
        // Call original function
        _onReaction.OriginalFunction(param1, param2);
    }
    
    /// <summary>
    /// Get human-readable name for reaction animation types
    /// Delegates to centralized ReactionTypes class
    /// </summary>
    private string GetReactionTypeName(int reactionType) => ReactionTypes.GetAnimationName(reactionType);

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
        if (DEBUG_DUMP_MAGIC_HIT)
        {
            LogDumpStructs.LogMagicHitDebug(_logger, _modConfig.ModId, info, R15, bnpcRow, a3, a4);
        }
    }
    
    /// <summary>
    /// Dump ProjectileData structure (RDX in FireMagicProjectile)
    /// </summary>
    private unsafe void DumpProjectileData(long ptr)
    {
        if (DEBUG_DUMP_PROJECTILE_DATA)
        {
            LogDumpStructs.DumpProjectileData(_logger, _modConfig.ModId, ptr);
        }
    }

    /// <summary>
    /// Dump R15 attack structure for reverse engineering
    /// </summary>
    private unsafe void DumpR15Structure(long R15)
    {
        if (DEBUG_DUMP_R15_STRUCTURE)
        {
            LogDumpStructs.DumpR15Structure(_logger, _modConfig.ModId, R15);
        }
    }
    
    /// <summary>
    /// Dump magic template structure for reverse engineering
    /// </summary>
    private unsafe void DumpMagicTemplate(long template)
    {
        if (DEBUG_DUMP_MAGIC_TEMPLATE)
        {
            LogDumpStructs.DumpMagicTemplate(_logger, _modConfig.ModId, template);
        }
    }
    
    #endregion
    
    #region Standard Overrides
    
    /// <summary>
    /// Called when the mod configuration is updated at runtime.
    /// This allows hot-reloading of settings without restarting the game.
    /// </summary>
    public override void ConfigurationUpdated(Config configuration)
    {
        _configuration = configuration;
        
        // Update DiaSystem settings
        if (_diaSystem != null)
        {
            _diaSystem.UpdateConfiguration(configuration);
        }
        
        // Update DiaraSystem settings
        if (_diaraSystem != null)
        {
            _diaraSystem.UpdateConfiguration(configuration);
        }
        
        // Update DarkraSystem settings
        if (_darkraSystem != null)
        {
            _darkraSystem.UpdateConfiguration(configuration);

        }
        
        // Update PhysicsSystem settings
        if (_physicsSystem != null)
        {
            _physicsSystem.UpdateConfiguration(configuration);
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] Configuration updated!", _logger.ColorGreen);
    }
    
    #endregion
}