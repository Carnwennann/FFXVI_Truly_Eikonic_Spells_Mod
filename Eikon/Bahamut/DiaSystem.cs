using System.Collections.Concurrent;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.Utils;

namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Dia System: Stacking damage bonus from Bahamut Dia spell
/// 
/// STACKING (requires Bahamut mode because it's his unique Dia mechanic)
/// 
/// SYNERGY (works with ANY Eikon):
/// - Synergy abilities benefit from stacks and most of them consumes them
/// - Works cross-Eikon (e.g., Shiva's Mesmerize benefits from Dia stacks)
/// 
/// Max 50 stacks = 50% bonus damage (1% per stack)
/// </summary>
public class DiaSystem
{
    // Track Dia stacks per enemy (using target pointer as ID)
    private readonly ConcurrentDictionary<long, int> _diaStacks = new();
    
    // Logger for debug output
    private readonly ILogger? _logger;
    private readonly string _modId;
    public bool DebugLogging { get; set; } = true;
    
    // Configuration (settable for hot-reload)
    public int MaxStacks { get; set; }
    public float DamagePerStack { get; set; }
    
    #region Action IDs
    
    // === Bahamut abilities that ADD Dia stacks ===
    private static readonly HashSet<int> _bahamutStackBuildingAbilities = new()
    {
        ActionIds.MAGIC_BURST_1,
        ActionIds.MAGIC_BURST_2,
        ActionIds.MAGIC_BURST_3,
        ActionIds.MAGIC_BURST_FINISH,
        ActionIds.NORMAL_SHOT_AIR,
        ActionIds.NORMAL_SHOT_GROUND,
    };

    // === Universal abilities that ADD Dia stacks ===
    private static readonly HashSet<int> _universalStackBuildingAbilities = new()
    {
        ActionIds.SATELLITES,  // Need to confirm ID
    };
    
    // === Abilities that CONSUME Dia stacks ===
    private static readonly HashSet<int> _stackConsumingAbilities = new()
    {
        ActionIds.PRECISION_SHOT,
        ActionIds.CHARGED_SHOT,
    };
    
    // === Abilities that SYNERGIZE (benefit from stacks without consuming) ===
    private static readonly HashSet<int> _synergyAbilities = new()
    {
        // Universal magic
        ActionIds.MAGIC_BURST_1,
        ActionIds.MAGIC_BURST_2,
        ActionIds.MAGIC_BURST_3,
        ActionIds.MAGIC_BURST_FINISH,
        ActionIds.NORMAL_SHOT_AIR,
        ActionIds.NORMAL_SHOT_GROUND,
        ActionIds.PRECISION_SHOT,
        ActionIds.CHARGED_SHOT,
        
        // Bahamut abilities
        ActionIds.SATELLITES,
        ActionIds.MEGAFLARE_LVL1,
        ActionIds.MEGAFLARE_LVL2,
        ActionIds.MEGAFLARE_LVL3,
        ActionIds.MEGAFLARE_LVL4,
        ActionIds.IMPULSE,
        ActionIds.FLARE_BREATH,
        ActionIds.GIGAFLARE,
        
        // Shiva abilities
        ActionIds.MESMERIZE,
        ActionIds.AERIAL_MESMERIZE,
        
        // Ramuh abilities
        ActionIds.BLIND_JUSTICE,
        
        // Phoenix abilities
        ActionIds.HEATWAVE,
        
        // Leviathan abilities
        ActionIds.PRECISION_TIDAL_TORRENT,
        ActionIds.DODGE_TIDAL_STREAM,
        ActionIds.TIDAL_TORRENT,
        ActionIds.CHARGED_TORRENT,
        ActionIds.TIDAL_STREAM,
        ActionIds.CHARGED_STREAM,
        ActionIds.DELUGE,
        ActionIds.CROSS_SWELL,
        ActionIds.ABYSSAL_TEAR,
        ActionIds.CHARGED_ABYSSAL_TEAR,
        ActionIds.AERIAL_ABYSSAL_TEAR,
        ActionIds.CHARGED_AERIAL_ABYSSAL_TEAR,
        ActionIds.TSUNAMI,
    };
    
    #endregion
    
    // Use shared Eikon constant
    private const int EIKON_BAHAMUT = EikonUtils.EIKON_BAHAMUT;
    
    public DiaSystem(int maxStacks = 50, float damagePerStack = 0.01f, ILogger? logger = null, string modId = "")
    {
        MaxStacks = maxStacks;
        DamagePerStack = damagePerStack;
        _logger = logger;
        _modId = modId;
    }
    
    #region Logging
    
    private void Log(string message, System.Drawing.Color? color = null)
    {
        if (!DebugLogging || _logger == null) return;
        _logger.WriteLine($"[{_modId}] [DIA] {message}", color ?? _logger.ColorGreen);
    }
    
    private void LogDebug(string message)
    {
        if (!DebugLogging || _logger == null) return;
        _logger.WriteLine($"[{_modId}] [DIA] {message}", _logger.ColorYellow);
    }
    
    #endregion
    
    #region Main Hook Entry Points
    
    /// <summary>
    /// Called from TrulyEikonicSpells.OnHitImpl - handles all Dia logic
    /// </summary>
    /// <param name="targetId">Enemy target ID (bnpcRow pointer)</param>
    /// <param name="actionId">The action ID of the attack</param>
    /// <param name="activeEikon">Currently active Eikon ID</param>
    /// <param name="R15">Attack info pointer for damage modification</param>
    /// <param name="isEnabled">Whether Dia System is enabled in config</param>
    public unsafe void OnHit(long targetId, int actionId, int activeEikon, long R15, bool isEnabled)
    {
        if (!isEnabled) return;
        
        var result = ProcessHit(targetId, actionId, activeEikon, R15);
        
        // Log results
        if (result.WasStackingHit)
        {
            string bonusText = result.DamageMultiplier > 1.0f ? $" (Damage x{result.DamageMultiplier:F2})" : "";
            Log($"+1 stack! Total: {result.CurrentStacks}/{MaxStacks}{bonusText}");
        }
        else if (result.StacksConsumed > 0)
        {
            Log($"Consumed {result.StacksConsumed} stacks! Damage x{result.DamageMultiplier:F2}");
        }
        else if (result.WasSynergyHit)
        {
            Log($"Synergy! Damage x{result.DamageMultiplier:F2} ({result.CurrentStacks} stacks)");
        }
    }
    
    #endregion
    
    #region Internal Processing
    
    /// <summary>
    /// Process a potential Dia hit (internal logic)
    /// </summary>
    /// <param name="targetId">Enemy target ID</param>
    /// <param name="actionId">The action ID of the attack</param>
    /// <param name="activeEikon">Currently active Eikon ID</param>
    /// <param name="R15">Attack info pointer for damage modification</param>
    public unsafe DiaResult ProcessHit(long targetId, int actionId, int activeEikon, long R15)
    {
        var result = new DiaResult();
        bool isBahamut = (activeEikon == EIKON_BAHAMUT);
        
        // Stack-building abilities with Bahamut (Magic Burst)
        if ((isBahamut && _bahamutStackBuildingAbilities.Contains(actionId)) // Bahamut-only abilities that add stacks
            || _universalStackBuildingAbilities.Contains(actionId))  // Universal abilities that add stacks
        {
            int currentStacks = GetDiaStacks(targetId);
            AddDiaStack(targetId);
            result.WasStackingHit = true;
            result.CurrentStacks = GetDiaStacks(targetId);
        }
        
        // Synergy abilities: apply bonus WITHOUT consuming (works with any Eikon)
        if (_synergyAbilities.Contains(actionId))
        {
            int stacks = GetDiaStacks(targetId);
            if (stacks > 0)
            {
                result.DamageMultiplier = 1.0f + (stacks * DamagePerStack);
                result.WasSynergyHit = true;
                result.CurrentStacks = stacks;
                ApplyDamageBonus(R15, result.DamageMultiplier);
            }
        }

        // Stack-consuming abilities
        if (_stackConsumingAbilities.Contains(actionId))
        {
            int stacks = GetDiaStacks(targetId);
            if (stacks > 0)
            {
                result.StacksConsumed = stacks;
                ClearDiaStacks(targetId);
                result.CurrentStacks = 0;
            }
        }
        
        return result;
    }
    
    private unsafe void ApplyDamageBonus(long R15, float multiplier)
    {
        int* dmgPtr = (int*)(R15 + 0x174);
        int originalDmg = *dmgPtr;
        *dmgPtr = (int)(originalDmg * multiplier);
    }
    
    private void AddDiaStack(long targetId)
    {
        _diaStacks.AddOrUpdate(
            targetId,
            1,
            (_, currentStacks) => Math.Min(currentStacks + 1, MaxStacks)
        );
    }
    
    public int GetDiaStacks(long targetId)
    {
        return _diaStacks.TryGetValue(targetId, out int stacks) ? stacks : 0;
    }
    
    private void ClearDiaStacks(long targetId)
    {
        _diaStacks.TryRemove(targetId, out _);
    }
    
    public int GetTotalStacks()
    {
        int total = 0;
        foreach (var kvp in _diaStacks)
            total += kvp.Value;
        return total;
    }
    
    public void Reset()
    {
        _diaStacks.Clear();
    }

    public void UpdateConfiguration(Config configuration)
    {
        // Check for changes and log
        if (MaxStacks != configuration.MaxDiaStacks)
            LogDebug($"MaxStacks changed: {MaxStacks} -> {configuration.MaxDiaStacks}");
            MaxStacks = configuration.MaxDiaStacks;
        if (DamagePerStack != configuration.DiaDamagePerStack)
            LogDebug($"DamagePerStack changed: {DamagePerStack} -> {configuration.DiaDamagePerStack}");
            DamagePerStack = configuration.DiaDamagePerStack;
    }
    
    #endregion
}

public struct DiaResult
{
    public bool WasStackingHit;        // stack-building (Bahamut only)
    public bool WasSynergyHit;    // Synergy ability (any Eikon)
    public int CurrentStacks;
    public int StacksConsumed;
    public float DamageMultiplier;
}
