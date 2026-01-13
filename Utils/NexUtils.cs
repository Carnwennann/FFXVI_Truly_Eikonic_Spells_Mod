using System;

namespace ff16.gameplay.truly_eikonic_spells.Utils
{
    public static unsafe class NexUtils
    {
        /// <summary>
        /// Reads the Data Pointer from a NexRowInstance structure.
        /// Reverses Nex::RowGetPtr (0x7ff6b0f91000).
        /// </summary>
        /// <param name="nexRowInstancePtr">Pointer to the NexRowInstance structure</param>
        /// <returns>Pointer to the actual data row, or 0 if invalid.</returns>
        public static long RowGetPtr(long nexRowInstancePtr)
        {
            if (nexRowInstancePtr == 0) return 0;

            // Offset 0: Ptr/Type/Index (long)
            long ptrValue = *(long*)nexRowInstancePtr;
            
            // Check top bit (Sign bit). If negative, it's invalid/empty.
            // "if ( (*p_NexRowInstance)->Ptr < 0 ) goto LABEL_7;"
            if (ptrValue < 0) return 0;

            // v1 = NexRowInstance->Ptr & 0x7FFFFFFFFFFFFFFFLL;
            long index = ptrValue & 0x7FFFFFFFFFFFFFFF;

            // if ( !v1 ) return 0;
            if (index == 0) return 0;

            // Field_8 = (int *)NexRowInstance->Field_8; (Offset 8)
            long field8Ptr = *(long*)(nexRowInstancePtr + 8);
            if (field8Ptr == 0) return 0;

            long offset = 0;
            
            // Logic from ASM:
            // v3 = v1 - 1;
            // if (v3) {
            //    if (v3 == 1) v4 = Field_8[2]; (Index 2 in int array -> Offset 8)
            //    else         v4 = Field_8[4]; (Index 4 in int array -> Offset 16)
            // } else {
            //    v4 = Field_8[1]; (Index 1 in int array -> Offset 4)
            // }

            // Simplified:
            // if (index == 1)      v4 = field8[1];
            // else if (index == 2) v4 = field8[2];
            // else                 v4 = field8[4];
            
            if (index == 1)
            {
                offset = *(int*)(field8Ptr + 4); // Field_8[1]
            }
            else if (index == 2)
            {
                offset = *(int*)(field8Ptr + 8); // Field_8[2]
            }
            else
            {
                offset = *(int*)(field8Ptr + 16); // Field_8[4]
            }

            // return (char *)Field_8 + v4;
            return field8Ptr + offset;
        }
    }
}
