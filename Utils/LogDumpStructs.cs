using FF16Tools.Files.Nex;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.Utils;

/// <summary>
/// Utility class for logging and dumping game structures for reverse engineering.
/// All debug logging functions are centralized here to keep the main mod clean.
/// </summary>
public static class LogDumpStructs
{
    #region Table Layout Dump
    
    public static void DumpTableLayout(ILogger logger, string modId, string tableName)
    {
        try 
        {
            var layout = TableMappingReader.ReadTableLayout(tableName, new Version(1, 0, 3), "ffxvi");
            logger.WriteLine($"[{modId}] === {tableName.ToUpper()} LAYOUT ===", logger.ColorYellow);
            foreach (var col in layout.Columns)
            {
                logger.WriteLine($"[{modId}] Column: {col.Key}, Offset: {col.Value.Offset}, Type: {col.Value.Type}", logger.ColorYellow);
            }
            logger.WriteLine($"[{modId}] === END LAYOUT ===", logger.ColorYellow);
        }
        catch (Exception ex)
        {
            logger.WriteLine($"[{modId}] Error dumping {tableName}: {ex.Message}", logger.ColorRed);
        }
    }
    
    #endregion
    
    #region Timeline Dumps
    
    public static unsafe void LogTimelineFromContext(
        ILogger logger, 
        string modId, 
        long a1, 
        long a2, 
        string context,
        long cachedTimelineParam1,
        long cachedTimelinePtr,
        Func<long, long> getTimelineOriginal)
    {
        try
        {
            logger.WriteLine($"[{modId}] [TIMELINE] ===========================================", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TIMELINE] Context: {context}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [TIMELINE] Dodge a1=0x{a1:X}, a2=0x{a2:X}", logger.ColorYellow);
            
            if (cachedTimelineParam1 != 0)
            {
                logger.WriteLine($"[{modId}] [TIMELINE] Using cached param_1=0x{cachedTimelineParam1:X}", logger.ColorYellow);
                
                long timelineResult = getTimelineOriginal(cachedTimelineParam1);
                
                logger.WriteLine($"[{modId}] [TIMELINE] GetTimeline result during {context}: 0x{timelineResult:X}", 
                    timelineResult != 0 ? logger.ColorGreen : logger.ColorRed);
                
                if (timelineResult != 0)
                {
                    LogTimelineObject(logger, modId, cachedTimelineParam1, timelineResult, context);
                }
                else
                {
                    logger.WriteLine($"[{modId}] [TIMELINE] Timeline is NULL - vtable[0x48] blocked!", logger.ColorRed);
                    long* vtableEntry = (long*)(cachedTimelineParam1 + 0x10);
                    logger.WriteLine($"[{modId}] [TIMELINE] param_1+0x10 = 0x{*vtableEntry:X}", logger.ColorYellow);
                }
            }
            else
            {
                logger.WriteLine($"[{modId}] [TIMELINE] No cached param_1 available - shoot once first!", logger.ColorRed);
            }
            
            if (cachedTimelinePtr != 0)
            {
                logger.WriteLine($"[{modId}] [TIMELINE] Last cached Timeline ptr=0x{cachedTimelinePtr:X}", logger.ColorYellow);
            }
            
            logger.WriteLine($"[{modId}] [TIMELINE] ===========================================", logger.ColorYellow);
        }
        catch (Exception ex)
        {
            logger.WriteLine($"[{modId}] [TIMELINE] Error in LogTimelineFromContext: {ex.Message}", logger.ColorRed);
        }
    }
    
    public static unsafe void LogTimelineObject(ILogger logger, string modId, long param_1, long timelineResult, string context)
    {
        try
        {
            logger.WriteLine($"[{modId}] [TIMELINE] ===========================================", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TIMELINE] Context: {context}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [TIMELINE] GetTimeline called! param_1=0x{param_1:X}", logger.ColorYellow);
            
            long timelinePtr = *(long*)(param_1 + 0x10);
            logger.WriteLine($"[{modId}] [TIMELINE] [param_1+0x10] (raw timeline ptr) = 0x{timelinePtr:X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TIMELINE] Result from GetTimeline = 0x{timelineResult:X}", logger.ColorYellow);
            
            if (timelinePtr != 0)
            {
                long vtablePtr = *(long*)timelinePtr;
                logger.WriteLine($"[{modId}] [TIMELINE] VTable ptr = 0x{vtablePtr:X}", logger.ColorGreen);
                
                long func48 = *(long*)(vtablePtr + 0x48);
                long func58 = *(long*)(vtablePtr + 0x58);
                long func98 = *(long*)(vtablePtr + 0x98);
                logger.WriteLine($"[{modId}] [TIMELINE] VTable[0x48] (IsMagicBlocked?) = 0x{func48:X}", logger.ColorGreen);
                logger.WriteLine($"[{modId}] [TIMELINE] VTable[0x58] (GetMagicType?)   = 0x{func58:X}", logger.ColorGreen);
                logger.WriteLine($"[{modId}] [TIMELINE] VTable[0x98] (IsMagicAllowed?) = 0x{func98:X}", logger.ColorGreen);
                
                logger.WriteLine($"[{modId}] [TIMELINE] --- Timeline Object Data (first 0x100 bytes) ---", logger.ColorYellow);
                for (int i = 0; i < 0x100; i += 0x20)
                {
                    long v0 = *(long*)(timelinePtr + i);
                    long v8 = *(long*)(timelinePtr + i + 0x8);
                    long v10 = *(long*)(timelinePtr + i + 0x10);
                    long v18 = *(long*)(timelinePtr + i + 0x18);
                    logger.WriteLine($"[{modId}] [TIMELINE] +0x{i:X2}: {v0:X16} {v8:X16} {v10:X16} {v18:X16}", logger.ColorYellow);
                }
            }
            else
            {
                logger.WriteLine($"[{modId}] [TIMELINE] Timeline ptr is NULL!", logger.ColorRed);
            }
            
            logger.WriteLine($"[{modId}] [TIMELINE] ===========================================", logger.ColorYellow);
        }
        catch (Exception ex)
        {
            logger.WriteLine($"[{modId}] [TIMELINE] Error logging: {ex.Message}", logger.ColorRed);
        }
    }
    
    #endregion
    
    #region Destination Structure Dump
    
    public static unsafe void DumpDestStructure(ILogger logger, string modId, long destStruct)
    {
        logger.WriteLine($"[{modId}] [DEST_STRUCT] === Destination Structure Before Copy ===", logger.ColorLightBlue);
        
        for (int i = 0; i < 0x100; i += 0x10)
        {
            long val0 = *(long*)(destStruct + i);
            long val8 = *(long*)(destStruct + i + 8);
            logger.WriteLine($"[{modId}] [DEST_STRUCT] +0x{i:X2}: 0x{val0:X16} | 0x{val8:X16}", logger.ColorLightBlue);
        }
        
        long entityPtr = *(long*)destStruct;
        int destActionId = *(int*)(destStruct + 0x58);
        logger.WriteLine($"[{modId}] [DEST_STRUCT] Entity ptr at +0x00: 0x{entityPtr:X}", logger.ColorLightBlue);
        logger.WriteLine($"[{modId}] [DEST_STRUCT] ActionId at +0x58 (before copy): {destActionId}", logger.ColorLightBlue);
    }
    
    #endregion
    
    #region OnReaction Data Dump
    
    public static unsafe void DumpOnReactionData(ILogger logger, string modId, long param1, long param2)
    {
        logger.WriteLine($"[{modId}] [REACTION] ========== TriggerReactionHit ==========", logger.ColorYellow);
        logger.WriteLine($"[{modId}] [REACTION] param1 (entity): 0x{param1:X}", logger.ColorYellow);
        logger.WriteLine($"[{modId}] [REACTION] param2 (attack): 0x{param2:X}", logger.ColorYellow);
        
        // === PARAM1 (Entity/Target structure) ===
        long* p1 = (long*)param1;
        
        byte skipFlag = *(byte*)(param1 + 0x1d);
        string skipText = (skipFlag & 1) != 0 ? "WOULD SKIP!" : "";
        logger.WriteLine($"[{modId}] [REACTION] param1+0x1d (skip flag): 0x{skipFlag:X2} (& 1 = {skipFlag & 1}) {skipText}", logger.ColorLightBlue);
        
        long p1_0 = p1[0];
        long p1_1 = p1[1];
        long p1_3 = p1[3];
        long p1_4 = p1[4];
        
        logger.WriteLine($"[{modId}] [REACTION] param1[0] +0x00: 0x{p1_0:X}", logger.ColorLightBlue);
        logger.WriteLine($"[{modId}] [REACTION] param1[1] +0x08 (entity data): 0x{p1_1:X}", logger.ColorLightBlue);
        logger.WriteLine($"[{modId}] [REACTION] param1[3] +0x18 (prev reaction): 0x{p1_3:X}", logger.ColorLightBlue);
        logger.WriteLine($"[{modId}] [REACTION] param1[4] +0x20 (handler): 0x{p1_4:X}", logger.ColorLightBlue);
        
        if (p1_1 != 0)
        {
            long battleData = *(long*)(p1_1 + 0x7298);
            long ptr9c70 = *(long*)(p1_1 + 0x9c70);
            logger.WriteLine($"[{modId}] [REACTION] entity+0x7298 (battle data): 0x{battleData:X}", logger.ColorLightBlue);
            logger.WriteLine($"[{modId}] [REACTION] entity+0x9c70: 0x{ptr9c70:X}", logger.ColorLightBlue);
            
            logger.WriteLine($"[{modId}] [REACTION] --- Entity Flags (searching for physics immunity) ---", logger.ColorYellow);
            
            for (int offset = 0x10; offset <= 0x40; offset += 4)
            {
                uint flagVal = *(uint*)(p1_1 + offset);
                if (flagVal != 0)
                {
                    logger.WriteLine($"[{modId}] [REACTION] entity+0x{offset:X}: 0x{flagVal:X8}", logger.ColorLightBlue);
                }
            }
            
            uint flags100 = *(uint*)(p1_1 + 0x100);
            uint flags104 = *(uint*)(p1_1 + 0x104);
            uint flags108 = *(uint*)(p1_1 + 0x108);
            uint flags1a0 = *(uint*)(p1_1 + 0x1a0);
            uint flags1a4 = *(uint*)(p1_1 + 0x1a4);
            byte flags1c = *(byte*)(p1_1 + 0x1c);
            byte flags1d = *(byte*)(p1_1 + 0x1d);
            byte flags1e = *(byte*)(p1_1 + 0x1e);
            
            logger.WriteLine($"[{modId}] [REACTION] entity+0x100: 0x{flags100:X8}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [REACTION] entity+0x104: 0x{flags104:X8}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [REACTION] entity+0x108: 0x{flags108:X8}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [REACTION] entity+0x1a0: 0x{flags1a0:X8}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [REACTION] entity+0x1a4: 0x{flags1a4:X8}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [REACTION] entity+0x1c/1d/1e: 0x{flags1c:X2} 0x{flags1d:X2} 0x{flags1e:X2}", logger.ColorYellow);
        }
        
        if (p1_3 != 0)
        {
            int prevReactionType = *(int*)(p1_3 + 0x15c);
            logger.WriteLine($"[{modId}] [REACTION] >>> Previous reaction type: {prevReactionType}", logger.ColorRed);
        }
        
        // === PARAM2 (Attack/Reaction data) ===
        logger.WriteLine($"[{modId}] [REACTION] --- param2 (Attack/Reaction Data) ---", logger.ColorGreen);
        
        int actionId = *(int*)(param2 + 0xB0);
        int damage = *(int*)(param2 + 0x174);
        int reactionType = *(int*)(param2 + 0x15c);
        int reactionVal2 = *(int*)(param2 + 0x160);
        int someId88 = *(int*)(param2 + 0x88);
        int val184 = *(int*)(param2 + 0x184);
        uint flags194 = *(uint*)(param2 + 0x194);
        byte flags196 = *(byte*)(param2 + 0x196);
        
        bool flag194_bit4 = ((flags194 >> 4) & 1) != 0;
        bool flag194_bit12 = ((flags194 >> 12) & 1) != 0;
        bool flag194_0x2800 = (flags194 & 0x2800) != 0;
        bool flag196_bit0 = (flags196 & 1) != 0;
        
        logger.WriteLine($"[{modId}] [REACTION] +0xB0 ActionId: {actionId}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [REACTION] +0x174 Damage: {damage}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [REACTION] +0x15c ReactionType: {reactionType} {ReactionTypes.GetAnimationName(reactionType)}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [REACTION] +0x160 PushDirection: {reactionVal2} {ReactionTypes.GetPushDirectionName(reactionVal2)}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [REACTION] +0x88 SomeId: {someId88}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [REACTION] +0x184 Val184: {val184}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [REACTION] +0x194 Flags: 0x{flags194:X8} (bit4={flag194_bit4}, bit12={flag194_bit12}, 0x2800={flag194_0x2800})", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [REACTION] +0x196 Flags: 0x{flags196:X2} (bit0={flag196_bit0})", logger.ColorGreen);
        
        logger.WriteLine($"[{modId}] [REACTION] ========================================", logger.ColorYellow);
    }
    
    #endregion
    
    #region Magic Hit Debug
    
    public static unsafe void LogMagicHitDebug(ILogger logger, string modId, AttackInfo info, long R15, long* bnpcRow, long a3, long a4)
    {
        logger.WriteLine($"[{modId}] [MAGIC_HIT] === MAGIC PROJECTILE HIT ===", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [MAGIC_HIT] ActionId: {info.ActionId} (218=Air, 219=Ground, 227=Charged)", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [MAGIC_HIT] R15 (attack struct): 0x{R15:X}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [MAGIC_HIT] bnpcRow (target): 0x{(long)bnpcRow:X}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [MAGIC_HIT] a3: 0x{a3:X}, a4: 0x{a4:X}", logger.ColorGreen);
        logger.WriteLine($"[{modId}] [MAGIC_HIT] Damage value at R15+0x174: {info.Damage}", logger.ColorGreen);
        
        DumpR15Structure(logger, modId, R15);
    }
    
    #endregion
    
    #region Projectile Data Dump
    
    public static unsafe void DumpProjectileData(ILogger logger, string modId, long ptr)
    {
        try
        {
            logger.WriteLine($"[{modId}] [PROJ_DUMP] === Projectile Data Dump (RDX) ===", logger.ColorYellow);
            
            for (int i = 0; i < 0x80; i += 0x10)
            {
                long v0 = *(long*)(ptr + i);
                long v8 = *(long*)(ptr + i + 8);
                logger.WriteLine($"[{modId}] [PROJ_DUMP] +0x{i:X2}: {v0:X16} {v8:X16}", logger.ColorYellow);
            }
            
            long ptr20 = *(long*)(ptr + 0x20);
            long ptr40 = *(long*)(ptr + 0x40);
            
            if (ptr20 != 0)
            {
                logger.WriteLine($"[{modId}] [PROJ_DUMP] Dereferencing Pointer at 0x20: {ptr20:X}", logger.ColorYellow);
                for (int i = 0; i < 0x40; i += 0x10)
                {
                    long v0 = *(long*)(ptr20 + i);
                    long v8 = *(long*)(ptr20 + i + 8);
                    logger.WriteLine($"[{modId}] [PROJ_DUMP] [0x20]+0x{i:X2}: {v0:X16} {v8:X16}", logger.ColorYellow);
                }
            }
            
            if (ptr40 != 0)
            {
                logger.WriteLine($"[{modId}] [PROJ_DUMP] Dereferencing Pointer at 0x40: {ptr40:X}", logger.ColorYellow);
                for (int i = 0; i < 0x40; i += 0x10)
                {
                    long v0 = *(long*)(ptr40 + i);
                    long v8 = *(long*)(ptr40 + i + 8);
                    logger.WriteLine($"[{modId}] [PROJ_DUMP] [0x40]+0x{i:X2}: {v0:X16} {v8:X16}", logger.ColorYellow);
                }
            }
        }
        catch (Exception ex)
        {
            logger.WriteLine($"[{modId}] [PROJ_DUMP] Error: {ex.Message}", logger.ColorRed);
        }
    }
    
    #endregion
    
    #region R15 Structure Dump
    
    public static unsafe void DumpR15Structure(ILogger logger, string modId, long R15)
    {
        try
        {
            logger.WriteLine($"[{modId}] [R15_DUMP] === R15 Structure Dump ===", logger.ColorGreen);
            
            int actionId = *(int*)(R15 + 0xB0);
            int damage = *(int*)(R15 + 0x174);
            long ptrAt88 = *(long*)(R15 + 0x88);
            
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x00: 0x{*(long*)R15:X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x08: 0x{*(long*)(R15 + 0x08):X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x10: 0x{*(long*)(R15 + 0x10):X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x18: 0x{*(long*)(R15 + 0x18):X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x20: 0x{*(long*)(R15 + 0x20):X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x28: 0x{*(long*)(R15 + 0x28):X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x30: 0x{*(long*)(R15 + 0x30):X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x88 (entity ptr?): 0x{ptrAt88:X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0xB0 (ActionId): {actionId}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x174 (Damage): {damage}", logger.ColorGreen);
            
            float f1 = *(float*)(R15 + 0x40);
            float f2 = *(float*)(R15 + 0x44);
            float f3 = *(float*)(R15 + 0x48);
            float f4 = *(float*)(R15 + 0x50);
            float f5 = *(float*)(R15 + 0x54);
            float f6 = *(float*)(R15 + 0x58);
            
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x40-0x48 (floats): {f1:F2}, {f2:F2}, {f3:F2}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [R15_DUMP] +0x50-0x58 (floats): {f4:F2}, {f5:F2}, {f6:F2}", logger.ColorGreen);
            
            logger.WriteLine($"[{modId}] [R15_DUMP] === For CheatEngine: Search for these hex values ===", logger.ColorYellow);
        }
        catch (Exception ex)
        {
            logger.WriteLine($"[{modId}] [R15_DUMP] Error: {ex.Message}", logger.ColorRed);
        }
    }
    
    #endregion
    
    #region Magic Template Dump
    
    public static unsafe void DumpMagicTemplate(ILogger logger, string modId, long template)
    {
        try
        {
            logger.WriteLine($"[{modId}] [TEMPLATE] === Magic Template Dump ===", logger.ColorYellow);
            
            int actionId = *(int*)(template + 0x58);
            int val5C = *(int*)(template + 0x5C);
            int val60 = *(int*)(template + 0x60);
            int val64 = *(int*)(template + 0x64);
            
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x00: 0x{*(long*)template:X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x08: 0x{*(long*)(template + 0x08):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x10: 0x{*(long*)(template + 0x10):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x18: 0x{*(long*)(template + 0x18):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x20: 0x{*(long*)(template + 0x20):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x28: 0x{*(long*)(template + 0x28):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x30: 0x{*(long*)(template + 0x30):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x38: 0x{*(long*)(template + 0x38):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x40: 0x{*(long*)(template + 0x40):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x48: 0x{*(long*)(template + 0x48):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x50: 0x{*(long*)(template + 0x50):X}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x58 (ActionId): {actionId}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x5C: {val5C}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x60: {val60}", logger.ColorYellow);
            logger.WriteLine($"[{modId}] [TEMPLATE] +0x64: {val64}", logger.ColorYellow);
            
            long baseAddr = (long)System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress;
            long moduleEnd = baseAddr + System.Diagnostics.Process.GetCurrentProcess().MainModule!.ModuleMemorySize;
            
            bool isStatic = template >= baseAddr && template < moduleEnd;
            logger.WriteLine($"[{modId}] [TEMPLATE] Template is static: {isStatic}", logger.ColorYellow);
            if (isStatic)
            {
                long offset = template - baseAddr;
                logger.WriteLine($"[{modId}] [TEMPLATE] Static offset: 0x{offset:X}", logger.ColorYellow);
            }
        }
        catch (Exception ex)
        {
            logger.WriteLine($"[{modId}] [TEMPLATE] Error: {ex.Message}", logger.ColorRed);
        }
    }
    
    /// <summary>
    /// Log CopyAttackData call information for reverse engineering.
    /// Returns the ActionId read from the template, or -1 on error.
    /// </summary>
    public static unsafe int DumpCopyAttackData(
        ILogger logger, 
        string modId, 
        long destAttackStruct, 
        long srcAttackTemplate,
        bool dumpMagicTemplate,
        bool dumpDestStructure)
    {
        try
        {
            // Read ActionId from source template (at offset 0x58 from the actual data start)
            // The function does: lea rbx, [rcx+58] then copies from [rdi+58] to [rbx+58]
            // So the ActionId is at srcAttackTemplate + 0x58
            int actionId = *(int*)(srcAttackTemplate + 0x58);
            
            // Get return address from stack to find caller
            long* stackPtr = (long*)&destAttackStruct;
            long returnAddr = *(stackPtr - 1);
            var baseAddr = System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64();
            var callerOffset = returnAddr - baseAddr;
            
            logger.WriteLine($"[{modId}] [COPY_ATTACK] === Attack Data Copy ===", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [COPY_ATTACK] Dest (attack struct): 0x{destAttackStruct:X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [COPY_ATTACK] Src (template): 0x{srcAttackTemplate:X}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [COPY_ATTACK] ActionId from template: {actionId}", logger.ColorGreen);
            logger.WriteLine($"[{modId}] [COPY_ATTACK] Return addr: 0x{returnAddr:X} (offset: 0x{callerOffset:X})", logger.ColorGreen);
            
            // Check if this is a magic projectile (218, 219, 227)
            if (actionId == 218 || actionId == 219 || actionId == 227)
            {
                logger.WriteLine($"[{modId}] [COPY_ATTACK] >>> MAGIC PROJECTILE DETECTED! <<<", logger.ColorYellow);
                logger.WriteLine($"[{modId}] [COPY_ATTACK] Stored magic template: 0x{srcAttackTemplate:X}", logger.ColorYellow);
                logger.WriteLine($"[{modId}] [COPY_ATTACK] Stored dest struct: 0x{destAttackStruct:X}", logger.ColorYellow);
                
                // Dump the template structure
                if (dumpMagicTemplate)
                {
                    DumpMagicTemplate(logger, modId, srcAttackTemplate);
                }
                
                // Dump the destination structure BEFORE copy
                if (dumpDestStructure)
                {
                    DumpDestStructure(logger, modId, destAttackStruct);
                }
            }
            
            return actionId;
        }
        catch (Exception ex)
        {
            logger.WriteLine($"[{modId}] [COPY_ATTACK] Error reading: {ex.Message}", logger.ColorRed);
            return -1;
        }
    }
    
    #endregion
}
