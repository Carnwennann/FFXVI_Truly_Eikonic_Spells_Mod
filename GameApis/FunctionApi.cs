using System.Numerics;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// Low-level function hooks for entity management.
/// Captures singletons and provides access to game functions.
/// Based on FF16Framework's EntityManagerHooks.
/// </summary>
public unsafe class FunctionApi
{
    // ============================================================
    // STRUCTURES (from FF16Framework)
    // ============================================================
    
    [StructLayout(LayoutKind.Sequential)]
    public struct NodePositionPair
    {
        public nint vtable;
        public nint ParentNode;  // Node*
        public Vector3 Position;
        public int dword1C;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct ActorReference
    {
        public nint __vftable;
        public uint ActorId;
        public uint EntityID;
        public nint Node;
        public nint Node2;
        public nint field_20;
        public nint field_28;
        public nint field_30;
        public nint field_38;
        public int UnkCounterIndex;
        public int Flags;
        public nint HasTypeBitset;
        public nint HasTypeBitset2;
        public nint field_58;
        public nint ListEntryByListTypeAndActorId;
        public nint field_68;
        public nint g_off;
        public nint field_78;
        public Vector3 UnkVec;
        public int field_8C;
    }
    
    // ============================================================
    // DELEGATES
    // ============================================================
    
    // Hook delegates (need to intercept to capture singletons)
    public delegate nint UnkSingletonPlayerOrCameraRelated_CtorDelegate(nint @this);
    public delegate nint StaticActorManager_GetOrCreateDelegate(nint @this, nint** outEntityInfo, uint entityId);
    public delegate ActorReference* ActorManager_SetupEntityDelegate(nint @this, nint entityPtr);
    
    // Function delegates (wrapper only, no hook needed)
    public delegate ActorReference* ActorManager_GetActorByKeyDelegate(nint @this, uint actorId);
    public delegate nint StaticActorInfo_IsValidActorDelegate(nint pStaticEntityInfo);
    public delegate NodePositionPair* StaticActorInfo_GetPositionDelegate(nint pStaticEntityInfo, NodePositionPair* outPair);
    public delegate Vector3* StaticActorInfo_GetRotationDelegate(nint pStaticEntityInfo, Vector3* outPair);
    public delegate Vector3* StaticActorInfo_GetForwardVectorDelegate(nint pStaticEntityInfo, Vector3* outPair);
    
    // ============================================================
    // HOOKS
    // ============================================================
    
    private IHook<UnkSingletonPlayerOrCameraRelated_CtorDelegate>? _unkSingletonCtorHook;
    private IHook<StaticActorManager_GetOrCreateDelegate>? _staticActorManagerGetOrCreateHook;
    private IHook<ActorManager_SetupEntityDelegate>? _actorManagerSetupEntityHook;
    
    // ============================================================
    // FUNCTION WRAPPERS
    // ============================================================
    
    private ActorManager_GetActorByKeyDelegate? _getActorByKeyFunc;
    private StaticActorInfo_IsValidActorDelegate? _isValidActorFunc;
    private StaticActorInfo_GetPositionDelegate? _getPositionFunc;
    private StaticActorInfo_GetRotationDelegate? _getRotationFunc;
    private StaticActorInfo_GetForwardVectorDelegate? _getForwardVectorFunc;
    
    // ============================================================
    // CAPTURED SINGLETONS
    // ============================================================
    
    public nint UnkSingletonPlayerOrCameraRelated { get; private set; }
    public nint StaticActorManager { get; private set; }
    public nint ActorManager { get; private set; }
    
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
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
        scans.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC ?? 48 8B F9 E8 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 07 33 C0", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find UnkSingletonPlayerOrCameraRelated_Ctor", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _unkSingletonCtorHook = hooks.CreateHook<UnkSingletonPlayerOrCameraRelated_CtorDelegate>(UnkSingletonCtorImpl, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Hooked UnkSingletonPlayerOrCameraRelated_Ctor at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorManager_GetOrCreate
        // Used to get static actor info by entity ID
        scans.AddMainModuleScan("48 89 5C 24 ?? 48 89 6C 24 ?? 44 89 44 24 ?? 56 57 41 54 41 56 41 57 48 83 EC ?? 45 33 E4", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find StaticActorManager_GetOrCreate", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _staticActorManagerGetOrCreateHook = hooks.CreateHook<StaticActorManager_GetOrCreateDelegate>(StaticActorManagerGetOrCreateImpl, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Hooked StaticActorManager_GetOrCreate at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // ActorManager_SetupEntity - captures ActorManager
        scans.AddMainModuleScan("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 48 8B F2 48 8B F9 E8 ?? ?? ?? ?? 48 85 C0", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find ActorManager_SetupEntity", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _actorManagerSetupEntityHook = hooks.CreateHook<ActorManager_SetupEntityDelegate>(ActorManagerSetupEntityImpl, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Hooked ActorManager_SetupEntity at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // ActorManager_GetActorByKey - wrapper only
        scans.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC ?? 8B FA 48 8B D9 E8 ?? ?? ?? ?? 48 85 C0 74 ?? 8B D7", result =>
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
        scans.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC ?? 48 8B DA 48 8B F9 E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 48", result =>
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
        scans.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC ?? 48 8B DA 48 8B F9 E8 ?? ?? ?? ?? 48 85 C0 74 ?? F3 0F 10 40 ?? F3 0F 10 48 ?? F3 0F 11 03", result =>
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
        scans.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC ?? 48 8B DA 48 8B F9 E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 48 ?? E8", result =>
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
    
    private nint GetAddressFromResult(int offset)
    {
        return System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress + offset;
    }
    
    // ============================================================
    // HOOK IMPLEMENTATIONS
    // ============================================================
    
    private nint UnkSingletonCtorImpl(nint @this)
    {
        UnkSingletonPlayerOrCameraRelated = @this;
        _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Captured UnkSingletonPlayerOrCameraRelated: 0x{@this:X}", _logger.ColorGreen);
        return _unkSingletonCtorHook!.OriginalFunction(@this);
    }
    
    private nint StaticActorManagerGetOrCreateImpl(nint @this, nint** outEntityInfo, uint entityId)
    {
        StaticActorManager = @this;
        return _staticActorManagerGetOrCreateHook!.OriginalFunction(@this, outEntityInfo, entityId);
    }
    
    private ActorReference* ActorManagerSetupEntityImpl(nint @this, nint entityPtr)
    {
        ActorManager = @this;
        return _actorManagerSetupEntityHook!.OriginalFunction(@this, entityPtr);
    }
    
    // ============================================================
    // LOW-LEVEL API (for PlayerSystem and other systems)
    // ============================================================
    
    /// <summary>
    /// Get actor reference by actor ID.
    /// </summary>
    public ActorReference* GetActorByKey(uint actorId)
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
        if (StaticActorManager == 0 || _staticActorManagerGetOrCreateHook == null)
            return 0;
        
        nint* staticActorInfo = null;
        _staticActorManagerGetOrCreateHook.OriginalFunction(StaticActorManager, &staticActorInfo, actorId);
        
        if (staticActorInfo == null)
            return 0;
        
        return *staticActorInfo;
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
    public NodePositionPair* GetPosition(nint staticActorInfo, NodePositionPair* outPair)
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
    /// Uses reverse-engineered offsets from TriggerReactionHit.
    /// </summary>
    /// <param name="entityPtr">Pointer to the NpcBaseEntity (bnpcRow).</param>
    public unsafe bool IsAirborne(long bnpcRow)
    {
        if (bnpcRow < 0x10000 || bnpcRow > 0x00007FFFFFFFFFFF) return false;

        try
        {
            long v9 = *(long*)(bnpcRow + 0x20);
            long actorPtr = *(long*)bnpcRow;

            if (actorPtr > 0x10000 && actorPtr < 0x00007FFFFFFFFFFF)
            {
                // Offset +0x158 detectado mediante ingeniería inversa de memoria:
                // 0x02 = Suelo / Neutral
                // > 0x02 (0x67, 0xC0, etc) = Aire / Reacción de impacto
                byte reactionState = *(byte*)(actorPtr + 0x158);
                bool airborne = (reactionState > 2);

                return airborne;
            }

            if (v9 > 0x10000) {
                 // Backup: Intentar vía StateList (Wrapper + 0x200) como vimos en IDA
                 long stateListPtr = *(long*)(v9 + 0x200);
                 if (stateListPtr > 0x10000) {
                     uint f234 = *(uint*)(stateListPtr + 0x234);
                     if (f234 != 0) return true;
                 }
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [DEBUG-AIR] Could not find Entry from Row 0x{bnpcRow:X}", _logger.ColorRed);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [DEBUG-AIR] Error: {ex.Message}", _logger.ColorRed);
        }
        return false;
    }
}
