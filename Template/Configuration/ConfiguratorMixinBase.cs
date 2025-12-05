using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells.Template.Configuration;

/// <summary>
/// Creates the various different configurations used by the mod.
/// These configurations are available in the dropdown in Reloaded launcher. 
/// </summary>
public class ConfiguratorMixinBase
{
    /// <summary>
    /// Defines the configuration items to create.
    /// </summary>
    /// <param name="configFolder">Folder storing the configuration.</param>
    public virtual IUpdatableConfigurable[] MakeConfigurations(string configFolder)
    {
        return new IUpdatableConfigurable[]
        {
            Configurable<Config>.FromFile(Path.Combine(configFolder, "Config.json"), "Default Config")
        };
    }

    /// <summary>
    /// Allows for custom launcher/configurator implementation.
    /// </summary>
    public virtual bool TryRunCustomConfiguration(Configurator configurator)
    {
        return false;
    }

    /// <summary>
    /// Migrates from the old config location to the newer config location.
    /// </summary>
    public virtual void Migrate(string oldDirectory, string newDirectory)
    {
    }
}
