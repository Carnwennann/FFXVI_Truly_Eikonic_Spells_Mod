namespace ff16.gameplay.truly_eikonic_spells.Utils;

/// <summary>
/// Centralized documentation for enemy reaction types and push directions.
/// 
/// The reaction system has two components:
/// 1. ReactionType (+0x15c): Controls the ANIMATION the enemy plays
/// 2. PushDirection (+0x160): Controls the DIRECTION/FORCE of the knockback
/// 
/// Both work together to create the full hit reaction effect.
/// </summary>
public static class ReactionTypes
{
    #region Reaction Animation Types (+0x15c)
    
    /// <summary>
    /// No reaction animation - hit is ignored visually
    /// </summary>
    public const int ANIM_NONE = 0;
    
    /// <summary>
    /// Skip reaction - same as NONE
    /// </summary>
    public const int ANIM_SKIP = 1;
    
    /// <summary>
    /// Small stagger backwards while standing (normal melee hit)
    /// </summary>
    public const int ANIM_STAGGER_BACK = 2;
    
    /// <summary>
    /// Stagger back variant (same as 2)
    /// </summary>
    public const int ANIM_STAGGER_BACK_ALT = 3;
    
    /// <summary>
    /// Enemy spins around from the hit
    /// </summary>
    public const int ANIM_SPIN = 4;
    
    /// <summary>
    /// Falls flat on the floor (for aerial/downward hits)
    /// </summary>
    public const int ANIM_FALL_FLAT = 5;
    
    /// <summary>
    /// Falls on back while spinning
    /// </summary>
    public const int ANIM_FALL_BACK_SPIN = 6;
    
    /// <summary>
    /// Falls on the floor face down (for aerial hits like Rising Flames)
    /// </summary>
    public const int ANIM_FALL_FACE_DOWN = 7;
    
    /// <summary>
    /// Falls on the floor variant
    /// </summary>
    public const int ANIM_FALL_FLOOR_8 = 8;
    
    /// <summary>
    /// Falls on the floor variant
    /// </summary>
    public const int ANIM_FALL_FLOOR_9 = 9;
    
    /// <summary>
    /// Slow stagger that ends with enemy on the floor
    /// </summary>
    public const int ANIM_SLOW_STAGGER_FALL = 10;
    
    /// <summary>
    /// Skip - no visible reaction
    /// </summary>
    public const int ANIM_SKIP_11 = 11;
    
    /// <summary>
    /// Freezes enemy animation (T-pose or idle keyframe)
    /// </summary>
    public const int ANIM_FREEZE = 12;
    
    #endregion
    
    #region Push Direction Types (+0x160)
    
    // The push direction system uses ranges of values for different trajectory types.
    // Within each range, higher values = stronger force.
    
    /// <summary>
    /// No push - enemy stays in place
    /// </summary>
    public const int PUSH_NONE = 0;
    
    // === RANGE 2-5: Step back (no parabola, ground slide) ===
    public const int PUSH_STEP_BACK_MIN = 2;      // Weakest step back
    public const int PUSH_STEP_BACK_MAX = 5;      // Strongest step back
    
    // === RANGE 6-9: Push backwards with parabola (slight lift) ===
    public const int PUSH_PARABOLA_MIN = 6;       // Weakest parabola push
    public const int PUSH_PARABOLA_MAX = 9;       // Strongest parabola push
    
    // === RANGE 10-13: Launch downwards (slam into ground) ===
    public const int PUSH_SLAM_DOWN_MIN = 10;     // Weakest downward slam
    public const int PUSH_SLAM_DOWN_MAX = 13;     // Strongest downward slam
    
    // === RANGE 14-17: Launch backwards with upward momentum ===
    public const int PUSH_LAUNCH_BACK_UP_MIN = 14;  // Weakest backward-up launch
    public const int PUSH_LAUNCH_BACK_UP_MAX = 17;  // Strongest backward-up launch
    
    // === RANGE 18-21: Launch upwards (vertical launch) ===
    public const int PUSH_LAUNCH_UP_MIN = 18;     // Weakest upward launch
    public const int PUSH_LAUNCH_UP_MAX = 21;     // Strongest upward launch
    
    // === RANGE 22-25: Launch downwards (second set) ===
    public const int PUSH_SLAM_DOWN2_MIN = 22;    // Weakest downward slam (set 2)
    public const int PUSH_SLAM_DOWN2_MAX = 25;    // Strongest downward slam (set 2)
    
    // === RANGE 26-29: Launch backwards with downward momentum ===
    public const int PUSH_LAUNCH_BACK_DOWN_MIN = 26; // Weakest backward-down launch
    public const int PUSH_LAUNCH_BACK_DOWN_MAX = 29; // Strongest backward-down launch
    
    // Values 30+ are unused/undefined behavior
    
    #endregion
    
    #region Config Description Constants (for [Description] attributes)
    
    /// <summary>
    /// Description for animation type config dropdown (use in [Description] attribute)
    /// </summary>
    public const string CONFIG_DESC_ANIMATION = 
        "Enemy reaction ANIMATION type:\n" +
        "0-1 = No reaction (skip)\n" +
        "2-3 = Stagger back (standing hit)\n" +
        "4 = Spins around\n" +
        "5 = Falls flat on floor\n" +
        "6 = Falls back spinning\n" +
        "7-9 = Falls on floor variants\n" +
        "10 = Slow stagger to floor\n" +
        "12 = Freeze animation";
    
    /// <summary>
    /// Description for push direction config dropdown (use in [Description] attribute)
    /// </summary>
    public const string CONFIG_DESC_PUSH = 
        "Enemy push/knockback DIRECTION (ranges):\n" +
        "0 = No push\n" +
        "2-5 = Step back (ground slide, weak→strong)\n" +
        "6-9 = Parabola push (slight lift, weak→strong)\n" +
        "10-13 = Slam downwards (weak→strong)\n" +
        "14-17 = Launch back+up (weak→strong)\n" +
        "18-21 = Launch upwards (weak→strong)\n" +
        "22-25 = Slam down set 2 (weak→strong)\n" +
        "26-29 = Launch back+down (weak→strong)\n" +
        "30+ = Unused";
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Get human-readable name for a reaction animation type
    /// </summary>
    public static string GetAnimationName(int reactionType)
    {
        return reactionType switch
        {
            ANIM_NONE => "(None/Skip)",
            ANIM_SKIP => "(None/Skip)",
            ANIM_STAGGER_BACK => "(Stagger back)",
            ANIM_STAGGER_BACK_ALT => "(Stagger back)",
            ANIM_SPIN => "(Spins around)",
            ANIM_FALL_FLAT => "(Falls flat)",
            ANIM_FALL_BACK_SPIN => "(Falls back spinning)",
            ANIM_FALL_FACE_DOWN => "(Falls face down)",
            ANIM_FALL_FLOOR_8 => "(Falls on floor)",
            ANIM_FALL_FLOOR_9 => "(Falls on floor)",
            ANIM_SLOW_STAGGER_FALL => "(Slow stagger to floor)",
            ANIM_SKIP_11 => "(None/Skip)",
            ANIM_FREEZE => "(Freeze/T-pose)",
            _ => $"(Unknown:{reactionType})"
        };
    }
    
    /// <summary>
    /// Get human-readable name for a push direction type
    /// </summary>
    public static string GetPushDirectionName(int pushType)
    {
        return pushType switch
        {
            // No push
            PUSH_NONE => "(No push)",
            1 => "(Minimal push)",
            
            // Range 2-5: Step back (ground slide)
            2 => "(Step back - weakest)",
            3 => "(Step back - weak)",
            4 => "(Step back - medium)",
            5 => "(Step back - strong)",
            
            // Range 6-9: Parabola push (slight lift)
            6 => "(Parabola push - weakest)",
            7 => "(Parabola push - weak)",
            8 => "(Parabola push - medium)",
            9 => "(Parabola push - strong)",
            
            // Range 10-13: Slam downwards
            10 => "(Slam down - weakest)",
            11 => "(Slam down - weak)",
            12 => "(Slam down - medium)",
            13 => "(Slam down - strong)",
            
            // Range 14-17: Launch backwards + upward momentum
            14 => "(Launch back-up - weakest)",
            15 => "(Launch back-up - weak)",
            16 => "(Launch back-up - medium)",
            17 => "(Launch back-up - strong)",
            
            // Range 18-21: Launch upwards (vertical)
            18 => "(Launch up - weakest)",
            19 => "(Launch up - weak)",
            20 => "(Launch up - medium)",
            21 => "(Launch up - strong)",
            
            // Range 22-25: Slam downwards (set 2)
            22 => "(Slam down 2 - weakest)",
            23 => "(Slam down 2 - weak)",
            24 => "(Slam down 2 - medium)",
            25 => "(Slam down 2 - strong)",
            
            // Range 26-29: Launch backwards + downward momentum
            26 => "(Launch back-down - weakest)",
            27 => "(Launch back-down - weak)",
            28 => "(Launch back-down - medium)",
            29 => "(Launch back-down - strong)",
            
            // 30+ unused
            _ when pushType >= 30 => $"(Unused:{pushType})",
            _ => $"(Unknown:{pushType})"
        };
    }
    
    /// <summary>
    /// Get description string for Config.cs animation dropdown
    /// </summary>
    public static string GetAnimationConfigDescription()
    {
        return "Enemy reaction ANIMATION type:\n" +
               $"{ANIM_NONE}-{ANIM_SKIP} = No reaction (skip)\n" +
               $"{ANIM_STAGGER_BACK} = Stagger back (standing hit)\n" +
               $"{ANIM_SPIN} = Spins around\n" +
               $"{ANIM_FALL_FLAT} = Falls flat on floor\n" +
               $"{ANIM_FALL_BACK_SPIN} = Falls back spinning\n" +
               $"{ANIM_FALL_FACE_DOWN} = Falls face down (aerial)\n" +
               $"{ANIM_SLOW_STAGGER_FALL} = Slow stagger to floor\n" +
               $"{ANIM_FREEZE} = Freeze animation";
    }
    
    /// <summary>
    /// Get description string for Config.cs push direction dropdown
    /// </summary>
    public static string GetPushConfigDescription()
    {
        return "Enemy push/knockback DIRECTION (ranges):\n" +
               "0 = No push\n" +
               "2-5 = Step back (ground slide, weak→strong)\n" +
               "6-9 = Parabola push (slight lift, weak→strong)\n" +
               "10-13 = Slam downwards (weak→strong)\n" +
               "14-17 = Launch back+up (weak→strong)\n" +
               "18-21 = Launch upwards (weak→strong)\n" +
               "22-25 = Slam down set 2 (weak→strong)\n" +
               "26-29 = Launch back+down (weak→strong)\n" +
               "30+ = Unused";
    }
    
    #endregion
}
