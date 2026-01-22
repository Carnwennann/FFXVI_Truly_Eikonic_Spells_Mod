using System.Runtime.InteropServices;

namespace ff16.gameplay.truly_eikonic_spells.GameStructs;

/// <summary>
/// Native game structures for magic system. 
/// These structs mirror the game's internal memory layout.
/// </summary>
/// 
// ============================================================
// POINTER VALIDATION CONSTANTS
// ============================================================

internal static class PointerValidation
{
    public const long MIN_VALID_ADDRESS = 0x10000;
    public const long MAX_VALID_ADDRESS = 0x00007FFFFFFFFFFF;
    
    public static bool IsValidPointer(long ptr) => 
        ptr >= MIN_VALID_ADDRESS && ptr <= MAX_VALID_ADDRESS && ptr % 8 == 0;
}

// ============================================================
// MAGIC FILE INSTANCE
// ============================================================

/// <summary>
/// Represents a MagicFile instance at runtime.
/// This struct is used when processing .magic files.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x208)]
internal unsafe struct MagicFileInstance
{
    [FieldOffset(0x000)] public long VTable;
    [FieldOffset(0x200)] public int MagicId;      // e.g. 214 for Dia
    [FieldOffset(0x204)] public int GroupId;      // e.g. 4338 for operation group
    
    public readonly bool IsValid => PointerValidation.IsValidPointer(VTable);
    public readonly bool HasValidMagicId => MagicIdRanges.IsValidId(MagicId);
    public readonly bool HasValidGroupId => MagicIdRanges.IsValidId(GroupId) && GroupId != MagicId;
}

// ============================================================
// MAGIC INPUT CONFIG (for Charged Shots)
// ============================================================

/// <summary>
/// Shot type enumeration for magic projectiles.
/// </summary>
internal enum MagicShotType : int
{
    Normal = 1,
    Charged = 2,
    Precision = 3,
    Burst = 4
}

// ============================================================
// MAGIC MANAGER
// ============================================================

/// <summary>
/// Offsets for MagicManager structure used in FireMagicProjectile.
/// We use offsets + helpers because the InputConfig field is a pointer stored in memory,
/// not a direct struct embed.
/// </summary>
internal static class MagicManagerOffsets
{
    public const int VTable = 0x00;
    public const int InputConfigPtr = 0x38;  // Pointer to MagicInputConfig
}

/// <summary>
/// Offsets for MagicInputConfig structure.
/// </summary>
internal static class MagicInputConfigOffsets
{
    public const int VTable = 0x00;
    public const int ShotType = 0x10;  // 1=Normal, 2=Charged, 3=Precision, 4=Burst
}

/// <summary>
/// Helper methods for reading MagicManager data from pointers.
/// NOTE: These helpers use minimal validation (just != 0) to match the game's behavior.
/// The game itself doesn't do extensive pointer validation in FireMagicProjectile.
/// </summary>
internal static unsafe class MagicManagerHelper
{
    /// <summary>
    /// Gets the InputConfig pointer from a MagicManager pointer.
    /// </summary>
    public static long GetInputConfigPtr(long magicManagerPtr)
    {
        if (magicManagerPtr == 0) return 0;
        return *(long*)(magicManagerPtr + MagicManagerOffsets.InputConfigPtr);
    }
    
    /// <summary>
    /// Gets the ShotType from an InputConfig pointer.
    /// </summary>
    public static int GetShotType(long inputConfigPtr)
    {
        if (inputConfigPtr == 0) return 0;
        return *(int*)(inputConfigPtr + MagicInputConfigOffsets.ShotType);
    }
    
    /// <summary>
    /// Gets the ShotType directly from a MagicManager pointer.
    /// Returns 0 if any pointer is null.
    /// </summary>
    public static int GetShotTypeFromManager(long magicManagerPtr)
    {
        long inputConfigPtr = GetInputConfigPtr(magicManagerPtr);
        return GetShotType(inputConfigPtr);
    }
    
    /// <summary>
    /// Checks if the MagicManager has a valid InputConfig.
    /// </summary>
    public static bool HasValidInputConfig(long magicManagerPtr)
    {
        return GetInputConfigPtr(magicManagerPtr) != 0;
    }
}

// ============================================================
// MAGIC PROPERTY DATA
// ============================================================

/// <summary>
/// Property data passed to MagicUnkExecute.
/// The actual value is at dataPtr + 8 (pointer indirection).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x10)]
internal unsafe struct MagicPropertyData
{
    [FieldOffset(0x00)] public long Unknown;
    [FieldOffset(0x08)] public void* ValuePtr;     // Pointer to actual value (float, int, or Vec3)
    
    public readonly float AsFloat => *(float*)ValuePtr;
    public readonly int AsInt => *(int*)ValuePtr;
    public readonly bool AsBool => *(int*)ValuePtr != 0;
    
    public void SetFloat(float value) => *(float*)ValuePtr = value;
    public void SetInt(int value) => *(int*)ValuePtr = value;
}

// ============================================================
// TARGET STRUCT (UnkTargetStruct in IDA)
// ============================================================

/// <summary>
/// Target position structure passed to SetupMagic.
/// Based on IDA analysis of faith::Battle::Magic::SetupMagic (UnkTargetStruct).
/// Size: 0x7C bytes
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x7C)]
public unsafe struct TargetStruct
{
    /// <summary>
    /// VTable pointer.
    /// </summary>
    [FieldOffset(0x00)] public nint VTable;
    
    [FieldOffset(0x08)] public long Field_8;
    [FieldOffset(0x10)] public long Field_10;
    [FieldOffset(0x18)] public long Field_18;
    
    /// <summary>
    /// Pointer to some global offset (p_g_off_7FF6A3500598 in IDA).
    /// </summary>
    [FieldOffset(0x20)] public nint GlobalOffset;
    
    /// <summary>
    /// Pointer to faith::Node for relative positioning.
    /// </summary>
    [FieldOffset(0x28)] public nint Node;
    
    /// <summary>
    /// Target position in world space.
    /// </summary>
    [FieldOffset(0x30)] public float X;
    [FieldOffset(0x34)] public float Y;
    [FieldOffset(0x38)] public float Z;
    
    [FieldOffset(0x3C)] public int Dword1C;
    
    /// <summary>
    /// Empty Vec3 (direction or secondary position).
    /// </summary>
    [FieldOffset(0x40)] public float DirectionX;
    [FieldOffset(0x44)] public float DirectionY;
    [FieldOffset(0x48)] public float DirectionZ;
    
    // Padding to 0x50
    [FieldOffset(0x4C)] public int Padding4C;
    
    /// <summary>
    /// Type of target. Used for targeting mode.
    /// </summary>
    [FieldOffset(0x50)] public int Type;
    
    [FieldOffset(0x54)] public int Field_54;
    [FieldOffset(0x58)] public int Field_58;
    [FieldOffset(0x5C)] public int Field_5C;
    [FieldOffset(0x60)] public int Field_60;
    [FieldOffset(0x64)] public int Field_64;
    [FieldOffset(0x68)] public int Field_68;
    
    /// <summary>
    /// Target actor ID.
    /// </summary>
    [FieldOffset(0x6C)] public int ActorId;
    
    [FieldOffset(0x70)] public float Field_70;
    [FieldOffset(0x74)] public int Field_74;
    [FieldOffset(0x78)] public int Field_78;
    
    /// <summary>
    /// Creates a TargetStruct from a world position.
    /// </summary>
    public static TargetStruct FromPosition(System.Numerics.Vector3 position)
    {
        return new TargetStruct
        {
            Type = 0,  // Position-based targeting
            Node = 0,  // World space (no parent node)
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            DirectionX = 0,
            DirectionY = 0,
            DirectionZ = 1  // Default forward direction
        };
    }
    
    /// <summary>
    /// Creates a TargetStruct from a position and direction.
    /// </summary>
    public static TargetStruct FromPositionAndDirection(System.Numerics.Vector3 position, System.Numerics.Vector3 direction)
    {
        return new TargetStruct
        {
            Type = 0,
            Node = 0,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            DirectionX = direction.X,
            DirectionY = direction.Y,
            DirectionZ = direction.Z
        };
    }
    
    /// <summary>
    /// Creates a TargetStruct from an actor ID and position.
    /// </summary>
    /// <param name="actorId">The target actor's ID.</param>
    /// <param name="position">The target position.</param>
    /// <param name="targetType">The targeting mode (0=Position, 1=Actor, 2+=Unknown/Testing).</param>
    public static TargetStruct FromActorId(int actorId, System.Numerics.Vector3 position, int targetType = 1)
    {
        return new TargetStruct
        {
            Type = targetType,
            Node = 0,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            ActorId = actorId,
            DirectionX = 0,
            DirectionY = 0,
            DirectionZ = 1
        };
    }
}

// ============================================================
// BATTLE MAGIC STRUCT
// ============================================================

/// <summary>
/// BattleMagic structure used by SetupMagic.
/// This is the 'this' parameter in faith::Battle::Magic::SetupMagic.
/// Size is approximately 0xE0 based on IDA analysis.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0xE0)]
internal unsafe struct BattleMagic
{
    /// <summary>
    /// VTable pointer - initialized to g_off_7FF6A3500598 in game code.
    /// </summary>
    [FieldOffset(0x00)] public nint VTable;
    
    /// <summary>
    /// Unknown qword at offset 0x08.
    /// </summary>
    [FieldOffset(0x08)] public long Field08;
    
    /// <summary>
    /// Position of the magic effect (Vec3).
    /// </summary>
    [FieldOffset(0x10)] public float PositionX;
    [FieldOffset(0x14)] public float PositionY;
    [FieldOffset(0x18)] public float PositionZ;
    
    [FieldOffset(0x1C)] public int Field1C;
    
    /// <summary>
    /// Nested structure at offset 0x20.
    /// </summary>
    [FieldOffset(0x20)] public long Qword20;
    [FieldOffset(0x28)] public long Qword28;
    [FieldOffset(0x30)] public long Qword30;
    [FieldOffset(0x38)] public long Qword38;
    
    /// <summary>
    /// Actor target information starts at 0x40.
    /// </summary>
    [FieldOffset(0x40)] public long ActorTarget_0;
    [FieldOffset(0x48)] public long ActorTarget_1;
    [FieldOffset(0x50)] public long ActorTarget_2;
    [FieldOffset(0x58)] public int ActorTarget_3;
    
    /// <summary>
    /// Parent node pointer.
    /// </summary>
    [FieldOffset(0x60)] public nint ParentNode;
    
    [FieldOffset(0x68)] public long Field68;
    [FieldOffset(0x70)] public long Field70;
    [FieldOffset(0x78)] public long Field78;
    [FieldOffset(0x80)] public long Field80;
    [FieldOffset(0x88)] public long Field88;
    [FieldOffset(0x90)] public long Field90;
    [FieldOffset(0x98)] public long Field98;
    [FieldOffset(0xA0)] public long FieldA0;
    [FieldOffset(0xA8)] public long FieldA8;
    [FieldOffset(0xB0)] public long FieldB0;
    [FieldOffset(0xB8)] public long FieldB8;
    
    /// <summary>
    /// Flags at offset 0xC0.
    /// </summary>
    [FieldOffset(0xC0)] public short FieldC0;  // Set to 1 in initialization
    [FieldOffset(0xC2)] public short FieldC2;  // Set to 0 in initialization
    
    [FieldOffset(0xC4)] public int FieldC4;
    
    /// <summary>
    /// MagicFileResource pointer.
    /// </summary>
    [FieldOffset(0xC8)] public nint MagicFileResource;
    
    /// <summary>
    /// Additional field at 0xD0.
    /// </summary>
    [FieldOffset(0xD0)] public long QwordD0;
    
    [FieldOffset(0xD8)] public long QwordD8;
    
    /// <summary>
    /// Initialize the struct with default values matching the game's initialization.
    /// </summary>
    public void Initialize()
    {
        VTable = 0;  // Should be set to g_off_7FF6A3500598 but we don't have access
        Field08 = 0;
        PositionX = 0;
        PositionY = 0;
        PositionZ = 0;
        Field1C = 0;
        Qword20 = 0;
        Qword28 = 0;
        Qword30 = 0;
        Qword38 = 0;
        ActorTarget_0 = 0;
        ActorTarget_1 = 0;
        ActorTarget_2 = 0;
        ActorTarget_3 = 0;
        ParentNode = 0;
        Field68 = 0;
        Field70 = 0;
        Field78 = 0;
        Field80 = 0;
        Field88 = 0;
        Field90 = 0;
        Field98 = 0;
        FieldA0 = 0;
        FieldA8 = 0;
        FieldB0 = 0;
        FieldB8 = 0;
        FieldC0 = 1;  // Default flag value
        FieldC2 = 0;
        FieldC4 = 0;
        MagicFileResource = 0;
        QwordD0 = 0;
        QwordD8 = 0;
    }
}

// ============================================================
// BATTLE BEHAVIOR ENTITY ENTRY
// ============================================================

/// <summary>
/// BattleBehaviorEntityEntry structure.
/// Contains the MagicFileResource used for spawning magic.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x4200)]
internal unsafe struct BattleBehaviorEntityEntry
{
    [FieldOffset(0x00)] public nint VTable;
    
    /// <summary>
    /// MagicFileResource (ResourceHandle) at offset known from IDA.
    /// The actual offset needs verification - in IDA it's accessed via btlBehaviorEntityEntry->MagicFileResource.
    /// </summary>
    [FieldOffset(0x10)] public nint MagicFileResource;
    
    /// <summary>
    /// Position struct maybe at offset 0x4160 (btlBehaviorEntityEntry->qword4160 in IDA).
    /// </summary>
    [FieldOffset(0x4160)] public nint PositionStructMaybe;
}

// ============================================================
// ID RANGE VALIDATION
// ============================================================

/// <summary>
/// Constants for validating magic/group IDs.
/// </summary>
internal static class MagicIdRanges
{
    public const int MIN_VALID_ID = 1;
    public const int MAX_VALID_ID = 30000;
    public const int MAX_EXTENDED_ID = 1000000;  // For data pointer pattern
    
    public static bool IsValidId(int id) => id > MIN_VALID_ID && id < MAX_VALID_ID;
    public static bool IsValidExtendedId(int id) => id > MIN_VALID_ID && id < MAX_EXTENDED_ID;
}
