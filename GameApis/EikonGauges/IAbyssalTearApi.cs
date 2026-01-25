namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// Interface for Leviathan's Abyssal Tear (Vent Gauge) API.
/// 
/// Abyssal Tear is ALWAYS AVAILABLE - it does NOT require Leviathan mode to be active.
/// The gauge builds up while fighting with any Eikon.
/// 
/// Key facts:
/// - Units = TIME IN SECONDS the ability has been charging
/// - 8 seconds per level (SecondsPerLevel)
/// - Levels are 1-based (MinLevel = 1, DefaultMaxLevel = 4)
/// </summary>
public interface IAbyssalTearApi
{
    #region Availability

    /// <summary>
    /// Gets the pointer to the Abyssal Tear structure.
    /// This is ALWAYS available (fixed location in player state).
    /// </summary>
    long GetAbyssalTearPointer();
    
    /// <summary>
    /// Check if the Abyssal Tear pointer is valid.
    /// Should always be true when in game.
    /// </summary>
    bool IsAvailable { get; }

    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current Abyssal Tear gauge units (truncated seconds).
    /// </summary>
    int GetUnits();

    /// <summary>
    /// Set the Abyssal Tear gauge units directly.
    /// </summary>
    /// <param name="units">New gauge value in seconds</param>
    /// <param name="maxLevel">Maximum level cap (default: 4)</param>
    void SetUnits(int units, int maxLevel = 4);

    /// <summary>
    /// Adds units (seconds) to the Abyssal Tear gauge.
    /// </summary>
    /// <param name="amount">Units to add (can be negative)</param>
    /// <param name="maxLevel">Maximum level cap (default: 4)</param>
    void AddUnits(int amount, int maxLevel = 4);

    #endregion

    #region Level

    /// <summary>
    /// Get the current Abyssal Tear level based on gauge time (1 to maxLevel).
    /// </summary>
    int GetLevel();

    /// <summary>
    /// Set the Abyssal Tear level directly.
    /// </summary>
    /// <param name="level">Target level (1 to maxLevel)</param>
    /// <param name="maxLevel">Maximum level cap</param>
    void SetLevel(int level, int maxLevel = 4);

    /// <summary>
    /// Add full levels to the Abyssal Tear gauge.
    /// </summary>
    /// <param name="levels">Number of levels to add (can be negative)</param>
    /// <param name="maxLevel">Maximum level cap</param>
    void AddLevels(int levels, int maxLevel = 4);

    #endregion

    #region Utilities

    /// <summary>
    /// Fill the Abyssal Tear gauge to maximum level.
    /// </summary>
    /// <param name="maxLevel">Maximum level (default: 4)</param>
    void FillGauge(int maxLevel = 4);

    /// <summary>
    /// Reset the Abyssal Tear gauge to minimum (Level 1).
    /// </summary>
    void EmptyGauge();

    #endregion

    #region State

    /// <summary>
    /// Get the Abyssal Tear state byte.
    /// Values: 0 = Inactive, 2 = Charging, 4 = Executed
    /// </summary>
    byte GetState();
    
    /// <summary>
    /// Check if Abyssal Tear is currently active/charging (state == 2).
    /// </summary>
    bool IsActive { get; }
    
    /// <summary>
    /// Check if Abyssal Tear was just executed (state == 4).
    /// </summary>
    bool IsExecuted { get; }

    #endregion

    #region Debug

    /// <summary>
    /// Log current Abyssal Tear state for debugging.
    /// </summary>
    void LogState();

    #endregion
}
