using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// API for managing Odin's Zantetsuken gauge and related state.
/// 
/// Zantetsuken Gauge System:
/// - 1500 units = 1 Level
/// - Level range is 1-5 (game displays 1-based levels)
/// - 0 units = Level 1, 1500 units = Level 2, etc.
/// - Max 7500 units = Level 5
/// </summary>
public unsafe class ZantetsukenApi : IZantetsukenApi
{
    /// <summary>
    /// Units required per Zantetsuken level.
    /// </summary>
    public const int UnitsPerLevel = 1500;
    
    /// <summary>
    /// Minimum Zantetsuken level (game UI shows 1-based levels).
    /// </summary>
    public const int MinLevel = 1;
    
    /// <summary>
    /// Maximum Zantetsuken level.
    /// </summary>
    public const int MaxLevel = 5;
    
    /// <summary>
    /// Maximum gauge units ((MaxLevel - 1) * UnitsPerLevel = 6000 to reach level 5).
    /// At 0 units you're at level 1, at 6000 units you're at level 5.
    /// </summary>
    public const int MaxUnits = 6000;

    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> _getIsSummonModeActive;
    private readonly ILogger? _logger;
    private readonly string _modId;

    public ZantetsukenApi(
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
    /// Gets the pointer to the Odin-specific Eikon structure.
    /// Returns 0 if Odin mode is not active.
    /// </summary>
    public long GetOdinEikonPointer()
    {
        var isSummonModeActive = _getIsSummonModeActive();
        var globalPlayerStatePtr = _getGlobalPlayerStatePtr();

        if (isSummonModeActive == null || globalPlayerStatePtr == 0) return 0;
        
        long playerState = *(long*)globalPlayerStatePtr;
        if (playerState == 0) return 0;
        
        // Odin ID is 7
        return isSummonModeActive(playerState + PlayerStateOffsets.EikonSummonData, EikonUtils.EIKON_ODIN);
    }

    /// <summary>
    /// Check if Odin mode is currently active.
    /// </summary>
    public bool IsOdinActive => GetOdinEikonPointer() != 0;

    #region Max Level

    /// <summary>
    /// Get the maximum level from the game.
    /// TODO: Hook Skill::GetPotencyParameter to get actual value.
    /// Currently returns MaxLevel constant (5).
    /// </summary>
    public int GetMaxLevel()
    {
        // TODO: Get from game via hook
        return MaxLevel;
    }

    /// <summary>
    /// Get the maximum units based on max level.
    /// </summary>
    public int GetMaxUnits()
    {
        return (GetMaxLevel() - MinLevel) * UnitsPerLevel;
    }

    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current Zantetsuken gauge units (0-6000).
    /// </summary>
    public int GetUnits()
    {
        long odinPtr = GetOdinEikonPointer();
        if (odinPtr == 0) return 0;
        return *(short*)(odinPtr + OdinEikonOffsets.ZantetsukenGauge);
    }

    /// <summary>
    /// Set the Zantetsuken gauge units directly.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="units">New gauge value</param>
    public void SetUnits(int units)
    {
        long odinPtr = GetOdinEikonPointer();
        if (odinPtr == 0) return;

        int maxUnits = GetMaxUnits();
        if (units > maxUnits) units = maxUnits;
        if (units < 0) units = 0;

        *(short*)(odinPtr + OdinEikonOffsets.ZantetsukenGauge) = (short)units;
    }

    /// <summary>
    /// Adds units to the Zantetsuken gauge.
    /// 1500 units = 1 Level.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="amount">Amount of gauge units to add (can be negative)</param>
    public void AddUnits(int amount)
    {
        long odinPtr = GetOdinEikonPointer();
        if (odinPtr == 0) return;

        short* pGauge = (short*)(odinPtr + OdinEikonOffsets.ZantetsukenGauge);
        int currentUnits = *pGauge;
        int newUnits = currentUnits + amount;
        
        int maxUnits = GetMaxUnits();
        if (newUnits > maxUnits) newUnits = maxUnits;
        if (newUnits < 0) newUnits = 0;

        *pGauge = (short)newUnits;
    }

    #endregion

    #region Level

    /// <summary>
    /// Get the current Zantetsuken level (1 to 5).
    /// Level 1 = 0 units, Level 2 = 1500 units, etc.
    /// </summary>
    public int GetLevel()
    {
        return (GetUnits() / UnitsPerLevel) + MinLevel;
    }

    /// <summary>
    /// Set the Zantetsuken level directly.
    /// Level 1 = 0 units, Level 2 = 1500 units, etc.
    /// Capped to game's max level.
    /// </summary>
    /// <param name="level">Target level</param>
    public void SetLevel(int level)
    {
        int maxLevel = GetMaxLevel();
        if (level < MinLevel) level = MinLevel;
        if (level > maxLevel) level = maxLevel;
        SetUnits((level - MinLevel) * UnitsPerLevel);
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
    /// Fill the gauge to maximum (max level from game).
    /// </summary>
    public void FillGauge()
    {
        SetLevel(GetMaxLevel());
    }

    /// <summary>
    /// Reset the gauge to minimum (Level 1 = 0 units).
    /// </summary>
    public void EmptyGauge()
    {
        SetLevel(MinLevel);
    }

    #endregion

    #region Debug

    /// <summary>
    /// Log current Zantetsuken state for debugging.
    /// </summary>
    public void LogState()
    {
        if (_logger == null) return;
        
        long odinPtr = GetOdinEikonPointer();
        if (odinPtr == 0)
        {
            _logger.WriteLine($"[{_modId}] [Zantetsuken] Odin mode not active");
            return;
        }
        
        int units = GetUnits();
        int level = GetLevel();
        int maxUnits = GetMaxUnits();
        int maxLevel = GetMaxLevel();
        
        _logger.WriteLine($"[{_modId}] [Zantetsuken] Ptr=0x{odinPtr:X}");
        _logger.WriteLine($"[{_modId}] [Zantetsuken] Units: {units}/{maxUnits} | Level: {level}/{maxLevel}");
    }

    #endregion
}
