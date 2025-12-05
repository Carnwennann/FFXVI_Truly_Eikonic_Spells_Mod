/*
 * This file and other files in the `Template` folder are intended to be left unedited (if possible),
 * to make it easier to upgrade to newer versions of the template.
*/

using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using ff16.gameplay.truly_eikonic_spells.Template.Configuration;
using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells;

public class Startup : IMod
{
    /// <summary>
    /// Used for writing text to the Reloaded log.
    /// </summary>
    private ILogger _logger = null!;

    /// <summary>
    /// Provides access to the mod loader API.
    /// </summary>
    private IModLoader _modLoader = null!;

    /// <summary>
    /// Stores the contents of your mod's configuration. Automatically updated by template.
    /// </summary>
    private Config _configuration = null!;

    /// <summary>
    /// An interface to Reloaded's the function hooks/detours library.
    /// </summary>
    private IReloadedHooks? _hooks;

    /// <summary>
    /// Configuration of the current mod.
    /// </summary>
    private IModConfig _modConfig = null!;

    /// <summary>
    /// Encapsulates your mod logic.
    /// </summary>
    private ModBase _mod = new TrulyEikonicSpellsMod();

    /// <summary>
    /// Entry point for your mod.
    /// </summary>
    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
    {
        _modLoader = (IModLoader)loaderApi;
        _modConfig = (IModConfig)modConfig;
        _logger = (ILogger)_modLoader.GetLogger();
        _modLoader.GetController<IReloadedHooks>()?.TryGetTarget(out _hooks!);

        // Your config file is in Config.json.
        var configurator = new Configurator(_modLoader.GetModConfigDirectory(_modConfig.ModId));
        _configuration = configurator.GetConfiguration<Config>(0);
        _configuration.ConfigurationUpdated += OnConfigurationUpdated;

        // Please put your mod code in the class below,
        // use this class for only interfacing with mod loader.
        _mod = new TrulyEikonicSpellsMod(new ModContext()
        {
            Logger = _logger,
            Hooks = _hooks,
            ModLoader = _modLoader,
            ModConfig = _modConfig,
            Owner = this,
            Configuration = _configuration,
        });
    }

    private void OnConfigurationUpdated(IConfigurable obj)
    {
        _configuration = (Config)obj;
        _mod.ConfigurationUpdated(_configuration);
    }

    /* Mod loader actions. */
    public void Suspend() => _mod.Suspend();
    public void Resume() => _mod.Resume();
    public void Unload() => _mod.Unload();

    public bool CanUnload() => _mod.CanUnload();
    public bool CanSuspend() => _mod.CanSuspend();

    /* Automatically called by the mod loader when the mod is about to be unloaded. */
    public Action Disposing => () => _mod.Disposing();
}
