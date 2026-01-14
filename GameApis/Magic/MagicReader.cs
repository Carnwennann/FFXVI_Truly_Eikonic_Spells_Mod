using ff16.gameplay.truly_eikonic_spells.GameApis.Magic.MagicFile;
using ff16.gameplay.truly_eikonic_spells.GameStructs;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

/// <summary>
/// Utility class for reading magic IDs from game memory structures.
/// Handles pointer validation and ID resolution from MagicFileInstance.
/// </summary>
internal static unsafe class MagicReader
{
    /// <summary>
    /// Resolves MagicId and GroupId from a MagicFileInstance pointer.
    /// </summary>
    /// <param name="instance">Pointer to MagicFileInstance</param>
    /// <param name="fallbackMagicId">Fallback ID if runtime resolution fails</param>
    /// <returns>Tuple of (magicId, groupId), both 0 if invalid</returns>
    public static (int magicId, int groupId) ResolveIds(long instance, int fallbackMagicId = 0)
    {
        if (!PointerValidation.IsValidPointer(instance)) return (0, 0);
        
        try
        {
            var magicFile = (MagicFileInstance*)instance;
            int magicId = magicFile->MagicId;
            int groupId = magicFile->GroupId;

            // Validate ranges and use fallback if needed
            if (!MagicIdRanges.IsValidId(magicId)) 
                magicId = GetIdFromRuntime(instance, fallbackMagicId);
            if (!MagicIdRanges.IsValidId(groupId) || groupId == magicId) 
                groupId = 0;

            return (magicId, groupId);
        }
        catch { return (0, 0); }
    }

    /// <summary>
    /// Attempts to read a valid magic ID directly from runtime memory.
    /// </summary>
    public static int GetIdFromRuntime(long instance, int fallbackId = 0)
    {
        if (!PointerValidation.IsValidPointer(instance)) return fallbackId;
        
        try
        {
            var magicFile = (MagicFileInstance*)instance;
            if (!PointerValidation.IsValidPointer(magicFile->VTable)) return fallbackId;

            if (MagicIdRanges.IsValidId(magicFile->MagicId)) return magicFile->MagicId;
            if (MagicIdRanges.IsValidId(magicFile->GroupId)) return magicFile->GroupId;
        }
        catch { }
        
        return fallbackId;
    }
}
