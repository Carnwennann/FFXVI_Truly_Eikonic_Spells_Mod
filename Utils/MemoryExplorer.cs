using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

namespace ff16.gameplay.truly_eikonic_spells.Utils
{
    public static class MemoryExplorer
    {
        public static unsafe string DumpMemory(long address, int size, int bytesPerRow = 16)
        {
            if (address < 0x10000) return "Invalid Address";
            
            StringBuilder sb = new StringBuilder();
            byte* ptr = (byte*)address;
            
            for (int i = 0; i < size; i += bytesPerRow)
            {
                sb.Append($"{address + i:X12}: ");
                
                // Hex
                for (int j = 0; j < bytesPerRow; j++)
                {
                    if (i + j < size)
                        sb.Append($"{ptr[i + j]:X2} ");
                    else
                        sb.Append("   ");
                }
                
                sb.Append(" | ");
                
                // ASCII
                for (int j = 0; j < bytesPerRow; j++)
                {
                    if (i + j < size)
                    {
                        char c = (char)ptr[i + j];
                        sb.Append(char.IsControl(c) ? '.' : c);
                    }
                }
                
                sb.AppendLine();
            }
            
            return sb.ToString();
        }

        public static unsafe List<long> FindPointersInRange(long address, int size)
        {
            List<long> pointers = new List<long>();
            if (address < 0x10000) return pointers;

            long* ptr = (long*)address;
            int count = size / 8;

            for (int i = 0; i < count; i++)
            {
                long val = ptr[i];
                // Simple heuristic for pointers in our process address space
                if (val > 0x100000000 && val < 0x7FFFFFFFFFFF)
                {
                    pointers.Add(val);
                }
            }

            return pointers;
        }
    }
}
