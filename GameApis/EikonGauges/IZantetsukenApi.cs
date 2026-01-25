namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// Interface for Odin's Zantetsuken gauge API.
/// 
/// Zantetsuken Gauge System:
/// - 1500 units = 1 Level (UnitsPerLevel)
/// - Level range is 1-5 (MinLevel to MaxLevel)
/// - 0 units = Level 1, 1500 units = Level 2, etc.
/// - Max 6000 units = Level 5 (MaxUnits)
/// </summary>
public interface IZantetsukenApi
{
    #region Availability

    /// <summary>
    /// Gets the pointer to the Odin-specific Eikon structure.
    /// Returns 0 if Odin mode is not active.
    /// </summary>
    long GetOdinEikonPointer();

    /// <summary>
    /// Check if Odin mode is currently active.
    /// </summary>
    bool IsOdinActive { get; }

    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current Zantetsuken gauge units (0-6000).
    /// </summary>
    int GetUnits();

    /// <summary>
    /// Set the Zantetsuken gauge units directly.
    /// </summary>
    /// <param name="units">New gauge value (0-6000)</param>
    void SetUnits(int units);

    /// <summary>
    /// Adds units to the Zantetsuken gauge.
    /// </summary>
    /// <param name="amount">Amount of gauge units to add (can be negative)</param>
    void AddUnits(int amount);

    #endregion

    #region Level

    /// <summary>
    /// Get the current Zantetsuken level (1 to 5).
    /// </summary>
    int GetLevel();

    /// <summary>
    /// Set the Zantetsuken level directly.
    /// </summary>
    /// <param name="level">Target level (1-5)</param>
    void SetLevel(int level);

    /// <summary>
    /// Add full levels to the gauge.
    /// </summary>
    /// <param name="levels">Number of levels to add (can be negative)</param>
    void AddLevels(int levels);

    #endregion

    #region Utilities

    /// <summary>
    /// Fill the gauge to maximum (Level 5).
    /// </summary>
    void FillGauge();

    /// <summary>
    /// Reset the gauge to minimum (Level 1).
    /// </summary>
    void EmptyGauge();

    #endregion

    #region Debug

    /// <summary>
    /// Log current Zantetsuken state for debugging.
    /// </summary>
    void LogState();

    #endregion
}
