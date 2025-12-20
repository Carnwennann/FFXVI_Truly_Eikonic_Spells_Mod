using System.ComponentModel;
using ff16.gameplay.truly_eikonic_spells.Template.Configuration;

namespace ff16.gameplay.truly_eikonic_spells.Configuration;

/// <summary>
/// Three-state enum for flag configuration: Default (don't modify), On (force enable), Off (force disable)
/// </summary>
public enum TriState
{
    Default = 0,
    On = 1,
    Off = 2
}

public class Config : Configurable<Config>
{
    // ==================== DIA SYSTEM ====================
    
    [DisplayName("Enable Dia System")]
    [Description("Whether to enable the Dia stacking mechanic (Bahamut)")]
    [DefaultValue(true)]
    [Category("Dia System")]
    public bool EnableDiaSystem { get; set; } = true;

    [DisplayName("Max Dia Stacks")]
    [Description("Maximum number of Dia stacks per enemy")]
    [DefaultValue(50)]
    [Category("Dia System")]
    public int MaxDiaStacks { get; set; } = 50;

    [DisplayName("Damage Per Stack")]
    [Description("Damage bonus per stack (0.01 = 1% per stack)")]
    [DefaultValue(0.01f)]
    [Category("Dia System")]
    public float DiaDamagePerStack { get; set; } = 0.01f;

    // ==================== DIARA SYSTEM ====================
    
    [DisplayName("Enable Diara System")]
    [Description("Whether to enable the Diara buff mechanic (Bahamut Charged Shot)")]
    [DefaultValue(true)]
    [Category("Diara System")]
    public bool EnableDiaraSystem { get; set; } = true;

    [DisplayName("Diara Buff Duration (seconds)")]
    [Description("How long the Diara buff lasts after casting Charged Shot")]
    [DefaultValue(120.0f)]
    [Category("Diara System")]
    public float DiaraBuffDuration { get; set; } = 120.0f;

    [DisplayName("Dia Spells Per Dodge")]
    [Description("Number of Dia spells fired on each perfect dodge during Diara buff")]
    [DefaultValue(5)]
    [Category("Diara System")]
    public int DiaSpellsPerDodge { get; set; } = 5;

    [DisplayName("Dia Magic ID")]
    [Description("Magic ID used for Dia spells fired during Diara buff")]
    [DefaultValue(1)]
    [Category("Diara System")]
    public int DiaMagicID { get; set; } = 1;

    // ==================== DARKRA SYSTEM ====================
    
    [DisplayName("Enable Darkra System")]
    [Description("Whether to enable the Darkra shadow debuff mechanic (Odin)")]
    [DefaultValue(true)]
    [Category("Darkra System")]
    public bool EnableDarkraSystem { get; set; } = true;

    [DisplayName("Shadow Hit Multiplier")]
    [Description("Shadow hit damage as percentage of original damage (0.1 = 10%)")]
    [DefaultValue(0.1f)]
    [Category("Darkra System")]
    public float ShadowHitMultiplier { get; set; } = 0.1f;

    [DisplayName("Shadow Debuff Duration (seconds)")]
    [Description("How long the Shadow debuff lasts on enemies")]
    [DefaultValue(120.0f)]
    [Category("Darkra System")]
    public float ShadowDebuffDuration { get; set; } = 120.0f;

    [DisplayName("Shadow Hit Delay (ms)")]
    [Description("Delay in milliseconds before the shadow hit triggers")]
    [DefaultValue(1000)]
    [Category("Darkra System")]
    public int ShadowHitDelayMs { get; set; } = 1000;

    [DisplayName("Shadow Hit Reaction Animation")]
    [Description(ReactionTypes.CONFIG_DESC_ANIMATION)]
    [DefaultValue(2)]
    [Category("Darkra System")]
    public int ShadowHitReactionType { get; set; } = 2;

    [DisplayName("Shadow Hit Push Direction")]
    [Description(ReactionTypes.CONFIG_DESC_PUSH)]
    [DefaultValue(2)]
    [Category("Darkra System")]
    public int ShadowHitReactionIntensity { get; set; } = 2;

    // === Shadow Hit Juggle Physics ===
    
    [DisplayName("Enable Shadow Hit Juggle")]
    [Description("Whether to apply custom physics to shadow hits for air juggling")]
    [DefaultValue(true)]
    [Category("Darkra System - Juggle")]
    public bool ShadowHitJuggleEnabled { get; set; } = true;

    [DisplayName("Shadow Hit Anim ID (0x15c)")]
    [Description("Animation ID for the shadow hit reaction (6 = launch up)")]
    [DefaultValue(6)]
    [Category("Darkra System - Juggle")]
    public int ShadowHitJuggleAnimId { get; set; } = 6;

    [DisplayName("Shadow Hit Vertical Push")]
    [Description("Vertical knockback force (positive = up, 1.0 = standard juggle)")]
    [DefaultValue(1.0f)]
    [Category("Darkra System - Juggle")]
    public float ShadowHitJuggleVerticalPush { get; set; } = 1.0f;

    [DisplayName("Shadow Hit Forward Push")]
    [Description("Horizontal knockback force (negative = pull toward, -0.1 = slight pull)")]
    [DefaultValue(-0.1f)]
    [Category("Darkra System - Juggle")]
    public float ShadowHitJuggleForwardPush { get; set; } = -0.1f;

    [DisplayName("Shadow Hit Forward Duration")]
    [Description("Duration of forward movement (0-1, 0.5 = medium travel)")]
    [DefaultValue(0.5f)]
    [Category("Darkra System - Juggle")]
    public float ShadowHitJuggleForwardDuration { get; set; } = 0.5f;

    [DisplayName("Shadow Hit Vertical Interpolation")]
    [Description("Speed of vertical movement (0-1, 0.3 = medium speed)")]
    [DefaultValue(0.3f)]
    [Category("Darkra System - Juggle")]
    public float ShadowHitJuggleVerticalInterpolation { get; set; } = 0.3f;

    // ==================== PHYSICS EXPERIMENTS ====================
    
    [DisplayName("Enable Physics Modification")]
    [Description("Whether to modify knockback physics parameters")]
    [DefaultValue(false)]
    [Category("Physics Experiments")]
    public bool EnablePhysicsModification { get; set; } = false;
    
    [DisplayName("Dump All SystemMove Columns")]
    [Description("Log ALL columns from SystemMove NEX table with names (for reverse engineering)")]
    [DefaultValue(false)]
    [Category("Physics Experiments")]
    public bool PhysicsDumpAllColumns { get; set; } = false;
    
    // === SystemMove Column Overrides (4 CONFIRMED COLUMNS) ===
    // All values: -999 = don't modify (use original)
    // Positive values push away/up, negative values pull/push down
    
    [DisplayName("+0x04 ForwardPush")]
    [Description("Horizontal knockback force (-999 = original, positive = push away, negative = pull toward)")]
    [DefaultValue(-999.0f)]
    [Category("Physics - SystemMove Columns")]
    public float PhysicsForwardPushOverride { get; set; } = -999.0f;
    
    [DisplayName("+0x08 ForwardDuration")]
    [Description("Duration of forward movement (0-1, 0 = stays in place, 1 = travels full distance)")]
    [DefaultValue(-999.0f)]
    [Category("Physics - SystemMove Columns")]
    public float PhysicsForwardDurationOverride { get; set; } = -999.0f;
    
    [DisplayName("+0x0C VerticalPush")]
    [Description("Vertical knockback force (-999 = original, positive = up, negative = down)")]
    [DefaultValue(-999.0f)]
    [Category("Physics - SystemMove Columns")]
    public float PhysicsVerticalPushOverride { get; set; } = -999.0f;
    
    [DisplayName("+0x10 VerticalInterpolation")]
    [Description("Speed of vertical movement (0-1, 0 = instant, 1 = very slow)")]
    [DefaultValue(-999.0f)]
    [Category("Physics - SystemMove Columns")]
    public float PhysicsVerticalInterpolationOverride { get; set; } = -999.0f;
    [DisplayName("Force Push Direction")]
    [Description("Force all attacks to use this PushDirection (-1 = disabled, 21 = Launch up strong, see ReactionTypes.cs)")]
    [DefaultValue(-1)]
    [Category("Physics Experiments")]
    public int PhysicsForcePushDirection { get; set; } = -1;

    [DisplayName("Flag 0x2800 (Reaction Enable)")]
    [Description("Controls knockback physics - Default=unchanged, On=force enable (works on bosses), Off=force disable")]
    [DefaultValue(TriState.Default)]
    [Category("Physics Flags")]
    public TriState PhysicsFlag0x2800 { get; set; } = TriState.Default;

    [DisplayName("Flag 0x10 (bit4)")]
    [Description("Unknown effect - Default=unchanged, On=force enable, Off=force disable")]
    [DefaultValue(TriState.Default)]
    [Category("Physics Flags")]
    public TriState PhysicsFlagBit4 { get; set; } = TriState.Default;

    [DisplayName("Flag 0x1000 (bit12) - Stagger Reaction")]
    [Description("Will Break/Stagger reaction animation with camera zoom - Default=unchanged, On=force enable, Off=force disable")]
    [DefaultValue(TriState.Default)]
    [Category("Physics Flags")]
    public TriState PhysicsFlagBit12 { get; set; } = TriState.Default;


    // ==================== DEBUG ====================
    
    [DisplayName("Debug Logging")]
    [Description("Enable verbose debug logging")]
    [DefaultValue(true)]
    [Category("Debug")]
    public bool DebugLogging { get; set; } = true;
}
