namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// Interface for Bahamut's Megaflare gauge API.
/// 
/// Megaflare Gauge System:
/// - 4000 units = 1 Level (UnitsPerLevel)
/// - Level range is 0-4 (starts at 0, DefaultMaxLevel = 4)
/// - Max 16000 units at level 4
/// </summary>
public interface IMegaflareApi
{
    #region Availability

    /// <summary>
    /// Gets the pointer to the Bahamut-specific Eikon structure.
    /// Returns 0 if Bahamut mode is not active.
    /// </summary>
    long GetBahamutEikonPointer();

    /// <summary>
    /// Check if Bahamut mode is currently active.
    /// </summary>
    bool IsBahamutActive { get; }

    #endregion

    #region Max Level

    /// <summary>
    /// Get the maximum level from the game.
    /// Typically 4 (base) or higher if upgraded.
    /// </summary>
    int GetMaxLevel();

    /// <summary>
    /// Get the maximum units based on max level (UnitsPerLevel * MaxLevel).
    /// </summary>
    int GetMaxUnits();

    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current Megaflare gauge units.
    /// </summary>
    int GetUnits();

    /// <summary>
    /// Set the Megaflare gauge units directly.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="units">New gauge value</param>
    void SetUnits(int units);

    /// <summary>
    /// Adds units to the Megaflare gauge.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="amount">Amount of gauge units to add (can be negative)</param>
    void AddUnits(int amount);

    #endregion

    #region Level

    /// <summary>
    /// Get the current Megaflare level (0 to maxLevel).
    /// </summary>
    int GetLevel();

    /// <summary>
    /// Set the Megaflare level directly.
    /// Capped to game's max level.
    /// </summary>
    /// <param name="level">Target level</param>
    void SetLevel(int level);

    /// <summary>
    /// Add full levels to the gauge.
    /// </summary>
    /// <param name="levels">Number of levels to add (can be negative)</param>
    void AddLevels(int levels);

    #endregion

    #region Utilities

    /// <summary>
    /// Fill the gauge to maximum.
    /// </summary>
    void FillGauge();

    /// <summary>
    /// Empty the gauge completely.
    /// </summary>
    void EmptyGauge();

    #endregion

    #region Debug

    /// <summary>
    /// Log current Megaflare state for debugging.
    /// </summary>
    void LogState();

    #endregion
}
