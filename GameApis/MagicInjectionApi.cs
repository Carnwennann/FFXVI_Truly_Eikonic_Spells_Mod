using System.Text.Json;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// API for casting magic with modified operations loaded from JSON files.
/// </summary>
public class MagicInjectionApi
{
    private readonly ILogger _logger;
    private readonly string _modId;
    private readonly MagicCastApi _magicCastApi;
    private readonly Dictionary<string, List<FuzzerEntry>> _cachedModifications = new();

    public MagicInjectionApi(ILogger logger, string modId, MagicCastApi magicCastApi)
    {
        _logger = logger;
        _modId = modId;
        _magicCastApi = magicCastApi;
    }

    /// <summary>
    /// Loads a JSON file containing fuzzer entries and caches them.
    /// </summary>
    public bool LoadModifications(string name, string filePath)
    {
        _logger.WriteLine($"[{_modId}] [MagicInjectionApi] LoadModifications called for '{name}' with path: {filePath}", _logger.ColorYellow);
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.WriteLine($"[{_modId}] [MagicInjectionApi] File not found: {filePath}", _logger.ColorRed);
                return false;
            }

            string json = File.ReadAllText(filePath);
            _logger.WriteLine($"[{_modId}] [MagicInjectionApi] File read successfully ({json.Length} bytes)", _logger.ColorYellow);
            
            var entries = JsonSerializer.Deserialize<List<FuzzerEntry>>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (entries != null)
            {
                _cachedModifications[name] = entries;
                _logger.WriteLine($"[{_modId}] [MagicInjectionApi] Loaded '{name}' with {entries.Count} modifications", _logger.ColorGreen);
                return true;
            }
            else
            {
                _logger.WriteLine($"[{_modId}] [MagicInjectionApi] Deserialization returned null for '{name}'", _logger.ColorRed);
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modId}] [MagicInjectionApi] Failed to load '{name}': {ex.Message}", _logger.ColorRed);
            _logger.WriteLine($"[{_modId}] [MagicInjectionApi] StackTrace: {ex.StackTrace}", _logger.ColorRed);
        }
        return false;
    }

    /// <summary>
    /// Casts a magic spell using the modifications from a previously loaded JSON.
    /// </summary>
    public bool CastModifiedMagic(string modificationName, int magicId, int count = 1)
    {
        _logger.WriteLine($"[{_modId}] [MagicInjectionApi] CastModifiedMagic called for '{modificationName}' Magic {magicId} x{count}", _logger.ColorYellow);
        
        if (!_cachedModifications.TryGetValue(modificationName, out var entries))
        {
            _logger.WriteLine($"[{_modId}] [MagicInjectionApi] Modification '{modificationName}' not found in cache", _logger.ColorRed);
            return false;
        }

        // Enqueue the same modification set 'count' times
        for (int i = 0; i < count; i++)
        {
            _magicCastApi.EnqueueModifications(magicId, entries);
        }

        return _magicCastApi.CastSpells(magicId, count);
    }

    /// <summary>
    /// Casts magic spells using a list of pre-adapted modification sets.
    /// Each list in the outer list represents one projectile's modifications.
    /// </summary>
    public bool CastModifiedMagic(int magicId, List<List<FuzzerEntry>> modifications)
    {
        _logger.WriteLine($"[{_modId}] [MagicInjectionApi] CastModifiedMagic called with {modifications.Count} custom modification sets", _logger.ColorYellow);

        foreach (var modSet in modifications)
        {
            _magicCastApi.EnqueueModifications(magicId, modSet);
        }

        return _magicCastApi.CastSpells(magicId, modifications.Count);
    }

    /// <summary>
    /// Gets the cached modifications for a given profile name.
    /// </summary>
    public List<FuzzerEntry>? GetModifications(string name)
    {
        if (_cachedModifications.TryGetValue(name, out var entries))
            return entries;
        return null;
    }
}
