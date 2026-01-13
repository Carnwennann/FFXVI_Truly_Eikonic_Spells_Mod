using System;
using System.Runtime.InteropServices;

namespace ff16.gameplay.truly_eikonic_spells.GameApis
{
    /// <summary>
    /// Represents the StaticActorInfo structure (the "Wrapper" at bnpcRow + 0x20).
    /// Updated based on RAW memory dump analysis (Offset +0x10 is ActorPtr).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 0x100)]
    public unsafe struct StaticActorInfo
    {
        [FieldOffset(0x00)] public long* VTable;
        
        [FieldOffset(0x08)] public int Dword8;
        [FieldOffset(0x0C)] public int State;
        
        // IDA dice ActorId (uint), pero en RAW vemos el puntero completo de 64 bits
        [FieldOffset(0x10)] public long ActorPtr;
        
        [FieldOffset(0x18)] public fixed long CacheBitflags[2]; 
        
        [FieldOffset(0x28)] public long CachedActorRef;
        
        [FieldOffset(0x30)] public long BattleBehaviorDataEntry31;

        [FieldOffset(0x38)] public long List13Entry;
        
        [FieldOffset(0x40)] public long EntryData12;
        
        [FieldOffset(0x48)] public long EntryData11;
        
        [FieldOffset(0x50)] public long EntryData46;
        
        [FieldOffset(0x58)] public long EntryData49;
        
        [FieldOffset(0x60)] public long EntryData4;
        
        [FieldOffset(0x68)] public long EntryData7;

        [FieldOffset(0x70)] public long EntryData10;
        [FieldOffset(0x78)] public long EntryData33;
        [FieldOffset(0x80)] public long EntryData32;
        [FieldOffset(0x88)] public long List19Entry;
        
        [FieldOffset(0x90)] public long List36Entry;
        
        [FieldOffset(0x98)] public long EntryData37;
        [FieldOffset(0x0A0)] public long VatbDataEntry;
        [FieldOffset(0x0A8)] public long EntryData3;
        [FieldOffset(0x0B0)] public long EntryData35;
        [FieldOffset(0x0B8)] public long EntryData45;
        [FieldOffset(0x0C0)] public long EntryData23;
        [FieldOffset(0x0C8)] public long EntryData42;
        
        // En RAW desaparecen los punteros aquí y aparecen Floats que cambian en medio del golpe
        [FieldOffset(0x0D0)] public float ReactionValue1;
        [FieldOffset(0x0D4)] public float ReactionValue2;
        
        [FieldOffset(0x0D8)] public long EidEntry;
        [FieldOffset(0x0E0)] public long EntryData2;
        [FieldOffset(0x0E8)] public long EntryData13;
        [FieldOffset(0x0F0)] public long EntryData16;
        [FieldOffset(0x0F8)] public long EntryData53;
    }

    /// <summary>
    /// The BattleBehaviorEntityEntry structure found inside StaticActorInfo at +0x7298
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct BattleBehaviorEntityEntry
    {
        [FieldOffset(0x00)] public long* VTable;
        
        // This is the pointer referenced in IDA as sub_7FF6B0970EF0(*(BBEE + 512) + 0x234)
        // 512 decimal = 0x200
        [FieldOffset(0x200)] public long StateList;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct StateList
    {
        // 0x234 is the bit used for Airborne/InAir state in IDA logic
        [FieldOffset(0x234)] public uint IsAirborneBit;
    }
}
