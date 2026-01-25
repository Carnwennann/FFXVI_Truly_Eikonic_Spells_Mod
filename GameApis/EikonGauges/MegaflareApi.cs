using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// API for managing Bahamut's Megaflare gauge and related state.
/// 
/// Megaflare Gauge System:
/// - 4000 units = 1 Level
/// - Max level depends on skill potency (typically 1-4)
/// - Wings activation state affects UI display
/// </summary>
public unsafe class MegaflareApi : IMegaflareApi
{
    /// <summary>
    /// Units required per Megaflare level.
    /// </summary>
    public const int UnitsPerLevel = 4000;
    
    /// <summary>
    /// Default maximum level (can be overridden by skill potency).
    /// </summary>
    public const int DefaultMaxLevel = 4;

    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> _getIsSummonModeActive;
    private readonly ILogger? _logger;
    private readonly string _modId;

    public MegaflareApi(
        Func<long> getGlobalPlayerStatePtr, 
        Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> getIsSummonModeActive, 
        ILogger? logger = null, 
        string modId = "")
    {
        _getGlobalPlayerStatePtr = getGlobalPlayerStatePtr;
        _getIsSummonModeActive = getIsSummonModeActive;
        _logger = logger;
        _modId = modId;
    }

    /// <summary>
    /// Gets the pointer to the Bahamut-specific Eikon structure.
    /// Returns 0 if Bahamut mode is not active.
    /// </summary>
    public long GetBahamutEikonPointer()
    {
        var isSummonModeActive = _getIsSummonModeActive();
        var globalPlayerStatePtr = _getGlobalPlayerStatePtr();

        if (isSummonModeActive == null || globalPlayerStatePtr == 0) return 0;
        
        long playerState = *(long*)globalPlayerStatePtr;
        if (playerState == 0) return 0;
        
        // Bahamut ID is 8
        return isSummonModeActive(playerState + PlayerStateOffsets.EikonSummonData, EikonUtils.EIKON_BAHAMUT);
    }

    /// <summary>
    /// Check if Bahamut mode is currently active.
    /// </summary>
    public bool IsBahamutActive => GetBahamutEikonPointer() != 0;

    #region Max Level

    /// <summary>
    /// Get the maximum level from the game.
    /// TODO: Hook Skill::GetPotencyParameter to get actual value.
    /// Currently returns DefaultMaxLevel (4).
    /// </summary>
    public int GetMaxLevel()
    {
        // TODO: Get from game via hook
        return DefaultMaxLevel;
    }

    /// <summary>
    /// Get the maximum units based on max level (UnitsPerLevel * MaxLevel).
    /// </summary>
    public int GetMaxUnits()
    {
        return GetMaxLevel() * UnitsPerLevel;
    }

    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current Megaflare gauge units (raw float value cast to int).
    /// The game stores this as a float but uses it as int (0-16000 typically).
    /// </summary>
    public int GetUnits()
    {
        long bahamutPtr = GetBahamutEikonPointer();
        if (bahamutPtr == 0) return 0;
        // Game stores as float, we truncate to int like the game does (vcvttss2si)
        return (int)*(float*)(bahamutPtr + BahamutEikonOffsets.MegaflareGauge);
    }

    /// <summary>
    /// Set the Megaflare gauge units directly.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="units">New gauge value</param>
    public void SetUnits(int units)
    {
        long bahamutPtr = GetBahamutEikonPointer();
        if (bahamutPtr == 0) return;

        int maxUnits = GetMaxUnits();
        if (units > maxUnits) units = maxUnits;
        if (units < 0) units = 0;

        *(float*)(bahamutPtr + BahamutEikonOffsets.MegaflareGauge) = (float)units;
    }

    /// <summary>
    /// Adds units to the Megaflare gauge.
    /// 4000 units = 1 Level.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="amount">Amount of gauge units to add (can be negative)</param>
    public void AddUnits(int amount)
    {
        long bahamutPtr = GetBahamutEikonPointer();
        if (bahamutPtr == 0) return;

        float* pGauge = (float*)(bahamutPtr + BahamutEikonOffsets.MegaflareGauge);
        float currentUnits = *pGauge;
        float newUnits = currentUnits + amount;
        
        float maxUnits = GetMaxUnits();
        if (newUnits > maxUnits) newUnits = maxUnits;
        if (newUnits < 0f) newUnits = 0f;

        *pGauge = newUnits;
    }

    #endregion

    #region Level

    /// <summary>
    /// Get the current Megaflare level (0 to maxLevel).
    /// </summary>
    public int GetLevel()
    {
        return GetUnits() / UnitsPerLevel;
    }

    /// <summary>
    /// Set the Megaflare level directly (sets units to level * UnitsPerLevel).
    /// Capped to game's max level.
    /// </summary>
    /// <param name="level">Target level</param>
    public void SetLevel(int level)
    {
        int maxLevel = GetMaxLevel();
        if (level < 0) level = 0;
        if (level > maxLevel) level = maxLevel;
        SetUnits(level * UnitsPerLevel);
    }

    /// <summary>
    /// Add full levels to the gauge.
    /// </summary>
    /// <param name="levels">Number of levels to add (can be negative)</param>
    public void AddLevels(int levels)
    {
        AddUnits(levels * UnitsPerLevel);
    }

    /// <summary>
    /// Fill the gauge to maximum.
    /// </summary>
    public void FillGauge()
    {
        SetUnits(GetMaxUnits());
    }

    /// <summary>
    /// Empty the gauge completely.
    /// </summary>
    public void EmptyGauge()
    {
        SetUnits(0);
    }

    #endregion

    #region Wings State

    // NOTE: Wings activation is NOT stored in the Bahamut Eikon structure.
    // The game checks it via: ActorData35Entry::GetCurrentPlayerMode() == 75
    // To implement wings detection, you need to hook or call GetCurrentPlayerMode.

    #endregion

    #region Debug

    /// <summary>
    /// Log current Megaflare state for debugging.
    /// </summary>
    public void LogState()
    {
        if (_logger == null) return;
        
        long bahamutPtr = GetBahamutEikonPointer();
        if (bahamutPtr == 0)
        {
            _logger.WriteLine($"[{_modId}] [Megaflare] Bahamut mode not active");
            return;
        }
        
        int units = GetUnits();
        int level = GetLevel();
        int maxUnits = GetMaxUnits();
        int maxLevel = GetMaxLevel();
        
        _logger.WriteLine($"[{_modId}] [Megaflare] Ptr=0x{bahamutPtr:X}");
        _logger.WriteLine($"[{_modId}] [Megaflare] Units: {units}/{maxUnits} | Level: {level}/{maxLevel}");
    }

    #endregion
}
