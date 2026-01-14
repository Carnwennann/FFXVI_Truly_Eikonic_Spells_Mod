using System.ComponentModel;
using ff16.gameplay.truly_eikonic_spells.Template.Configuration;
using ff16.gameplay.truly_eikonic_spells.Utils;

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

public class FuzzerEntry
{
    public bool Enabled { get; set; } = true;
    public bool IsInjection { get; set; } = false; // Si es true, se inyecta aunque no exista
    public int TargetMagicId { get; set; } = -1;   // -1 para todos, o el ID del log (ej: -1610379072)
    public int InjectAfterOp { get; set; } = -1;   // Inyectar después de esta Op (-1 = al final)
    public bool DisableOp { get; set; } = false;   // Si es true, bloquea esta operación original
    public int OpType { get; set; } = 51;
    public int PropertyId { get; set; } = 8; // -1 para eliminar todas las propiedades de la Op
    public bool UseFloat { get; set; } = true;
    public float FloatValue { get; set; } = 0.0f;
    public int IntValue { get; set; } = 0;

    // Nuevos campos
    public bool UseVec3 { get; set; } = false;
    public float Vec3X { get; set; } = 0.0f;
    public float Vec3Y { get; set; } = 0.0f;
    public float Vec3Z { get; set; } = 0.0f;

    public int Occurrence { get; set; } = -1; // -1 para todas, 0 para la primera, 1 para la segunda...
    public int TargetOperationGroupId { get; set; } = -1; // -1 para todos, o el ID del grupo (ej: 4338)

    public FuzzerEntry Clone()
    {
        return new FuzzerEntry
        {
            Enabled = this.Enabled,
            IsInjection = this.IsInjection,
            TargetMagicId = this.TargetMagicId,
            InjectAfterOp = this.InjectAfterOp,
            DisableOp = this.DisableOp,
            OpType = this.OpType,
            PropertyId = this.PropertyId,
            UseFloat = this.UseFloat,
            FloatValue = this.FloatValue,
            IntValue = this.IntValue,
            UseVec3 = this.UseVec3,
            Vec3X = this.Vec3X,
            Vec3Y = this.Vec3Y,
            Vec3Z = this.Vec3Z,
            Occurrence = this.Occurrence,
            TargetOperationGroupId = this.TargetOperationGroupId
        };
    }
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
    [DefaultValue(214)]
    [Category("Diara System")]
    public int DiaMagicID { get; set; } = 214;

    [DisplayName("Dia Fan Angle Step")]
    [Description("Degrees of separation between each Dia spell in the fan")]
    [DefaultValue(15.0f)]
    [Category("Diara System")]
    public float DiaFanAngleStep { get; set; } = 15.0f;

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

    [DisplayName("Shadow Land Hit Reaction Animation")]
    [Description(ReactionTypes.CONFIG_DESC_ANIMATION)]
    [DefaultValue(2)]
    [Category("Darkra System")]
    public int ShadowHitReactionType { get; set; } = 2;

    [DisplayName("Shadow Land Hit Push Direction")]
    [Description(ReactionTypes.CONFIG_DESC_PUSH)]
    [DefaultValue(2)]
    [Category("Darkra System")]
    public int ShadowHitReactionIntensity { get; set; } = 2;

    // === Shadow Hit Juggle Physics ===
    
    [DisplayName("Enable Shadow Hit Juggle")]
    [Description("Whether to apply custom physics to shadow hits for air juggling")]
    [DefaultValue(true)]
    [Category("Darkra System")]
    public bool ShadowHitJuggleEnabled { get; set; } = true;

    [DisplayName("Shadow Air Hit Anim ID (0x15c)")]
    [Description("Animation ID for the shadow hit reaction (6 = launch up)")]
    [DefaultValue(6)]
    [Category("Darkra System")]
    public int ShadowHitJuggleAnimId { get; set; } = 6;

    [DisplayName("Shadow Air Hit Vertical Push")]
    [Description("Vertical knockback force (positive = up, 1.0 = standard juggle)")]
    [DefaultValue(0.4f)]
    [Category("Darkra System")]
    public float ShadowHitJuggleVerticalPush { get; set; } = 0.4f;

    [DisplayName("Shadow Air Hit Forward Push")]
    [Description("Horizontal knockback force (negative = pull toward, -0.1 = slight pull)")]
    [DefaultValue(-0.1f)]
    [Category("Darkra System")]
    public float ShadowHitJuggleForwardPush { get; set; } = -0.1f;

    [DisplayName("Shadow Air Hit Forward Duration")]
    [Description("Duration of forward movement (0-1, 0.5 = medium travel)")]
    [DefaultValue(0.5f)]
    [Category("Darkra System")]
    public float ShadowHitJuggleForwardDuration { get; set; } = 0.5f;

    [DisplayName("Shadow Air Hit Vertical Interpolation")]
    [Description("Speed of vertical movement (0-1, 0.3 = medium speed)")]
    [DefaultValue(0.2f)]
    [Category("Darkra System")]
    public float ShadowHitJuggleVerticalInterpolation { get; set; } = 0.2f;

    // === Zantetsuken Ticks ===
    
    [DisplayName("Enable Zantetsuken Ticks")]
    [Description("Whether shadow hits generate Zantetsuken gauge")]
    [DefaultValue(true)]
    [Category("Darkra System")]
    public bool ShadowHitZantetsukenTicksEnabled { get; set; } = true;

    [DisplayName("Zantetsuken Tick Amount")]
    [Description("Amount of Zantetsuken gauge per shadow hit (1500 = 1 full level)")]
    [DefaultValue(35)]
    [Category("Darkra System")]
    public int ShadowHitZantetsukenTickAmount { get; set; } = 20;

    // ==================== PHYSICS EXPERIMENTS ====================

    [DisplayName("Enable Physics Modification")]
    [Description("Whether to enable manual physics/knockback overrides")]
    [DefaultValue(false)]
    [Category("Physics Experiments")]
    public bool EnablePhysicsModification { get; set; } = false;

    [DisplayName("Force PushDirection")]
    [Description("Force a specific PushDirection ID (-1 to disable)")]
    [DefaultValue(-1)]
    [Category("Physics Experiments")]
    public int PhysicsForcePushDirection { get; set; } = -1;

    [DisplayName("Forward Push Override")]
    [Description("Override ForwardPush value (-1.0 to 1.0)")]
    [DefaultValue(0.0f)]
    [Category("Physics Experiments")]
    public float PhysicsForwardPushOverride { get; set; } = 0.0f;

    [DisplayName("Forward Duration Override")]
    [Description("Override ForwardDuration value (0.0 to 1.0)")]
    [DefaultValue(0.0f)]
    [Category("Physics Experiments")]
    public float PhysicsForwardDurationOverride { get; set; } = 0.0f;

    [DisplayName("Vertical Push Override")]
    [Description("Override VerticalPush value (-1.0 to 1.0)")]
    [DefaultValue(0.0f)]
    [Category("Physics Experiments")]
    public float PhysicsVerticalPushOverride { get; set; } = 0.0f;

    [DisplayName("Vertical Interpolation Override")]
    [Description("Override VerticalInterpolation value (0.0 to 1.0)")]
    [DefaultValue(0.0f)]
    [Category("Physics Experiments")]
    public float PhysicsVerticalInterpolationOverride { get; set; } = 0.0f;

    [DisplayName("Flag 0x2800")]
    [Description("Force flag 0x2800 in reaction data")]
    [DefaultValue(TriState.Default)]
    [Category("Physics Experiments")]
    public TriState PhysicsFlag0x2800 { get; set; } = TriState.Default;

    [DisplayName("Flag Bit 4 (0x10)")]
    [Description("Force bit 4 (0x10) in reaction data")]
    [DefaultValue(TriState.Default)]
    [Category("Physics Experiments")]
    public TriState PhysicsFlagBit4 { get; set; } = TriState.Default;

    [DisplayName("Flag Bit 12 (0x1000)")]
    [Description("Force bit 12 (0x1000) in reaction data")]
    [DefaultValue(TriState.Default)]
    [Category("Physics Experiments")]
    public TriState PhysicsFlagBit12 { get; set; } = TriState.Default;

    [DisplayName("Dump All Columns")]
    [Description("Dump all columns of the SystemMove row on hit")]
    [DefaultValue(false)]
    [Category("Physics Experiments")]
    public bool PhysicsDumpAllColumns { get; set; } = false;

    // ==================== POSITION OVERRIDES ====================

    [DisplayName("Enable Position Overrides")]
    [Description("Whether to override the spawn position of magic spells")]
    [DefaultValue(false)]
    [Category("Position Overrides")]
    public bool EnablePositionOverrides { get; set; } = false;

    [DisplayName("X Offset")]
    [Description("Horizontal offset (left/right)")]
    [DefaultValue(0.0f)]
    [Category("Position Overrides")]
    public float PositionXOffset { get; set; } = 0.0f;

    [DisplayName("Y Offset")]
    [Description("Vertical offset (up/down)")]
    [DefaultValue(0.0f)]
    [Category("Position Overrides")]
    public float PositionYOffset { get; set; } = 0.0f;

    [DisplayName("Z Offset")]
    [Description("Forward offset (front/back)")]
    [DefaultValue(0.0f)]
    [Category("Position Overrides")]
    public float PositionZOffset { get; set; } = 0.0f;

    // ==================== UNIVERSAL FUZZER ====================

    [DisplayName("Enable Universal Fuzzer")]
    [Description("Whether to enable the universal magic property fuzzer/injector")]
    [DefaultValue(false)]
    [Category("Universal Fuzzer")]
    public bool EnableUniversalFuzzer { get; set; } = false;

    [DisplayName("Fuzzer Entries")]
    [Description("List of properties to fuzz or inject")]
    [Category("Universal Fuzzer")]
    public List<FuzzerEntry> FuzzerEntries { get; set; } = new List<FuzzerEntry>();

    // ==================== DEBUG ====================
    
    [DisplayName("Debug Logging")]
    [Description("Enable verbose debug logging")]
    [DefaultValue(true)]
    [Category("Debug")]
    public bool DebugLogging { get; set; } = true;

    // === Magic Tester ===
    public int TestMagicID { get; set; } = 214;
    public int TestMagicCount { get; set; } = 1;

    // ==================== HIT VFX OVERRIDES ====================

    [DisplayName("Enable Hit VFX Override")]
    [Description("Whether to force a specific VFX ID on every hit")]
    [DefaultValue(false)]
    [Category("Hit VFX")]
    public bool EnableHitVfxOverride { get; set; } = false;

    [DisplayName("Forced Hit VFX ID")]
    [Description("The VFX ID to use (reads from R15 + 392)")]
    [DefaultValue(0)]
    [Category("Hit VFX")]
    public int ForcedHitVfxId { get; set; } = 0;
}
