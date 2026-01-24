using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// API for managing Leviathan's abilities and gauges.
/// 
/// Leviathan has TWO separate systems with DIFFERENT availability:
/// 
/// 1. ABYSSAL TEAR (Fixed location at playerState + 0x1C888):
///    - ALWAYS AVAILABLE - does NOT require Leviathan mode to be active
///    - Can build up gauge while fighting with any Eikon
///    - Gauge = TIME IN SECONDS the ability has been charging (NOT damage units!)
///    - State: 0=inactive, 2=charging, 4=executed
///    - CurrentLevel increases as time thresholds are reached
///    - Offsets: Gauge (0x378), State (0x37C), MaxLevel (0x37D), CurrentLevel (0x37E)
///    
/// 2. SERPENT'S CRY (Leviathan Eikon structure via IsSummonModeActive):
///    - ONLY available when Leviathan mode is active
///    - TidalUnitsUsed: 0-100 (or 0-150 if skill upgraded)
///    - UI shows: percentage = 100 * TidalUnitsUsed / MaxTidalUnits
///    - UnlimitedTidalSeconds: Timer for unlimited tidal mode
///    - Offsets: UnlimitedTidalSeconds (0x1C0C), TidalUnitsUsed (0x1C18), etc.
/// </summary>
public unsafe class LeviathanApi
{
    #region Constants
    
    /// <summary>
    /// Seconds required per Abyssal Tear level.
    /// The gauge accumulates time and gains 1 level every 8 seconds.
    /// </summary>
    public const float SecondsPerLevel = 8.0f;
    
    /// <summary>
    /// Default maximum Abyssal Tear level.
    /// </summary>
    public const int DefaultMaxLevel = 4;
    
    /// <summary>
    /// Base max tidal units for Serpent's Cry.
    /// Actual max comes from Skill::GetPotencyParameter(0x36).
    /// </summary>
    public const int BaseTidalMax = 100;
    
    /// <summary>
    /// Upgraded max tidal units for Serpent's Cry.
    /// </summary>
    public const int UpgradedTidalMax = 150;
    
    #endregion

    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> _getIsSummonModeActive;
    private readonly ILogger? _logger;
    private readonly string _modId;

    public LeviathanApi(
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
    /// Gets the pointer to the Leviathan-specific Eikon structure (for Serpent's Cry).
    /// Returns 0 if Leviathan mode is not active.
    /// </summary>
    public long GetLeviathanEikonPointer()
    {
        var isSummonModeActive = _getIsSummonModeActive();
        var globalPlayerStatePtr = _getGlobalPlayerStatePtr();

        if (isSummonModeActive == null || globalPlayerStatePtr == 0) return 0;
        
        long playerState = *(long*)globalPlayerStatePtr;
        if (playerState == 0) return 0;
        
        // Leviathan ID is 9
        return isSummonModeActive(playerState + PlayerStateOffsets.EikonSummonData, EikonUtils.EIKON_LEVIATHAN);
    }

    /// <summary>
    /// Check if Leviathan mode is currently active (for Serpent's Cry).
    /// </summary>
    public bool IsLeviathanActive => GetLeviathanEikonPointer() != 0;
    
    /// <summary>
    /// Check if the Abyssal Tear pointer is valid (should always be true when in game).
    /// </summary>
    public bool IsAbyssalTearAvailable => GetAbyssalTearPointer() != 0;

    // ================================================================
    // ABYSSAL TEAR - Fixed location in PlayerState
    // Does NOT require Leviathan mode to be active
    // Base: playerState + 0x1C888
    // ================================================================
    
    #region Abyssal Tear (VentGauge)

    /// <summary>
    /// Get the current Abyssal Tear gauge value (float).
    /// This is TIME IN SECONDS that the ability has been charging.
    /// Works regardless of whether Leviathan mode is active.
    /// </summary>
    public float GetAbyssalTearGauge()
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return 0f;
        return *(float*)(abyssalPtr + AbyssalTearOffsets.Gauge);
    }

    /// <summary>
    /// Get the current Abyssal Tear gauge as int (truncated seconds).
    /// </summary>
    public int GetAbyssalTearUnits()
    {
        return (int)GetAbyssalTearGauge();
    }
    
    /// <summary>
    /// Get the current Abyssal Tear time in seconds (float precision).
    /// Alias for GetAbyssalTearGauge().
    /// </summary>
    public float GetAbyssalTearUnitsFloat()
    {
        return GetAbyssalTearGauge();
    }
    
    /// <summary>
    /// Get the Abyssal Tear state byte.
    /// Values:
    ///   0 = Inactive (not charging)
    ///   2 = Charging (Vent Gauge active, building levels)
    ///   4 = Executed (ability fired, consumed levels)
    /// </summary>
    public byte GetAbyssalTearState()
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return 0;
        return *(byte*)(abyssalPtr + AbyssalTearOffsets.State);
    }
    
    /// <summary>
    /// Check if Abyssal Tear is currently charging (state == 2).
    /// </summary>
    public bool IsAbyssalTearActive => GetAbyssalTearState() == 2;
    
    /// <summary>
    /// Check if Abyssal Tear is charging (state == 2).
    /// Alias for IsAbyssalTearActive.
    /// </summary>
    public bool IsAbyssalTearCharging => GetAbyssalTearState() == 2;
    
    /// <summary>
    /// Check if Abyssal Tear was just executed (state == 4).
    /// </summary>
    public bool IsAbyssalTearExecuted => GetAbyssalTearState() == 4;
    
    /// <summary>
    /// Get the maximum level for current Abyssal Tear activation.
    /// </summary>
    public byte GetAbyssalTearMaxLevel()
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return 0;
        return *(byte*)(abyssalPtr + AbyssalTearOffsets.MaxLevel);
    }
    
    /// <summary>
    /// Get the current level progress for Abyssal Tear.
    /// </summary>
    public byte GetAbyssalTearCurrentLevel()
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
    public void SetAbyssalTearMaxLevel(byte maxLevel)
    {
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0) return;
        *(byte*)(abyssalPtr + AbyssalTearOffsets.MaxLevel) = maxLevel;
    }

    /// <summary>
    /// Set the current level for Abyssal Tear by adjusting the gauge time.
    /// This sets the gauge to (level * SecondsPerLevel) seconds.
    /// The game will automatically update the CurrentLevel field based on time.
    /// </summary>
    /// <param name="level">Target level (0 to MaxLevel)</param>
    public void SetAbyssalTearCurrentLevel(int level)
    {
        if (level < 0) level = 0;
        float targetSeconds = level * SecondsPerLevel;
        SetAbyssalTearGauge(targetSeconds);
    }

    /// <summary>
    /// Add levels to Abyssal Tear by adding time to the gauge.
    /// Each level = 8 seconds of charging time.
    /// The game will automatically update the CurrentLevel field based on time.
    /// </summary>
    /// <param name="levels">Levels to add (can be negative)</param>
    public void AddAbyssalTearCurrentLevel(int levels)
    {
        float secondsToAdd = levels * SecondsPerLevel;
        float currentGauge = GetAbyssalTearGauge();
        float newGauge = currentGauge + secondsToAdd;
        if (newGauge < 0f) newGauge = 0f;
        SetAbyssalTearGauge(newGauge);
    }

    /// <summary>
    /// Set the Abyssal Tear gauge value directly.
    /// </summary>
    /// <param name="value">New gauge value</param>
    public void SetAbyssalTearGauge(float value)
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
    public void SetAbyssalTearSeconds(float seconds, int maxLevel = DefaultMaxLevel)
    {
        float maxSeconds = maxLevel * SecondsPerLevel;
        if (seconds > maxSeconds) seconds = maxSeconds;
        if (seconds < 0f) seconds = 0f;
        SetAbyssalTearGauge(seconds);
    }

    /// <summary>
    /// Adds seconds to the Abyssal Tear gauge.
    /// </summary>
    /// <param name="seconds">Seconds to add (can be negative)</param>
    /// <param name="maxLevel">Maximum level cap (default: 4)</param>
    public void AddAbyssalTearSeconds(float seconds, int maxLevel = DefaultMaxLevel)
    {
        float currentSeconds = GetAbyssalTearGauge();
        float newSeconds = currentSeconds + seconds;
        
        float maxSeconds = maxLevel * SecondsPerLevel;
        if (newSeconds > maxSeconds) newSeconds = maxSeconds;
        if (newSeconds < 0f) newSeconds = 0f;

        SetAbyssalTearGauge(newSeconds);
    }

    #endregion

    #region Abyssal Tear Level Helpers

    /// <summary>
    /// Get the current Abyssal Tear level based on gauge time (0 to maxLevel).
    /// Each level = 8 seconds.
    /// </summary>
    public int GetAbyssalTearLevel()
    {
        return (int)(GetAbyssalTearGauge() / SecondsPerLevel);
    }

    /// <summary>
    /// Get seconds within the current Abyssal Tear level (0 to SecondsPerLevel).
    /// </summary>
    public float GetAbyssalTearSecondsInCurrentLevel()
    {
        return GetAbyssalTearGauge() % SecondsPerLevel;
    }

    /// <summary>
    /// Set the Abyssal Tear level directly (sets gauge to level * SecondsPerLevel).
    /// </summary>
    /// <param name="level">Target level</param>
    /// <param name="maxLevel">Maximum level cap</param>
    public void SetAbyssalTearLevel(int level, int maxLevel = DefaultMaxLevel)
    {
        if (level < 0) level = 0;
        if (level > maxLevel) level = maxLevel;
        SetAbyssalTearSeconds(level * SecondsPerLevel, maxLevel);
    }

    /// <summary>
    /// Add full levels to the Abyssal Tear gauge.
    /// Each level = 8 seconds.
    /// </summary>
    /// <param name="levels">Number of levels to add (can be negative)</param>
    /// <param name="maxLevel">Maximum level cap</param>
    public void AddAbyssalTearLevels(int levels, int maxLevel = DefaultMaxLevel)
    {
        AddAbyssalTearSeconds(levels * SecondsPerLevel, maxLevel);
    }

    /// <summary>
    /// Fill the Abyssal Tear gauge to maximum.
    /// </summary>
    /// <param name="maxLevel">Maximum level (default: 4)</param>
    public void FillAbyssalTearGauge(int maxLevel = DefaultMaxLevel)
    {
        SetAbyssalTearSeconds(maxLevel * SecondsPerLevel, maxLevel);
    }

    /// <summary>
    /// Empty the Abyssal Tear gauge completely.
    /// </summary>
    public void EmptyAbyssalTearGauge()
    {
        SetAbyssalTearGauge(0f);
    }

    #endregion

    // ================================================================
    // SERPENT'S CRY - TidalUnitsUsed (0x1C18) + UnlimitedTidalSeconds (0x1C0C)
    // TidalUnitsUsed: 0-100 (base) or 0-150 (upgraded skill)
    // UI shows percentage = 100 * TidalUnitsUsed / MaxTidalUnits
    // ================================================================
    
    #region Serpent's Cry (Tidal Units)

    /// <summary>
    /// Get the current Tidal units for Serpent's Cry.
    /// Value ranges from 0 to MaxTidalUnits (100 base, 150 upgraded).
    /// UI shows: percentage = 100 * TidalUnitsUsed / MaxTidalUnits
    /// </summary>
    public int GetTidalUnitsUsed()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(ushort*)(leviathanPtr + LeviathanEikonOffsets.TidalUnitsUsed);
    }

    /// <summary>
    /// Set the Tidal units for Serpent's Cry.
    /// </summary>
    /// <param name="units">Tidal units (0 to maxUnits)</param>
    /// <param name="maxUnits">Maximum units (100 base, 150 upgraded)</param>
    public void SetTidalUnitsUsed(int units, int maxUnits = UpgradedTidalMax)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        if (units < 0) units = 0;
        if (units > maxUnits) units = maxUnits;
        *(ushort*)(leviathanPtr + LeviathanEikonOffsets.TidalUnitsUsed) = (ushort)units;
    }

    /// <summary>
    /// Get the Tidal gauge percentage as displayed in the UI.
    /// Formula: 100 * TidalUnitsUsed / MaxTidalUnits
    /// </summary>
    /// <param name="maxUnits">Maximum units (100 base, 150 upgraded)</param>
    public int GetTidalPercentage(int maxUnits = UpgradedTidalMax)
    {
        int units = GetTidalUnitsUsed();
        if (maxUnits <= 0) return 0;
        return (100 * units) / maxUnits;
    }

    /// <summary>
    /// Add to the Tidal gauge for Serpent's Cry.
    /// Positive values = GAIN gauge (we subtract from TidalUnitsUsed since lower = more gauge).
    /// Negative values = LOSE gauge (we add to TidalUnitsUsed).
    /// 
    /// How it works internally:
    /// - TidalUnitsUsed tracks CONSUMED/USED units
    /// - UI shows: MaxTidal - TidalUnitsUsed = available gauge
    /// - So to GAIN gauge, we must DECREASE TidalUnitsUsed
    /// </summary>
    /// <param name="amount">Amount to add to gauge (positive = gain, negative = lose)</param>
    public void AddTidalGauge(int amount)
    {
        int maxUnits = GetStoredMaxTidalUnits();
        if (maxUnits <= 0) maxUnits = BaseTidalMax;
        
        // TidalUnitsUsed is CONSUMED units, so to GAIN gauge we SUBTRACT
        int current = GetTidalUnitsUsed();
        int newValue = current - amount; // SUBTRACT to gain gauge
        if (newValue < 0) newValue = 0;
        if (newValue > maxUnits) newValue = maxUnits;
        SetTidalUnitsUsedRaw(newValue);
    }
    
    /// <summary>
    /// Add to TidalUnitsUsed directly (consumed/used units).
    /// Higher TidalUnitsUsed = less gauge available.
    /// Use AddTidalGauge() for intuitive "gain gauge" behavior.
    /// </summary>
    /// <param name="amount">Amount to add to consumed units (can be negative)</param>
    public void AddTidalUnitsUsed(int amount)
    {
        int maxUnits = GetStoredMaxTidalUnits();
        if (maxUnits <= 0) maxUnits = BaseTidalMax;
        
        int current = GetTidalUnitsUsed();
        int newValue = current + amount;
        if (newValue < 0) newValue = 0;
        if (newValue > maxUnits) newValue = maxUnits;
        SetTidalUnitsUsedRaw(newValue);
    }
    
    /// <summary>
    /// Set TidalUnitsUsed directly (raw internal value).
    /// </summary>
    private void SetTidalUnitsUsedRaw(int rawValue)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        if (rawValue < 0) rawValue = 0;
        *(ushort*)(leviathanPtr + LeviathanEikonOffsets.TidalUnitsUsed) = (ushort)rawValue;
    }

    /// <summary>
    /// Fill the Tidal gauge to maximum (sets TidalUnitsUsed to 0).
    /// </summary>
    public void FillTidalGauge()
    {
        SetTidalUnitsUsedRaw(0);
    }

    /// <summary>
    /// Empty the Tidal gauge completely (sets TidalUnitsUsed to max).
    /// </summary>
    public void EmptyTidalGauge()
    {
        int maxUnits = GetStoredMaxTidalUnits();
        if (maxUnits <= 0) maxUnits = BaseTidalMax;
        SetTidalUnitsUsedRaw(maxUnits);
    }
    
    /// <summary>
    /// Get the unlimited tidal seconds remaining (Serpent's Cry effect).
    /// </summary>
    public float GetUnlimitedTidalSeconds()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0f;
        return *(float*)(leviathanPtr + LeviathanEikonOffsets.UnlimitedTidalSeconds);
    }

    /// <summary>
    /// Set the unlimited tidal seconds (Serpent's Cry effect).
    /// </summary>
    public void SetUnlimitedTidalSeconds(float seconds)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        *(float*)(leviathanPtr + LeviathanEikonOffsets.UnlimitedTidalSeconds) = seconds;
    }

    /// <summary>
    /// Add to the unlimited tidal seconds (Serpent's Cry effect).
    /// </summary>
    public void AddUnlimitedTidalSeconds(float seconds)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        float* pGauge = (float*)(leviathanPtr + LeviathanEikonOffsets.UnlimitedTidalSeconds);
        *pGauge += seconds;
    }

    #endregion

    #region Serpent's Cry State Bytes (Leviathan Mode Only)

    /// <summary>
    /// Get state byte 1 (offset 0x1C1C from Leviathan Eikon pointer).
    /// These states are for Serpent's Cry and require Leviathan mode to be active.
    /// </summary>
    public byte GetSerpentsCryState1()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(byte*)(leviathanPtr + LeviathanEikonOffsets.State1);
    }

    /// <summary>
    /// Set state byte 1 (Serpent's Cry state).
    /// </summary>
    public void SetSerpentsCryState1(byte value)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        *(byte*)(leviathanPtr + LeviathanEikonOffsets.State1) = value;
    }

    /// <summary>
    /// Get state byte 2 (offset 0x1C1D) - Reload animation state.
    /// Values: 0 = idle, 1 = pending, 2 = active.
    /// </summary>
    public byte GetSerpentsCryState2()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(byte*)(leviathanPtr + LeviathanEikonOffsets.State2);
    }

    /// <summary>
    /// Check if State2 == 2 (reload animation active).
    /// </summary>
    public bool IsSerpentsCryState2Active => GetSerpentsCryState2() == 2;

    /// <summary>
    /// Get state byte 3 (offset 0x1C1E) - Gate for State4.
    /// </summary>
    public byte GetSerpentsCryState3()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(byte*)(leviathanPtr + LeviathanEikonOffsets.State3);
    }

    /// <summary>
    /// Get state byte 4 (offset 0x1C1F) - Special effect state.
    /// Values: 0 = idle, 1 = pending, 2 = active.
    /// </summary>
    public byte GetSerpentsCryState4()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(byte*)(leviathanPtr + LeviathanEikonOffsets.State4);
    }

    /// <summary>
    /// Check if State4 == 2 (special effect active).
    /// </summary>
    public bool IsSerpentsCryState4Active => GetSerpentsCryState4() == 2;

    #endregion

    #region Timer Internals

    /// <summary>
    /// Get TidalRecoveryTimer (offset 0x1C10) - Seconds until next TidalUnits recovery.
    /// After an attack, this is typically set to ~2.5 seconds.
    /// When it reaches 0, TidalUnits regenerates.
    /// </summary>
    public float GetTidalRecoveryTimer()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0f;
        return *(float*)(leviathanPtr + LeviathanEikonOffsets.TidalTimer);
    }

    /// <summary>
    /// Set TidalRecoveryTimer (offset 0x1C10) - Seconds until next TidalUnits recovery.
    /// Set to 0 for immediate recovery, or higher values to delay recovery.
    /// Game typically sets this to ~2.5 seconds after attacks.
    /// </summary>
    /// <param name="seconds">Seconds until recovery (0 = immediate)</param>
    public void SetTidalRecoveryTimer(float seconds)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        if (seconds < 0f) seconds = 0f;
        *(float*)(leviathanPtr + LeviathanEikonOffsets.TidalTimer) = seconds;
    }

    /// <summary>
    /// Reset the TidalRecoveryTimer to 0 for immediate TidalUnits recovery.
    /// </summary>
    public void ResetTidalRecoveryTimer()
    {
        SetTidalRecoveryTimer(0f);
    }

    /// <summary>
    /// Get TidalTimer2 (offset 0x1C14) - Second internal accumulator (unknown purpose).
    /// </summary>
    public float GetTidalTimer2()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0f;
        return *(float*)(leviathanPtr + LeviathanEikonOffsets.TidalTimer2);
    }

    /// <summary>
    /// Get the max tidal units stored in memory (offset 0x1C1A).
    /// This value is set based on skill potency parameter.
    /// </summary>
    public int GetStoredMaxTidalUnits()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(ushort*)(leviathanPtr + LeviathanEikonOffsets.MaxTidalUnits);
    }

    #endregion

    #region Debug

    /// <summary>
    /// Log current Leviathan state for debugging.
    /// Shows Abyssal Tear (always available) and Serpent's Cry (only when Leviathan active).
    /// </summary>
    public void LogState()
    {
        if (_logger == null) return;
        
        // Abyssal Tear - Always available (fixed location in player state)
        long abyssalPtr = GetAbyssalTearPointer();
        if (abyssalPtr == 0)
        {
            _logger.WriteLine($"[{_modId}] [Abyssal Tear] Pointer not available (not in game?)");
        }
        else
        {
            float abyssalGauge = GetAbyssalTearGauge();
            byte abyssalState = GetAbyssalTearState();
            byte abyssalMaxLevel = GetAbyssalTearMaxLevel();
            byte abyssalCurrentLevel = GetAbyssalTearCurrentLevel();
            int calculatedLevel = GetAbyssalTearLevel();
            
            _logger.WriteLine($"[{_modId}] [Abyssal Tear] Ptr=0x{abyssalPtr:X}");
            _logger.WriteLine($"[{_modId}] [Abyssal Tear] Gauge: {abyssalGauge:F2} | Level: {calculatedLevel} | State: {abyssalState} | MaxLvl: {abyssalMaxLevel} | CurLvl: {abyssalCurrentLevel}");
        }
        
        // Serpent's Cry - Only when Leviathan mode is active
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0)
        {
            _logger.WriteLine($"[{_modId}] [Serpent's Cry] Leviathan mode not active");
            return;
        }
        
        // Serpent's Cry (TidalUnits + UnlimitedSeconds)
        int tidalUnitsUsed = GetTidalUnitsUsed();
        int storedMaxTidal = GetStoredMaxTidalUnits();
        int tidalPercent = GetTidalPercentage();
        float unlimitedSeconds = GetUnlimitedTidalSeconds();
        float recoveryTimer = GetTidalRecoveryTimer();
        float timer2 = GetTidalTimer2();
        
        // States (Serpent's Cry)
        byte s1 = GetSerpentsCryState1();
        byte s2 = GetSerpentsCryState2();
        byte s3 = GetSerpentsCryState3();
        byte s4 = GetSerpentsCryState4();
        
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] Ptr=0x{leviathanPtr:X}");
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] TidalUnitsUsed: {tidalUnitsUsed}/{storedMaxTidal} ({tidalPercent}%) | UnlimitedSec: {unlimitedSeconds:F2}");
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] RecoveryTimer: {recoveryTimer:F2}s | Timer2: {timer2:F3}");
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] States: S1={s1} S2={s2} S3={s3} S4={s4}");
    }

    #endregion
}
