using ff16.gameplay.truly_eikonic_spells.Utils;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// API for managing Odin's Zantetsuken gauge and related state.
/// </summary>
public unsafe class ZantetsukenApi
{
    private readonly Func<long> _getGlobalPlayerStatePtr;
    private readonly Func<TrulyEikonicSpellsMod.IsSummonModeActiveDelegate?> _getIsSummonModeActive;
    private readonly ILogger? _logger;
    private readonly string _modId;

    public ZantetsukenApi(
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
    /// Gets the pointer to the Odin-specific Eikon structure.
    /// returns 0 if Odin mode is not active.
    /// </summary>
    public long GetOdinEikonPointer()
    {
        var isSummonModeActive = _getIsSummonModeActive();
        var globalPlayerStatePtr = _getGlobalPlayerStatePtr();

        if (isSummonModeActive == null || globalPlayerStatePtr == 0) return 0;
        
        long playerState = *(long*)globalPlayerStatePtr;
        if (playerState == 0) return 0;
        
        // Odin ID is 7
        return isSummonModeActive(playerState + 0x4798, EikonUtils.EIKON_ODIN);
    }

    /// <summary>
    /// Adds units to the Zantetsuken gauge.
    /// 1500 units = 1 Bar Level. Max 7500 (Level 5).
    /// </summary>
    /// <param name="amount">Amount of gauge units to add (can be negative)</param>
    public void AddUnits(int amount)
    {
        long odinPtr = GetOdinEikonPointer();
        if (odinPtr == 0) return;

        // Gauge is at offset 0x1C08 (7176) as __int16
        short* pGauge = (short*)(odinPtr + 7176);
        short currentUnits = *pGauge;
        short newUnits = (short)(currentUnits + (short)amount);
        
        // Cap at Level 5 (7500 units)
        if (newUnits > 7500) newUnits = 7500;
        if (newUnits < 0) newUnits = 0;

        *pGauge = newUnits;
    }

    /// <summary>
    /// Get the current Zantetsuken gauge units (0-7500).
    /// </summary>
    public short GetUnits()
    {
        long odinPtr = GetOdinEikonPointer();
        if (odinPtr == 0) return 0;
        return *(short*)(odinPtr + 7176);
    }
}
