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
