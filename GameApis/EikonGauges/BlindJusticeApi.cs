using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using ff16.gameplay.truly_eikonic_spells.GameApis.Actor;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// API for managing Ramuh's Blind Justice stack gauge.
/// 
/// Blind Justice System (from IDA analysis):
/// - Stack count stored at ActorData35Entry + 0xE0 (224 decimal) as int
/// - Max stacks from Skill::GetPotencyParameter(skill_29 = 0x1D)
/// - Blind Justice mode = PlayerMode 74
/// - Checked via IsBlindJusticeActive() before UI update
/// 
/// Unlike other Eikon gauges, this is stored in ActorData35Entry,
/// not in the Eikon summon structure.
/// </summary>
public unsafe class BlindJusticeApi : IBlindJusticeApi
{
    #region Constants
    
    /// <summary>
    /// Minimum stacks (game UI shows 1-based stacks).
    /// </summary>
    public const int MinStacks = 1;
    
    /// <summary>
    /// Default maximum stacks (skill 29 potency at base level).
    /// Base ability gives 3, mastered gives 6.
    /// </summary>
    public const int DefaultMaxStacks = 6;
    
    /// <summary>
    /// PlayerMode value for Blind Justice (Ramuh satellite mode).
    /// </summary>
    public const int BlindJusticePlayerMode = 74;
    
    #endregion
    
    #region Fields
    
    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> _getIsSummonModeActive;
    private readonly IActorApi? _actorApi;
    private readonly SkillPotencyApi? _skillPotencyApi;
    private readonly ILogger? _logger;
    private readonly string _modId;
    
    #endregion
    
    #region Constructor
    
    /// <summary>
    /// Creates a new BlindJusticeApi.
    /// </summary>
    /// <param name="getGlobalPlayerStatePtr">Function to get global player state pointer</param>
    /// <param name="getIsSummonModeActive">Function to get IsSummonModeActive delegate</param>
    /// <param name="actorApi">ActorApi for accessing ActorData35Entry (required for lock count)</param>
    /// <param name="skillPotencyApi">Centralized SkillPotencyApi for max stacks</param>
    /// <param name="logger">Logger for debug output</param>
    /// <param name="modId">Mod ID for log prefixes</param>
    public BlindJusticeApi(
        Func<long> getGlobalPlayerStatePtr, 
        Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> getIsSummonModeActive,
        IActorApi? actorApi = null,
        SkillPotencyApi? skillPotencyApi = null,
        ILogger? logger = null, 
        string modId = "")
    {
        _getGlobalPlayerStatePtr = getGlobalPlayerStatePtr;
        _getIsSummonModeActive = getIsSummonModeActive;
        _actorApi = actorApi;
        _skillPotencyApi = skillPotencyApi;
        _logger = logger;
        _modId = modId;
    }
    
    #endregion
    
    #region Max Units
    
    /// <summary>
    /// Gets the maximum units from the game via SkillPotencyApi.
    /// Base ability gives 3, mastered gives 6.
    /// </summary>
    public int GetMaxUnits()
    {
        return _skillPotencyApi?.BlindJusticeMaxStacks ?? DefaultMaxStacks;
    }
    
    #endregion
    
    #region Availability

    /// <summary>
    /// Gets the pointer to the Ramuh-specific Eikon structure.
    /// Returns 0 if Ramuh mode is not active.
    /// </summary>
    public long GetRamuhEikonPointer()
    {
        var isSummonModeActive = _getIsSummonModeActive();
        var globalPlayerStatePtr = _getGlobalPlayerStatePtr();

        if (isSummonModeActive == null || globalPlayerStatePtr == 0) return 0;
        
        long playerState = *(long*)globalPlayerStatePtr;
        if (playerState == 0) return 0;
        
        // Ramuh ID is 4
        return isSummonModeActive(playerState + PlayerStateOffsets.EikonSummonData, EikonUtils.EIKON_RAMUH);
    }

    /// <summary>
    /// Check if Ramuh mode is currently active.
    /// </summary>
    public bool IsRamuhActive => GetRamuhEikonPointer() != 0;

    /// <summary>
    /// Gets the ActorData35Entry pointer for Blind Justice lock count access.
    /// The lock count is stored at ActorData35Entry + 0xE0.
    /// </summary>
    /// <returns>Pointer to ActorData35Entry, or 0 if not available</returns>
    private long GetActorData35EntryPtr()
    {
        if (_actorApi != null)
            return _actorApi.GetPlayerActorData35Entry();
        
        // Fallback: return 0 if no ActorApi provided
        return 0;
    }
    
    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current stack count.
    /// Reads from ActorData35Entry + 0xE0 (224 decimal).
    /// </summary>
    public int GetUnits()
    {
        long actorData35 = GetActorData35EntryPtr();
        if (actorData35 == 0) return 0;
        return *(int*)(actorData35 + ActorData35Offsets.BlindJusticeLockCount);
    }

    /// <summary>
    /// Set the stack count directly.
    /// Writes to ActorData35Entry + 0xE0.
    /// Uses the current max units (override or vanilla) for capping.
    /// </summary>
    /// <param name="count">New stack count</param>
    public void SetUnits(int count)
    {
        long actorData35 = GetActorData35EntryPtr();
        if (actorData35 == 0) return;

        int maxUnits = GetMaxUnits();
        if (count > maxUnits) count = maxUnits;
        if (count < 0) count = 0;

        *(int*)(actorData35 + ActorData35Offsets.BlindJusticeLockCount) = count;
    }

    /// <summary>
    /// Adds stacks to the count.
    /// Uses the current max units (override or vanilla) for capping.
    /// </summary>
    /// <param name="amount">Amount of stacks to add (can be negative)</param>
    public void AddUnits(int amount)
    {
        long actorData35 = GetActorData35EntryPtr();
        if (actorData35 == 0) return;

        int maxUnits = GetMaxUnits();
        int* pCount = (int*)(actorData35 + ActorData35Offsets.BlindJusticeLockCount);
        int currentCount = *pCount;
        int newCount = currentCount + amount;
        
        if (newCount > maxUnits) newCount = maxUnits;
        if (newCount < 0) newCount = 0;

        *pCount = newCount;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Fill stacks to maximum.
    /// Uses the current max units (override or vanilla).
    /// </summary>
    public void FillGauge()
    {
        int maxUnits = GetMaxUnits();
        SetUnits(maxUnits);
    }

    /// <summary>
    /// Empty all stacks.
    /// </summary>
    public void EmptyGauge()
    {
        SetUnits(0);
    }

    #endregion

    #region Debug

    /// <summary>
    /// Log current Blind Justice state for debugging.
    /// </summary>
    public void LogState()
    {
        if (_logger == null) return;
        
        long ramuhPtr = GetRamuhEikonPointer();
        if (ramuhPtr == 0)
        {
            _logger.WriteLine($"[{_modId}] [BlindJustice] Ramuh mode not active");
            return;
        }
        
        int units = GetUnits();
        int maxUnits = GetMaxUnits();
        
        _logger.WriteLine($"[{_modId}] [BlindJustice] Ptr=0x{ramuhPtr:X}");
        _logger.WriteLine($"[{_modId}] [BlindJustice] Stacks: {units}/{maxUnits}");
    }

    #endregion
}
