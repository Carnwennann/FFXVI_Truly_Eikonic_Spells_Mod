using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Security;
using System.Runtime.ExceptionServices;

namespace ff16.gameplay.truly_eikonic_spells.Utils
{
    public static class ActorInfoLogger
    {
        [DllImport("kernel32.dll")]
        static extern int VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        const uint MEM_COMMIT = 0x1000;
        const uint PAGE_NOACCESS = 0x01;
        const uint PAGE_GUARD = 0x100;

        public static bool IsMemoryReadable(IntPtr address, int size)
        {
            if (address == IntPtr.Zero) return false;
            
            MEMORY_BASIC_INFORMATION mbi;
            if (VirtualQuery(address, out mbi, (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) == 0)
                return false;

            if (mbi.State != MEM_COMMIT) return false;
            if ((mbi.Protect & PAGE_NOACCESS) == PAGE_NOACCESS || (mbi.Protect & PAGE_GUARD) == PAGE_GUARD) return false;

            return true;
        }

        public static unsafe string LogStaticActorInfo(long address)
        {
            if (address < 0x10000) return "Invalid StaticActorInfo Address";

            var sb = new StringBuilder();
            var info = (GameApis.StaticActorInfo*)address;

            sb.AppendLine($"\n[EXPLORER] === StaticActorInfo @ 0x{address:X} ===");
            
            // Explicit Field Dump to identify missing pointers
            sb.AppendLine("  [Fields]");
            sb.AppendLine($"    +0x10 ActorId: {info->ActorId}");
            sb.AppendLine($"    +0x14 EntityId: {info->EntityId}");
            sb.AppendLine($"    +0x20 Node: 0x{info->Node:X}");
            sb.AppendLine($"    +0x28 ActionActor: 0x{info->ActionActor:X}");
            sb.AppendLine($"    +0x30 WorldContext: 0x{info->WorldContext:X}");
            sb.AppendLine($"    +0x58 ActorRef (Base): 0x{info->ActorRef:X}");
            sb.AppendLine($"    +0x7298 BattleBehavior: 0x{info->BattleBehavior:X}");

            // Scan component pointers
            sb.AppendLine("  [SCAN] Probing components for Nex Stats...");
            TryProbeNexStats(sb, "WorldContext (+0x30)", info->WorldContext);
            TryProbeNexStats(sb, "Node (+0x20)", info->Node);
            TryProbeNexStats(sb, "ActorRef (+0x58)", info->ActorRef);

            sb.AppendLine("  [SCAN] Heuristic Float Scan (Looking for HP-ish values > 5.0 in ActorRef)");
            if (info->ActorRef > 0x10000)
            {
                ScanForFloats(sb, "ActorRef", info->ActorRef, 0x2000); 
                ScanForInts(sb, "ActorRef", info->ActorRef, 0x2000);
            }
            
            return sb.ToString();
        }

        private static unsafe void ScanForShorts(StringBuilder sb, string label, long address, int size)
        {
            if (address < 0x10000 || !IsMemoryReadable((IntPtr)address, size)) return;

            short* pShorts = (short*)address;
            int count = size / 2;
            
            // Targets: Base HP (3500), Max HP Candidates (1750, 2450, 2520)
            // Also Unk4 (4000), Unk5 (14585) to confirm location.
            HashSet<short> targets = new HashSet<short> { 3500, 1750, 2450, 2520, 4000, 14585, 14000 };

            sb.AppendLine($"    [{label}] Scanning for Shorts (Targets: 3500, 4000, 14585, etc)...");
            for (int i=0; i<count; i++) 
            {
                short val = pShorts[i];
                if (targets.Contains(val))
                {
                    sb.AppendLine($"      [SHORT] +0x{i*2:X}: {val} <----- HIT");
                }
            }
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static unsafe void ScanForInts(StringBuilder sb, string label, long address, int size)
        {
             if (address < 0x10000 || !IsMemoryReadable((IntPtr)address, size)) return;
             
             int* pInts = (int*)address;
             int count = size / 4;
             bool found = false;
             
             // Target values based on Level 60 Goblin Mugger (ParamGrow ID 5)
             // Base: 3500. Multipliers: 0.5 (1750), 0.7 (2450), 0.72 (2520)
             HashSet<int> targets = new HashSet<int> { 3500, 1750, 2450, 2520, 25200, 24500, 17500, 35000 };

             sb.AppendLine($"    [{label}] Scanning for Ints (Heuristic & Targets: 3500, 1750, 2450)...");
             for (int i=0; i<count; i++) 
             {
                 int val = pInts[i];
                 
                 // Highlight exact matches
                 if (targets.Contains(val))
                 {
                     sb.AppendLine($"      [MATCH!] +0x{i*4:X}: {val} <----- POTENTIAL HP");
                     found = true;
                     continue;
                 }

                 // HP heuristic: > 100 and < 10,000,000. Divisible by 10 often?
                 if (val > 100 && val < 1000000)
                 {
                     // Filter out likely pointers (very large overlap with addresses, but addresses are > 2^32 usually on x64)
                     // Valid Ints are usually small positive.
                     
                     // Extra filter to reduce noise:
                     if (val > 500000) continue; 
                     if (val == 32759) continue; // Noise seen in logs
                     if (val == 65600) continue; // Noise seen in logs

                     sb.AppendLine($"      [INT] +0x{i*4:X}: {val}");
                     found = true;
                 }
             }
             if (!found) sb.AppendLine("      No plausible Ints found.");
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static unsafe void ScanForFloats(StringBuilder sb, string label, long address, int size)
        {
            if (address < 0x10000)
            {
                 sb.AppendLine($"    [{label}] Skipped: Invalid Address");
                 return;
            }
            if (!IsMemoryReadable((IntPtr)address, size))
            {
                 sb.AppendLine($"    [{label}] Skipped: Memory Unreadable");
                 return;
            }

            float* pFloats = (float*)address;
            int count = size / 4;
            bool foundAny = false;
            
            for (int i = 0; i < count; i++)
            {
                float val = pFloats[i];
                // Relaxed filter: Values between 1 and 1 million
                if (val >= 1.0f && val <= 2000000.0f) 
                {
                   // Filter out small integers unless they really look like stats (e.g. > 50)
                   // or if they are exactly 1.0 (scaling factor) we might ignore them to reduce noise
                   // unless we want to see everything.
                   if (val > 10.0f || (val >= 1.0f && val <= 5.0f && (val % 1.0f != 0))) // Skip plain 1.0, 2.0 flags
                   {
                        sb.AppendLine($"    [{label}] +0x{i*4:X}: {val:F2}");
                        foundAny = true;
                   }
                   else if (val > 30.0f) // Catch HP
                   {
                        sb.AppendLine($"    [{label}] +0x{i*4:X}: {val:F2}");
                        foundAny = true;
                   }
                }
            }
            if (!foundAny) sb.AppendLine($"    [{label}] No plausible float values found.");
        }


        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static unsafe void TryProbeNexStats(StringBuilder sb, string label, long entryPtr)
        {
            if (entryPtr < 0x10000) return; // Ignore null/invalid

            try 
            {
                // Validation 1: entryPtr readable?
                if (!IsMemoryReadable((IntPtr)entryPtr, 128)) return;
                
                // Logic: Entry -> +0x60 -> NexRowInstance -> RowGetPtr -> Data
                // Validation 2: entryPtr + 0x60 readable?
                long ptrToRowAddr = entryPtr + 0x60;
                // Since we checked 128 bytes above, this is redundant but safe

                long ptrToRow = *(long*)ptrToRowAddr;
                if (ptrToRow < 0x10000 || ptrToRow > 0x7FFFFFFFFFFF) return;

                // Validation 3: ptrToRow readable?
                if (!IsMemoryReadable((IntPtr)ptrToRow, 8)) return;

                long rowInstance = *(long*)ptrToRow;
                if (rowInstance < 0x10000 || rowInstance > 0x7FFFFFFFFFFF) return;

                // Try to resolve data
                long dataPtr = NexUtils.RowGetPtr(rowInstance);
                if (dataPtr == 0) return;
                
                // Validation 4: dataPtr readable?
                if (!IsMemoryReadable((IntPtr)dataPtr, 0x60)) return;

                // If we get here, we successfully resolved a Nex Row. Check for HP-like floats.
                float f0x40 = *(float*)(dataPtr + 0x40);
                float f0x44 = *(float*)(dataPtr + 0x44);
                float f0x48 = *(float*)(dataPtr + 0x48);
                float f0x50 = *(float*)(dataPtr + 0x50);

                sb.AppendLine($"    [HIT] {label} has NexRow! Instance=0x{rowInstance:X} Data=0x{dataPtr:X}");
                sb.AppendLine($"         +0x40: {f0x40:F1}");
                sb.AppendLine($"         +0x44: {f0x44:F1}");
                sb.AppendLine($"         +0x48: {f0x48:F1}");
                sb.AppendLine($"         +0x50: {f0x50:F1}"); // Will Gauge?
            }
            catch (Exception)
            {
                sb.AppendLine($"    [ERR] Probe failed for {label}");
            }
        }

        private static unsafe void LogEntryIfValid(StringBuilder sb, string label, long entryAddr)
        {
            if (entryAddr < 0x10000 || entryAddr > 0x7FFFFFFFFFFF) return;
            if (!IsMemoryReadable((IntPtr)entryAddr, 32)) return;

            // Leemos los primeros 32 bytes de la entrada para ver firmas o punteros
            long* data = (long*)entryAddr;
            sb.AppendLine($"  > {label} @ 0x{entryAddr:X}");
            sb.AppendLine($"    [0x00] 0x{data[0]:X16} (VTable?)");
            sb.AppendLine($"    [0x08] 0x{data[1]:X16} | [0x10] 0x{data[2]:X16}");
            
            // Si parece tener un float en +0x08 (común en stats), lo mostramos
            try {
                float fval = *(float*)(entryAddr + 0x08);
                if (fval > 0.001f && fval < 1000000f)
                    sb.AppendLine($"    [0x08] Float: {fval:F3}");
            } catch {}
        }
    }
}
