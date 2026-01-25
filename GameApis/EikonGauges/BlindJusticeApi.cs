using System.Diagnostics;
using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using ff16.gameplay.truly_eikonic_spells.GameApis.Actor;
using Reloaded.Mod.Interfaces;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// API for managing Ramuh's Blind Justice stack gauge.
/// 
/// Blind Justice System (from IDA analysis):
/// - Stack count stored at ActorData35Entry + 0xE0 (224 decimal) as int
/// - Max stacks from Skill::GetPotencyParameter(skill_29)
/// - Blind Justice mode = PlayerMode 74
/// - Checked via IsBlindJusticeActive() before UI update
/// 
/// Signature Patterns:
/// - GetBlindJusticeMaxGaugeMaxLevel: 48 89 5C 24 ?? 57 48 83 EC ?? ... 84 C0 74 71
///   Returns max number of locked-on projectiles (skill_29 potency)
/// 
/// Unlike other Eikon gauges, this is stored in ActorData35Entry,
/// not in the Eikon summon structure.
/// </summary>
public unsafe class BlindJusticeApi : IBlindJusticeApi
{
    #region Signatures
    
    /// <summary>
    /// Signature for GetBlindJusticeMaxGaugeMaxLevel function.
    /// Distinguished by "jz short 0x71" at byte 59 (84 C0 74 71).
    /// </summary>
    private const string SIG_GET_MAX_LEVEL = 
        "48 89 5C 24 ?? 57 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 48 8D 54 24 ?? " +
        "48 8B 0D ?? ?? ?? ?? 44 8B 80 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 4C 24 ?? " +
        "E8 ?? ?? ?? ?? 48 8B 5C 24 ?? 84 C0 74 71";
    
    #endregion
    
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
    
    #region Delegates
    
    /// <summary>
    /// Delegate for GetBlindJusticeMaxGaugeMaxLevel function.
    /// Returns the maximum number of locked-on projectiles.
    /// </summary>
    [Function(CallingConventions.Microsoft)]
    private delegate long GetBlindJusticeMaxGaugeMaxLevelDelegate();
    
    #endregion
    
    #region Fields
    
    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> _getIsSummonModeActive;
    private readonly IActorApi? _actorApi;
    private readonly ILogger? _logger;
    private readonly string _modId;
    
    // Hook for max level override
    private IHook<GetBlindJusticeMaxGaugeMaxLevelDelegate>? _getMaxLevelHook;
    private nint _baseAddress;
    
    // Max level override state
    private int? _maxLevelOverride = null;
    
    #endregion
    
    #region Constructor
    
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
        _baseAddress = Process.GetCurrentProcess().MainModule!.BaseAddress;
    }
    
    #endregion
    
    #region Setup
    
    /// <summary>
    /// Set up signature scans and hooks for Blind Justice functions.
    /// </summary>
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        // Hook GetBlindJusticeMaxGaugeMaxLevel to allow overriding max level
        scans.AddMainModuleScan(SIG_GET_MAX_LEVEL, result =>
        {
            if (!result.Found)
            {
                _logger?.WriteLine($"[{_modId}] [BlindJustice] FAILED to find GetBlindJusticeMaxGaugeMaxLevel", _logger.ColorRed);
                return;
            }
            var addr = (nint)(_baseAddress + result.Offset);
            _getMaxLevelHook = hooks.CreateHook<GetBlindJusticeMaxGaugeMaxLevelDelegate>(GetMaxLevelImpl, addr).Activate();
            _logger?.WriteLine($"[{_modId}] [BlindJustice] Hooked GetBlindJusticeMaxGaugeMaxLevel at 0x{addr:X}", _logger.ColorGreen);
        });
    }
    
    /// <summary>
    /// Hook implementation for GetBlindJusticeMaxGaugeMaxLevel.
    /// Returns override value if set, otherwise calls original function.
    /// </summary>
    private long GetMaxLevelImpl()
    {
        // If we have an override set, return that instead
        if (_maxLevelOverride.HasValue)
        {
            return _maxLevelOverride.Value;
        }
        
        // Otherwise call the original function
        return _getMaxLevelHook!.OriginalFunction();
    }
    
    #endregion
    
    #region Max Units Override
    
    /// <summary>
    /// Gets the current maximum units (either override or vanilla value).
    /// </summary>
    public int GetMaxUnits()
    {
        if (_maxLevelOverride.HasValue)
        {
            return _maxLevelOverride.Value;
        }
        
        // If hook is active, call original to get vanilla value
        if (_getMaxLevelHook != null)
        {
            return (int)_getMaxLevelHook.OriginalFunction();
        }
        
        // Fallback to default
        return DefaultMaxStacks;
    }
    
    /// <summary>
    /// Sets a custom maximum units for Blind Justice.
    /// This will override the vanilla value from Skill::GetPotencyParameter(29).
    /// </summary>
    /// <param name="maxUnits">The new maximum units (number of projectiles)</param>
    public void SetMaxUnits(int maxUnits)
    {
        if (maxUnits < 1) maxUnits = 1;
        _maxLevelOverride = maxUnits;
        _logger?.WriteLine($"[{_modId}] [BlindJustice] Max units override set to {maxUnits}", _logger.ColorGreen);
    }
    
    /// <summary>
    /// Resets the maximum units to the vanilla value.
    /// After calling this, GetBlindJusticeMaxGaugeMaxLevel will return the game's default.
    /// </summary>
    public void ResetMaxUnits()
    {
        _maxLevelOverride = null;
        _logger?.WriteLine($"[{_modId}] [BlindJustice] Max units override reset to vanilla", _logger.ColorGreen);
    }
    
    /// <summary>
    /// Check if max units is currently being overridden.
    /// </summary>
    public bool IsMaxUnitsOverridden => _maxLevelOverride.HasValue;
    
    /// <summary>
    /// Gets the vanilla max units (from game function, ignoring any override).
    /// </summary>
    private int GetVanillaMaxUnits()
    {
        if (_getMaxLevelHook != null)
        {
            return (int)_getMaxLevelHook.OriginalFunction();
        }
        return DefaultMaxStacks;
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
        bool isOverridden = IsMaxUnitsOverridden;
        
        _logger.WriteLine($"[{_modId}] [BlindJustice] Ptr=0x{ramuhPtr:X}");
        _logger.WriteLine($"[{_modId}] [BlindJustice] Stacks: {units}/{maxUnits} | MaxOverridden: {isOverridden}");
    }

    #endregion
}
