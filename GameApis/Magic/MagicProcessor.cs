using System.Numerics;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.GameApis.Magic.MagicFile;

namespace ff16.gameplay.truly_eikonic_spells.GameApis.Magic;

/// <summary>
/// Handles magic file processing, property fuzzing, and injection logic.
/// Separated from MagicGameSystem to follow single responsibility principle.
/// </summary>
internal unsafe class MagicProcessor
{
    // ============================================================
    // DELEGATES
    // ============================================================
    
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate void MagicUnkExecuteDelegate(long magicFileInstance, int opType, int propertyId, long dataPtr);

    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long OperationFactoryDelegate(long a1, int opType, long a3);

    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long GenericMagicDelegate(long a1, long a2, long a3, long a4);

    // ============================================================
    // CONSTANTS
    // ============================================================
    
    private const string MAGIC_UNK_EXECUTE_SIG = "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 49 8B F9 41 8B D8 8B F2 41 83 F8 02 75 2F 48 8D 59 10 48 8D B9 10 01 00 00 EB 1B 48 8B 0B 48 85 C9 74 0F 39 71 20 75 0A";
    private const string OPERATION_FACTORY_SIG = "48 89 5C 24 08 57 48 83 EC 20 49 8B F8 81 FA B7 00 00 00 75 65 48 8B 01 4C 8D 4C 24 48 33 DB 48 89 5C 24 48 8D 53 48 44 8D 43 08 FF 50 30 48 8B D0 48 85 C0 74 6A 48 8B 0F 48 8D 05 04 96 E8 00 48 89 02 44 8D 43 01 41 8B C0 87 42 0C 83 4A 20 FF";
    private const string MAGIC_FILE_PROCESS_SIG = "48 8B C4 48 89 58 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D 68 ?? 48 81 EC ?? ?? ?? ?? C5 F8 29 70 ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 45 33 F6 48 89 55";
    private const string MAGIC_FILE_HANDLE_SUB_ENTRY_SIG = "40 55 53 56 57 41 54 41 56 41 57 48 8B EC 48 83 EC ?? 48 8D 59";

    // ============================================================
    // HOOKS
    // ============================================================
    
    private IHook<MagicUnkExecuteDelegate>? _magicUnkExecuteHook;
    private IHook<OperationFactoryDelegate>? _operationFactoryHook;
    private IHook<GenericMagicDelegate>? _magicFileProcessHook;
    private IHook<GenericMagicDelegate>? _magicFileHandleSubEntryHook;

    // ============================================================
    // STATE - Queues and Trackers
    // ============================================================
    
    private Dictionary<(int magicId, int groupId), Queue<List<FuzzerEntry>>> _groupedQueues = new();
    private List<FuzzerEntry>? _activeInstanceEntries = null;
    private int _activeInstanceMagicId = 0;
    
    private Dictionary<int, int> _opInstanceTracker = new();
    private Dictionary<long, int> _propInstanceTracker = new();
    private int _lastOpType = -1;
    private List<FuzzerEntry> _pendingInjections = new();
    private bool _isProcessingInjections = false;
    
    private readonly Dictionary<long, string> _operationNames;

    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private readonly IStartupScanner _scanner;
    private Config _configuration;

    // External state provider (from MagicGameSystem)
    private Func<int>? _getCurrentlyCastingMagicId;

    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public MagicProcessor(ILogger logger, IModConfig modConfig, Config configuration, IStartupScanner scanner)
    {
        _logger = logger;
        _modConfig = modConfig;
        _configuration = configuration;
        _scanner = scanner;
        _operationNames = MagicOperations.GetDefaultNames();
    }

    // ============================================================
    // INITIALIZATION
    // ============================================================
    
    /// <summary>
    /// Sets up hooks for magic file processing.
    /// </summary>
    public void SetupScans(IReloadedHooks hooks, Func<int>? getCurrentlyCastingMagicId = null)
    {
        _getCurrentlyCastingMagicId = getCurrentlyCastingMagicId;

        _scanner.AddScan(MAGIC_UNK_EXECUTE_SIG, address =>
        {
            _magicUnkExecuteHook = hooks.CreateHook<MagicUnkExecuteDelegate>(MagicUnkExecuteImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicProcessor] Hooked MagicUnkExecute at 0x{address:X}", _logger.ColorGreen);
        });

        _scanner.AddScan(OPERATION_FACTORY_SIG, address =>
        {
            _operationFactoryHook = hooks.CreateHook<OperationFactoryDelegate>(OperationFactoryImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicProcessor] Hooked OperationFactory at 0x{address:X}", _logger.ColorGreen);
        });

        _scanner.AddScan(MAGIC_FILE_PROCESS_SIG, address =>
        {
            _magicFileProcessHook = hooks.CreateHook<GenericMagicDelegate>(MagicFileProcessImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicProcessor] Hooked MagicFile::Process at 0x{address:X}", _logger.ColorGreen);
        });

        _scanner.AddScan(MAGIC_FILE_HANDLE_SUB_ENTRY_SIG, address =>
        {
            _magicFileHandleSubEntryHook = hooks.CreateHook<GenericMagicDelegate>(MagicFileHandleSubEntryImpl, address).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicProcessor] Hooked MagicFile::HandleSubEntry at 0x{address:X}", _logger.ColorGreen);
        });
    }

    // ============================================================
    // PUBLIC API
    // ============================================================
    
    /// <summary>
    /// Enqueues modifications to be applied when a magic spell is processed.
    /// </summary>
    public void EnqueueModifications(int magicId, List<FuzzerEntry> entries)
    {
        var grouped = entries.GroupBy(e => e.TargetOperationGroupId);
        
        foreach (var group in grouped)
        {
            var key = (magicId, group.Key);
            if (!_groupedQueues.TryGetValue(key, out var queue))
            {
                queue = new Queue<List<FuzzerEntry>>();
                _groupedQueues[key] = queue;
            }
            queue.Enqueue(group.ToList());
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicProcessor] Enqueued {group.Count()} entries for Magic {magicId} Group {group.Key}", _logger.ColorYellow);
        }
    }

    /// <summary>
    /// Updates the configuration reference.
    /// </summary>
    public void UpdateConfiguration(Config configuration)
    {
        _configuration = configuration;
    }

    // ============================================================
    // HOOK IMPLEMENTATIONS
    // ============================================================
    
    private long MagicFileProcessImpl(long a1, long a2, long a3, long a4)
    {
        // Reset trackers for new process call
        _opInstanceTracker.Clear();
        _propInstanceTracker.Clear();
        _lastOpType = -1;
        _pendingInjections.Clear();
        _activeInstanceEntries = null;
        _activeInstanceMagicId = 0;

        try
        {
            long result = _magicFileProcessHook!.OriginalFunction(a1, a2, a3, a4);

            // Process remaining injections at end of group
            if (_pendingInjections.Count > 0)
            {
                _isProcessingInjections = true;
                try
                {
                    foreach (var entry in _pendingInjections)
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] [INJECTOR] Injecting Op {entry.OpType} Prop {entry.PropertyId} AFTER Op {_lastOpType} (End of Group)", _logger.ColorGreen);
                        PerformInjection(a1, entry);
                    }
                    _pendingInjections.Clear();
                }
                finally
                {
                    _isProcessingInjections = false;
                }
            }

            // Inject end-of-group properties
            if (_activeInstanceEntries != null)
            {
                foreach (var entry in _activeInstanceEntries)
                {
                    if (entry.Enabled && entry.IsInjection && entry.InjectAfterOp == -1)
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] [INJECTOR] Injecting Op {entry.OpType} Prop {entry.PropertyId} at END of Group", _logger.ColorGreen);
                        PerformInjection(a1, entry);
                    }
                }
            }

            return result;
        }
        finally
        {
            _activeInstanceEntries = null;
            _activeInstanceMagicId = 0;
        }
    }

    private long MagicFileHandleSubEntryImpl(long a1, long a2, long a3, long a4)
    {
        int opType = (int)a2;
        CheckOpChange(a1, opType);
        return _magicFileHandleSubEntryHook!.OriginalFunction(a1, a2, a3, a4);
    }

    private long OperationFactoryImpl(long a1, int opType, long a3)
    {
        long result = _operationFactoryHook!.OriginalFunction(a1, opType, a3);
        
        if (result != 0)
        {
            long vtable = *(long*)result;
            if (!_operationNames.ContainsKey(vtable))
            {
                string name = $"Operation_{opType}";
                _operationNames[vtable] = name;
                _logger.WriteLine($"[{_modConfig.ModId}] [VTABLE_MAP] Mapped {name} to VTable 0x{vtable:X}", _logger.ColorGreen);
            }
        }
        
        return result;
    }

    private void MagicUnkExecuteImpl(long magicFileInstance, int opType, int propertyId, long dataPtr)
    {
        // Update VFX API factory context
        VfxApi.UpdateFactory(magicFileInstance);

        int fallbackId = _activeInstanceMagicId != 0 ? _activeInstanceMagicId : (_getCurrentlyCastingMagicId?.Invoke() ?? 0);
        var (magicId, groupId) = MagicReader.ResolveIds(magicFileInstance, fallbackId);

        // Activate queued entries
        if (_activeInstanceEntries == null && (magicId != 0 || groupId != 0))
        {
            var key = (magicId, groupId);
            if (_groupedQueues.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                _activeInstanceEntries = queue.Dequeue();
                _activeInstanceMagicId = magicId;
                _logger.WriteLine($"[{_modConfig.ModId}] [ACTIVATE] Linked {_activeInstanceEntries.Count} mods to Magic {magicId} Group {groupId}", _logger.ColorGreen);
            }
        }

        CheckOpChange(magicFileInstance, opType);

        // Track occurrences
        long propKey = ((long)opType << 32) | (uint)propertyId;
        int propOccurrence = _propInstanceTracker.GetValueOrDefault(propKey, 0);
        _propInstanceTracker[propKey] = propOccurrence + 1;

        int opOccurrence = _opInstanceTracker.GetValueOrDefault(opType, 0) - 1;
        if (opOccurrence < 0) opOccurrence = 0;
        
        // Get active entries and check if fuzzer is enabled
        var activeEntries = _activeInstanceEntries ?? _configuration.FuzzerEntries;
        bool fuzzerEnabled = _activeInstanceEntries != null || _configuration.EnableUniversalFuzzer;

        // Check for DisableOp
        if (fuzzerEnabled)
        {
            foreach (var entry in activeEntries)
            {
                if (entry.Enabled && entry.DisableOp && entry.OpType == opType)
                {
                    int targetOcc = (entry.PropertyId == -1) ? opOccurrence : propOccurrence;
                    if (EntryMatchesContext(entry, magicId, groupId, targetOcc) && (entry.PropertyId == -1 || entry.PropertyId == propertyId))
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {magicId} Group {groupId} Op {opType} Prop {propertyId} DISABLED (Occ {targetOcc})", _logger.ColorRed);
                        return;
                    }
                }
            }
        }

        long valuePtr = *(long*)(dataPtr + 8);

        // Apply fuzzer override
        var (isFuzzed, activeEntry, originalValue) = ApplyFuzzerOverride(
            activeEntries, fuzzerEnabled, opType, propertyId, 
            magicId, groupId, propOccurrence, valuePtr);

        // Log property value if enabled
        if (_configuration.EnablePropertyLogging)
        {
            LogPropertyValue(magicId, groupId, opType, propertyId, valuePtr);
        }

        // Execute original
        _magicUnkExecuteHook!.OriginalFunction(magicFileInstance, opType, propertyId, dataPtr);

        // Restore original value
        if (isFuzzed && activeEntry != null)
        {
            RestoreOriginalValue(activeEntry, valuePtr, originalValue);
        }
    }

    // ============================================================
    // INTERNAL HELPERS
    // ============================================================
    
    private void PerformInjection(long magicFileInstance, FuzzerEntry entry)
    {
        byte* buffer = stackalloc byte[16];
        long* fakeData = stackalloc long[2];
        fakeData[0] = 0;
        fakeData[1] = (long)buffer;

        if (entry.UseVec3)
            *(Vector3*)buffer = new Vector3(entry.Vec3X, entry.Vec3Y, entry.Vec3Z);
        else if (entry.UseFloat)
            *(float*)buffer = entry.FloatValue;
        else
            *(int*)buffer = entry.IntValue;

        MagicUnkExecuteImpl(magicFileInstance, entry.OpType, entry.PropertyId, (long)fakeData);
    }

    private void CheckOpChange(long magicFileInstance, int opType)
    {
        if (_isProcessingInjections) return;
        if (opType == _lastOpType) return;

        int fallbackId = _activeInstanceMagicId != 0 ? _activeInstanceMagicId : (_getCurrentlyCastingMagicId?.Invoke() ?? 0);
        var (magicId, groupId) = MagicReader.ResolveIds(magicFileInstance, fallbackId);
        
        // Process pending injections from previous operation
        if (_pendingInjections.Count > 0)
        {
            _isProcessingInjections = true;
            try
            {
                foreach (var entry in _pendingInjections)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [INJECTOR] Injecting Op {entry.OpType} Prop {entry.PropertyId} AFTER Op {_lastOpType} in Magic {magicId} Group {groupId}", _logger.ColorGreen);
                    PerformInjection(magicFileInstance, entry);
                }
                _pendingInjections.Clear();
            }
            finally
            {
                _isProcessingInjections = false;
            }
        }

        // Update state for new operation
        _lastOpType = opType;
        int currentOpOccurrence = _opInstanceTracker.GetValueOrDefault(opType, 0);
        _opInstanceTracker[opType] = currentOpOccurrence + 1;

        // Check for injections after this operation
        var activeEntries = _activeInstanceEntries ?? _configuration.FuzzerEntries;
        bool fuzzerEnabled = _activeInstanceEntries != null || _configuration.EnableUniversalFuzzer;

        if (fuzzerEnabled)
        {
            foreach (var entry in activeEntries)
            {
                if (entry.Enabled && entry.IsInjection && entry.InjectAfterOp == opType && EntryMatchesContext(entry, magicId, groupId, currentOpOccurrence))
                {
                    _pendingInjections.Add(entry);
                    _logger.WriteLine($"[{_modConfig.ModId}] [QUEUE_INJECT] Queued Op {entry.OpType} to inject after Op {opType} (Occ {currentOpOccurrence})", _logger.ColorBlue);
                }
            }
        }
    }

    private static bool EntryMatchesContext(FuzzerEntry entry, int magicId, int groupId, int occurrence)
    {
        if (entry.TargetMagicId != -1 && entry.TargetMagicId != magicId) return false;
        if (entry.TargetOperationGroupId != -1 && entry.TargetOperationGroupId != groupId) return false;
        if (entry.Occurrence != -1 && entry.Occurrence != occurrence) return false;
        return true;
    }

    private (bool isFuzzed, FuzzerEntry? entry, (float f, int i, Vector3 v) original) ApplyFuzzerOverride(
        List<FuzzerEntry> entries, bool enabled, int opType, int propertyId,
        int magicId, int groupId, int occurrence, long valuePtr)
    {
        if (!enabled) return (false, null, default);

        foreach (var entry in entries)
        {
            if (!entry.Enabled || entry.IsInjection || entry.DisableOp) continue;
            if (entry.PropertyId != propertyId) continue;
            if (entry.OpType != -1 && entry.OpType != opType) continue;
            if (!EntryMatchesContext(entry, magicId, groupId, occurrence)) continue;

            string contextStr = $"[Magic {magicId} Group {groupId}]";
            (float f, int i, Vector3 v) original = default;

            if (entry.UseVec3)
            {
                original.v = *(Vector3*)valuePtr;
                *(Vector3*)valuePtr = new Vector3(entry.Vec3X, entry.Vec3Y, entry.Vec3Z);
                _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {contextStr} Op {opType} Prop {propertyId} (Vec3) OVERRIDE: {original.v} -> {*(Vector3*)valuePtr} (Occ {occurrence})", _logger.ColorYellow);
            }
            else if (entry.UseFloat)
            {
                original.f = *(float*)valuePtr;
                *(float*)valuePtr = entry.FloatValue;
                _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {contextStr} Op {opType} Prop {propertyId} (Float) OVERRIDE: {original.f:F4} -> {entry.FloatValue:F4} (Occ {occurrence})", _logger.ColorYellow);
            }
            else
            {
                original.i = *(int*)valuePtr;
                *(int*)valuePtr = entry.IntValue;
                _logger.WriteLine($"[{_modConfig.ModId}] [FUZZER] {contextStr} Op {opType} Prop {propertyId} (Int) OVERRIDE: {original.i} -> {entry.IntValue} (Occ {occurrence})", _logger.ColorYellow);
            }
            return (true, entry, original);
        }
        return (false, null, default);
    }

    private void RestoreOriginalValue(FuzzerEntry entry, long valuePtr, (float f, int i, Vector3 v) original)
    {
        if (entry.UseVec3)
            *(Vector3*)valuePtr = original.v;
        else if (entry.UseFloat)
            *(float*)valuePtr = original.f;
        else
            *(int*)valuePtr = original.i;
    }

    private void LogPropertyValue(int magicId, int groupId, int opType, int propertyId, long valuePtr)
    {
        string contextStr = $"[Magic {magicId} Group {groupId}]";

        if (MagicProperties.Definitions.TryGetValue(propertyId, out var info))
        {
            string valStr = info.Type switch
            {
                MagicPropertyType.Float => $"float={*(float*)valuePtr:F4}",
                MagicPropertyType.Int => $"int={*(int*)valuePtr} (0x{*(int*)valuePtr:X})",
                MagicPropertyType.Bool => $"bool={(*(int*)valuePtr != 0)}",
                MagicPropertyType.Vec3Float => $"vec3<f>=({(*(Vector3*)valuePtr).X:F4}, {(*(Vector3*)valuePtr).Y:F4}, {(*(Vector3*)valuePtr).Z:F4})",
                MagicPropertyType.Vec3Int => $"vec3<i>=({((int*)valuePtr)[0]}, {((int*)valuePtr)[1]}, {((int*)valuePtr)[2]})",
                _ => "unknown"
            };
            _logger.WriteLine($"[{_modConfig.ModId}] [PROP_LOG] {contextStr} Op {opType} Prop {propertyId} ({info.Name}): {valStr}", _logger.ColorBlue);
        }
        else
        {
            float fVal = *(float*)valuePtr;
            int iVal = *(int*)valuePtr;
            var v = *(Vector3*)valuePtr;
            int* iVec = (int*)valuePtr;
            
            _logger.WriteLine($"[{_modConfig.ModId}] [PROP_LOG] {contextStr} Op {opType} Prop {propertyId} (UNKNOWN): " +
                $"int={iVal}, float={fVal:F4}, vec3<f>=({v.X:F4}, {v.Y:F4}, {v.Z:F4}), vec3<i>=({iVec[0]}, {iVec[1]}, {iVec[2]})", _logger.ColorYellow);
        }
    }
}
