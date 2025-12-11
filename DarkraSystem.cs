using System.Collections.Concurrent;

namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Darkra System: Shadow debuff from Odin's Dark magic
/// 
/// MECHANICS:
/// - When an enemy is hit by Darkra (Odin's magic shot), they receive a Shadow debuff
/// - While the Shadow debuff is active, ANY hit on that enemy triggers a second "shadow" hit
/// - The shadow hit deals 50% of the original damage
/// - Hitting with Darkra again resets the debuff duration
/// </summary>
public class DarkraSystem
{
    // Track Shadow debuff per enemy (targetId -> expiration time)
    private readonly ConcurrentDictionary<long, DateTime> _shadowDebuffs = new();

    // Use shared Eikon constant
    private const int EIKON_ODIN = EikonUtils.EIKON_ODIN;
    
    // Configuration
    private const float SHADOW_HIT_MULTIPLIER = 0.5f;  // Shadow hit deals 50% of original damage
    private const float DEBUFF_DURATION = 10.0f;       // Debuff lasts 10 seconds
    
    #region Action IDs
    
    // === Odin abilities that APPLY Shadow debuff ===
    private static readonly HashSet<int> _darkraAbilities = new()
    {
        // Magic shots with Odin active become Darkra
        222,  // Precision Shot (becomes Darkra with Odin)
        227,  // Charged magic shot (becomes Darkra with Odin)
        228,  // Aerial Charged Shot (becomes Darkra with Odin)
    };
    
    // === Abilities that DON'T trigger shadow hit (to prevent infinite loops) ===
    private static readonly HashSet<int> _excludedFromShadowHit = new()
    {
        // Add any action IDs that shouldn't trigger shadow damage
    };
    
    #endregion

    
    public DarkraSystem()
    {
    }
    
    /// <summary>
    /// Process a hit and check for Darkra mechanics
    /// </summary>
    public unsafe DarkraResult ProcessHit(long targetId, int actionId, int activeEikon, long R15)
    {
        var result = new DarkraResult();
        bool isOdin = (activeEikon == EIKON_ODIN);
        
        // Clean up expired debuffs
        CleanupExpiredDebuffs();
        
        // Step 1: Check if this attack should APPLY/RESET the Shadow debuff
        if (isOdin && _darkraAbilities.Contains(actionId))
        {
            ApplyDebuff(targetId);
            result.AppliedDebuff = true;
        }
        
        // Step 2: Check if target has Shadow debuff and should take extra hit
        if (HasShadowDebuff(targetId) && !_excludedFromShadowHit.Contains(actionId))
        {
            // Calculate shadow damage (flat 50%)
            int originalDamage = *(int*)(R15 + 0x174);
            int shadowDamage = (int)(originalDamage * SHADOW_HIT_MULTIPLIER);
            
            result.TriggeredShadowHit = true;
            result.ShadowDamage = shadowDamage;
            
            // Apply the shadow damage by adding to the original hit
            *(int*)(R15 + 0x174) = originalDamage + shadowDamage;
        }
        
        return result;
    }
    
    /// <summary>
    /// Apply or reset the Shadow debuff on a target
    /// </summary>
    private void ApplyDebuff(long targetId)
    {
        var expirationTime = DateTime.UtcNow.AddSeconds(DEBUFF_DURATION);
        _shadowDebuffs[targetId] = expirationTime;
    }
    
    /// <summary>
    /// Check if target has an active Shadow debuff
    /// </summary>
    public bool HasShadowDebuff(long targetId)
    {
        if (_shadowDebuffs.TryGetValue(targetId, out var expiration))
        {
            return DateTime.UtcNow < expiration;
        }
        return false;
    }
    
    /// <summary>
    /// Get remaining duration of debuff in seconds
    /// </summary>
    public float GetRemainingDuration(long targetId)
    {
        if (_shadowDebuffs.TryGetValue(targetId, out var expiration))
        {
            var remaining = (expiration - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? (float)remaining : 0;
        }
        return 0;
    }
    
    /// <summary>
    /// Remove expired debuffs from tracking
    /// </summary>
    private void CleanupExpiredDebuffs()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _shadowDebuffs
            .Where(kvp => kvp.Value <= now)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in expiredKeys)
        {
            _shadowDebuffs.TryRemove(key, out _);
        }
    }
    
    /// <summary>
    /// Get total number of enemies with active Shadow debuff
    /// </summary>
    public int GetActiveDebuffCount()
    {
        CleanupExpiredDebuffs();
        return _shadowDebuffs.Count;
    }
    
    /// <summary>
    /// Reset all debuffs (e.g., on area transition)
    /// </summary>
    public void Reset()
    {
        _shadowDebuffs.Clear();
    }
}

/// <summary>
/// Result of processing a hit through the Darkra system
/// </summary>
public struct DarkraResult
{
    public bool AppliedDebuff;      // True if this hit applied/reset Shadow debuff
    public bool TriggeredShadowHit; // True if target had debuff and took shadow damage
    public int ShadowDamage;        // Amount of shadow damage dealt
}
