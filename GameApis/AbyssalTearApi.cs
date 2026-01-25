using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// API for managing Leviathan's Abyssal Tear (Vent Gauge).
/// 
/// Abyssal Tear is ALWAYS AVAILABLE - it does NOT require Leviathan mode to be active.
/// The gauge builds up while fighting with any Eikon.
/// 
/// Key facts:
/// - Fixed location at playerState + 0x1C888
/// - Gauge = TIME IN SECONDS the ability has been charging (NOT damage units!)
/// - State: 0=inactive, 2=charging, 4=executed
/// - CurrentLevel increases as time thresholds are reached (8 seconds per level)
/// - Offsets: Gauge (0x378), State (0x37C), MaxLevel (0x37D), CurrentLevel (0x37E)
/// </summary>
public unsafe class AbyssalTearApi
{
    #region Constants
    
    /// <summary>
    /// Seconds required per Abyssal Tear level.
    /// The gauge accumulates time and gains 1 level every 8 seconds.
    /// </summary>
    public const float SecondsPerLevel = 8.0f;
    
    /// <summary>
    /// Minimum Abyssal Tear level (game UI shows 1-based levels).
    /// </summary>
    public const int MinLevel = 1;
    
    /// <summary>
    /// Default maximum Abyssal Tear level.
    /// </summary>
    public const int DefaultMaxLevel = 4;
    
    #endregion

    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly ILogger? _logger;
    private readonly string _modId;

    public AbyssalTearApi(
        Func<long> getGlobalPlayerStatePtr, 
        ILogger? logger = null, 
        string modId = "")
    {
        _getGlobalPlayerStatePtr = getGlobalPlayerStatePtr;
        _logger = logger;
        _modId = modId;
    }

    /// <summary>
    /// Gets the pointer to the Abyssal Tear structure.
    /// This is ALWAYS available (fixed location in player state) - does NOT require Leviathan mode.
    /// Base: playerState + 0x1C888
    /// </summary>
    public long GetAbyssalTearPointer()
    {
        var globalPlayerStatePtr = _getGlobalPlayerStatePtr();
        if (globalPlayerStatePtr == 0) return 0;
        
        long playerState = *(long*)globalPlayerStatePtr;
        if (playerState == 0) return 0;
        
        return playerState + PlayerStateOffsets.AbyssalTearBase;
    }
    
    /// <summary>
    /// Check if the Abyssal Tear pointer is valid (should always be true when in game).
    /// </summary>
    public bool IsAvailable => GetAbyssalTearPointer() != 0;

    #region Gauge Values

    /// <summary>
    /// Get the current Abyssal Tear gauge value (float).
    /// This is TIME IN SECONDS that the ability has been charging.
    /// Works regardless of whether Leviathan mode is active.
    /// </summary>
    public float GetGauge()
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return 0f;
        return *(float*)(abyssalPtr + AbyssalTearOffsets.Gauge);
    }

    /// <summary>
    /// Get the current Abyssal Tear gauge as int (truncated seconds).
    /// </summary>
    public int GetUnits()
    {
        return (int)GetGauge();
    }
    
    /// <summary>
    /// Get the current Abyssal Tear time in seconds (float precision).
    /// Alias for GetGauge().
    /// </summary>
    public float GetSeconds()
    {
        return GetGauge();
    }

    /// <summary>
    /// Set the Abyssal Tear gauge value directly.
    /// </summary>
    /// <param name="value">New gauge value in seconds</param>
    public void SetGauge(float value)
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return;
        if (value < 0f) value = 0f;
        *(float*)(abyssalPtr + AbyssalTearOffsets.Gauge) = value;
    }

    /// <summary>
    /// Set the Abyssal Tear gauge time directly (in seconds).
    /// </summary>
    /// <param name="seconds">New gauge time in seconds</param>
    /// <param name="maxLevel">Maximum level cap (default: 4)</param>
    public void SetSeconds(float seconds, int maxLevel = DefaultMaxLevel)
    {
        float maxSeconds = maxLevel * SecondsPerLevel;
        if (seconds > maxSeconds) seconds = maxSeconds;
        if (seconds < 0f) seconds = 0f;
        SetGauge(seconds);
    }

    /// <summary>
    /// Adds seconds to the Abyssal Tear gauge.
    /// </summary>
    /// <param name="seconds">Seconds to add (can be negative)</param>
    /// <param name="maxLevel">Maximum level cap (default: 4)</param>
    public void AddSeconds(float seconds, int maxLevel = DefaultMaxLevel)
    {
        float currentSeconds = GetGauge();
        float newSeconds = currentSeconds + seconds;
        
        float maxSeconds = maxLevel * SecondsPerLevel;
        if (newSeconds > maxSeconds) newSeconds = maxSeconds;
        if (newSeconds < 0f) newSeconds = 0f;

        SetGauge(newSeconds);
    }

    #endregion

    #region State

    /// <summary>
    /// Get the Abyssal Tear state byte.
    /// Values:
    ///   0 = Inactive (not charging)
    ///   2 = Charging (Vent Gauge active, building levels)
    ///   4 = Executed (ability fired, consumed levels)
    /// </summary>
    public byte GetState()
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return 0;
        return *(byte*)(abyssalPtr + AbyssalTearOffsets.State);
    }
    
    /// <summary>
    /// Check if Abyssal Tear is currently charging (state == 2).
    /// </summary>
    public bool IsActive => GetState() == 2;
    
    /// <summary>
    /// Check if Abyssal Tear is charging (state == 2).
    /// Alias for IsActive.
    /// </summary>
    public bool IsCharging => GetState() == 2;
    
    /// <summary>
    /// Check if Abyssal Tear was just executed (state == 4).
    /// </summary>
    public bool IsExecuted => GetState() == 4;
    
    /// <summary>
    /// Get the maximum level for current Abyssal Tear activation.
    /// </summary>
    public byte GetMaxLevelStored()
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return 0;
        return *(byte*)(abyssalPtr + AbyssalTearOffsets.MaxLevel);
    }
    
    /// <summary>
    /// Get the current level progress for Abyssal Tear (game-tracked value).
    /// </summary>
    public byte GetCurrentLevelStored()
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return 0;
        return *(byte*)(abyssalPtr + AbyssalTearOffsets.CurrentLevel);
    }

    /// <summary>
    /// Set the maximum level for Abyssal Tear.
    /// This determines how many levels can be accumulated.
    /// </summary>
    /// <param name="maxLevel">New max level (typically 3 or 4)</param>
    public void SetMaxLevelStored(byte maxLevel)
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return;
        *(byte*)(abyssalPtr + AbyssalTearOffsets.MaxLevel) = maxLevel;
    }

    #endregion

    #region Level Helpers

    /// <summary>
    /// Get the current Abyssal Tear level based on gauge time (1 to maxLevel).
    /// Level 1 = 0 seconds, Level 2 = 8 seconds, etc.
    /// </summary>
    public int GetLevel()
    {
        return (int)(GetGauge() / SecondsPerLevel) + MinLevel;
    }

    /// <summary>
    /// Get seconds within the current Abyssal Tear level (0 to SecondsPerLevel).
    /// </summary>
    public float GetSecondsInCurrentLevel()
    {
        return GetGauge() % SecondsPerLevel;
    }

    /// <summary>
    /// Set the Abyssal Tear level directly.
    /// Level 1 = 0 seconds, Level 2 = 8 seconds, etc.
    /// </summary>
    /// <param name="level">Target level (1 to maxLevel)</param>
    /// <param name="maxLevel">Maximum level cap</param>
    public void SetLevel(int level, int maxLevel = DefaultMaxLevel)
    {
        if (level < MinLevel) level = MinLevel;
        if (level > maxLevel) level = maxLevel;
        SetSeconds((level - MinLevel) * SecondsPerLevel, maxLevel);
    }

    /// <summary>
    /// Add full levels to the Abyssal Tear gauge.
    /// Each level = 8 seconds.
    /// </summary>
    /// <param name="levels">Number of levels to add (can be negative)</param>
    /// <param name="maxLevel">Maximum level cap</param>
    public void AddLevels(int levels, int maxLevel = DefaultMaxLevel)
    {
        AddSeconds(levels * SecondsPerLevel, maxLevel);
    }

    /// <summary>
    /// Fill the Abyssal Tear gauge to maximum level.
    /// </summary>
    /// <param name="maxLevel">Maximum level (default: 4)</param>
    public void FillGauge(int maxLevel = DefaultMaxLevel)
    {
        SetLevel(maxLevel, maxLevel);
    }

    /// <summary>
    /// Reset the Abyssal Tear gauge to minimum (Level 1 = 0 seconds).
    /// </summary>
    public void EmptyGauge()
    {
        SetLevel(MinLevel);
    }

    #endregion

    #region Debug

    /// <summary>
    /// Log current Abyssal Tear state for debugging.
    /// </summary>
    public void LogState()
    {
        if (_logger == null) return;
        
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0)
        {
            _logger.WriteLine($"[{_modId}] [Abyssal Tear] Pointer not available (not in game?)");
            return;
        }
        
        float gauge = GetGauge();
        byte state = GetState();
        byte maxLevel = GetMaxLevelStored();
        byte currentLevel = GetCurrentLevelStored();
        int calculatedLevel = GetLevel();
        
        _logger.WriteLine($"[{_modId}] [Abyssal Tear] Ptr=0x{abyssalPtr:X}");
        _logger.WriteLine($"[{_modId}] [Abyssal Tear] Gauge: {gauge:F2}s | Level: {calculatedLevel} | State: {state} | MaxLvl: {maxLevel} | CurLvl: {currentLevel}");
    }

    #endregion
}
