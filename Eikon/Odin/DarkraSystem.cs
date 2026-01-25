using System.Collections.Concurrent;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameApis;
using ff16.gameplay.truly_eikonic_spells.GameApis.Actor;
using ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Darkra System: Shadow debuff from Odin's Dark magic
/// 
/// MECHANICS:
/// - When an enemy is hit by Darkra (Odin's magic shot), they receive a Shadow debuff
/// - While the Shadow debuff is active, ANY hit on that enemy triggers a second "shadow" hit
/// - The shadow hit deals a percentage of the original damage (after a short delay)
/// - Hitting with Darkra again resets the debuff duration
/// - Shadow hits can juggle enemies in the air with custom physics
/// </summary>
public class DarkraSystem
{
    // Track Shadow debuff per enemy (targetId -> expiration time)
    private readonly ConcurrentDictionary<long, DateTime> _shadowDebuffs = new();

    // Logger for debug output
    private readonly ILogger? _logger;
    private readonly string _modId;
    public bool DebugLogging { get; set; } = true;

    // Use shared Eikon constant
    private const int EIKON_ODIN = EikonUtils.EIKON_ODIN;
    
    // === Function pointers for calling game functions ===
    // Using Func/Action to avoid duplicate delegate definitions
    private Func<long, long, long, long, long>? _onHitOriginal;  // (bnpcRow, R15, a3, a4) -> result
    private Action<long, long>? _onReactionOriginal;              // (battleContext, R15)
    public Func<long>? GetBattleContext { get; set; }             // Returns current battle context - Exposed for TrulyEikonicSpells
    private Action<float, float, float, float>? _applyPhysics;    // Physics system callback
    
    // === Core Configuration ===
    public float ShadowHitMultiplier { get; set; }
    public float DebuffDuration { get; set; }
    public int ShadowHitDelayMs { get; set; }
    
    // === Reaction Configuration ===
    public int ReactionAnimationType { get; set; }
    public int ReactionPushDirection { get; set; }
    
    // === Juggle Physics Configuration ===
    public bool JuggleEnabled { get; set; }
    public int JuggleAnimId { get; set; }
    public float JuggleVerticalPush { get; set; }
    public float JuggleForwardPush { get; set; }
    public float JuggleForwardDuration { get; set; }
    public float JuggleVerticalInterpolation { get; set; }
    
    // === Zantetsuken Ticks Configuration ===
    public bool ZantetsukenTicksEnabled { get; set; }
    public int ZantetsukenTickAmount { get; set; }
    public ZantetsukenApi? ZantetsukenApi { get; set; }
    public IActorApi? ActorApi { get; set; }
    
    #region Action IDs
    
    // === Odin abilities that APPLY Shadow debuff ===
    private static readonly HashSet<int> _darkraAbilities = new()
    {
        // Magic shots with Odin active become Darkra
        ActionIds.PRECISION_SHOT,
        ActionIds.CHARGED_SHOT,
        ActionIds.AERIAL_CHARGED_SHOT,
    };
    
    // === Abilities that DON'T trigger shadow hit (to prevent infinite loops) ===
    private static readonly HashSet<int> _excludedFromShadowHit = new()
    {
        // Shadow hit itself (prevent recursion)
        ActionIds.SHADOW_HIT,
        
        // Magic shots - the Darkra that applies the debuff shouldn't also trigger shadow damage
        // This prevents double-dipping on the initial application
        ActionIds.PRECISION_SHOT,
        ActionIds.CHARGED_SHOT,
        ActionIds.AERIAL_CHARGED_SHOT,
    };
    
    #endregion

    
    public DarkraSystem(
        float shadowHitMultiplier = 0.1f, 
        float debuffDuration = 120.0f, 
        int shadowHitDelayMs = 1000,
        int reactionAnimationType = 2,
        int reactionPushDirection = 2,
        bool juggleEnabled = true,
        int juggleAnimId = 6,
        float juggleVerticalPush = 1.0f,
        float juggleForwardPush = -0.1f,
        float juggleForwardDuration = 0.5f,
        float juggleVerticalInterpolation = 0.3f,
        bool zantetsukenTicksEnabled = true,
        int zantetsukenTickAmount = 35,
        ILogger? logger = null,
        string modId = "")
    {
        ShadowHitMultiplier = shadowHitMultiplier;
        DebuffDuration = debuffDuration;
        ShadowHitDelayMs = shadowHitDelayMs;
        ReactionAnimationType = reactionAnimationType;
        ReactionPushDirection = reactionPushDirection;
        JuggleEnabled = juggleEnabled;
        JuggleAnimId = juggleAnimId;
        JuggleVerticalPush = juggleVerticalPush;
        JuggleForwardPush = juggleForwardPush;
        JuggleForwardDuration = juggleForwardDuration;
        JuggleVerticalInterpolation = juggleVerticalInterpolation;
        ZantetsukenTicksEnabled = zantetsukenTicksEnabled;
        ZantetsukenTickAmount = zantetsukenTickAmount;
        _logger = logger;
        _modId = modId;
    }
    
    /// <summary>
    /// Set the game function hooks needed to execute shadow hits
    /// Call this after hooks are initialized in the main mod
    /// </summary>
    public void SetHooks(
        Func<long, long, long, long, long> onHitOriginal,
        Action<long, long> onReactionOriginal,
        Func<long> getBattleContext,
        Action<float, float, float, float> applyPhysics)
    {
        _onHitOriginal = onHitOriginal;
        _onReactionOriginal = onReactionOriginal;
        GetBattleContext = getBattleContext;
        _applyPhysics = applyPhysics;
    }
    
    #region Logging
    
    private void Log(string message, System.Drawing.Color? color = null)
    {
        if (!DebugLogging || _logger == null) return;
        _logger.WriteLine($"[{_modId}] [DARKRA] {message}", color ?? _logger.ColorBlue);
    }
    
    private void LogDebug(string message)
    {
        if (_logger == null) return;
        _logger.WriteLine($"[{_modId}] [DARKRA] {message}", _logger.ColorYellow);
    }
    
    #endregion
    
    #region Main Hook Entry Points
    
    /// <summary>
    /// Called from TrulyEikonicSpells.OnHitImpl - handles all Darkra logic
    /// Automatically schedules shadow hits if triggered
    /// </summary>
    public unsafe void OnHit(long targetId, int actionId, int activeEikon, long R15, bool isEnabled, long* bnpcRow, long a3, long a4)
    {
        if (!isEnabled) return;
        
        var result = ProcessHit(targetId, actionId, activeEikon, R15);
        
        // Log events
        if (result.AppliedDebuff)
        {
            Log("Shadow debuff applied!");
        }
        
        if (result.TriggeredShadowHit)
        {
            // Capture airborne state NOW while pointers are guaranteed valid
            bool isAirborneState = false;
            
            // Pass the bnpcRow (RCX) directly, as ActorApi now uses the IDA path (Row + 0x20)
            if (ActorApi != null)
                isAirborneState = ActorApi.IsAirborne((long)bnpcRow);

            Log($">>> Shadow hit triggered! Target=0x{(long)bnpcRow:X}, Damage=+{result.ShadowDamage}, Airborne={isAirborneState}", _logger?.ColorGreen);
            
            // Reverting to use ORIGINAL R15 pointer for now but with safety checks,
            // as using a stack-allocated or local array might not be recognized by the game's dispatcher.
            ScheduleShadowHit((long)bnpcRow, R15, a3, a4, result.ShadowDamage, isAirborneState);
        }
    }
    
    #endregion
    
    #region Shadow Hit Execution
    
    /// <summary>
    /// Schedule a shadow hit to be triggered after the configured delay
    /// </summary>
    private void ScheduleShadowHit(long bnpcRowValue, long r15Value, long a3, long a4, int shadowDamage, bool isAirborne)
    {
        Task.Run(() =>
        {
            Thread.Sleep(ShadowHitDelayMs);
            ExecuteShadowHit(bnpcRowValue, r15Value, a3, a4, shadowDamage, isAirborne);
        });
    }
    
    /// <summary>
    /// Execute the shadow hit by modifying R15 and calling OnHit/OnReaction
    /// </summary>
    private unsafe void ExecuteShadowHit(long bnpcRowValue, long r15Value, long a3, long a4, int shadowDamage, bool isAirborne)
    {
        if (_onHitOriginal == null)
        {
            LogDebug("Cannot execute shadow hit - hooks not set!");
            return;
        }
        
        try
        {
            // Range check for r15Value to prevent crash
            if (r15Value < 0x10000 || r15Value > 0x00007FFFFFFFFFFF) return;

            // Prepare R15 with shadow hit values
            PrepareR15ForShadowHit(r15Value, shadowDamage, isAirborne);
            
            // Apply juggle physics ONLY if airborne and enabled
            if (isAirborne && JuggleEnabled && _applyPhysics != null)
            {
                _applyPhysics(JuggleForwardPush, JuggleForwardDuration, JuggleVerticalPush, JuggleVerticalInterpolation);
            }
            
            // Call OnHit
            // We pass the original bnpcRowValue (RCX) which the game expects
            _onHitOriginal(bnpcRowValue, r15Value, a3, a4);
            
            // Call OnReaction
            long battleContext = GetBattleContext?.Invoke() ?? 0;
            bool hadReaction = false;
            if (battleContext != 0 && _onReactionOriginal != null)
            {
                _onReactionOriginal(battleContext, r15Value);
                hadReaction = true;
            }

            // --- Odin Zantetsuken Gauge Ticks ---
            if (ZantetsukenTicksEnabled && ZantetsukenApi != null)
            {
                ZantetsukenApi.AddUnits(ZantetsukenTickAmount);
            }

            // Log result
            if (hadReaction)
                Log($"Shadow hit executed with reaction! Damage: {shadowDamage}, ActionId: {ActionIds.SHADOW_HIT}");
            else
                Log($"Shadow hit executed (no reaction context)! Damage: {shadowDamage}");
        }
        catch (Exception ex)
        {
            LogDebug($"Error executing shadow hit: {ex.Message}");
        }
    }
    
    #endregion

    /// <summary>
    /// Process a hit and check for Darkra mechanics.
    /// Returns info about what shadow hit should be triggered (caller handles the actual hit).
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
            // Calculate shadow damage
            int originalDamage = *(int*)(R15 + 0x174);
            int shadowDamage = (int)(originalDamage * ShadowHitMultiplier);
            
            result.TriggeredShadowHit = true;
            result.ShadowDamage = shadowDamage;
            result.OriginalDamage = originalDamage;
        }
        
        return result;
    }
    
    /// <summary>
    /// Prepare R15 structure for a shadow hit execution.
    /// Sets action ID, damage, and reaction values.
    /// </summary>
    public unsafe void PrepareR15ForShadowHit(long r15Value, int shadowDamage, bool isAirborne)
    {
        // Set the action ID to SHADOW_HIT to prevent recursion
        int* actionIdPtr = (int*)(r15Value + 0xB0);
        *actionIdPtr = ActionIds.SHADOW_HIT;
        
        // Set the damage
        int* dmgPtr = (int*)(r15Value + 0x174);
        *dmgPtr = shadowDamage;
        
        // Set reaction animation and push direction
        int* reactionTypePtr = (int*)(r15Value + 0x15c);
        int* reactionPushPtr = (int*)(r15Value + 0x160);
        
        if (isAirborne && JuggleEnabled)
        {
            // Enemies in air take the JUGGLE reaction (to stay up)
            *reactionTypePtr = JuggleAnimId;
            *reactionPushPtr = ReactionPushDirection;
        }
        else
        {
            // Enemies on ground take the configured LAND reaction
            *reactionTypePtr = ReactionAnimationType;
            *reactionPushPtr = ReactionPushDirection;
        }
    }
    
    /// <summary>
    /// Get the physics values to apply for a shadow hit juggle.
    /// Returns true if juggle physics should be applied.
    /// </summary>
    public bool GetJugglePhysics(out float forwardPush, out float forwardDuration, out float verticalPush, out float verticalInterpolation)
    {
        if (JuggleEnabled)
        {
            forwardPush = JuggleForwardPush;
            forwardDuration = JuggleForwardDuration;
            verticalPush = JuggleVerticalPush;
            verticalInterpolation = JuggleVerticalInterpolation;
            return true;
        }
        
        forwardPush = forwardDuration = verticalPush = verticalInterpolation = 0;
        return false;
    }
    
    /// <summary>
    /// Apply or reset the Shadow debuff on a target
    /// </summary>
    private void ApplyDebuff(long targetId)
    {
        var expirationTime = DateTime.UtcNow.AddSeconds(DebuffDuration);
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

    public void UpdateConfiguration(Config configuration)
    {
        // Check for changes and log
        if (ShadowHitMultiplier != configuration.ShadowHitMultiplier)
            LogDebug($"ShadowHitMultiplier changed: {ShadowHitMultiplier} -> {configuration.ShadowHitMultiplier}");
            ShadowHitMultiplier = configuration.ShadowHitMultiplier;
        if (DebuffDuration != configuration.ShadowDebuffDuration)
            LogDebug($"DebuffDuration changed: {DebuffDuration} -> {configuration.ShadowDebuffDuration}");
            DebuffDuration = configuration.ShadowDebuffDuration;
        if (ShadowHitDelayMs != configuration.ShadowHitDelayMs)
            LogDebug($"ShadowHitDelayMs changed: {ShadowHitDelayMs} -> {configuration.ShadowHitDelayMs}");
            ShadowHitDelayMs = configuration.ShadowHitDelayMs;
        if (ReactionAnimationType != configuration.ShadowHitReactionType)
            LogDebug($"ReactionAnimationType changed: {ReactionAnimationType} -> {configuration.ShadowHitReactionType}");
            ReactionAnimationType = configuration.ShadowHitReactionType;
        if (ReactionPushDirection != configuration.ShadowHitReactionIntensity)
            LogDebug($"ReactionPushDirection changed: {ReactionPushDirection} -> {configuration.ShadowHitReactionIntensity}");
            ReactionPushDirection = configuration.ShadowHitReactionIntensity;
        if (JuggleEnabled != configuration.ShadowHitJuggleEnabled)
            LogDebug($"JuggleEnabled changed: {JuggleEnabled} -> {configuration.ShadowHitJuggleEnabled}");
            JuggleEnabled = configuration.ShadowHitJuggleEnabled;
        if (JuggleAnimId != configuration.ShadowHitJuggleAnimId)
            LogDebug($"JuggleAnimId changed: {JuggleAnimId} -> {configuration.ShadowHitJuggleAnimId}");
            JuggleAnimId = configuration.ShadowHitJuggleAnimId;
        if (JuggleVerticalPush != configuration.ShadowHitJuggleVerticalPush)
            LogDebug($"JuggleVerticalPush changed: {JuggleVerticalPush} -> {configuration.ShadowHitJuggleVerticalPush}");
            JuggleVerticalPush = configuration.ShadowHitJuggleVerticalPush;
        if (JuggleForwardPush != configuration.ShadowHitJuggleForwardPush)
            LogDebug($"JuggleForwardPush changed: {JuggleForwardPush} -> {configuration.ShadowHitJuggleForwardPush}");
            JuggleForwardPush = configuration.ShadowHitJuggleForwardPush;
        if (JuggleForwardDuration != configuration.ShadowHitJuggleForwardDuration)
            LogDebug($"JuggleForwardDuration changed: {JuggleForwardDuration} -> {configuration.ShadowHitJuggleForwardDuration}");
            JuggleForwardDuration = configuration.ShadowHitJuggleForwardDuration;
        if (JuggleVerticalInterpolation != configuration.ShadowHitJuggleVerticalInterpolation)
            LogDebug($"JuggleVerticalInterpolation changed: {JuggleVerticalInterpolation} -> {configuration.ShadowHitJuggleVerticalInterpolation}");
            JuggleVerticalInterpolation = configuration.ShadowHitJuggleVerticalInterpolation;
        if (ZantetsukenTicksEnabled != configuration.ShadowHitZantetsukenTicksEnabled)
            LogDebug($"ZantetsukenTicksEnabled changed: {ZantetsukenTicksEnabled} -> {configuration.ShadowHitZantetsukenTicksEnabled}");
            ZantetsukenTicksEnabled = configuration.ShadowHitZantetsukenTicksEnabled;
        if (ZantetsukenTickAmount != configuration.ShadowHitZantetsukenTickAmount)
            LogDebug($"ZantetsukenTickAmount changed: {ZantetsukenTickAmount} -> {configuration.ShadowHitZantetsukenTickAmount}");
            ZantetsukenTickAmount = configuration.ShadowHitZantetsukenTickAmount;
    }
}

/// <summary>
/// Result of processing a hit through the Darkra system
/// </summary>
public struct DarkraResult
{
    public bool AppliedDebuff;      // True if this hit applied/reset Shadow debuff
    public bool TriggeredShadowHit; // True if target had debuff and should take shadow damage
    public int ShadowDamage;        // Amount of shadow damage to deal
    public int OriginalDamage;      // Original damage of the hit
}
