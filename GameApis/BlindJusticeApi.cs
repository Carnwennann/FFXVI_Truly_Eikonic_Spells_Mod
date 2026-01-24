using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using ff16.gameplay.truly_eikonic_spells.GameApis.Actor;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// API for managing Ramuh's Blind Justice stack gauge.
/// 
/// Blind Justice System (from IDA analysis):
/// - Stack count stored at ActorData35Entry + 0xE0 (224 decimal) as int
/// - Max stacks from Skill::GetPotencyParameter(skill_29)
/// - Blind Justice mode = PlayerMode 74
/// - Checked via IsBlindJusticeActive() before UI update
/// 
/// Unlike other Eikon gauges, this is stored in ActorData35Entry,
/// not in the Eikon summon structure.
/// </summary>
public unsafe class BlindJusticeApi
{
    /// <summary>
    /// Default maximum stacks (skill 29 potency at base level).
    /// Base ability gives 3, mastered gives 6.
    /// </summary>
    public const int DefaultMaxStacks = 6;
    
    /// <summary>
    /// PlayerMode value for Blind Justice (Ramuh satellite mode).
    /// </summary>
    public const int BlindJusticePlayerMode = 74;

    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> _getIsSummonModeActive;
    private readonly IActorApi? _actorApi;
    private readonly ILogger? _logger;
    private readonly string _modId;

    /// <summary>
    /// Creates a new BlindJusticeApi.
    /// </summary>
    /// <param name="getGlobalPlayerStatePtr">Function to get global player state pointer</param>
    /// <param name="getIsSummonModeActive">Function to get IsSummonModeActive delegate</param>
    /// <param name="actorApi">ActorApi for accessing ActorData35Entry (required for lock count)</param>
    /// <param name="logger">Logger for debug output</param>
    /// <param name="modId">Mod ID for log prefixes</param>
    public BlindJusticeApi(
        Func<long> getGlobalPlayerStatePtr, 
        Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> getIsSummonModeActive,
        IActorApi? actorApi = null,
        ILogger? logger = null, 
        string modId = "")
    {
        _getGlobalPlayerStatePtr = getGlobalPlayerStatePtr;
        _getIsSummonModeActive = getIsSummonModeActive;
        _actorApi = actorApi;
        _logger = logger;
        _modId = modId;
    }

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
    public long GetActorData35EntryPtr()
    {
        if (_actorApi != null)
            return _actorApi.GetPlayerActorData35Entry();
        
        // Fallback: return 0 if no ActorApi provided
        return 0;
    }

    #region Stack Count

    /// <summary>
    /// Get the current stack count.
    /// Reads from ActorData35Entry + 0xE0 (224 decimal).
    /// </summary>
    public int GetStacks()
    {
        long actorData35 = GetActorData35EntryPtr();
        if (actorData35 == 0) return 0;
        return *(int*)(actorData35 + ActorData35Offsets.BlindJusticeLockCount);
    }

    /// <summary>
    /// Set the stack count directly.
    /// Writes to ActorData35Entry + 0xE0.
    /// </summary>
    /// <param name="count">New stack count</param>
    /// <param name="maxStacks">Maximum stack cap (default: 6)</param>
    public void SetStacks(int count, int maxStacks = DefaultMaxStacks)
    {
        long actorData35 = GetActorData35EntryPtr();
        if (actorData35 == 0) return;

        if (count > maxStacks) count = maxStacks;
        if (count < 0) count = 0;

        *(int*)(actorData35 + ActorData35Offsets.BlindJusticeLockCount) = count;
    }

    /// <summary>
    /// Adds stacks to the count.
    /// </summary>
    /// <param name="amount">Amount of stacks to add (can be negative)</param>
    /// <param name="maxStacks">Maximum stack cap (default: 6)</param>
    public void AddStacks(int amount, int maxStacks = DefaultMaxStacks)
    {
        long actorData35 = GetActorData35EntryPtr();
        if (actorData35 == 0) return;

        int* pCount = (int*)(actorData35 + ActorData35Offsets.BlindJusticeLockCount);
        int currentCount = *pCount;
        int newCount = currentCount + amount;
        
        if (newCount > maxStacks) newCount = maxStacks;
        if (newCount < 0) newCount = 0;

        *pCount = newCount;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Fill stacks to maximum.
    /// </summary>
    /// <param name="maxStacks">Maximum stacks (default: 6)</param>
    public void FillStacks(int maxStacks = DefaultMaxStacks)
    {
        SetStacks(maxStacks, maxStacks);
    }

    /// <summary>
    /// Empty all stacks.
    /// </summary>
    public void EmptyStacks()
    {
        SetStacks(0);
    }

    /// <summary>
    /// Check if stacks are at maximum capacity.
    /// </summary>
    public bool IsFull(int maxStacks = DefaultMaxStacks)
    {
        return GetStacks() >= maxStacks;
    }

    /// <summary>
    /// Check if there are any stacks available.
    /// </summary>
    public bool HasStacks => GetStacks() > 0;

    #endregion

    #region Debug

    /// <summary>
    /// Log current Blind Justice state for debugging.
    /// </summary>
    public void LogState()
    {
        if (_logger == null) return;
        
        long actorData35 = GetActorData35EntryPtr();
        if (actorData35 == 0)
        {
            _logger.WriteLine($"[{_modId}] [BlindJustice] ActorData35Entry not available");
            return;
        }

        _logger.WriteLine($"[{_modId}] [BlindJustice] Stacks: {GetStacks()}, ActorData35: 0x{actorData35:X}, RamuhActive: {IsRamuhActive}");
    }

    #endregion
}
