using System.ComponentModel;
using ff16.gameplay.truly_eikonic_spells.Template.Configuration;

namespace ff16.gameplay.truly_eikonic_spells.Configuration;

public class Config : Configurable<Config>
{
    [DisplayName("Enable Dia System")]
    [Description("Whether to enable the Dia stacking mechanic")]
    [DefaultValue(true)]
    public bool EnableDiaSystem { get; set; } = true;

    [DisplayName("Max Dia Stacks")]
    [Description("Maximum number of Dia stacks per enemy")]
    [DefaultValue(500)]
    public int MaxDiaStacks { get; set; } = 500;

    [DisplayName("Damage Per Stack")]
    [Description("Damage bonus per stack (0.001 = 0.1%)")]
    [DefaultValue(0.001f)]
    public float DamagePerStack { get; set; } = 0.001f;

    [DisplayName("Debug Logging")]
    [Description("Enable verbose debug logging")]
    [DefaultValue(true)]
    public bool DebugLogging { get; set; } = true;
}
