using System.Numerics;
using System.Runtime.InteropServices;

namespace ff16.gameplay.truly_eikonic_spells.GameStructs;

// ============================================================
// GLOBAL OFFSETS (from base address)
// ============================================================

/// <summary>
/// Global memory offsets from the game's base address.
/// These point to singleton instances or global state.
/// </summary>
internal static class GlobalOffsets
{
    /// <summary>
    /// Singleton that contains player/camera related state.
    /// The current controlling ActorId is at +0xC8.
    /// </summary>
    public const int UnkSingletonPlayerOrCamera = 0x1816608;
    
    /// <summary>
    /// The BattleMagicExecutor singleton used for casting magic spells.
    /// </summary>
    public const int BattleMagicExecutor = 0x18168E8;
}

// ============================================================
// PLAYER/CAMERA SINGLETON
// ============================================================

/// <summary>
/// Offsets within the UnkSingletonPlayerOrCamera structure.
/// </summary>
internal static class UnkSingletonOffsets
{
    /// <summary>
    /// The ActorId of the currently controlled actor (usually Clive).
    /// Type: uint
    /// </summary>
    public const int CurrentActorId = 0xC8;
}

// ============================================================
// PLAYER STATE
// ============================================================

/// <summary>
/// Offsets within the PlayerState structure.
/// </summary>
internal static class PlayerStateOffsets
{
    /// <summary>
    /// Offset to the EikonSummonData array/structure.
    /// Used for checking if a specific Eikon mode is active.
    /// </summary>
    public const int EikonSummonData = 0x4798;
}

// ============================================================
// ODIN EIKON STRUCTURE
// ============================================================

/// <summary>
/// Offsets within the Odin Eikon structure.
/// </summary>
internal static class OdinEikonOffsets
{
    /// <summary>
    /// Zantetsuken gauge value (0-7500, where 1500 = 1 bar level).
    /// Type: short (__int16)
    /// </summary>
    public const int ZantetsukenGauge = 0x1C08; // 7176 decimal
}

// ============================================================
// BAHAMUT EIKON STRUCTURE
// ============================================================

/// <summary>
/// Offsets within the Bahamut Eikon structure.
/// Used for Megaflare gauge management.
/// 
/// Megaflare system:
/// - 4000 units = 1 level
/// - Max level depends on skill potency (typically 1-4)
/// 
/// Note: Wings activation is NOT stored here.
/// It's checked via ActorData35Entry::GetCurrentPlayerMode() == 75
/// </summary>
internal static class BahamutEikonOffsets
{
    /// <summary>
    /// Megaflare gauge total units.
    /// Level = units / 4000, UnitsInLevel = units % 4000
    /// Type: float (dword, converted to int via vcvttss2si)
    /// </summary>
    public const int MegaflareGauge = 0x1C38; // 7224 decimal
    
    /// <summary>
    /// Unknown state byte (checked == 2 for some UI trigger).
    /// Type: byte
    /// </summary>
    public const int UnkState1C4A = 0x1C4A; // 7242 decimal
    
    /// <summary>
    /// Unknown state byte (checked for some condition).
    /// Type: byte
    /// </summary>
    public const int UnkState1C4B = 0x1C4B; // 7243 decimal
}

// ============================================================
// BNPC ROW (NPC Entity)
// ============================================================

/// <summary>
/// Offsets within the BnpcRow (NPC base entity) structure.
/// </summary>
internal static class BnpcRowOffsets
{
    /// <summary>
    /// Pointer to the StaticActorInfo wrapper.
    /// Type: StaticActorInfo*
    /// </summary>
    public const int StaticActorInfoPtr = 0x20;
}

// ============================================================
// ACTOR (Internal game Actor object)
// ============================================================

/// <summary>
/// Offsets within the internal Actor object.
/// This is accessed via StaticActorInfo.ActorRef.
/// </summary>
internal static class ActorOffsets
{
    /// <summary>
    /// Reaction/State byte.
    /// Values:
    /// - 0x02 = Ground/Neutral
    /// - 0x03-0x05 = Ground reactions (Step Back/Slide)
    /// - > 0x05 = Airborne/Launch reaction (0x67, 0xC0, etc)
    /// Type: byte
    /// </summary>
    public const int ReactionState = 0x158;
}

// ============================================================
// STATIC ACTOR INFO
// ============================================================

/// <summary>
/// Represents the StaticActorInfo structure (known as 'Actor' in FaithFramework).
/// This is the wrapper found at bnpcRow + 0x20.
/// Updated to match Nenkai's FaithFramework structures.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x7400)] 
public unsafe struct StaticActorInfo
{
    [FieldOffset(0x00)] public long* VTable;
    
    [FieldOffset(0x10)] public uint ActorId;
    [FieldOffset(0x14)] public uint EntityId;

    [FieldOffset(0x20)] public long Node; // Node*
    
    [FieldOffset(0x28)] public long ActionActor; // ActionActor*
    
    [FieldOffset(0x30)] public long WorldContext;
    
    /// <summary>
    /// Reference to the internal game Actor object.
    /// Use ActorOffsets to access fields within.
    /// </summary>
    [FieldOffset(0x58)] public long ActorRef;

    /// <summary>
    /// Global Battle Behavior entry. 
    /// Contains the MagicFileResource (factory) at offset 0.
    /// </summary>
    [FieldOffset(0x7298)] public long BattleBehavior; // BattleBehavior*
}

/// <summary>
/// Mapping of the BattleBehavior structure based on FaithFramework
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x300)]
public unsafe struct BattleBehavior
{
    [FieldOffset(0x00)] public long* VTable;
    
    /// <summary>
    /// This is the 'MagicFileInstance' or Resource Container
    /// used for spawning projectiles and VFX.
    /// Found at offset 0x10 in FaithFramework.
    /// </summary>
    [FieldOffset(0x10)] public long MagicFileInstance;

    // Found at +0x200 in IDA analysis for Airborne checks
    [FieldOffset(0x200)] public long StateList;
}

/// <summary>
/// StateList structure for airborne state detection.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public unsafe struct StateList
{
    // 0x234 is the bit used for Airborne/InAir state in IDA logic
    [FieldOffset(0x234)] public uint IsAirborneBit;
}

// ============================================================
// NODE POSITION PAIR
// ============================================================

/// <summary>
/// Structure used for passing position data to game functions.
/// Matching FF16Framework's NodePositionPair exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 0x20)]
public struct NodePositionPair
{
    public nint VTable;     // 0x00
    public nint ParentNode; // 0x08 - Pointer to Node, or 0 for World Space
    public Vector3 Position;// 0x10 - X, Y, Z (12 bytes)
    public int Padding;     // 0x1C - Padding to align to 0x20
}

// ============================================================
// ACTOR REFERENCE
// ============================================================

/// <summary>
/// The ActorManager is a singleton responsible for managing all actor instances.
/// Used by GetActorByKey to resolve ActorId -> ActorReference.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ActorManager
{
    public nint VTable;
    // The internal implementation uses a hashmap/list structure
    // We access it through function wrappers, not directly
}

/// <summary>
/// Actor reference structure returned by ActorManager_GetActorByKey.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ActorReference
{
    public nint VTable;
    public uint ActorId;
    public uint EntityID;
    public nint Node;
    public nint Node2;
    public nint field_20;
    public nint field_28;
    public nint field_30;
    public nint field_38;
    public int UnkCounterIndex;
    public int Flags;
    public nint HasTypeBitset;
    public nint HasTypeBitset2;
    public nint field_58;
    public nint ListEntryByListTypeAndActorId;
    public nint field_68;
    public nint g_off;
    public nint field_78;
    public Vector3 UnkVec;
    public int field_8C;
}
