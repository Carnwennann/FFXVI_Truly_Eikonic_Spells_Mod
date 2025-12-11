namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Centralized repository of all known Action IDs in FFXVI
/// Use these constants instead of hardcoded magic numbers
/// </summary>
public static class ActionIds
{
    #region Universal Magic Shots
    
    public const int NORMAL_SHOT_AIR = 218;
    public const int NORMAL_SHOT_GROUND = 219;
    public const int PRECISION_SHOT = 222;
    public const int CHARGED_SHOT = 227;
    
    #endregion
    
    #region Magic Burst Combo
    
    public const int MAGIC_BURST_1 = 199;
    public const int MAGIC_BURST_2 = 200;
    public const int MAGIC_BURST_3 = 201;
    public const int MAGIC_BURST_FINISH = 202;
    
    #endregion
    
    #region Bahamut Abilities
    
    public const int MEGAFLARE_LVL1 = 776;
    public const int MEGAFLARE_LVL2 = 777;
    public const int MEGAFLARE_LVL3 = 800;
    public const int MEGAFLARE_LVL4 = 801;
    public const int IMPULSE = 824;
    public const int FLARE_BREATH = 830;
    public const int GIGAFLARE = 845;
    public const int SATELLITES = 0;  // Need to confirm ID
    
    #endregion
    
    #region Phoenix Abilities
    
    public const int HEATWAVE = 376;
    // Add more Phoenix abilities as discovered
    
    #endregion
    
    #region Shiva Abilities
    
    public const int MESMERIZE = 747;
    public const int AERIAL_MESMERIZE = 748;
    // Add more Shiva abilities as discovered
    
    #endregion
    
    #region Ramuh Abilities
    
    public const int BLIND_JUSTICE = 628;
    // Add more Ramuh abilities as discovered
    
    #endregion
    
    #region Leviathan Abilities
    
    public const int PRECISION_TIDAL_TORRENT = 1028;
    public const int DODGE_TIDAL_STREAM = 1029;
    public const int TIDAL_TORRENT = 1046;
    public const int CHARGED_TORRENT = 1053;
    public const int TIDAL_STREAM = 1065;
    public const int CHARGED_STREAM = 1069;
    public const int DELUGE = 1078;
    public const int CROSS_SWELL = 1091;
    public const int ABYSSAL_TEAR = 1096;
    public const int CHARGED_ABYSSAL_TEAR = 1097;
    public const int AERIAL_ABYSSAL_TEAR = 1098;
    public const int CHARGED_AERIAL_ABYSSAL_TEAR = 1099;
    public const int TSUNAMI = 1124;
    
    #endregion
    
    #region Garuda Abilities
    
    // Add Garuda abilities as discovered
    
    #endregion
    
    #region Titan Abilities
    
    // Add Titan abilities as discovered
    
    #endregion
    
    #region Odin Abilities
    
    // Add Odin abilities as discovered
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Check if the action is a magic shot (normal, precision, or charged)
    /// </summary>
    public static bool IsMagicShot(int actionId)
    {
        return actionId == NORMAL_SHOT_AIR 
            || actionId == NORMAL_SHOT_GROUND 
            || actionId == PRECISION_SHOT 
            || actionId == CHARGED_SHOT;
    }
    
    /// <summary>
    /// Check if the action is part of the Magic Burst combo
    /// </summary>
    public static bool IsMagicBurst(int actionId)
    {
        return actionId >= MAGIC_BURST_1 && actionId <= MAGIC_BURST_FINISH;
    }
    
    /// <summary>
    /// Check if the action is a Megaflare variant
    /// </summary>
    public static bool IsMegaflare(int actionId)
    {
        return actionId == MEGAFLARE_LVL1 
            || actionId == MEGAFLARE_LVL2 
            || actionId == MEGAFLARE_LVL3 
            || actionId == MEGAFLARE_LVL4;
    }
    
    #endregion
}
