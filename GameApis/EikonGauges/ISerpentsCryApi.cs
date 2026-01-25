namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// Interface for Leviathan's Serpent's Cry (Tidal Gauge) API.
/// 
/// Serpent's Cry is ONLY available when Leviathan mode is active.
/// 
/// Key facts:
/// - Units: 0-100 (base) or 0-150 (upgraded)
/// - UI shows: percentage = 100 * Units / MaxUnits
/// - No levels - this is a percentage-based resource gauge
/// </summary>
public interface ISerpentsCryApi
{
    #region Availability

    /// <summary>
    /// Gets the pointer to the Leviathan-specific Eikon structure.
    /// Returns 0 if Leviathan mode is not active.
    /// </summary>
    long GetLeviathanEikonPointer();

    /// <summary>
    /// Check if Leviathan mode is currently active.
    /// </summary>
    bool IsLeviathanActive { get; }

    #endregion

    #region Tidal Units

    /// <summary>
    /// Get the max tidal units from the game.
    /// Falls back to UpgradedTidalMax (150) if not available.
    /// </summary>
    int GetMaxUnits();

    /// <summary>
    /// Get the current Tidal units available (MaxUnits - Used).
    /// </summary>
    int GetUnits();

    /// <summary>
    /// Set the Tidal units for Serpent's Cry.
    /// </summary>
    /// <param name="units">Tidal units (0 to maxUnits)</param>
    void SetUnits(int units);

    /// <summary>
    /// Add to the Tidal gauge for Serpent's Cry.
    /// Positive = GAIN gauge, Negative = LOSE gauge.
    /// </summary>
    /// <param name="amount">Amount to add (can be negative)</param>
    void AddUnits(int amount);

    #endregion

    #region Utilities

    /// <summary>
    /// Fill the Tidal gauge to maximum.
    /// </summary>
    void FillGauge();

    /// <summary>
    /// Empty the Tidal gauge completely.
    /// </summary>
    void EmptyGauge();

    #endregion

    #region Unlimited Units Timer

    /// <summary>
    /// Get the unlimited units timer seconds remaining.
    /// </summary>
    float GetUnlimitedUnitsTimer();

    /// <summary>
    /// Set the unlimited units timer seconds.
    /// </summary>
    void SetUnlimitedUnitsTimer(float seconds);

    /// <summary>
    /// Add to the unlimited units timer seconds.
    /// </summary>
    void AddUnlimitedUnitsTimer(float seconds);

    #endregion

    #region Start Recovery Timer

    /// <summary>
    /// Get StartRecoveryTimer (seconds until next TidalUnits recovery).
    /// </summary>
    float GetStartRecoveryTimer();

    /// <summary>
    /// Set StartRecoveryTimer.
    /// </summary>
    /// <param name="seconds">Seconds until recovery (0 = immediate)</param>
    void SetStartRecoveryTimer(float seconds);

    /// <summary>
    /// Reset the StartRecoveryTimer to 0 for immediate recovery.
    /// </summary>
    void ResetStartRecoveryTimer();

    #endregion

    #region Debug

    /// <summary>
    /// Log current Serpent's Cry state for debugging.
    /// </summary>
    void LogState();

    #endregion
}
