using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

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
/// - Max level from Skill::GetPotencyParameter(0x34)
/// - Offsets: Gauge (0x378), State (0x37C), MaxLevel (0x37D), CurrentLevel (0x37E)
/// </summary>
public unsafe class AbyssalTearApi : IAbyssalTearApi
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
    private readonly SkillPotencyApi? _skillPotencyApi;
    private readonly ILogger? _logger;
    private readonly string _modId;

    public AbyssalTearApi(
        Func<long> getGlobalPlayerStatePtr,
        SkillPotencyApi? skillPotencyApi = null,
        ILogger? logger = null, 
        string modId = "")
    {
        _getGlobalPlayerStatePtr = getGlobalPlayerStatePtr;
        _skillPotencyApi = skillPotencyApi;
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

    #region Max Level

    /// <summary>
    /// Get the maximum level from the game via SkillPotencyApi.
    /// </summary>
    public int GetMaxLevel()
    {
        return _skillPotencyApi?.AbyssalTearMaxLevel ?? DefaultMaxLevel;
    }

    /// <summary>
    /// Get the maximum units based on max level (SecondsPerLevel * MaxLevel).
    /// </summary>
    public int GetMaxUnits()
    {
        return (int)(GetMaxLevel() * SecondsPerLevel);
    }

    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current Abyssal Tear gauge as int (truncated seconds).
    /// </summary>
    public int GetUnits()
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return 0;
        return (int)*(float*)(abyssalPtr + AbyssalTearOffsets.Gauge);
    }

    /// <summary>
    /// Set the Abyssal Tear gauge units directly.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="units">New gauge value in seconds</param>
    public void SetUnits(int units)
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return;
        
        int maxUnits = GetMaxUnits();
        if (units > maxUnits) units = maxUnits;
        if (units < 0) units = 0;
        
        *(float*)(abyssalPtr + AbyssalTearOffsets.Gauge) = (float)units;
    }

    /// <summary>
    /// Adds units (seconds) to the Abyssal Tear gauge.
    /// Capped to max units based on game's max level.
    /// </summary>
    /// <param name="amount">Units to add (can be negative)</param>
    public void AddUnits(int amount)
    {
        int currentUnits = GetUnits();
        int newUnits = currentUnits + amount;
        
        int maxUnits = GetMaxUnits();
        if (newUnits > maxUnits) newUnits = maxUnits;
        if (newUnits < 0) newUnits = 0;

        SetUnits(newUnits);
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
        return (GetUnits() / (int)SecondsPerLevel) + MinLevel;
    }

    /// <summary>
    /// Set the Abyssal Tear level directly.
    /// Level 1 = 0 seconds, Level 2 = 8 seconds, etc.
    /// Capped to game's max level.
    /// </summary>
    /// <param name="level">Target level</param>
    public void SetLevel(int level)
    {
        int maxLevel = GetMaxLevel();
        if (level < MinLevel) level = MinLevel;
        if (level > maxLevel) level = maxLevel;
        SetUnits((int)((level - MinLevel) * SecondsPerLevel));
    }

    /// <summary>
    /// Add full levels to the Abyssal Tear gauge.
    /// Each level = 8 seconds.
    /// </summary>
    /// <param name="levels">Number of levels to add (can be negative)</param>
    public void AddLevels(int levels)
    {
        AddUnits((int)(levels * SecondsPerLevel));
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Fill the Abyssal Tear gauge to maximum level.
    /// </summary>
    public void FillGauge()
    {
        int maxLevel = GetMaxLevel();
        SetLevel(maxLevel);
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
        
        int units = GetUnits();
        byte state = GetState();
        int level = GetLevel();
        bool isActive = IsActive;
        bool isExecuted = IsExecuted;
        
        _logger.WriteLine($"[{_modId}] [Abyssal Tear] Ptr=0x{abyssalPtr:X}");
        _logger.WriteLine($"[{_modId}] [Abyssal Tear] Units: {units}s | Level: {level} | State: {state} | IsActive: {isActive} | IsExecuted: {isExecuted}");
    }

    #endregion
}
