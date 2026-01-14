using System;
using System.Runtime.InteropServices;

namespace ff16.gameplay.truly_eikonic_spells.GameApis
{
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

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct StateList
    {
        // 0x234 is the bit used for Airborne/InAir state in IDA logic
        [FieldOffset(0x234)] public uint IsAirborneBit;
    }
}
