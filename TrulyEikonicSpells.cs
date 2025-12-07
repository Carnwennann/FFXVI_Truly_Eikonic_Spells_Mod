using ff16.gameplay.truly_eikonic_spells.Configuration;
using FF16Framework.Interfaces.Nex;
using FF16Framework.Interfaces.Nex.Structures;
using FF16Tools.Files.Nex;
using FF16Tools.Files.Nex.Entities;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Diagnostics;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ff16.gameplay.truly_eikonic_spells;

public class TrulyEikonicSpellsMod : ModBase
{
    // ============================================================
    // DEBUG FLAGS - Set to false to disable reverse engineering logs
    // ============================================================
    private const bool DEBUG_MAGIC_HIT = true;      // Log detailed magic hit info + R15 dump
    private const bool DEBUG_BATTLE_TECHNIQUE = true; // Log BattleTechnique calls
    private const bool DEBUG_PERFECT_DODGE = true;   // Log perfect dodge events
    private const bool DEBUG_WINGS_DODGE = true;     // Log Wings of Light dodge handler
    private const bool DEBUG_PLAYER_MODE = false;    // Log player mode changes (spammy)
    private const bool DEBUG_COPY_ATTACK_DATA = true; // Log CopyAttackData calls (projectile creation)
    private const bool DEBUG_PREPARE_TEMPLATE = true; // Log PrepareAttackTemplate calls
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
    
    // Wings of Light context - store a1 from when it's called normally
    private long _wingsA1Context = 0;
    
    // Systems
    private DiaSystem _diaSystem;
    private DiaraSystem _diaraSystem;
    
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
        // Dump attack param layout to discover fields
        DumpAttackParamLayout();
    }
    
    // Dump all attackparam columns to find element-related fields
    private void DumpAttackParamLayout()
    {
        _logger.WriteLine($"[{_modConfig.ModId}] === ATTACKPARAM LAYOUT ===", _logger.ColorYellow);
        foreach (var col in _attackParamLayout.Columns)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Column: {col.Key}, Offset: {col.Value.Offset}", _logger.ColorYellow);
        }
        _logger.WriteLine($"[{_modConfig.ModId}] === END LAYOUT ===", _logger.ColorYellow);
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
    }
    
    private long OnLevelLoadImpl(long a1, double a2, double a3, double a4)
    {
        _diaSystem.Reset();
        _diaraSystem.Reset();
        _currentEikonMode = 0;
        _logger.WriteLine($"[{_modConfig.ModId}] Level loaded, reset Dia/Diara systems and Eikon mode", _logger.ColorYellow);
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
            _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Should spawn {diaSpellsToSpawn} Dia spells! Last target: 0x{_lastAttackedEnemyPtr:X}", _logger.ColorGreen);
            
            // Try to trigger Wings of Light effect if we have context
            TryTriggerWingsEffect(a2);
        }
        
        return _onPerfectDodge.OriginalFunction(a1, a2, a3, a4);
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
        // TODO: Actually spawn the Dia spells
        // This requires finding how to spawn projectiles in the game
        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Spawning {spellCount} Dia spells triggered!", _logger.ColorGreen);
        
        // EXPERIMENT: Try to trigger Dia spell using BattleTechnique
        // We need to find the correct techId for Dia spell
        // Common magic shot IDs from OnHit: 218 (air), 219 (ground), 227 (charged)
        // BattleTechnique might use different IDs
        TrySpawnDiaSpells(spellCount);
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
                
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] === Attack Data Copy ===", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Dest (attack struct): 0x{destAttackStruct:X}", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Src (template): 0x{srcAttackTemplate:X}", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] ActionId from template: {actionId}", _logger.ColorGreen);
                
                // Check if this is a magic projectile (218, 219, 227)
                if (actionId == 218 || actionId == 219 || actionId == 227)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] >>> MAGIC PROJECTILE DETECTED! <<<", _logger.ColorYellow);
                    
                    // Store the template pointer - this is the key to spawning our own projectiles!
                    _lastMagicTemplatePtr = srcAttackTemplate;
                    _logger.WriteLine($"[{_modConfig.ModId}] [COPY_ATTACK] Stored magic template: 0x{srcAttackTemplate:X}", _logger.ColorYellow);
                    
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
        _logger.WriteLine($"[{_modConfig.ModId}] [DEST_STRUCT] === Destination Structure Before Copy ===", _logger.ColorCyan);
        
        // Dump first 0x100 bytes to see what's already there
        for (int i = 0; i < 0x100; i += 0x10)
        {
            long val0 = *(long*)(destStruct + i);
            long val8 = *(long*)(destStruct + i + 8);
            _logger.WriteLine($"[{_modConfig.ModId}] [DEST_STRUCT] +0x{i:X2}: 0x{val0:X16} | 0x{val8:X16}", _logger.ColorCyan);
        }
        
        // Check important offsets
        long entityPtr = *(long*)destStruct;
        int destActionId = *(int*)(destStruct + 0x58);
        _logger.WriteLine($"[{_modConfig.ModId}] [DEST_STRUCT] Entity ptr at +0x00: 0x{entityPtr:X}", _logger.ColorCyan);
        _logger.WriteLine($"[{_modConfig.ModId}] [DEST_STRUCT] ActionId at +0x58 (before copy): {destActionId}", _logger.ColorCyan);
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
                
                // Store last attacked enemy for Diara system (for magic shots only)
                bool isMagicShot = info.ActionId == 218 || info.ActionId == 219 || info.ActionId == 227;
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
                
                // === DIARA SYSTEM: Charged Shot (227) with Bahamut activates buff ===
                if (info.ActionId == DiaraSystem.CHARGED_SHOT_ACTION_ID && activeEikon == 8)
                {
                    // Activate Diara buff
                    bool shouldSuppressProjectile = _diaraSystem.OnChargedShotCast(activeEikon);
                    
                    if (shouldSuppressProjectile)
                    {
                        // TODO: Actually suppress the projectile damage
                        // For now, we still let it hit but the buff is activated
                        _logger.WriteLine($"[{_modConfig.ModId}] [DIARA] Charged Shot detected - buff activated! (projectile suppression TODO)", _logger.ColorGreen);
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
    public enum SpellElement { None, Fire, Dia, Dark, Aero, Ice, Thunder, Earth, Water, Ruin }
    
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