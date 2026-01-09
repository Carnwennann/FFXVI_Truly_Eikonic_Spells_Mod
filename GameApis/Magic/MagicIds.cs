using System.Collections.Generic;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

/// <summary>
/// Known Magic IDs for various Eikons and abilities.
/// </summary>
public static class MagicIds
{
    public struct MagicEntry
    {
        public int Id;
        public string Name;
        public MagicEntry(int id, string name) { Id = id; Name = name; }
    }

    public static class Ifrit
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(524, "Will O' The Wykes") 
        };
    }

    public static class Phoenix
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(1, "Fire"),
            new(1564, "Fira"),
            new(914, "Heatwave (1st slash)"),
            new(915, "Heatwave (2nd slash)"),
            new(916, "Heatwave (3rd slash counter)"),
            new(917, "Heatwave (4th slash counter)"),
            new(1146, "Flames of Rebirth (1)"),
            new(1147, "Flames of Rebirth (2)"),
            new(1144, "Flames of Rebirth (3)")
        };
    }

    public static class Garuda
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(2, "Aero"),
            new(1565, "Aerora"),
            new(1321, "Deadly Embrace"),
            new(480, "Aerial Blast")
        };
    }

    public static class Titan
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(3, "Stone"),
            new(1566, "Stonera"),
            new(1154, "Earthen Fury (1)"),
            new(1155, "Earthen Fury (2)"),
            new(1153, "Earthen Fury (3)")
        };
    }

    public static class Ramuh
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(210, "Thunder"),
            new(1567, "Thundara"),
            new(674, "Blind Justice (L)"),
            new(673, "Blind Justice (R)"),
            new(653, "Thunderstorm (1)"),
            new(649, "Thunderstorm (2)"),
            new(806, "Thunderstorm (3)"),
            new(807, "Thunderstorm (4)"),
            new(822, "Lightning Rod (Object)"),
            new(820, "Lightning Rod (Proc Hits)"),
            new(1025, "Judgement Bolt (1)"),
            new(1026, "Judgement Bolt (2)")
        };
    }

    public static class Bahamut
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(214, "Dia"),
            new(1571, "Diara"),
            new(1083, "Megaflare"),
            new(623, "Gigaflare"),
            new(815, "Impulse (1st projectile)"),
            new(816, "Impulse (2nd projectile)"),
            new(817, "Impulse (3rd projectile)"),
            new(818, "Impulse (4th projectile)"),
            new(1108, "Satellite (L)"),
            new(1109, "Satellite (R)"),
            new(583, "Flare Breath (Stream)"),
            new(1360, "Flare Breath (End)")
        };
    }

    public static class Shiva
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(213, "Blizzard"),
            new(1570, "Blizzara"),
            new(992, "Cold Snap (Permafrost)"),
            new(624, "Ice Age (undershooted)"),
            new(627, "Ice Age (overshooted)"),
            new(1543, "Ice Age (timed)"),
            new(981, "Rime"),
            new(625, "Diamond Dust (1)"),
            new(982, "Diamond Dust (2)")
        };
    }

    public static class Odin
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(211, "Dark"),
            new(1568, "Darkra")
        };
    }

    public static class Leviathan
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(215, "Water"),
            new(1572, "Watera"),
            new(1574, "Tidal Torrent"),
            new(1575, "Charged Torrent"),
            new(1576, "Tidal Stream"),
            new(1943, "Charged Stream"),
            new(1578, "Deluge (Stream)"),
            new(1792, "Deluge (End)"),
            new(1580, "Cross Swell"),
            new(1621, "Abyssal Tear (vent lvl 1)"),
            new(1650, "Abyssal Tear (vent lvl 2)"),
            new(1651, "Abyssal Tear (vent lvl 3)"),
            new(1652, "Abyssal Tear (vent lvl 4)"),
            new(983, "Tsunami (1)"),
            new(1811, "Tsunami (2)")
        };
    }

    public static class Ultima
    {
        public static readonly List<MagicEntry> All = new() 
        { 
            new(1561, "Ruin"),
            new(1573, "Ruinra"),
            new(1697, "Purge"),
            new(1646, "Rampant Ruin"),
            new(1648, "Rampant Ruinra"),
            new(1612, "Proselytize"),
            new(1722, "Dominion"),
            new(1609, "Voice of God"),
            new(1610, "Ultimate Demise")
        };
    }
}
