using System.Collections.Generic;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

internal enum MagicPropertyType
{
    Int,
    Float,
    Vec3Float,
    Vec3Int,
    Bool
}

internal record MagicPropertyInfo(string Name, MagicPropertyType Type);

internal static class MagicProperties
{
    public static readonly Dictionary<int, MagicPropertyInfo> Definitions = new()
    {
        { 2, new("???", MagicPropertyType.Int) },
        { 8, new("Speed", MagicPropertyType.Float) },
        { 13, new("Calculate target trajectory", MagicPropertyType.Bool) },
        { 14, new("Pi value", MagicPropertyType.Float) },
        { 22, new("Vertical Angle Degrees offset", MagicPropertyType.Float) },
        { 30, new("Disappear after duration?", MagicPropertyType.Bool) },
        { 31, new("Scale projectile body (default=1.0)", MagicPropertyType.Float) },
        { 35, new("Duration (s)", MagicPropertyType.Float) },
        { 36, new("Hitbox Behaviour ID?", MagicPropertyType.Int) },
        { 41, new("Hitbox Behaviour Impact ID?", MagicPropertyType.Int) },
        { 42, new("Hitbox/Attachment Size?", MagicPropertyType.Float) },
        { 73, new("Location spawn type ID", MagicPropertyType.Int) },
        { 89, new("VFX ID?", MagicPropertyType.Int) },
        { 187, new("Type of trayectory ID", MagicPropertyType.Int) },
        { 2227, new("???", MagicPropertyType.Int) },
        { 2430, new("Trajectory variables", MagicPropertyType.Vec3Float) },
        { 2351, new("Unknown variable", MagicPropertyType.Float) },
        { 2593, new("Trayectory intensity curve strength", MagicPropertyType.Float) },
    };
}
