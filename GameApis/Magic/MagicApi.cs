using System.Text.Json;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.Utils;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

/// <summary>
/// High-level API for casting magic and applying modifications.
/// This is the main interface for other mod systems to trigger magic effects.
/// See <see cref="MagicIds"/> for known magic IDs.
/// </summary>
public class MagicApi
{
    private readonly ILogger _logger;
    private readonly string _modId;
    private readonly MagicGameSystem _magicGameSystem;
    private readonly Dictionary<string, List<FuzzerEntry>> _cachedModifications = new();

    internal MagicApi(ILogger logger, string modId, MagicGameSystem magicGameSystem)
    {
        _logger = logger;
        _modId = modId;
        _magicGameSystem = magicGameSystem;
    }

    /// <summary>
    /// Updates the internal configuration of the magic system.
    /// </summary>
    public void UpdateConfiguration(Config configuration)
    {
        _magicGameSystem.UpdateConfiguration(configuration);
    }

    /// <summary>
    /// Registers a handler that can decide whether to suppress a charged shot.
    /// The handler receives the active Eikon ID and should return true to suppress.
    /// </summary>
    public void RegisterChargedShotHandler(Func<int, bool> handler)
    {
        // For now, we support one handler (the last one registered)
        // In the future, this could be a list of handlers
        _magicGameSystem.OnChargedShotDetected = (eikon, mgr, proj) => handler(eikon);
    }

    /// <summary>
    /// True if the magic system has captured the necessary game context to cast spells.
    /// </summary>
    public bool HasMagicContext => _magicGameSystem.HasMagicContext;

    /// <summary>
    /// Loads a JSON file containing fuzzer entries and caches them.
    /// </summary>
    public bool LoadModifications(string name, string filePath)
    {
        _logger.WriteLine($"[{_modId}] [MagicApi] LoadModifications called for '{name}' with path: {filePath}", _logger.ColorYellow);
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.WriteLine($"[{_modId}] [MagicApi] File not found: {filePath}", _logger.ColorRed);
                return false;
            }

            string json = File.ReadAllText(filePath);
            _logger.WriteLine($"[{_modId}] [MagicApi] File read successfully ({json.Length} bytes)", _logger.ColorYellow);
            
            var entries = JsonSerializer.Deserialize<List<FuzzerEntry>>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (entries != null)
            {
                _cachedModifications[name] = entries;
                _logger.WriteLine($"[{_modId}] [MagicApi] Loaded '{name}' with {entries.Count} modifications", _logger.ColorGreen);
                return true;
            }
            else
            {
                _logger.WriteLine($"[{_modId}] [MagicApi] Deserialization returned null for '{name}'", _logger.ColorRed);
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modId}] [MagicApi] Failed to load '{name}': {ex.Message}", _logger.ColorRed);
            _logger.WriteLine($"[{_modId}] [MagicApi] StackTrace: {ex.StackTrace}", _logger.ColorRed);
        }
        return false;
    }

    /// <summary>
    /// Casts a magic spell using the modifications from a previously loaded JSON.
    /// Falls back to normal cast if the modification profile is not found.
    /// </summary>
    public bool CastModifiedMagic(string modificationName, int magicId, int count = 1)
    {
        if (!_magicGameSystem.HasMagicContext) return false;

        if (_cachedModifications.TryGetValue(modificationName, out var entries))
        {
            _logger.WriteLine($"[{_modId}] [MagicApi] CastModifiedMagic: Using profile '{modificationName}' for Magic {magicId} x{count}", _logger.ColorYellow);
            for (int i = 0; i < count; i++)
            {
                _magicGameSystem.EnqueueModifications(magicId, entries);
            }
        }
        else
        {
            _logger.WriteLine($"[{_modId}] [MagicApi] Profile '{modificationName}' not found, falling back to normal cast", _logger.ColorYellow);
        }

        return CastSpells(magicId, count);
    }

    /// <summary>
    /// Casts magic spells using a list of pre-adapted modification sets.
    /// Each list in the outer list represents one projectile's modifications.
    /// </summary>
    public bool CastModifiedMagic(int magicId, List<List<FuzzerEntry>> modifications)
    {
        if (!_magicGameSystem.HasMagicContext) return false;

        _logger.WriteLine($"[{_modId}] [MagicApi] CastModifiedMagic: Applying {modifications.Count} custom modification sets", _logger.ColorYellow);

        foreach (var modSet in modifications)
        {
            _magicGameSystem.EnqueueModifications(magicId, modSet);
        }

        return CastSpells(magicId, modifications.Count);
    }

    /// <summary>
    /// Cast magic spells using the internal game system.
    /// </summary>
    public bool CastSpells(int magicId, int count = 1)
    {
        if (!_magicGameSystem.HasMagicContext)
        {
            _logger.WriteLine($"[{_modId}] [MagicApi] FAIL: No magic context! Fire a normal shot first.", _logger.ColorRed);
            return false;
        }

        _logger.WriteLine($"[{_modId}] [MagicApi] Attempting to cast {count} spells (ID {magicId})...", _logger.ColorGreen);
        
        int successCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (_magicGameSystem.CastMagicSpell(magicId))
                successCount++;
        }
        
        return successCount > 0;
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
