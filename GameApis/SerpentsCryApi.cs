using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// API for managing Leviathan's Serpent's Cry (Tidal Gauge).
/// 
/// Serpent's Cry is ONLY available when Leviathan mode is active.
/// 
/// Key facts:
/// - TidalUnitsUsed: 0-100 (or 0-150 if skill upgraded)
/// - UI shows: percentage = 100 * TidalUnitsUsed / MaxTidalUnits
/// - UnlimitedTidalSeconds: Timer for unlimited tidal mode
/// - Offsets: UnlimitedTidalSeconds (0x1C0C), TidalUnitsUsed (0x1C18), etc.
/// </summary>
public unsafe class SerpentsCryApi
{
    #region Constants
    
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

    public SerpentsCryApi(
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

    #region Tidal Units

    /// <summary>
    /// Get the max tidal units from the game (stored at offset 0x1C1A).
    /// Falls back to UpgradedTidalMax (150) if not available.
    /// </summary>
    public int GetMaxTidalUnits()
    {
        int stored = GetStoredMaxTidalUnits();
        return stored > 0 ? stored : UpgradedTidalMax;
    }

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
        int maxUnits = GetMaxTidalUnits();
        
        // TidalUnitsUsed is CONSUMED units, so to GAIN gauge we SUBTRACT
        int current = GetTidalUnitsUsed();
        int newValue = current - amount; // SUBTRACT to gain gauge
        if (newValue < 0) newValue = 0;
        if (newValue > maxUnits) newValue = maxUnits;
        SetTidalUnitsUsedRaw(newValue);
    }
    
    /// <summary>
    /// Subtract from the Tidal gauge for Serpent's Cry.
    /// This is the opposite of AddTidalGauge - positive values = LOSE gauge.
    /// </summary>
    /// <param name="amount">Amount to subtract from gauge (positive = lose, negative = gain)</param>
    public void SubtractTidalGauge(int amount)
    {
        AddTidalGauge(-amount);
    }
    
    /// <summary>
    /// Add to TidalUnitsUsed directly (consumed/used units).
    /// Higher TidalUnitsUsed = less gauge available.
    /// Use AddTidalGauge() for intuitive "gain gauge" behavior.
    /// </summary>
    /// <param name="amount">Amount to add to consumed units (can be negative)</param>
    public void AddTidalUnitsUsed(int amount)
    {
        int maxUnits = GetMaxTidalUnits();
        
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
    /// Uses the game's actual max tidal units value.
    /// </summary>
    public void EmptyTidalGauge()
    {
        int maxUnits = GetMaxTidalUnits();
        SetTidalUnitsUsedRaw(maxUnits);
    }

    #endregion

    #region Unlimited Tidal

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
        if (seconds < 0f) seconds = 0f;
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
        float newValue = *pGauge + seconds;
        if (newValue < 0f) newValue = 0f;
        *pGauge = newValue;
    }

    #endregion

    #region State Bytes

    /// <summary>
    /// Get state byte 1 (offset 0x1C1C from Leviathan Eikon pointer).
    /// These states are for Serpent's Cry and require Leviathan mode to be active.
    /// </summary>
    public byte GetState1()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(byte*)(leviathanPtr + LeviathanEikonOffsets.State1);
    }

    /// <summary>
    /// Set state byte 1 (Serpent's Cry state).
    /// </summary>
    public void SetState1(byte value)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        *(byte*)(leviathanPtr + LeviathanEikonOffsets.State1) = value;
    }

    /// <summary>
    /// Get state byte 2 (offset 0x1C1D) - Reload animation state.
    /// Values: 0 = idle, 1 = pending, 2 = active.
    /// </summary>
    public byte GetState2()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(byte*)(leviathanPtr + LeviathanEikonOffsets.State2);
    }

    /// <summary>
    /// Check if State2 == 2 (reload animation active).
    /// </summary>
    public bool IsState2Active => GetState2() == 2;

    /// <summary>
    /// Get state byte 3 (offset 0x1C1E) - Gate for State4.
    /// </summary>
    public byte GetState3()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(byte*)(leviathanPtr + LeviathanEikonOffsets.State3);
    }

    /// <summary>
    /// Get state byte 4 (offset 0x1C1F) - Special effect state.
    /// Values: 0 = idle, 1 = pending, 2 = active.
    /// </summary>
    public byte GetState4()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(byte*)(leviathanPtr + LeviathanEikonOffsets.State4);
    }

    /// <summary>
    /// Check if State4 == 2 (special effect active).
    /// </summary>
    public bool IsState4Active => GetState4() == 2;

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
    /// Log current Serpent's Cry state for debugging.
    /// </summary>
    public void LogState()
    {
        if (_logger == null) return;
        
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0)
        {
            _logger.WriteLine($"[{_modId}] [Serpent's Cry] Leviathan mode not active");
            return;
        }
        
        int tidalUnitsUsed = GetTidalUnitsUsed();
        int storedMaxTidal = GetStoredMaxTidalUnits();
        int tidalPercent = GetTidalPercentage();
        float unlimitedSeconds = GetUnlimitedTidalSeconds();
        float recoveryTimer = GetTidalRecoveryTimer();
        float timer2 = GetTidalTimer2();
        
        byte s1 = GetState1();
        byte s2 = GetState2();
        byte s3 = GetState3();
        byte s4 = GetState4();
        
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] Ptr=0x{leviathanPtr:X}");
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] TidalUnitsUsed: {tidalUnitsUsed}/{storedMaxTidal} ({tidalPercent}%) | UnlimitedSec: {unlimitedSeconds:F2}");
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] RecoveryTimer: {recoveryTimer:F2}s | Timer2: {timer2:F3}");
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] States: S1={s1} S2={s2} S3={s3} S4={s4}");
    }

    #endregion
}
