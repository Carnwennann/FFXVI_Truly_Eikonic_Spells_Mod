namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Shared utilities and constants for Eikon-related functionality
/// </summary>
public static class EikonUtils
{
    // SummonModeIds (corrected from testing):
    // 0=Phoenix/Leviathan/Ultima, 2=Garuda, 3=Titan, 4=Ramuh, 5=Shiva, 7=Odin, 8=Bahamut
    public static readonly int[] KnownEikonIds = { 0, 2, 3, 4, 5, 7, 8 };
    
    // Eikon ID constants
    public const int EIKON_PHOENIX = 0;    // Also Leviathan/Ultima (DLC)
    public const int EIKON_GARUDA = 2;
    public const int EIKON_TITAN = 3;
    public const int EIKON_RAMUH = 4;
    public const int EIKON_SHIVA = 5;
    public const int EIKON_ODIN = 7;
    public const int EIKON_BAHAMUT = 8;
    
    // Spell element types
    public enum SpellElement { None, Fire, Dia, Dark, Aero, Ice, Thunder, Earth, Water, Ruin }
    
    /// <summary>
    /// Get the display name for an Eikon ID
    /// </summary>
    public static string GetEikonName(int eikonId)
    {
        return eikonId switch
        {
            EIKON_PHOENIX => "Phoenix",  // Also Leviathan/Ultima (DLC)
            EIKON_GARUDA => "Garuda",
            EIKON_TITAN => "Titan",
            EIKON_RAMUH => "Ramuh",
            EIKON_SHIVA => "Shiva",
            EIKON_ODIN => "Odin",
            EIKON_BAHAMUT => "Bahamut",
            -1 => "No Function",
            -2 => "No PlayerState",
            -3 => "Error",
            _ => $"Unknown({eikonId})"
        };
    }
    
    /// <summary>
    /// Get the spell element for an Eikon
    /// </summary>
    public static SpellElement GetSpellElement(int eikonId)
    {
        return eikonId switch
        {
            EIKON_PHOENIX => SpellElement.Fire,
            EIKON_BAHAMUT => SpellElement.Dia,
            EIKON_ODIN => SpellElement.Dark,
            EIKON_GARUDA => SpellElement.Aero,
            EIKON_SHIVA => SpellElement.Ice,
            EIKON_RAMUH => SpellElement.Thunder,
            EIKON_TITAN => SpellElement.Earth,
            _ => SpellElement.None
        };
    }
    
    /// <summary>
    /// Read active Eikon from player state memory
    /// </summary>
    /// <param name="globalPlayerStatePtr">Pointer to global player state</param>
    /// <returns>Active Eikon ID, or negative on error</returns>
    public static unsafe int GetActiveEikon(long globalPlayerStatePtr)
    {
        try
        {
            // Formula from another modder - reads memory directly to check active summon mode
            // isSummonModeActive_a1 = [baseAddress + 0x1816608] + 0x4798 + 0x14650
            long basePtr = *(long*)globalPlayerStatePtr;
            if (basePtr == 0) return -2;
            
            long isSummonModeActive_a1 = basePtr + 0x4798 + 0x14650;
            
            // Read the comparison value: [isSummonModeActive_a1 + 0x58 + [isSummonModeActive_a1 + 0x70] * 8]
            long offset70 = *(long*)(isSummonModeActive_a1 + 0x70);
            long compareValue = *(long*)(isSummonModeActive_a1 + 0x58 + offset70 * 8);
            
            // Check each Eikon: [isSummonModeActive_a1 + 8 * summonModeId] == compareValue
            foreach (int eikonId in KnownEikonIds)
            {
                long eikonValue = *(long*)(isSummonModeActive_a1 + 8 * eikonId);
                if (eikonValue == compareValue)
                    return eikonId;
            }
            
            return 0; // No Eikon active
        }
        catch
        {
            return -3; // Error reading memory
        }
    }
}

/// <summary>
/// Parsed attack information from OnHit
/// </summary>
public struct AttackInfo
{
    public int ActionId;
    public int Damage;
    public long TargetId;
    public bool IsCliveAttack;
    public bool IsCliveTarget;
    public bool IsHealOrEffect;
}
