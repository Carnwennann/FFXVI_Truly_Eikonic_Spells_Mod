using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Base class for implementing mod functionality.
/// </summary>
public class ModBase
{
    /// <summary>
    /// Returns true if the suspend functionality is supported, else false.
    /// </summary>
    public virtual bool CanSuspend() => false;

    /// <summary>
    /// Returns true if the unload functionality is supported, else false.
    /// </summary>
    public virtual bool CanUnload() => false;

    /// <summary>
    /// Suspends your mod, i.e. mod stops performing its functionality but is not unloaded.
    /// </summary>
    public virtual void Suspend()
    {
    }

    /// <summary>
    /// Unloads your mod, i.e. mod stops performing its functionality but is not unloaded.
    /// </summary>
    public virtual void Unload()
    {
    }

    /// <summary>
    /// Automatically called by the mod loader when the mod is about to be unloaded.
    /// </summary>
    public virtual void Disposing()
    {
    }

    /// <summary>
    /// Automatically called by the mod loader when the mod is about to be unloaded.
    /// </summary>
    public virtual void Resume()
    {
    }

    public virtual void ConfigurationUpdated(Config configuration)
    {
    }
}
