using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameApis;
using ff16.gameplay.truly_eikonic_spells.GameApis.Magic;
using FF16Framework.Interfaces.Nex;
using FF16Framework.Interfaces.Nex.Structures;
using NenTools.ImGui.Interfaces;
using NenTools.ImGui.Interfaces.Shell;
using FF16Tools.Files.Nex;
using FF16Tools.Files.Nex.Entities;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Runtime.InteropServices;
using System.Diagnostics;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ff16.gameplay.truly_eikonic_spells;

public class TrulyEikonicSpellsMod : ModBase
{
    // ============================================================
    // DEBUG FLAGS - Set to false to disable reverse engineering logs
    // ============================================================
    private const bool DEBUG_ON_HIT = true;       
    private const bool DEBUG_BATTLE_TECHNIQUE = false; // Log BattleTechnique calls
    private const bool DEBUG_PERFECT_DODGE = true;  // Log perfect dodge events
    private const bool DEBUG_WINGS_DODGE = false;    // Log Wings of Light dodge handler
    private const bool DEBUG_PLAYER_MODE = false;    // Log player mode changes (spammy)
    private const bool DEBUG_ON_REACTION = true;     // Log OnReaction calls (knockback/stagger)
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
    
    // PhysicsUpdate - now handled by PhysicsApi class
    // See PhysicsApi.cs for physics manipulation logic
    
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
    
    // CopyAttackData hook - copies attack parameters from template to attack struct
    // This is called when creating projectiles/attacks
    public unsafe delegate void CopyAttackDataDelegate(long destAttackStruct, long srcAttackTemplate);
    private IHook<CopyAttackDataDelegate> _copyAttackData;
    
    public delegate long GetOrCreateEntityDelegate(long entityManager, out long outEntityInfo, long entityIdPtr);
    private GetOrCreateEntityDelegate _getOrCreateEntity;
    
    public delegate long GetBnpcIdFromEntityDelegate(long entity);
    private GetBnpcIdFromEntityDelegate _getBnpcIdFromEntity;
    
    // IsSummonModeActive - checks if a specific summon mode is active
    // returns pointer to Eikon struct if active, 0 otherwise
    public delegate long IsSummonModeActiveDelegate(long playerState, int summonModeId);
    private IsSummonModeActiveDelegate _isSummonModeActive;
    
    // Global pointers (same as combo meter)
    private long _globalEntityManagerPtr;
    private long _globalPlayerStatePtr;  // For reading player state like active Eikon
    
    private IStartupScanner _startupScanner;
    
    // Current active Eikon tracking
    private uint _currentEikonMode = 0;
    
    // Systems
    private DiaSystem _diaSystem;
    private DiaraSystem _diaraSystem;
    private DarkraSystem _darkraSystem;
    private PhysicsApi _physicsApi;
    private FunctionApi _functionApi;
    private MagicGameSystem _magicGameSystem;
    private MagicApi _magicApi;
    private PlayerApi _playerApi;
    private ZantetsukenApi _zantetsukenApi;
    private ImGuiConfigurator? _imGuiConfigurator;
    
    // NEX
    private WeakReference<INextExcelDBApiManaged> _managedNexApi;
    public WeakReference<INextExcelDBApi> _rawNexApi;
    
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
        
        // Setup scans
        var scansController = _modLoader.GetController<IStartupScanner>();
        if (!scansController.TryGetTarget(out IStartupScanner scans))
        {
            throw new Exception($"[{_modConfig.ModId}] Unable to get ISharedScans!");
        }
        _startupScanner = scans;

        SetupGameApis();

        SetupModSystems();
        
        SetupImGui();
        
        SetupScans(scans);
        
        // Global pointers
        var baseAddress = Process.GetCurrentProcess().MainModule!.BaseAddress;
        _globalEntityManagerPtr = baseAddress + 0x1816CD0;
        _globalPlayerStatePtr = baseAddress + 0x1816608;  // Same as globalUnk in combo_meter
    }

    private void SetupGameApis()
    {
        // Initialize FunctionApi
        _functionApi = new FunctionApi(_logger, _modConfig);

        // Initialize MagicGameSystem (handles all magic projectile spawning)
        _magicGameSystem = new MagicGameSystem(_logger, _modConfig, _configuration, _startupScanner, _functionApi);
        // Setup MagicGameSystem callbacks
        _magicGameSystem.GetActiveEikon = GetActiveEikon;

        // Initialize MagicApi
        _magicApi = new MagicApi(_logger, _modConfig.ModId, _magicGameSystem);
        
        // Load Dia modifications
        string modDir = _modLoader.GetDirectoryForModId(_modConfig.ModId);
        string diaModPath = Path.Combine(modDir, "Eikon", "Bahamut", "Diara", "DiaModifications.json");
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicApi] Attempting to load modifications from: {diaModPath}", _logger.ColorYellow);
        _magicApi.LoadModifications("DiaModified", diaModPath);

        // Initialize PlayerApi (handles all player-related information)
        _playerApi = new PlayerApi(_logger, _modConfig);
        
        // Get NEX API
        _managedNexApi = _modLoader.GetController<INextExcelDBApiManaged>();
        _rawNexApi = _modLoader.GetController<INextExcelDBApi>();
        
        if (!_managedNexApi.TryGetTarget(out INextExcelDBApiManaged managedApi))
        {
            throw new Exception($"[{_modConfig.ModId}] Could not get INextExcelDBApi!");
        }
    }

    private void SetupImGui()
    {
        var imGuiController = _modLoader.GetController<IImGui>();
        var imGuiShellController = _modLoader.GetController<IImGuiShell>();

        if (imGuiController != null && imGuiShellController != null && 
            imGuiController.TryGetTarget(out var imGui) && imGuiShellController.TryGetTarget(out var imGuiShell))
        {
            _imGuiConfigurator = new ImGuiConfigurator(imGui, _configuration, ConfigurationUpdated, _magicApi);
            imGuiShell.AddComponent(_imGuiConfigurator);
            _logger.WriteLine($"[{_modConfig.ModId}] ImGui Configurator initialized", _logger.ColorGreen);
        }
        else
        {
            _logger.WriteLine($"[{_modConfig.ModId}] ImGui not available (Controller or Shell missing)", _logger.ColorYellow);
        }
    }

    private unsafe void SetupModSystems()
    {   
        // Initialize systems with configuration

        // Initialize APIs
        _zantetsukenApi = new ZantetsukenApi(
            () => _globalPlayerStatePtr, 
            () => _isSummonModeActive, 
            _logger, 
            _modConfig.ModId
        );

        // DIA SYSTEM
        _diaSystem = new DiaSystem(
            maxStacks: _configuration.MaxDiaStacks,
            damagePerStack: _configuration.DiaDamagePerStack,
            logger: _logger,
            modId: _modConfig.ModId
        );
        _diaSystem.DebugLogging = _configuration.DebugLogging;
        
        // DIARA SYSTEM
        _diaraSystem = new DiaraSystem(
            buffDurationSeconds: _configuration.DiaraBuffDuration,
            diaSpellsPerDodge: _configuration.DiaSpellsPerDodge,
            magicID: _configuration.DiaMagicID,
            fanAngleStep: _configuration.DiaFanAngleStep,
            logger: _logger,
            modId: _modConfig.ModId
        );
        _diaraSystem.DebugLogging = _configuration.DebugLogging;
        // Connect MagicApi for all magic operations
        _diaraSystem.SetMagicApi(_magicApi);
        
        // Setup Diara logging
        _diaraSystem.Log = (msg) => _logger.WriteLine($"[{_modConfig.ModId}] {msg}", _logger.ColorGreen);
        
        // Setup Diara logging
        _diaraSystem.Log = (msg) => _logger.WriteLine($"[{_modConfig.ModId}] {msg}", _logger.ColorGreen);
        

        // DARKRA SYSTEM
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
            zantetsukenTicksEnabled: _configuration.ShadowHitZantetsukenTicksEnabled,
            zantetsukenTickAmount: _configuration.ShadowHitZantetsukenTickAmount,
            logger: _logger,
            modId: _modConfig.ModId
        );
        _darkraSystem.DebugLogging = _configuration.DebugLogging;
        
        // Connect systems
        _darkraSystem.GetBattleContext = () => _battleContextForReaction;
        _darkraSystem.ZantetsukenApi = _zantetsukenApi;
        _darkraSystem.FunctionApi = _functionApi;
    }
    

    private unsafe void SetupScans(IStartupScanner scans)
    {
        
        // En SetupScans:
        _playerApi.SetupScans(scans, _hooks!);
        
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
            
            // Initialize PhysicsApi - handles knockback physics manipulation
            try
            {
                _physicsApi = new PhysicsApi(_logger, _modConfig, _configuration);
                _physicsApi.Initialize(_hooks!, baseAddress);
                _logger.WriteLine($"[{_modConfig.ModId}] PhysicsApi initialized", _logger.ColorGreen);
                
                // Set up DarkraSystem hooks now that all hooks are ready
                _darkraSystem.SetHooks(
                    (bnpcRow, R15, a3, a4) => { unsafe { return _onHit.OriginalFunction((long*)bnpcRow, R15, a3, a4); } },
                    (ctx, R15) => _onReaction.OriginalFunction(ctx, R15),
                    () => _battleContextForReaction,
                    _physicsApi.ApplyShadowHitPhysics
                );
                _logger.WriteLine($"[{_modConfig.ModId}] DarkraSystem hooks configured", _logger.ColorGreen);
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] Failed to initialize PhysicsApi: {ex.Message}", _logger.ColorRed);
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
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked CopyAttackData at 0x{address:X}", _logger.ColorGreen);
        });
        
        // Initialize MagicGameSystem hooks (MagicExecute, CastMagic)
        _magicGameSystem.SetupScans(scans, _hooks!);
        
        // Initialize Universal Magic Hooks (Logger, Fuzzer, VTable Mapper)
        _magicGameSystem.InitializeUniversalMagicHooks(_hooks!);
    }
    
    private long OnLevelLoadImpl(long a1, double a2, double a3, double a4)
    {
        _diaSystem.Reset();
        _diaraSystem.Reset();
        _darkraSystem.Reset();
        _magicGameSystem.Reset();
        _currentEikonMode = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] Level loaded, reset all systems", _logger.ColorYellow);
        return _onLevelLoad.OriginalFunction(a1, a2, a3, a4);
    }

    private unsafe long OnHitImpl(long* bnpcRow, long R15, long a3, long a4)
    {
        try
        {
            if (DEBUG_ON_HIT)
                _logger.WriteLine($"[{_modConfig.ModId}] [OnHit] bnpcRowPtr=0x{(long)bnpcRow:X}, actorPtr=0x{*bnpcRow:X}, R15=0x{R15:X}", _logger.ColorYellow);

            // Skip processing for our own shadow hits to prevent recursion and crashes
            // Shadow hits might use transient pointers that are no longer valid for ParseAttackInfo
            if (*(int*)(R15 + 0xB0) == ActionIds.SHADOW_HIT)
            {
                return _onHit.OriginalFunction(bnpcRow, R15, a3, a4);
            }

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
        
        // Don't apply general physics overrides to special shadow hits
        // Shadow hits have their own physics configured in DarkraSystem
        if (*(int*)(param2 + 0xB0) == ActionIds.SHADOW_HIT)
        {
            if (DEBUG_ON_REACTION)
                _logger.WriteLine($"[{_modConfig.ModId}] [REACTION] Processing Shadow Hit reaction, skipping general overrides", _logger.ColorBlue);
            
            _onReaction.OriginalFunction(param1, param2);
            return;
        }

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
        
        // Call original function
        _onReaction.OriginalFunction(param1, param2);
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
        // DiaraSystem handles projectile spawning via MagicApi
        _diaraSystem.OnPerfectDodge();
        
        return _onPerfectDodge.OriginalFunction(a1, a2, a3, a4);
    }
    
    // === MaybeHandleWingsPerfectDodge Handler - for Wings of Light effects ===
    private unsafe long MaybeHandleWingsPerfectDodgeImpl(long a1, long a2)
    {
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
    
    // === BattleTechnique Handler (for logging special abilities only) ===
    private char OnBattleTechniqueImpl(long a1, uint techId, char a3)
    {
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
        // Call original function
        _copyAttackData.OriginalFunction(destAttackStruct, srcAttackTemplate);
    }
    
    private unsafe char StartPlayerModeImpl(long a1, uint playerMode, long a3)
    {
        // Track potential Eikon mode changes
        _currentEikonMode = playerMode;
        
        // === DEBUG: Reverse Engineering - Player Mode changes ===
        if (DEBUG_PLAYER_MODE)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MODE] StartPlayerMode called with playerMode: {playerMode}, a1: 0x{a1:X}", _logger.ColorYellow);
        }
        
        return _startPlayerMode.OriginalFunction(a1, playerMode, a3);
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
            TargetId = *bnpcRow,
            IsCliveAttack = _cliveIds.Contains(atkSource) || (atkSource == 100 && attackTarget != 1),
            IsCliveTarget = _cliveIds.Contains(attackTarget),
            IsHealOrEffect = attackTarget == 1 && rawDmg <= 0
        };
    }
    
    // Use shared AttackInfo from EikonUtils.cs
    
    // Delegate to shared EikonUtils for Eikon detection
    private unsafe int GetActiveEikon() => EikonUtils.GetActiveEikon(_globalPlayerStatePtr);
    
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
        
        // Update PhysicsApi settings
        if (_physicsApi != null)
        {
            _physicsApi.UpdateConfiguration(configuration);
        }

        // Update MagicApi settings (which updates internal system)
        if (_magicApi != null)
        {
            _magicApi.UpdateConfiguration(configuration);
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] Configuration updated!", _logger.ColorGreen);
    }
    
    #endregion
}
