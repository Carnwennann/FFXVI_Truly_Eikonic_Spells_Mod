using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Handles physics/knockback manipulation for hit reactions.
/// Hooks into the game's PhysicsUpdate function to modify SystemMove NEX data.
/// 
/// SystemMove NEX table confirmed columns:
/// +0x04: ForwardPush (positive = push away, negative = pull toward)
/// +0x08: ForwardDuration (0-1, 0 = stays in place, 1 = travels full distance)
/// +0x0C: VerticalPush (positive = up, negative = down)
/// +0x10: VerticalInterpolation (0-1, 0 = instant, 1 = very slow)
/// </summary>
public unsafe class PhysicsSystem
{
    // Function signatures
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate void PhysicsUpdateDelegate(long reactionHandler);
    
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long NexRowGetPtrDelegate(long nexRowInstance);
    
    // Hook and function pointer
    private IHook<PhysicsUpdateDelegate>? _physicsUpdateHook;
    private NexRowGetPtrDelegate? _nexRowGetPtrFunc;
    
    // Dependencies
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private Config _configuration;

    // Events
    public event Action? OnUpdate;
    
    // Game addresses (offsets from base)
    private const int PHYSICS_UPDATE_OFFSET = 0x24CC14;      // FUN_14024cc14
    private const int NEX_ROW_GET_PTR_OFFSET = 0xC01000;     // FUN_140c01000
    
    // Handler structure offsets
    private const int NEX_ROW_INSTANCE_INDEX = 0x0E;  // handler[0x0E] = NexRowInstance pointer
    
    public PhysicsSystem(ILogger logger, IModConfig modConfig, Config configuration)
    {
        _logger = logger;
        _modConfig = modConfig;
        _configuration = configuration;
    }
    
    /// <summary>
    /// Initialize hooks for physics manipulation
    /// </summary>
    public void Initialize(IReloadedHooks hooks, long baseAddress)
    {
        // Set up NexRowGetPtr as a function pointer (NOT a hook - it crashes if hooked)
        long nexRowGetPtrAddr = baseAddress + NEX_ROW_GET_PTR_OFFSET;
        _nexRowGetPtrFunc = hooks.CreateWrapper<NexRowGetPtrDelegate>(nexRowGetPtrAddr, out _);
        _logger.WriteLine($"[{_modConfig.ModId}] [PhysicsSystem] NexRowGetPtr function at 0x{nexRowGetPtrAddr:X}", _logger.ColorGreen);
        
        // Hook PhysicsUpdate
        long physicsUpdateAddr = baseAddress + PHYSICS_UPDATE_OFFSET;
        _physicsUpdateHook = hooks.CreateHook<PhysicsUpdateDelegate>(PhysicsUpdateImpl, physicsUpdateAddr).Activate();
        _logger.WriteLine($"[{_modConfig.ModId}] [PhysicsSystem] PhysicsUpdate hook at 0x{physicsUpdateAddr:X}", _logger.ColorGreen);
    }
    
    /// <summary>
    /// Update configuration reference
    /// </summary>
    public void UpdateConfiguration(Config configuration)
    {
        _configuration = configuration;
    }
    
    /// <summary>
    /// PhysicsUpdate hook implementation - modifies knockback physics parameters
    /// </summary>
    private void PhysicsUpdateImpl(long reactionHandler)
    {
        // Trigger update event for other systems
        OnUpdate?.Invoke();

        // Safety check
        if (_configuration == null || _physicsUpdateHook == null)
        {
            _physicsUpdateHook?.OriginalFunction(reactionHandler);
            return;
        }
        
        // Check if shadow hit physics is pending (always process this)
        bool hasShadowHitPhysics = _shadowHitPhysicsActive;
        
        // Skip if physics modification is disabled AND no shadow hit pending
        if (!_configuration.EnablePhysicsModification && !hasShadowHitPhysics)
        {
            _physicsUpdateHook.OriginalFunction(reactionHandler);
            return;
        }
        
        if (_configuration.EnablePhysicsModification || hasShadowHitPhysics)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] === PhysicsUpdate called! handler=0x{reactionHandler:X} (shadowHit={hasShadowHitPhysics}) ===", _logger.ColorGreen);
        }
        
        try
        {
            if (reactionHandler >= 0x10000 && reactionHandler <= 0x7FFFFFFFFFFF)
            {
                long* handlerArray = (long*)reactionHandler;
                
                // Dump handler structure for debugging (only if physics debug enabled)
                if (_configuration.EnablePhysicsModification)
                {
                    DumpHandlerStructure(handlerArray);
                }
                
                // Get NexRowInstance from handler[0x0E] (offset 0x70)
                long nexRowInstance = handlerArray[NEX_ROW_INSTANCE_INDEX];
                
                if (_configuration.EnablePhysicsModification)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] handler[0x0E] = 0x{nexRowInstance:X}", _logger.ColorGreen);
                }
                
                if (nexRowInstance != 0 && _nexRowGetPtrFunc != null)
                {
                    // Get pointer to NEX row data
                    if (_configuration.EnablePhysicsModification)
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Calling NexRowGetPtr(0x{nexRowInstance:X})...", _logger.ColorYellow);
                    }
                    long dataPtr = _nexRowGetPtrFunc(nexRowInstance);
                    
                    if (_configuration.EnablePhysicsModification)
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] NexRowGetPtr returned: 0x{dataPtr:X}", _logger.ColorGreen);
                    }
                    
                    if (dataPtr != 0)
                    {
                        // Dump all columns if enabled
                        if (_configuration.PhysicsDumpAllColumns)
                        {
                            DumpSystemMoveColumns(dataPtr);
                        }
                        
                        // Apply physics modifications (from config OR shadow hit)
                        bool modified = ApplyPhysicsOverrides(dataPtr);
                        
                        if (_configuration.EnablePhysicsModification)
                        {
                            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] === Calling Original Function ===", _logger.ColorGreen);
                        }
                        
                        // Call original function with modified values
                        _physicsUpdateHook.OriginalFunction(reactionHandler);
                        
                        // Restore original values after the function completes
                        if (modified)
                        {
                            RestoreOriginalValues(dataPtr);
                        }
                        
                        if (_configuration.EnablePhysicsModification)
                        {
                            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] === End PhysicsUpdate ===", _logger.ColorGreen);
                        }
                        return; // Already called original, exit early
                    }
                }
                else if (_nexRowGetPtrFunc == null)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] ERROR: _nexRowGetPtrFunc is NULL!", _logger.ColorRed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Error: {ex.Message}", _logger.ColorRed);
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] === End PhysicsUpdate ===", _logger.ColorGreen);
        
        // Call original function (fallback if no modification was done)
        _physicsUpdateHook.OriginalFunction(reactionHandler);
    }
    
    // Saved original values for restoration
    private float _orig_0x04, _orig_0x08, _orig_0x0C, _orig_0x10;
    private long _lastDataPtr;
    
    /// <summary>
    /// Apply physics overrides to the SystemMove NEX data
    /// Returns true if any modification was made
    /// </summary>
    private bool ApplyPhysicsOverrides(long dataPtr)
    {
        // Save original values BEFORE modification
        _orig_0x04 = *(float*)(dataPtr + 0x04);  // ForwardPush
        _orig_0x08 = *(float*)(dataPtr + 0x08);  // ForwardDuration
        _orig_0x0C = *(float*)(dataPtr + 0x0C);  // VerticalPush
        _orig_0x10 = *(float*)(dataPtr + 0x10);  // VerticalInterpolation
        _lastDataPtr = dataPtr;
        
        bool modified = false;
        
        // First priority: Shadow hit physics (one-shot, consumes the values)
        if (ConsumeShadowHitPhysics(out float shFwdPush, out float shFwdDur, out float shVertPush, out float shVertInterp))
        {
            *(float*)(dataPtr + 0x04) = shFwdPush;
            *(float*)(dataPtr + 0x08) = shFwdDur;
            *(float*)(dataPtr + 0x0C) = shVertPush;
            *(float*)(dataPtr + 0x10) = shVertInterp;
            
            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Shadow hit juggle applied! FwdPush={shFwdPush:F2}, FwdDur={shFwdDur:F2}, VertPush={shVertPush:F2}, VertInterp={shVertInterp:F2}", _logger.ColorBlue);
            return true; // Always restore after shadow hit
        }
        
        // Second priority: Config overrides (persistent)
        // +0x04: ForwardPush (horizontal knockback)
        if (_configuration.PhysicsForwardPushOverride != -999.0f)
        {
            *(float*)(dataPtr + 0x04) = _configuration.PhysicsForwardPushOverride;
            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] +0x04 ForwardPush: {_orig_0x04:F2} -> {_configuration.PhysicsForwardPushOverride:F2}", _logger.ColorGreen);
            modified = true;
        }
        
        // +0x08: ForwardDuration (0-1, how far character travels)
        if (_configuration.PhysicsForwardDurationOverride != -999.0f)
        {
            *(float*)(dataPtr + 0x08) = _configuration.PhysicsForwardDurationOverride;
            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] +0x08 ForwardDuration: {_orig_0x08:F2} -> {_configuration.PhysicsForwardDurationOverride:F2}", _logger.ColorGreen);
            modified = true;
        }
        
        // +0x0C: VerticalPush (vertical knockback)
        if (_configuration.PhysicsVerticalPushOverride != -999.0f)
        {
            *(float*)(dataPtr + 0x0C) = _configuration.PhysicsVerticalPushOverride;
            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] +0x0C VerticalPush: {_orig_0x0C:F2} -> {_configuration.PhysicsVerticalPushOverride:F2}", _logger.ColorGreen);
            modified = true;
        }
        
        // +0x10: VerticalInterpolation (0-1, speed of vertical movement)
        if (_configuration.PhysicsVerticalInterpolationOverride != -999.0f)
        {
            *(float*)(dataPtr + 0x10) = _configuration.PhysicsVerticalInterpolationOverride;
            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] +0x10 VerticalInterp: {_orig_0x10:F2} -> {_configuration.PhysicsVerticalInterpolationOverride:F2}", _logger.ColorGreen);
            modified = true;
        }
        
        return modified;
    }
    
    /// <summary>
    /// Restore original NEX values after the physics function completes
    /// </summary>
    private void RestoreOriginalValues(long dataPtr)
    {
        *(float*)(dataPtr + 0x04) = _orig_0x04;
        *(float*)(dataPtr + 0x08) = _orig_0x08;
        *(float*)(dataPtr + 0x0C) = _orig_0x0C;
        *(float*)(dataPtr + 0x10) = _orig_0x10;
        _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Restored original NEX values", _logger.ColorYellow);
    }
    
    // Shadow hit physics override values (set per-hit, applied during next PhysicsUpdate)
    private bool _shadowHitPhysicsActive = false;
    private float _shadowHitForwardPush;
    private float _shadowHitForwardDuration;
    private float _shadowHitVerticalPush;
    private float _shadowHitVerticalInterpolation;
    
    /// <summary>
    /// Set physics values for the next shadow hit
    /// These will be applied during the next PhysicsUpdate call
    /// </summary>
    public void ApplyShadowHitPhysics(float forwardPush, float forwardDuration, float verticalPush, float verticalInterpolation)
    {
        _shadowHitPhysicsActive = true;
        _shadowHitForwardPush = forwardPush;
        _shadowHitForwardDuration = forwardDuration;
        _shadowHitVerticalPush = verticalPush;
        _shadowHitVerticalInterpolation = verticalInterpolation;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Shadow hit physics set: FwdPush={forwardPush:F2}, FwdDur={forwardDuration:F2}, VertPush={verticalPush:F2}, VertInterp={verticalInterpolation:F2}", _logger.ColorBlue);
    }
    
    /// <summary>
    /// Check if shadow hit physics should be applied and return the values
    /// Clears the flag after returning
    /// </summary>
    public bool ConsumeShadowHitPhysics(out float forwardPush, out float forwardDuration, out float verticalPush, out float verticalInterpolation)
    {
        if (_shadowHitPhysicsActive)
        {
            forwardPush = _shadowHitForwardPush;
            forwardDuration = _shadowHitForwardDuration;
            verticalPush = _shadowHitVerticalPush;
            verticalInterpolation = _shadowHitVerticalInterpolation;
            _shadowHitPhysicsActive = false;
            return true;
        }
        
        forwardPush = forwardDuration = verticalPush = verticalInterpolation = 0;
        return false;
    }
    
    /// <summary>
    /// Dump handler structure for debugging
    /// </summary>
    private void DumpHandlerStructure(long* handlerArray)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS] Handler structure dump:", _logger.ColorYellow);
        for (int i = 0; i < 20; i++)
        {
            long val = handlerArray[i];
            bool looksLikePtr = val > 0x10000 && val < 0x7FFFFFFFFFFF;
            string ptrMark = looksLikePtr ? " <-- PTR?" : "";
            _logger.WriteLine($"[{_modConfig.ModId}] [PHYSICS]   [{i:D2}] +0x{i*8:X2}: 0x{val:X16}{ptrMark}", _logger.ColorYellow);
        }
    }
    
    /// <summary>
    /// Dump all SystemMove NEX columns with their names
    /// </summary>
    private void DumpSystemMoveColumns(long dataPtr)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [SYSTEMMOVE DUMP] === Full Column Dump (with names) ===", _logger.ColorYellow);
        
        // SystemMove column layout - based on NEX table structure analysis
        DumpColumn(dataPtr, 0x00, "Key", true);                    // INTEGER - Row ID
        DumpColumn(dataPtr, 0x04, "ForwardPush", false);           // REAL - Horizontal knockback
        DumpColumn(dataPtr, 0x08, "ForwardDuration", false);       // REAL - Movement duration
        DumpColumn(dataPtr, 0x0C, "VerticalPush", false);          // REAL - Vertical lift
        DumpColumn(dataPtr, 0x10, "VerticalInterpolation", false); // REAL - Vertical speed
        DumpColumn(dataPtr, 0x14, "Unk_0x14", false);              // REAL
        DumpColumn(dataPtr, 0x18, "FallGravity", false);           // REAL
        DumpColumn(dataPtr, 0x1C, "ForwardPush2", false);          // REAL
        DumpColumn(dataPtr, 0x20, "SlamDownForce", false);         // REAL - Negative for slam down
        DumpColumn(dataPtr, 0x24, "DownwardMomentum", false);      // REAL - Negative for slam down
        DumpColumn(dataPtr, 0x28, "DownwardMomentum2", false);     // REAL - Usually 0.2 (interpolation?)
        DumpColumn(dataPtr, 0x2C, "LaunchVelocity", false);        // REAL - 20.0 for launches
        DumpColumn(dataPtr, 0x30, "Unk12_RefID", true);            // INTEGER - Reference ID
        DumpColumn(dataPtr, 0x34, "Unk13_Extra", false);           // REAL
        DumpColumn(dataPtr, 0x4C, "Unk18_Flags", true);            // INTEGER - 256/257 flags
        DumpColumn(dataPtr, 0x50, "Unk19", true);                  // INTEGER
        DumpColumn(dataPtr, 0x54, "Unk20", true);                  // INTEGER
        DumpColumn(dataPtr, 0x58, "Unk21_Param1", false);          // REAL - Secondary params
        DumpColumn(dataPtr, 0x5C, "Unk22_Param2", false);          // REAL
        DumpColumn(dataPtr, 0x60, "Unk23_Param3", false);          // REAL
        DumpColumn(dataPtr, 0x64, "Unk24_Param4", false);          // REAL
        DumpColumn(dataPtr, 0x68, "Unk25", true);                  // INTEGER
        DumpColumn(dataPtr, 0x6C, "Unk26_SlamForce", false);       // REAL - 44.0 for back-down slam
        DumpColumn(dataPtr, 0x70, "Unk27", false);                 // REAL
        DumpColumn(dataPtr, 0x74, "NegGravity1", false);           // REAL - -15.0
        DumpColumn(dataPtr, 0x78, "NegGravity2", false);           // REAL - -20.0
        DumpColumn(dataPtr, 0x7C, "FinalInterp", false);           // REAL - Always 0.2
        DumpColumn(dataPtr, 0x80, "LaunchVelocity2", false);       // REAL - 20.0 for some launches
        
        _logger.WriteLine($"[{_modConfig.ModId}] [SYSTEMMOVE DUMP] === End Dump ===", _logger.ColorYellow);
    }
    
    /// <summary>
    /// Helper to dump a single column value
    /// </summary>
    private void DumpColumn(long dataPtr, int offset, string name, bool isInt)
    {
        int intVal = *(int*)(dataPtr + offset);
        float floatVal = *(float*)(dataPtr + offset);
        
        // Only log non-zero values
        if (intVal != 0)
        {
            if (isInt)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [SYSTEMMOVE] +0x{offset:X2} {name,-22}: {intVal,10} (0x{intVal:X8})", _logger.ColorGreen);
            }
            else
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [SYSTEMMOVE] +0x{offset:X2} {name,-22}: {floatVal,10:F4}", _logger.ColorGreen);
            }
        }
    }
}
