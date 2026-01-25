using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;

/// <summary>
/// Interface for Ramuh's Blind Justice stack gauge API.
/// 
/// Blind Justice System:
/// - Units = stack count (1-6 by default)
/// - Max stacks from Skill::GetPotencyParameter(skill_29)
/// - Blind Justice mode = PlayerMode 74
/// - No levels - this is a discrete counter gauge
/// </summary>
public interface IBlindJusticeApi
{
    #region Setup

    /// <summary>
    /// Set up signature scans and hooks for Blind Justice functions.
    /// </summary>
    void SetupScans(IStartupScanner scans, IReloadedHooks hooks);

    #endregion

    #region Availability

    /// <summary>
    /// Gets the pointer to the Ramuh-specific Eikon structure.
    /// Returns 0 if Ramuh mode is not active.
    /// </summary>
    long GetRamuhEikonPointer();

    /// <summary>
    /// Check if Ramuh mode is currently active.
    /// </summary>
    bool IsRamuhActive { get; }

    #endregion

    #region Max Units

    /// <summary>
    /// Gets the maximum units from the game (Skill::GetPotencyParameter).
    /// Base ability gives 3, mastered gives 6.
    /// </summary>
    int GetMaxUnits();

    #endregion

    #region Gauge Units

    /// <summary>
    /// Get the current stack count.
    /// </summary>
    int GetUnits();

    /// <summary>
    /// Set the stack count directly.
    /// Uses the current max units for capping.
    /// </summary>
    /// <param name="count">New stack count</param>
    void SetUnits(int count);

    /// <summary>
    /// Adds stacks to the count.
    /// Uses the current max units for capping.
    /// </summary>
    /// <param name="amount">Amount of stacks to add (can be negative)</param>
    void AddUnits(int amount);

    #endregion

    #region Utilities

    /// <summary>
    /// Fill stacks to maximum.
    /// </summary>
    void FillGauge();

    /// <summary>
    /// Empty all stacks.
    /// </summary>
    void EmptyGauge();

    #endregion

    #region Debug

    /// <summary>
    /// Log current Blind Justice state for debugging.
    /// </summary>
    void LogState();

    #endregion
}
