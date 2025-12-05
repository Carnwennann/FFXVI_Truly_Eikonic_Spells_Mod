using System.Diagnostics;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells;

public static class Extensions
{
    public static void AddScan(this IStartupScanner scans, string pattern, Action<nint> action)
    {
        var baseAddress = Process.GetCurrentProcess().MainModule!.BaseAddress;
        scans!.AddMainModuleScan(pattern, result =>
        {
            if (!result.Found)
                throw new Exception($"Scan unable to find pattern: {pattern}!");
            action(result.Offset + baseAddress);
        });
    }
}
