using System.Numerics;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.GameStructs;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// Low-level function hooks for entity management.
/// Captures singletons and provides access to game functions.
/// Based on FF16Framework's EntityManagerHooks.
/// </summary>
public unsafe class FunctionApi
{
    // ============================================================
    // STRUCTURES - See GameStructs/ActorStructs.cs for definitions:
    // NodePositionPair, ActorReference, StaticActorInfo, etc.
    // ============================================================
    
    // ============================================================
    // DELEGATES
    // ============================================================
    
    // Hook delegates (need to intercept to capture singletons)
    public delegate nint UnkSingletonPlayerOrCameraRelated_CtorDelegate(nint @this);
    public delegate nint StaticActorManager_GetOrCreateDelegate(nint @this, nint** outEntityInfo, uint entityId);
    public delegate ActorReference* ActorManager_SetupEntityDelegate(nint @this, nint entityPtr);
    public delegate void SetControlledActorDelegate(nint @this, nint staticActorInfo);
    
    // Function delegates (wrapper only, no hook needed)
    public delegate GameStructs.ActorReference* ActorManager_GetActorByKeyDelegate(nint @this, uint actorId);
    public delegate nint StaticActorInfo_IsValidActorDelegate(nint pStaticEntityInfo);
    public delegate GameStructs.NodePositionPair* StaticActorInfo_GetPositionDelegate(nint pStaticEntityInfo, GameStructs.NodePositionPair* outPair);
    public delegate Vector3* StaticActorInfo_GetRotationDelegate(nint pStaticEntityInfo, Vector3* outPair);
    public delegate Vector3* StaticActorInfo_GetForwardVectorDelegate(nint pStaticEntityInfo, Vector3* outPair);
    
    // ============================================================
    // HOOKS
    // ============================================================
    
    private IHook<StaticActorManager_GetOrCreateDelegate>? _staticActorManagerGetOrCreateHook;
    
    // ============================================================
    // FUNCTION WRAPPERS
    // ============================================================
    
    private StaticActorManager_GetOrCreateDelegate? _getOrCreateEntityFunc;
    private ActorManager_GetActorByKeyDelegate? _getActorByKeyFunc;
    private StaticActorInfo_IsValidActorDelegate? _isValidActorFunc;
    private StaticActorInfo_GetPositionDelegate? _getPositionFunc;
    private StaticActorInfo_GetRotationDelegate? _getRotationFunc;
    private StaticActorInfo_GetForwardVectorDelegate? _getForwardVectorFunc;
    
    // ============================================================
    // CAPTURED SINGLETONS & PLAYER INFO
    // ============================================================
    
    public nint UnkSingletonPlayerOrCameraRelated { get; set; }
    public nint StaticActorManager { get; set; }
    public nint ActorManager { get; set; }
    public nint PlayerStaticActorInfo { get; set; }
    
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    
    // Tracking physics for more accurate airborne detection
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, float> _npcVerticalPush = new();
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    /// <summary>
    /// Update the vertical push recorded for an NPC.
    /// This is used to improve airborne detection.
    /// </summary>
    public void UpdateNpcPhysics(long bnpcRow, float verticalPush)
    {
        _npcVerticalPush[bnpcRow] = verticalPush;
    }

    /// <summary>
    /// Returns true if all required singletons have been captured.
    /// </summary>
    public bool IsInitialized => UnkSingletonPlayerOrCameraRelated != 0 && StaticActorManager != 0 && ActorManager != 0;
    
    /// <summary>
    /// Returns true if position functions are available.
    /// </summary>
    public bool HasPositionFunctions => _getPositionFunc != null && _getRotationFunc != null && _getForwardVectorFunc != null;
    
    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public FunctionApi(ILogger logger, IModConfig modConfig)
    {
        _logger = logger;
        _modConfig = modConfig;
    }
    
    // ============================================================
    // INITIALIZATION
    // ============================================================
    
    /// <summary>
    /// Set up signature scans for all entity-related hooks.
    /// Patterns are from FF16Framework's RyoTune configuration.
    /// </summary>
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        // UnkSingletonPlayerOrCameraRelated_Ctor
        // This singleton is created early and contains player state at +0xC8
        /* scans.AddMainModuleScan("48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 41 54 41 55 41 56 41 57 48 8B EC 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 4C 8D 81", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find UnkSingletonPlayerOrCameraRelated_Ctor", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _unkSingletonCtorHook = hooks.CreateHook<UnkSingletonPlayerOrCameraRelated_CtorDelegate>(UnkSingletonCtorImpl, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Hooked UnkSingletonPlayerOrCameraRelated_Ctor at 0x{addr:X}", _logger.ColorGreen);
        }); */
        
        // StaticActorManager_GetOrCreate
        // HOOK to capture StaticActorManager singleton when called
        scans.AddMainModuleScan("48 89 5C 24 ?? 48 89 6C 24 ?? 44 89 44 24 ?? 56 57 41 54 41 56 41 57 48 83 EC ?? 45 33 E4", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find StaticActorManager_GetOrCreate", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _staticActorManagerGetOrCreateHook = hooks.CreateHook<StaticActorManager_GetOrCreateDelegate>(StaticActorManagerGetOrCreateImpl, addr).Activate();
            _getOrCreateEntityFunc = _staticActorManagerGetOrCreateHook.OriginalFunction;
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Hooked StaticActorManager_GetOrCreate at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // ActorManager_GetActorByKey
        scans.AddMainModuleScan("89 54 24 ?? 4C 8B D1 85 D2", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find ActorManager_GetActorByKey", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getActorByKeyFunc = hooks.CreateWrapper<ActorManager_GetActorByKeyDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Found ActorManager_GetActorByKey at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorInfo_IsValidActor - wrapper only
        scans.AddMainModuleScan("48 83 EC ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? 8B 40", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find StaticActorInfo_IsValidActor", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _isValidActorFunc = hooks.CreateWrapper<StaticActorInfo_IsValidActorDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Found StaticActorInfo_IsValidActor at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorInfo_GetPosition - wrapper only
        scans.AddMainModuleScan("40 53 48 83 EC ?? 48 8B DA E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B D3 48 8B C8 E8 ?? ?? ?? ?? EB ?? 48 83 63", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find StaticActorInfo_GetPosition", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getPositionFunc = hooks.CreateWrapper<StaticActorInfo_GetPositionDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Found StaticActorInfo_GetPosition at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorInfo_GetRotation - wrapper only
        scans.AddMainModuleScan("40 53 48 83 EC ?? 48 8B DA E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 40", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find StaticActorInfo_GetRotation", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getRotationFunc = hooks.CreateWrapper<StaticActorInfo_GetRotationDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Found StaticActorInfo_GetRotation at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorInfo_GetForwardVector - wrapper only
        scans.AddMainModuleScan("40 53 48 83 EC ?? 48 8B DA E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 50", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find StaticActorInfo_GetForwardVector", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getForwardVectorFunc = hooks.CreateWrapper<StaticActorInfo_GetForwardVectorDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Found StaticActorInfo_GetForwardVector at 0x{addr:X}", _logger.ColorGreen);
        });
    }
    
    // ============================================================
    // UTILITIES
    // ============================================================
    
    private nint GetAddressFromResult(int offset)
    {
        return System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress + offset;
    }
    
    // ============================================================
    // HOOK IMPLEMENTATIONS
    // ============================================================
    
    /// <summary>
    /// Hook implementation for StaticActorManager_GetOrCreate.
    /// Captures the StaticActorManager singleton pointer.
    /// </summary>
    private nint StaticActorManagerGetOrCreateImpl(nint @this, nint** outEntityInfo, uint entityId)
    {
        // Capture the StaticActorManager singleton
        if (StaticActorManager == 0)
        {
            StaticActorManager = @this;
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Captured StaticActorManager: 0x{@this:X}", _logger.ColorGreen);
        }
        
        // Call original function
        return _staticActorManagerGetOrCreateHook!.OriginalFunction(@this, outEntityInfo, entityId);
    }
    
    /// <summary>
    /// Actively retrieves Clive's StaticActorInfo using Nenkai's FaithFramework logic.
    /// Uses UnkSingleton (+0xC8) and StaticActorManager_GetOrCreate.
    /// </summary>
    public nint GetPlayerStaticActorInfo()
    {
        unsafe
        {
            // UnkSingleton can be read from global offset (it's written there)
            nint baseAddress = System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress;
            if (UnkSingletonPlayerOrCameraRelated == 0)
            {
                UnkSingletonPlayerOrCameraRelated = *(nint*)(baseAddress + GlobalOffsets.UnkSingletonPlayerOrCamera);
            }
            
            // StaticActorManager is captured via HOOK (not available from global offset)
            // If we don't have it yet, we can't resolve actor info
            if (UnkSingletonPlayerOrCameraRelated == 0 || StaticActorManager == 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Waiting for singletons. UnkSingleton=0x{UnkSingletonPlayerOrCameraRelated:X}, StaticActorMgr=0x{StaticActorManager:X}", _logger.ColorYellow);
                return PlayerStaticActorInfo;
            }

            // Extract the Current Controlling Actor ID (usually Clive)
            if (UnkSingletonPlayerOrCameraRelated < 0x10000 || UnkSingletonPlayerOrCameraRelated > 0x00007FFFFFFFFFFF)
            {
                return PlayerStaticActorInfo;
            }

            uint currentActorId = *(uint*)(UnkSingletonPlayerOrCameraRelated + UnkSingletonOffsets.CurrentActorId);
            if (currentActorId == 0) 
            {
                return PlayerStaticActorInfo;
            }

            // Resolve via StaticActorManager
            nint resolved = GetStaticActorInfo(currentActorId);
            if (resolved != 0)
            {
                PlayerStaticActorInfo = resolved;
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Resolved Clive StaticActorInfo: 0x{resolved:X} (ActorId: 0x{currentActorId:X})", _logger.ColorGreen);
            }

            return PlayerStaticActorInfo;
        }
    }
    
    // ============================================================
    // LOW-LEVEL API (for PlayerSystem and other systems)
    // ============================================================
    
    /// <summary>
    /// Get actor reference by actor ID.
    /// </summary>
    public GameStructs.ActorReference* GetActorByKey(uint actorId)
    {
        if (ActorManager == 0 || _getActorByKeyFunc == null)
            return null;
        
        return _getActorByKeyFunc(ActorManager, actorId);
    }
    
    /// <summary>
    /// Get static actor info pointer by actor ID.
    /// </summary>
    public nint GetStaticActorInfo(uint actorId)
    {
        if (StaticActorManager == 0 || _getOrCreateEntityFunc == null)
            return 0;
        
        unsafe
        {
            nint* staticActorInfo = null;
            _getOrCreateEntityFunc(StaticActorManager, &staticActorInfo, actorId);
            
            if (staticActorInfo == null)
                return 0;
            
            return (nint)staticActorInfo;
        }
    }
    
    /// <summary>
    /// Check if a static actor info is valid.
    /// </summary>
    public bool IsValidActor(nint staticActorInfo)
    {
        if (_isValidActorFunc == null || staticActorInfo == 0)
            return false;
        
        return _isValidActorFunc(staticActorInfo) != 0;
    }
    
    /// <summary>
    /// Get position from static actor info.
    /// </summary>
    public GameStructs.NodePositionPair* GetPosition(nint staticActorInfo, GameStructs.NodePositionPair* outPair)
    {
        if (_getPositionFunc == null || staticActorInfo == 0)
            return null;
        
        return _getPositionFunc(staticActorInfo, outPair);
    }
    
    /// <summary>
    /// Get rotation from static actor info.
    /// </summary>
    public Vector3* GetRotation(nint staticActorInfo, Vector3* outRotation)
    {
        if (_getRotationFunc == null || staticActorInfo == 0)
            return null;
        
        return _getRotationFunc(staticActorInfo, outRotation);
    }
    
    /// <summary>
    /// Get forward vector from static actor info.
    /// </summary>
    public Vector3* GetForwardVector(nint staticActorInfo, Vector3* outForward)
    {
        if (_getForwardVectorFunc == null || staticActorInfo == 0)
            return null;
        
        return _getForwardVectorFunc(staticActorInfo, outForward);
    }

    /// <summary>
    /// Detects if an entity is currently airborne (not on the ground).
    /// Uses reverse-engineered offsets from state check logic.
    /// </summary>
    /// <param name="bnpcRow">Pointer to the NpcBaseEntity row.</param>
    public unsafe bool IsAirborne(long bnpcRow)
    {
        if (bnpcRow < 0x10000 || bnpcRow > 0x00007FFFFFFFFFFF) return false;

        try
        {
            GameStructs.StaticActorInfo* info = (GameStructs.StaticActorInfo*)*(long*)(bnpcRow + BnpcRowOffsets.StaticActorInfoPtr); // Wrapper
            
            long actorPtr = 0;
            if (info != null && (long)info > 0x10000)
            {
                // In FaithFramework, the reference to the internal game Actor object is at +0x58
                actorPtr = info->ActorRef;
            }
            
            // Fallback: Si info es null o no tiene ActorRef, intentamos leer bnpcRow + 0 directamente (método antiguo)
            if (actorPtr == 0) 
            {
                actorPtr = *(long*)bnpcRow;
            }

            if (actorPtr > 0x10000 && actorPtr < 0x00007FFFFFFFFFFF)
            {
                // ReactionState (Byte):
                // 0x02 = Ground / Neutral
                // 0x03-0x05 = Ground reactions (Step Back/Slide)
                // > 0x05 = Airborne / Launch reaction (0x67, 0xC0, etc)
                byte reactionState = *(byte*)(actorPtr + ActorOffsets.ReactionState);
                if (reactionState > 5) return true;
                
                // Si el ID es 0x02, pero tenemos registro de que ha sido lanzado verticalmente recientemente,
                // mantenemos el estado de aire hasta que el juego lo resetee.
                if (reactionState == 2)
                {
                    if (_npcVerticalPush.TryGetValue(bnpcRow, out float push) && push > 0.1f)
                    {
                        // Nota: El juego suele resetear +0x158 a 2 cuando toca el suelo o termina la reacción.
                        _npcVerticalPush.TryRemove(bnpcRow, out _);
                        return false; 
                    }
                    return false;
                }
            }
/*
            // MÉTODO 2: BattleBehavior (Desactivado temporalmente hasta confirmar offset RAW)
            if (info != null && (long)info > 0x10000) {
                 // El raw dump mostró ceros en +0x30, así que este path puede fallar si no usamos +0x58
                 // ...
            }
*/
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DEBUG-AIR] Error: {ex.Message}", _logger.ColorRed);
        }
        return false;
    }
}
