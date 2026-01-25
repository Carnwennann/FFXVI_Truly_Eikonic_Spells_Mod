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

    #region Max Level

    /// <summary>
    /// Get the maximum level from the game.
    /// Typically 4 (base) or higher if upgraded.
    /// </summary>
    int GetMaxLevel();

    /// <summary>
    /// Get the maximum units based on max level (SecondsPerLevel * MaxLevel).
    /// </summary>
    int GetMaxUnits();

    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current Abyssal Tear gauge units (truncated seconds).
    /// </summary>
    int GetUnits();

    /// <summary>
    /// Set the Abyssal Tear gauge units directly.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="units">New gauge value in seconds</param>
    void SetUnits(int units);

    /// <summary>
    /// Adds units (seconds) to the Abyssal Tear gauge.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="amount">Units to add (can be negative)</param>
    void AddUnits(int amount);

    #endregion

    #region Level

    /// <summary>
    /// Get the current Abyssal Tear level based on gauge time (1 to maxLevel).
    /// </summary>
    int GetLevel();

    /// <summary>
    /// Set the Abyssal Tear level directly.
    /// Capped to game's max level.
    /// </summary>
    /// <param name="level">Target level</param>
    void SetLevel(int level);

    /// <summary>
    /// Add full levels to the Abyssal Tear gauge.
    /// </summary>
    /// <param name="levels">Number of levels to add (can be negative)</param>
    void AddLevels(int levels);

    #endregion

    #region Utilities

    /// <summary>
    /// Fill the Abyssal Tear gauge to maximum level.
    /// </summary>
    void FillGauge();

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
