using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

/// <summary>
/// Implementation of IMagicBuilder.
/// Provides fluent API for configuring magic spell modifications.
/// </summary>
internal class MagicBuilder : IMagicBuilder
{
    private readonly MagicCastingEngine _engine;
    private readonly ILogger _logger;
    private readonly string _modId;
    private readonly List<MagicModification> _modifications = new();
    
    // Cached magic structure info for validation
    private readonly Dictionary<int, HashSet<int>> _operationGroupOperations = new();
    private bool _structureLoaded = false;
    
    public int MagicId { get; }
    
    internal MagicBuilder(int magicId, MagicCastingEngine engine, ILogger logger, string modId)
    {
        MagicId = magicId;
        _engine = engine;
        _logger = logger;
        _modId = modId;
        
        // Try to load the magic structure for validation
        LoadMagicStructure();
    }
    
    private void LoadMagicStructure()
    {
        // TODO: Load actual magic structure from game memory
        // For now, we'll allow any operationGroupId/operationType combination
        // and validate at cast time
        _structureLoaded = false;
    }
    
    // ========================================
    // VALIDATION
    // ========================================
    
    public bool HasOperationGroup(int operationGroupId)
    {
        if (!_structureLoaded)
        {
            // Without structure data, we can't validate - allow it
            return true;
        }
        return _operationGroupOperations.ContainsKey(operationGroupId);
    }
    
    public bool HasOperation(int operationGroupId, int operationType)
    {
        if (!_structureLoaded)
        {
            // Without structure data, we can't validate - allow it
            return true;
        }
        return _operationGroupOperations.TryGetValue(operationGroupId, out var ops) 
               && ops.Contains(operationType);
    }
    
    public IReadOnlyList<int> GetOperationGroupIds()
    {
        if (!_structureLoaded)
        {
            return Array.Empty<int>();
        }
        return _operationGroupOperations.Keys.ToList();
    }
    
    public IReadOnlyList<int> GetOperationTypes(int operationGroupId)
    {
        if (!_structureLoaded || !_operationGroupOperations.TryGetValue(operationGroupId, out var ops))
        {
            return Array.Empty<int>();
        }
        return ops.ToList();
    }
    
    private void ValidateOperationGroup(int operationGroupId)
    {
        if (_structureLoaded && !HasOperationGroup(operationGroupId))
        {
            throw new ArgumentException($"OperationGroupId {operationGroupId} does not exist in Magic {MagicId}");
        }
    }
    
    private void ValidateOperation(int operationGroupId, int operationType)
    {
        ValidateOperationGroup(operationGroupId);
        if (_structureLoaded && !HasOperation(operationGroupId, operationType))
        {
            throw new ArgumentException($"OperationType {operationType} does not exist in OperationGroup {operationGroupId} of Magic {MagicId}");
        }
    }
    
    // ========================================
    // PROPERTY MODIFICATIONS
    // ========================================
    
    public IMagicBuilder SetProperty(int operationGroupId, int operationId, int propertyId, object value)
    {
        ValidateOperation(operationGroupId, operationId);
        
        _modifications.Add(new MagicModification
        {
            Type = MagicModificationType.SetProperty,
            OperationGroupId = operationGroupId,
            OperationType = operationId,
            PropertyId = propertyId,
            Value = NormalizeValue(value)
        });
        
        return this;
    }
    
    public IMagicBuilder RemoveProperty(int operationGroupId, int operationId, int propertyId)
    {
        ValidateOperation(operationGroupId, operationId);
        
        _modifications.Add(new MagicModification
        {
            Type = MagicModificationType.RemoveProperty,
            OperationGroupId = operationGroupId,
            OperationType = operationId,
            PropertyId = propertyId
        });
        
        return this;
    }
    
    public IMagicBuilder AddProperty(int operationGroupId, int operationId, int propertyId, object value)
    {
        ValidateOperation(operationGroupId, operationId);
        
        _modifications.Add(new MagicModification
        {
            Type = MagicModificationType.AddProperty,
            OperationGroupId = operationGroupId,
            OperationType = operationId,
            PropertyId = propertyId,
            Value = NormalizeValue(value)
        });
        
        return this;
    }
    
    // ========================================
    // OPERATION MODIFICATIONS
    // ========================================
    
    public IMagicBuilder AddOperation(int operationGroupId, int operationType)
    {
        ValidateOperationGroup(operationGroupId);
        
        _modifications.Add(new MagicModification
        {
            Type = MagicModificationType.AddOperation,
            OperationGroupId = operationGroupId,
            OperationType = operationType
        });
        
        return this;
    }
    
    public IMagicBuilder AddOperation(int operationGroupId, int operationType, IList<int> propertyIds, IList<object> values)
    {
        ValidateOperationGroup(operationGroupId);
        
        if (propertyIds.Count != values.Count)
        {
            throw new ArgumentException($"propertyIds ({propertyIds.Count}) and values ({values.Count}) must have the same length");
        }
        
        if (propertyIds.Count == 0)
        {
            return AddOperation(operationGroupId, operationType);
        }
        
        // Normalize all values
        var normalizedValues = values.Select(NormalizeValue).ToList();
        
        _modifications.Add(new MagicModification
        {
            Type = MagicModificationType.AddOperation,
            OperationGroupId = operationGroupId,
            OperationType = operationType,
            PropertyId = propertyIds[0],
            Value = normalizedValues[0],
            AdditionalPropertyIds = propertyIds.Count > 1 ? propertyIds.Skip(1).ToList() : null,
            AdditionalValues = normalizedValues.Count > 1 ? normalizedValues.Skip(1).ToList() : null
        });
        
        return this;
    }
    
    public IMagicBuilder RemoveOperation(int operationGroupId, int operationType)
    {
        ValidateOperationGroup(operationGroupId);
        
        _modifications.Add(new MagicModification
        {
            Type = MagicModificationType.RemoveOperation,
            OperationGroupId = operationGroupId,
            OperationType = operationType
        });
        
        return this;
    }
    
    // ========================================
    // EXECUTION
    // ========================================
    
    public bool Cast(nint? sourceActor = null, nint? targetActor = null)
    {
        return _engine.CastSpell(BuildCastRequest(sourceActor, targetActor));
    }
    
    // ========================================
    // SERIALIZATION
    // ========================================
    
    public string ExportToJson()
    {
        var config = new MagicSpellConfig
        {
            MagicId = MagicId,
            Name = $"Magic_{MagicId}",
            Description = $"Exported spell configuration for Magic ID {MagicId}",
            Modifications = _modifications.Select(ConvertToConfig).ToList()
        };
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        
        return JsonSerializer.Serialize(config, options);
    }
    
    public bool ExportToFile(string filePath)
    {
        try
        {
            var json = ExportToJson();
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(filePath, json);
            _logger.WriteLine($"[{_modId}] [MagicBuilder] Exported to: {filePath}", _logger.ColorGreen);
            return true;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modId}] [MagicBuilder] Failed to export: {ex.Message}", _logger.ColorRed);
            return false;
        }
    }
    
    public IMagicBuilder ImportFromJson(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<MagicSpellConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (config == null)
            {
                throw new ArgumentException("Failed to parse JSON");
            }
            
            // Validate magic ID matches (or allow if this is a fresh builder)
            if (config.MagicId != 0 && config.MagicId != MagicId)
            {
                _logger.WriteLine($"[{_modId}] [MagicBuilder] Warning: JSON MagicId ({config.MagicId}) differs from builder ({MagicId})", _logger.ColorYellow);
            }
            
            foreach (var modConfig in config.Modifications)
            {
                ApplyModificationConfig(modConfig);
            }
            
            return this;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON format: {ex.Message}", ex);
        }
    }
    
    public IReadOnlyList<MagicModification> GetModifications()
    {
        return _modifications.AsReadOnly();
    }
    
    public IMagicBuilder Reset()
    {
        _modifications.Clear();
        return this;
    }
    
    // ========================================
    // INTERNAL HELPERS
    // ========================================
    
    private MagicCastRequest BuildCastRequest(nint? sourceActor, nint? targetActor)
    {
        return new MagicCastRequest
        {
            MagicId = MagicId,
            SourceActor = sourceActor,
            TargetActor = targetActor,
            Modifications = _modifications.ToList()
        };
    }
    
    private static object NormalizeValue(object value)
    {
        // Ensure consistent value types
        return value switch
        {
            int i => i,
            float f => f,
            double d => (float)d,
            bool b => b,
            Vector3 v => v,
            _ => value
        };
    }
    
    private MagicModificationConfig ConvertToConfig(MagicModification mod)
    {
        var config = new MagicModificationConfig
        {
            Type = mod.Type.ToString(),
            OperationGroupId = mod.OperationGroupId,
            OperationId = mod.OperationType
        };
        
        if (mod.Type == MagicModificationType.AddOperation && 
            (mod.AdditionalPropertyIds?.Count > 0 || mod.PropertyId != 0))
        {
            // Build properties list for AddOperation
            config.Properties = new List<PropertyValuePair>();
            
            if (mod.PropertyId != 0 || mod.Value != null)
            {
                config.Properties.Add(new PropertyValuePair
                {
                    PropertyId = mod.PropertyId,
                    Value = SerializeValue(mod.Value)
                });
            }
            
            if (mod.AdditionalPropertyIds != null && mod.AdditionalValues != null)
            {
                for (int i = 0; i < mod.AdditionalPropertyIds.Count; i++)
                {
                    config.Properties.Add(new PropertyValuePair
                    {
                        PropertyId = mod.AdditionalPropertyIds[i],
                        Value = SerializeValue(mod.AdditionalValues[i])
                    });
                }
            }
        }
        else if (mod.Type != MagicModificationType.RemoveOperation)
        {
            config.PropertyId = mod.PropertyId;
            config.Value = SerializeValue(mod.Value);
        }
        
        return config;
    }
    
    private static object? SerializeValue(object? value)
    {
        if (value is Vector3 v)
        {
            return new float[] { v.X, v.Y, v.Z };
        }
        return value;
    }
    
    private void ApplyModificationConfig(MagicModificationConfig config)
    {
        var type = Enum.Parse<MagicModificationType>(config.Type, ignoreCase: true);
        
        switch (type)
        {
            case MagicModificationType.SetProperty:
                if (config.PropertyId.HasValue && config.Value != null)
                {
                    SetProperty(config.OperationGroupId, config.OperationId, config.PropertyId.Value, DeserializeValue(config.Value));
                }
                break;
                
            case MagicModificationType.RemoveProperty:
                if (config.PropertyId.HasValue)
                {
                    RemoveProperty(config.OperationGroupId, config.OperationId, config.PropertyId.Value);
                }
                break;
                
            case MagicModificationType.AddProperty:
                if (config.PropertyId.HasValue && config.Value != null)
                {
                    AddProperty(config.OperationGroupId, config.OperationId, config.PropertyId.Value, DeserializeValue(config.Value));
                }
                break;
                
            case MagicModificationType.AddOperation:
                if (config.Properties != null && config.Properties.Count > 0)
                {
                    var propertyIds = config.Properties.Select(p => p.PropertyId).ToList();
                    var values = config.Properties.Select(p => DeserializeValue(p.Value)!).ToList();
                    AddOperation(config.OperationGroupId, config.OperationId, propertyIds, values);
                }
                else
                {
                    AddOperation(config.OperationGroupId, config.OperationId);
                }
                break;
                
            case MagicModificationType.RemoveOperation:
                RemoveOperation(config.OperationGroupId, config.OperationId);
                break;
        }
    }
    
    private static object DeserializeValue(object? value)
    {
        if (value == null) return 0;
        
        // Handle JSON element types
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.TryGetInt32(out int i) ? i : element.GetSingle(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array when element.GetArrayLength() == 3 => 
                    new Vector3(
                        element[0].GetSingle(),
                        element[1].GetSingle(),
                        element[2].GetSingle()
                    ),
                _ => value
            };
        }
        
        // Handle arrays (for Vector3)
        if (value is float[] arr && arr.Length == 3)
        {
            return new Vector3(arr[0], arr[1], arr[2]);
        }
        
        return value;
    }
}

/// <summary>
/// Internal request structure for casting a spell.
/// Contains all configuration from the builder.
/// </summary>
internal record MagicCastRequest
{
    public int MagicId { get; init; }
    public nint? SourceActor { get; init; }
    public nint? TargetActor { get; init; }
    public List<MagicModification> Modifications { get; init; } = new();
}
