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
    
    // Systems
    private DiaSystem _diaSystem;
    
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
    }
    
    private long OnLevelLoadImpl(long a1, double a2, double a3, double a4)
    {
        _diaSystem.Reset();
        _currentEikonMode = 0;
        _logger.WriteLine($"[{_modConfig.ModId}] Level loaded, reset Dia stacks and Eikon mode", _logger.ColorYellow);
        return _onLevelLoad.OriginalFunction(a1, a2, a3, a4);
    }
    
    private unsafe char StartPlayerModeImpl(long a1, uint playerMode, long a3)
    {
        // Save the player mode structure pointer
        _modeA1 = a1;
        
        // Log mode changes for debug
        _logger.WriteLine($"[{_modConfig.ModId}] [MODE] StartPlayerMode called with playerMode: {playerMode}, a1: 0x{a1:X}", _logger.ColorYellow);
        
        // Track potential Eikon mode changes
        _currentEikonMode = playerMode;
        
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
                
                // DEBUG: Log ALL attack IDs to discover ability IDs
                _logger.WriteLine($"[{_modConfig.ModId}] [HIT] ActionId: {info.ActionId}, Eikon: {GetEikonName(activeEikon)}", _logger.ColorYellow);
                
                // Process Dia system - handles both stacking (Bahamut only) and synergies (any Eikon)
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
            TargetId = (long)bnpcRow,
            IsCliveAttack = _cliveIds.Contains(atkSource) || (atkSource == 100 && attackTarget != 1),
            IsCliveTarget = _cliveIds.Contains(attackTarget),
            IsHealOrEffect = attackTarget == 1 && rawDmg <= 0
        };
    }
    
    private struct AttackInfo
    {
        public int ActionId;
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
}