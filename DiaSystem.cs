using System.Collections.Concurrent;

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
    
    // Configuration
    private const int MAX_DIA_STACKS = 50;
    private const float DAMAGE_PER_STACK = 0.01f; // 1% per stack
    
    #region Action IDs
    
    // === Bahamut abilities that ADD Dia stacks ===
    private static readonly HashSet<int> _bahamutStackBuildingAbilities = new()
    {
        199,  // Magic Burst 1
        200,  // Magic Burst 2
        201,  // Magic Burst 3
        202,  // Magic Burst Finish
        218,  // Air magic shot
        219,  // Ground magic shot
    };

    // === Universal abilities that ADD Dia stacks ===
    private static readonly HashSet<int> _universalStackBuildingAbilities = new()
    {
        0,    // Satellites?? (need to confirm ID)
    };
    
    // === Abilities that CONSUME Dia stacks ===
    private static readonly HashSet<int> _stackConsumingAbilities = new()
    {
        222,  // Precision Shot
        227,  // Charged magic shot
    };
    
    // === Abilities that SYNERGIZE (benefit from stacks without consuming) ===
    private static readonly HashSet<int> _synergyAbilities = new()
    {
        199,  // Magic Burst 1
        200,  // Magic Burst 2
        201,  // Magic Burst 3
        202,  // Magic Burst Finish
        218,  // Air magic shot
        219,  // Ground magic shot
        222,  // Precision Shot
        227,  // Charged magic shot
        
        // Bahamut abilities
        0,    // Satellites?? (need to confirm ID)
        776,  // Megaflare lvl1
        777,  // Megaflare lvl2
        800,  // Megaflare lvl3
        801,  // Megaflare lvl4
        824,  // Impulse
        830,  // Flare Breath
        845,  // Gigaflare
        
        // Shiva abilities
        747,  // Mesmerize
        748,  // Aerial Mesmerize
        
        // Ramuh abilities
        628,  // Blind Justice
        
        // Phoenix abilities
        376,  // Heatwave
        
        // Leviathan abilities
        1028, // Precision Tidal Torrent
        1029, // Dodge Tidal Stream
        1046, // Tidal Torrent
        1053, // Charged Torrent
        1065, // Tidal Stream
        1069, // Charged Stream
        1078, // Deluge
        1091, // Cross Swell
        1096, // Abyssal Tear
        1097, // Charged Abyssal Tear
        1098, // Aerial Abyssal Tear
        1099, // Charged Aerial Abyssal Tear
        1124, // Tsunami
    };
    
    #endregion
    
    // Bahamut Eikon ID
    private const int EIKON_BAHAMUT = 8;
    
    public DiaSystem()
    {
    }
    
    /// <summary>
    /// Process a potential Dia hit
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
                result.DamageMultiplier = 1.0f + (stacks * DAMAGE_PER_STACK);
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
            (_, currentStacks) => Math.Min(currentStacks + 1, MAX_DIA_STACKS)
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
}

public struct DiaResult
{
    public bool WasStackingHit;        // stack-building (Bahamut only)
    public bool WasSynergyHit;    // Synergy ability (any Eikon)
    public int CurrentStacks;
    public int StacksConsumed;
    public float DamageMultiplier;
}
