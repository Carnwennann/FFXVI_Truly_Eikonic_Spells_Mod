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

    // ==================== MAGIC EXPERIMENTS ====================

    [DisplayName("a5 param from SetupMagic")]
    [Description("a5 (-999 = original)")]
    [DefaultValue(-999)]
    [Category("Magic Experiments")]
    public int a5_experiment { get; set; } = -999;

    [DisplayName("a6 param from SetupMagic")]
    [Description("a6 (-999 = original)")]
    [DefaultValue(-999)]
    [Category("Magic Experiments")]
    public int a6_experiment { get; set; } = -999;

    [DisplayName("Op35 Duration Override")]
    [Description("Override for Operation 35 Property 35 (Duration/Speed). -999 = original.")]
    [DefaultValue(-999.0f)]
    [Category("Magic Experiments")]
    public float Op35SpeedOverride { get; set; } = -999.0f;

    [DisplayName("Op35 MoveType Override")]
    [Description("Override for Operation 35 Property 37 (MoveType). -999 = original.")]
    [DefaultValue(-999)]
    [Category("Magic Experiments")]
    public int Op35MoveTypeOverride { get; set; } = -999;

    [DisplayName("Op35 Homing Override")]
    [Description("Override for Operation 35 Property 38 (Homing). Default = original.")]
    [DefaultValue(TriState.Default)]
    [Category("Magic Experiments")]
    public TriState Op35HomingOverride { get; set; } = TriState.Default;

    // ==================== UNIVERSAL PROPERTY FUZZER ====================
    
    [DisplayName("Enable Universal Fuzzer")]
    [Description("Enable the universal property override system")]
    [DefaultValue(false)]
    [Category("Magic - Universal Fuzzer")]
    public bool EnableUniversalFuzzer { get; set; } = false;

    [DisplayName("Fuzzer Entries")]
    [Description("List of property overrides to apply")]
    [Category("Magic - Universal Fuzzer")]
    public List<FuzzerEntry> FuzzerEntries { get; set; } = new();

    // ==================== MAGIC STRUCT OVERRIDES ====================
    // These modify the BattleMagic struct fields before CastMagic executes
    // Use -999 to keep the original value
    
    [DisplayName("Enable Magic Struct Overrides")]
    [Description("Whether to apply the struct field overrides below")]
    [DefaultValue(false)]
    [Category("Magic Struct Overrides")]
    public bool EnableMagicStructOverrides { get; set; } = false;
    
    [DisplayName("+0x18 Timing Override")]
    [Description("Timing/delay float (-999 = original, Dia~4-6, Diara~3.9, Impulse~7.1)")]
    [DefaultValue(-999.0f)]
    [Category("Magic Struct Overrides")]
    public float MagicTimingOverride { get; set; } = -999.0f;
    
    [DisplayName("+0x90 Scale Override")]
    [Description("Scale float (-999 = original, Dia=0, Diara=-19.82, Impulse=1.0)")]
    [DefaultValue(-999.0f)]
    [Category("Magic Struct Overrides")]
    public float MagicScaleOverride { get; set; } = -999.0f;
    
    [DisplayName("+0xD8 Aim Angle Override (degrees)")]
    [Description("Horizontal aim angle in degrees (-999 = original). Auto-calculates Y/W to maintain unit quaternion. 0=forward, 90=right, -90=left, 180=backward")]
    [DefaultValue(-999.0f)]
    [Category("Magic Struct Overrides")]
    public float MagicAimAngleOverride { get; set; } = -999.0f;
    
    // ==================== POSITION STRUCT OVERRIDES ====================
    // These modify the PositionStruct passed to SetupMagic
    // Offsets +0x30, +0x34, +0x38 appear to be position (X, Y, Z)
    
    [DisplayName("Enable Position Overrides")]
    [Description("Whether to apply position overrides to the PositionStruct")]
    [DefaultValue(false)]
    [Category("Position Struct Overrides")]
    public bool EnablePositionOverrides { get; set; } = false;
    
    [DisplayName("+0x30 Position X Offset")]
    [Description("Add this value to X position (-999 = don't modify)")]
    [DefaultValue(-999.0f)]
    [Category("Position Struct Overrides")]
    public float PositionXOffset { get; set; } = -999.0f;
    
    [DisplayName("+0x34 Position Y Offset")]
    [Description("Add this value to Y position (height) (-999 = don't modify)")]
    [DefaultValue(-999.0f)]
    [Category("Position Struct Overrides")]
    public float PositionYOffset { get; set; } = -999.0f;
    
    [DisplayName("+0x38 Position Z Offset")]
    [Description("Add this value to Z position (-999 = don't modify)")]
    [DefaultValue(-999.0f)]
    [Category("Position Struct Overrides")]
    public float PositionZOffset { get; set; } = -999.0f;


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
