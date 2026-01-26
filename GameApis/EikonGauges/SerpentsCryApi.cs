using ff16.gameplay.truly_eikonic_spells.Utils;
using ff16.gameplay.truly_eikonic_spells.GameStructs;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// API for managing Leviathan's Serpent's Cry (Tidal Gauge).
/// 
/// Serpent's Cry is ONLY available when Leviathan mode is active.
/// 
/// Key facts:
/// - TidalUnitsUsed: 0-100 (or 0-150 if skill upgraded)
/// - UI shows: percentage = 100 * TidalUnitsUsed / MaxTidalUnits
/// - UnlimitedTidalSeconds: Timer for unlimited tidal mode
/// - Max units from Skill::GetPotencyParameter(0x36)
/// - Offsets: UnlimitedTidalSeconds (0x1C0C), TidalUnitsUsed (0x1C18), etc.
/// </summary>
public unsafe class SerpentsCryApi : ISerpentsCryApi
{
    #region Constants
    
    /// <summary>
    /// Base max tidal units for Serpent's Cry.
    /// </summary>
    public const int BaseTidalMax = 100;
    
    /// <summary>
    /// Default max tidal units for Serpent's Cry (upgraded value).
    /// Actual max comes from Skill::GetPotencyParameter(0x36).
    /// </summary>
    public const int DefaultMaxUnits = 150;
    
    #endregion

    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> _getIsSummonModeActive;
    private readonly SkillPotencyApi? _skillPotencyApi;
    private readonly ILogger? _logger;
    private readonly string _modId;

    public SerpentsCryApi(
        Func<long> getGlobalPlayerStatePtr, 
        Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> getIsSummonModeActive,
        SkillPotencyApi? skillPotencyApi = null,
        ILogger? logger = null, 
        string modId = "")
    {
        _getGlobalPlayerStatePtr = getGlobalPlayerStatePtr;
        _getIsSummonModeActive = getIsSummonModeActive;
        _skillPotencyApi = skillPotencyApi;
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

    #region Gauge Units

    /// <summary>
    /// Get the max tidal units from the game via SkillPotencyApi.
    /// Base is 100, upgraded is 150.
    /// </summary>
    public int GetMaxUnits()
    {
        return _skillPotencyApi?.SerpentsCryMaxUnits ?? DefaultMaxUnits;
    }

    /// <summary>
    /// Get the current available Tidal units (MaxUnits - UnitsUsed).
    /// Higher value = more gauge available.
    /// </summary>
    public int GetUnits()
    {
        int maxUnits = GetMaxUnits();
        int usedUnits = GetTidalUnitsUsedInternal();
        return maxUnits - usedUnits;
    }
    
    /// <summary>
    /// Internal: Get the current Tidal units USED/CONSUMED.
    /// </summary>
    private int GetTidalUnitsUsedInternal()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0;
        return *(ushort*)(leviathanPtr + LeviathanEikonOffsets.TidalUnitsUsed);
    }

    /// <summary>
    /// Set the Tidal gauge units (available units).
    /// </summary>
    /// <param name="units">Available tidal units (0 to maxUnits)</param>
    public void SetUnits(int units)
    {
        int maxUnits = GetMaxUnits();
        if (units < 0) units = 0;
        if (units > maxUnits) units = maxUnits;
        
        // Convert to "used" units (internal representation)
        int usedUnits = maxUnits - units;
        SetTidalUnitsUsedRaw(usedUnits);
    }

    /// <summary>
    /// Add to the Tidal gauge for Serpent's Cry.
    /// Positive = GAIN gauge, Negative = LOSE gauge.
    /// </summary>
    /// <param name="amount">Amount to add (can be negative)</param>
    public void AddUnits(int amount)
    {
        int maxUnits = GetMaxUnits();
        int currentUnits = GetUnits();
        int newUnits = currentUnits + amount;
        
        if (newUnits < 0) newUnits = 0;
        if (newUnits > maxUnits) newUnits = maxUnits;
        
        SetUnits(newUnits);
    }
    
    /// <summary>
    /// Internal: Set TidalUnitsUsed directly (raw internal value).
    /// </summary>
    private void SetTidalUnitsUsedRaw(int rawValue)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        if (rawValue < 0) rawValue = 0;
        *(ushort*)(leviathanPtr + LeviathanEikonOffsets.TidalUnitsUsed) = (ushort)rawValue;
    }

    /// <summary>
    /// Fill the Tidal gauge to maximum.
    /// </summary>
    public void FillGauge()
    {
        int maxUnits = GetMaxUnits();
        SetUnits(maxUnits);
    }

    /// <summary>
    /// Empty the Tidal gauge completely.
    /// </summary>
    public void EmptyGauge()
    {
        SetUnits(0);
    }

    #endregion

    #region Unlimited Units Timer

    /// <summary>
    /// Get the unlimited units timer seconds remaining (Serpent's Cry effect).
    /// </summary>
    public float GetUnlimitedUnitsTimer()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0f;
        return *(float*)(leviathanPtr + LeviathanEikonOffsets.UnlimitedTidalSeconds);
    }

    /// <summary>
    /// Set the unlimited units timer seconds (Serpent's Cry effect).
    /// </summary>
    public void SetUnlimitedUnitsTimer(float seconds)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        if (seconds < 0f) seconds = 0f;
        *(float*)(leviathanPtr + LeviathanEikonOffsets.UnlimitedTidalSeconds) = seconds;
    }

    /// <summary>
    /// Add to the unlimited units timer seconds (Serpent's Cry effect).
    /// </summary>
    public void AddUnlimitedUnitsTimer(float seconds)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        float* pGauge = (float*)(leviathanPtr + LeviathanEikonOffsets.UnlimitedTidalSeconds);
        float newValue = *pGauge + seconds;
        if (newValue < 0f) newValue = 0f;
        *pGauge = newValue;
    }

    #endregion

    #region Start Recovery Timer

    /// <summary>
    /// Get StartRecoveryTimer (offset 0x1C10) - Seconds until next TidalUnits recovery.
    /// After an attack, this is typically set to ~2.5 seconds.
    /// When it reaches 0, TidalUnits regenerates.
    /// </summary>
    public float GetStartRecoveryTimer()
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return 0f;
        return *(float*)(leviathanPtr + LeviathanEikonOffsets.TidalTimer);
    }

    /// <summary>
    /// Set StartRecoveryTimer (offset 0x1C10) - Seconds until next TidalUnits recovery.
    /// Set to 0 for immediate recovery, or higher values to delay recovery.
    /// Game typically sets this to ~2.5 seconds after attacks.
    /// </summary>
    /// <param name="seconds">Seconds until recovery (0 = immediate)</param>
    public void SetStartRecoveryTimer(float seconds)
    {
        long leviathanPtr = GetLeviathanEikonPointer();
        if (leviathanPtr == 0) return;
        if (seconds < 0f) seconds = 0f;
        *(float*)(leviathanPtr + LeviathanEikonOffsets.TidalTimer) = seconds;
    }

    /// <summary>
    /// Reset the StartRecoveryTimer to 0 for immediate TidalUnits recovery.
    /// </summary>
    public void ResetStartRecoveryTimer()
    {
        SetStartRecoveryTimer(0f);
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
        
        int units = GetUnits();
        int maxUnits = GetMaxUnits();
        float unlimitedTimer = GetUnlimitedUnitsTimer();
        float recoveryTimer = GetStartRecoveryTimer();
        
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] Ptr=0x{leviathanPtr:X}");
        _logger.WriteLine($"[{_modId}] [Serpent's Cry] Units: {units}/{maxUnits} | UnlimitedTimer: {unlimitedTimer:F2}s | RecoveryTimer: {recoveryTimer:F2}s");
    }

    #endregion
}
