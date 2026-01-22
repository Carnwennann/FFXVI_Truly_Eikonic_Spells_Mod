using System.Numerics;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.GameStructs;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// Unified Entity API for actor and player management.
/// Based on FF16Framework's EntityManagerHooks pattern.
/// 
/// This consolidates FunctionApi and PlayerApi into a single cohesive API.
/// </summary>
public unsafe class EntityApi
{
    // ============================================================
    // DELEGATES (matching FaithFramework patterns)
    // ============================================================
    
    // Hook delegates (to capture singletons)
    public delegate nint UnkSingletonPlayerOrCameraRelated_CtorDelegate(nint @this);
    public delegate nint StaticActorManager_GetOrCreateDelegate(nint @this, nint** outEntityInfo, uint entityId);
    public delegate ActorReference* ActorManager_SetupEntityDelegate(ActorManager* @this, nint entityPtr);
    
    // Function delegates (wrappers)
    public delegate ActorReference* ActorManager_GetActorByKeyDelegate(ActorManager* @this, uint actorId);
    public delegate nint StaticActorInfo_IsValidActorDelegate(nint pStaticEntityInfo);
    public delegate NodePositionPair* StaticActorInfo_GetPositionDelegate(nint pStaticEntityInfo, NodePositionPair* outPair);
    public delegate Vector3* StaticActorInfo_GetRotationDelegate(nint pStaticEntityInfo, Vector3* outPair);
    public delegate Vector3* StaticActorInfo_GetForwardVectorDelegate(nint pStaticEntityInfo, Vector3* outPair);
    
    // World position delegate (converts Node-relative position to world coordinates)
    public delegate nint NodePositionPair_ComputeWorldPositionDelegate(NodePositionPair* @this, Vector3* outVec);
    
    // Targeting delegates (from UnkList35Hooks)
    public delegate nint UnkSingletonPlayer_GetList35EntryDelegate(nint @this);
    public delegate TargetStruct* UnkList35Entry_GetCurrentTargettedEnemyDelegate(nint @this, byte forceUnk);
    
    // ============================================================
    // HOOKS
    // ============================================================
    
    private IHook<StaticActorManager_GetOrCreateDelegate>? _staticActorManagerGetOrCreateHook;
    private IHook<UnkSingletonPlayerOrCameraRelated_CtorDelegate>? _unkSingletonCtorHook;
    private IHook<ActorManager_SetupEntityDelegate>? _actorManagerSetupEntityHook;
    
    // ============================================================
    // FUNCTION WRAPPERS
    // ============================================================
    
    private StaticActorManager_GetOrCreateDelegate? _getOrCreateEntityFunc;
    private ActorManager_GetActorByKeyDelegate? _getActorByKeyFunc;
    private StaticActorInfo_IsValidActorDelegate? _isValidActorFunc;
    private StaticActorInfo_GetPositionDelegate? _getPositionFunc;
    private StaticActorInfo_GetRotationDelegate? _getRotationFunc;
    private StaticActorInfo_GetForwardVectorDelegate? _getForwardVectorFunc;
    private UnkSingletonPlayer_GetList35EntryDelegate? _getList35EntryFunc;
    private UnkList35Entry_GetCurrentTargettedEnemyDelegate? _getCurrentTargetFunc;
    private NodePositionPair_ComputeWorldPositionDelegate? _computeWorldPositionFunc;
    
    // ============================================================
    // SINGLETONS (captured at runtime)
    // ============================================================
    
    /// <summary>
    /// Player/Camera singleton. Contains current actor ID at +0xC8.
    /// </summary>
    public nint UnkSingletonPlayerOrCameraRelated { get; private set; }
    
    /// <summary>
    /// Static actor manager. Used to get StaticActorInfo by actor ID.
    /// </summary>
    public nint StaticActorManager { get; private set; }
    
    /// <summary>
    /// Actor manager. Contains actor references and entity lists.
    /// </summary>
    public ActorManager* ActorManager { get; private set; }
    
    /// <summary>
    /// Cached player (Clive) StaticActorInfo pointer.
    /// </summary>
    public nint PlayerStaticActorInfo { get; private set; }
    
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private readonly long _baseAddress;
    
    // Physics tracking for airborne detection
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, float> _npcVerticalPush = new();
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    /// <summary>
    /// Returns true if all required singletons have been captured.
    /// </summary>
    public bool IsInitialized => 
        UnkSingletonPlayerOrCameraRelated != 0 && 
        StaticActorManager != 0 && 
        ActorManager != null;
    
    /// <summary>
    /// Returns true if position/rotation functions are available.
    /// </summary>
    public bool HasPositionFunctions => 
        _getPositionFunc != null && 
        _getRotationFunc != null && 
        _getForwardVectorFunc != null;
    
    /// <summary>
    /// Returns true if targeting functions are available.
    /// </summary>
    public bool HasTargetingFunctions =>
        _getList35EntryFunc != null &&
        _getCurrentTargetFunc != null;
    
    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public EntityApi(ILogger logger, IModConfig modConfig)
    {
        _logger = logger;
        _modConfig = modConfig;
        _baseAddress = System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress;
    }
    
    // ============================================================
    // INITIALIZATION
    // ============================================================
    
    /// <summary>
    /// Set up signature scans for all entity-related functions.
    /// Patterns are from FF16Framework's RyoTune configuration.
    /// </summary>
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        // UnkSingletonPlayerOrCameraRelated_Ctor (captures player singleton)
        scans.AddMainModuleScan("48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 41 54 41 55 41 56 41 57 48 8B EC 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 4C 8D 81", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find UnkSingletonPlayerOrCameraRelated_Ctor", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _unkSingletonCtorHook = hooks.CreateHook<UnkSingletonPlayerOrCameraRelated_CtorDelegate>(UnkSingletonCtorImpl, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Hooked UnkSingletonPlayerOrCameraRelated_Ctor at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorManager_GetOrCreate (captures StaticActorManager)
        scans.AddMainModuleScan("48 89 5C 24 ?? 48 89 6C 24 ?? 44 89 44 24 ?? 56 57 41 54 41 56 41 57 48 83 EC ?? 45 33 E4", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find StaticActorManager_GetOrCreate", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _staticActorManagerGetOrCreateHook = hooks.CreateHook<StaticActorManager_GetOrCreateDelegate>(StaticActorManagerGetOrCreateImpl, addr).Activate();
            _getOrCreateEntityFunc = _staticActorManagerGetOrCreateHook.OriginalFunction;
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Hooked StaticActorManager_GetOrCreate at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // ActorManager_SetupEntity (captures ActorManager)
        scans.AddMainModuleScan("48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8B EC 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 4C 8B F9 48 8B F2", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find ActorManager_SetupEntity", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _actorManagerSetupEntityHook = hooks.CreateHook<ActorManager_SetupEntityDelegate>(ActorManagerSetupEntityImpl, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Hooked ActorManager_SetupEntity at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // ActorManager_GetActorByKey - WRAPPER only
        scans.AddMainModuleScan("89 54 24 ?? 4C 8B D1 85 D2", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find ActorManager_GetActorByKey", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getActorByKeyFunc = hooks.CreateWrapper<ActorManager_GetActorByKeyDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Found ActorManager_GetActorByKey at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorInfo_IsValidActor
        scans.AddMainModuleScan("48 83 EC ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? 8B 40", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find StaticActorInfo_IsValidActor", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _isValidActorFunc = hooks.CreateWrapper<StaticActorInfo_IsValidActorDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Found StaticActorInfo_IsValidActor at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorInfo_GetPosition
        scans.AddMainModuleScan("40 53 48 83 EC ?? 48 8B DA E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B D3 48 8B C8 E8 ?? ?? ?? ?? EB ?? 48 83 63", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find StaticActorInfo_GetPosition", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getPositionFunc = hooks.CreateWrapper<StaticActorInfo_GetPositionDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Found StaticActorInfo_GetPosition at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorInfo_GetRotation
        scans.AddMainModuleScan("40 53 48 83 EC ?? 48 8B DA E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 40", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find StaticActorInfo_GetRotation", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getRotationFunc = hooks.CreateWrapper<StaticActorInfo_GetRotationDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Found StaticActorInfo_GetRotation at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // StaticActorInfo_GetForwardVector
        scans.AddMainModuleScan("40 53 48 83 EC ?? 48 8B DA E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 50", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find StaticActorInfo_GetForwardVector", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getForwardVectorFunc = hooks.CreateWrapper<StaticActorInfo_GetForwardVectorDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Found StaticActorInfo_GetForwardVector at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // UnkSingletonPlayer_GetList35Entry (for targeting)
        // Pattern from FF16Framework UnkList35Hooks
        scans.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC ?? 44 8B 81 ?? ?? ?? ?? 48 8D 54 24 ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 4C 24 ?? E8 ?? ?? ?? ?? 48 8B 5C 24 ?? 84 C0 74 ?? 48 8B CB E8 ?? ?? ?? ?? EB", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find UnkSingletonPlayer_GetList35Entry", _logger.ColorYellow);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getList35EntryFunc = hooks.CreateWrapper<UnkSingletonPlayer_GetList35EntryDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Found UnkSingletonPlayer_GetList35Entry at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // UnkList35Entry_GetCurrentTargettedEnemy
        scans.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC ?? 48 8B D9 40 8A FA 48 8B 89 ?? ?? ?? ?? 48 85 C9 0F 84", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find UnkList35Entry_GetCurrentTargettedEnemy", _logger.ColorYellow);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getCurrentTargetFunc = hooks.CreateWrapper<UnkList35Entry_GetCurrentTargettedEnemyDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Found UnkList35Entry_GetCurrentTargettedEnemy at 0x{addr:X}", _logger.ColorGreen);
        });
        
        // NodePositionPair_ComputeWorldPosition - converts Node-relative position to world coordinates
        scans.AddMainModuleScan("48 8B C4 48 89 58 ?? 48 89 78 ?? 55 48 8D 68 ?? 48 81 EC ?? ?? ?? ?? C5 F8 29 70 ?? C5 F8 29 78 ?? C5 78 29 40 ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 48 8B F9 48 8B DA 48 8B 49", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] FAILED to find NodePositionPair_ComputeWorldPosition", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _computeWorldPositionFunc = hooks.CreateWrapper<NodePositionPair_ComputeWorldPositionDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Found NodePositionPair_ComputeWorldPosition at 0x{addr:X}", _logger.ColorGreen);
        });
    }
    
    // ============================================================
    // UTILITIES
    // ============================================================
    
    private nint GetAddressFromResult(int offset) => (nint)(_baseAddress + offset);
    
    // ============================================================
    // HOOK IMPLEMENTATIONS
    // ============================================================
    
    private nint UnkSingletonCtorImpl(nint @this)
    {
        UnkSingletonPlayerOrCameraRelated = @this;
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Captured UnkSingletonPlayerOrCameraRelated: 0x{@this:X}", _logger.ColorGreen);
        return _unkSingletonCtorHook!.OriginalFunction(@this);
    }
    
    private nint StaticActorManagerGetOrCreateImpl(nint @this, nint** outEntityInfo, uint entityId)
    {
        if (StaticActorManager == 0)
        {
            StaticActorManager = @this;
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Captured StaticActorManager: 0x{@this:X}", _logger.ColorGreen);
        }
        return _staticActorManagerGetOrCreateHook!.OriginalFunction(@this, outEntityInfo, entityId);
    }
    
    private ActorReference* ActorManagerSetupEntityImpl(ActorManager* @this, nint entityPtr)
    {
        if (ActorManager == null)
        {
            ActorManager = @this;
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] Captured ActorManager: 0x{(nint)@this:X}", _logger.ColorGreen);
        }
        return _actorManagerSetupEntityHook!.OriginalFunction(@this, entityPtr);
    }
    
    /// <summary>
    /// Set ActorManager from external source (e.g., FF16Framework).
    /// </summary>
    public void SetActorManager(ActorManager* actorManager)
    {
        if (ActorManager == null && actorManager != null)
        {
            ActorManager = actorManager;
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] ActorManager set externally: 0x{(nint)actorManager:X}", _logger.ColorGreen);
        }
    }
    
    // ============================================================
    // PLAYER API
    // ============================================================
    
    /// <summary>
    /// Get current player's actor ID from the singleton.
    /// </summary>
    public uint GetPlayerActorId()
    {
        // Try to read from global offset if singleton not captured yet
        if (UnkSingletonPlayerOrCameraRelated == 0)
        {
            UnkSingletonPlayerOrCameraRelated = *(nint*)(_baseAddress + GlobalOffsets.UnkSingletonPlayerOrCamera);
        }
        
        if (UnkSingletonPlayerOrCameraRelated == 0)
            return 0;
        
        return *(uint*)(UnkSingletonPlayerOrCameraRelated + UnkSingletonOffsets.CurrentActorId);
    }
    
    /// <summary>
    /// Get current player's entity ID.
    /// </summary>
    public uint GetPlayerEntityId()
    {
        var actorRef = GetPlayerActorReference();
        return actorRef != null ? actorRef->EntityID : 0;
    }
    
    /// <summary>
    /// Get the ActorReference pointer for the player.
    /// </summary>
    public ActorReference* GetPlayerActorReference()
    {
        uint actorId = GetPlayerActorId();
        return actorId != 0 ? GetActorByKey(actorId) : null;
    }
    
    /// <summary>
    /// Get the static actor info pointer for the player.
    /// </summary>
    public nint GetPlayerStaticActorInfo()
    {
        uint actorId = GetPlayerActorId();
        
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] GetPlayerStaticActorInfo: ActorId=0x{actorId:X}, StaticActorManager=0x{StaticActorManager:X}", _logger.ColorYellow);
        
        if (actorId == 0) 
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   -> Using cached: 0x{PlayerStaticActorInfo:X}", _logger.ColorYellow);
            return PlayerStaticActorInfo;
        }
        
        nint resolved = GetStaticActorInfo(actorId);
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   -> Resolved: 0x{resolved:X}", _logger.ColorYellow);
        
        if (resolved != 0)
            PlayerStaticActorInfo = resolved;
        
        return PlayerStaticActorInfo;
    }
    
    /// <summary>
    /// Get current player position.
    /// </summary>
    public Vector3? GetPlayerPosition()
    {
        nint staticActorInfo = GetPlayerStaticActorInfo();
        if (staticActorInfo == 0) return null;
        
        NodePositionPair position;
        var result = GetPosition(staticActorInfo, &position);
        return result != null ? position.Position : null;
    }
    
    /// <summary>
    /// Get current player rotation (Euler angles).
    /// </summary>
    public Vector3? GetPlayerRotation()
    {
        nint staticActorInfo = GetPlayerStaticActorInfo();
        if (staticActorInfo == 0) return null;
        
        Vector3 rotation;
        var result = GetRotation(staticActorInfo, &rotation);
        return result != null ? rotation : null;
    }
    
    /// <summary>
    /// Get current player forward vector.
    /// </summary>
    public Vector3? GetPlayerForwardVector()
    {
        nint staticActorInfo = GetPlayerStaticActorInfo();
        if (staticActorInfo == 0) return null;
        
        Vector3 forward;
        var result = GetForwardVector(staticActorInfo, &forward);
        return result != null ? forward : null;
    }
    
    /// <summary>
    /// Get a position in front of the player at a specified distance.
    /// </summary>
    public Vector3? GetPositionInFront(float distance)
    {
        var position = GetPlayerPosition();
        var forward = GetPlayerForwardVector();
        
        if (!position.HasValue || !forward.HasValue)
            return null;
        
        return position.Value + forward.Value * distance;
    }
    
    // ============================================================
    // TARGETING API
    // ============================================================
    
    /// <summary>
    /// Gets the currently targeted enemy (soft/hard lock from camera).
    /// Returns null if no target or targeting functions not available.
    /// </summary>
    public TargetStruct* GetTargetedEnemy()
    {
        if (!HasTargetingFunctions || UnkSingletonPlayerOrCameraRelated == 0)
            return null;
        
        uint actorId = GetPlayerActorId();
        if (actorId == 0)
            return null;
        
        nint list35Entry = _getList35EntryFunc!(UnkSingletonPlayerOrCameraRelated);
        if (list35Entry == nint.Zero)
            return null;
        
        return _getCurrentTargetFunc!(list35Entry, 0);
    }
    
    /// <summary>
    /// Gets the currently locked target as a StaticActorInfo pointer.
    /// Returns nint.Zero if no target.
    /// </summary>
    public nint GetLockedTargetStaticActorInfo()
    {
        var target = GetTargetedEnemy();
        if (target == null || target->ActorId == 0)
            return nint.Zero;
        
        return GetStaticActorInfo((uint)target->ActorId);
    }
    
    /// <summary>
    /// Gets the currently locked target's position.
    /// Returns null if no target.
    /// </summary>
    public Vector3? GetLockedTargetPosition()
    {
        var target = GetTargetedEnemy();
        if (target == null)
            return null;
        
        return new Vector3(target->X, target->Y, target->Z);
    }
    
    /// <summary>
    /// Copies the game's own TargetStruct for the currently locked enemy.
    /// This is the correct way to get body-targeting position (Y=1.23 instead of Y=0.26).
    /// The game's targeting system already calculates the correct position.
    /// </summary>
    /// <returns>A copy of the game's TargetStruct, or null if no target locked.</returns>
    public TargetStruct? CopyGameTargetStruct()
    {
        var gameTarget = GetTargetedEnemy();
        if (gameTarget == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] CopyGameTargetStruct: No target locked", _logger.ColorYellow);
            return null;
        }
        
        // Copy all fields from the game's TargetStruct
        // Force Type = 1 so the spell tracks/follows the enemy
        var copy = new TargetStruct
        {
            VTable = gameTarget->VTable,
            Field_8 = gameTarget->Field_8,
            Field_10 = gameTarget->Field_10,
            Field_18 = gameTarget->Field_18,
            GlobalOffset = gameTarget->GlobalOffset,
            Node = gameTarget->Node,
            X = gameTarget->X,
            Y = gameTarget->Y,
            Z = gameTarget->Z,
            Dword1C = gameTarget->Dword1C,
            DirectionX = gameTarget->DirectionX,
            DirectionY = gameTarget->DirectionY,
            DirectionZ = gameTarget->DirectionZ,
            Padding4C = gameTarget->Padding4C,
            Type = 1,  // Force actor-tracking mode (spell follows enemy)
            Field_54 = gameTarget->Field_54,
            Field_58 = gameTarget->Field_58,
            Field_5C = gameTarget->Field_5C,
            Field_60 = gameTarget->Field_60,
            Field_64 = gameTarget->Field_64,
            Field_68 = gameTarget->Field_68,
            ActorId = gameTarget->ActorId,
            Field_70 = gameTarget->Field_70,
            Field_74 = gameTarget->Field_74,
            Field_78 = gameTarget->Field_78
        };
        
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] CopyGameTargetStruct: ActorId={copy.ActorId:X}, Type={copy.Type} (forced=1), GameType={gameTarget->Type}, Pos=({copy.X:F2}, {copy.Y:F2}, {copy.Z:F2})", _logger.ColorGreen);
        
        return copy;
    }
    
    // ============================================================
    // ENTITY LOOKUP API
    // ============================================================
    
    /// <summary>
    /// Get actor reference by actor ID.
    /// </summary>
    public ActorReference* GetActorByKey(uint actorId)
    {
        if (ActorManager == null || _getActorByKeyFunc == null)
            return null;
        
        return _getActorByKeyFunc(ActorManager, actorId);
    }
    
    /// <summary>
    /// Get static actor info by actor ID.
    /// </summary>
    public nint GetStaticActorInfo(uint actorId)
    {
        if (StaticActorManager == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] GetStaticActorInfo: StaticActorManager not captured yet!", _logger.ColorRed);
            return 0;
        }
        
        if (_getOrCreateEntityFunc == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] GetStaticActorInfo: _getOrCreateEntityFunc is null!", _logger.ColorRed);
            return 0;
        }
        
        nint* staticActorInfo = null;
        _getOrCreateEntityFunc(StaticActorManager, &staticActorInfo, actorId);
        
        nint result = staticActorInfo != null ? (nint)staticActorInfo : 0;
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] GetStaticActorInfo(0x{actorId:X}): Result=0x{result:X}", _logger.ColorYellow);
        
        return result;
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
    /// Computes world position from a NodePositionPair.
    /// This transforms the position from node-relative coordinates to world space,
    /// which includes the vertical offset from the parent node's transform.
    /// </summary>
    public Vector3? ComputeWorldPosition(NodePositionPair* positionPair)
    {
        if (_computeWorldPositionFunc == null || positionPair == null)
            return null;
        
        Vector3 worldPos;
        _computeWorldPositionFunc(positionPair, &worldPos);
        return worldPos;
    }
    
    /// <summary>
    /// Gets the world position of an actor (includes Node transform offset).
    /// This is the correct position to target for spells - it's the body center, not the feet.
    /// </summary>
    public Vector3? GetActorWorldPosition(nint staticActorInfo)
    {
        if (staticActorInfo == 0 || _getPositionFunc == null || _computeWorldPositionFunc == null)
            return null;
        
        NodePositionPair position;
        var result = _getPositionFunc(staticActorInfo, &position);
        if (result == null)
            return null;
        
        Vector3 worldPos;
        _computeWorldPositionFunc(&position, &worldPos);
        return worldPos;
    }
    
    // ============================================================
    // MAGIC TARGETING API
    // ============================================================
    
    /// <summary>
    /// Gets the ActorRef from a StaticActorInfo (for SetupMagic).
    /// Tries multiple methods: ActorManager lookup first, then direct struct access.
    /// </summary>
    public long GetActorRef(nint staticActorInfo)
    {
        if (staticActorInfo == 0 || staticActorInfo < 0x10000)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] GetActorRef: Invalid pointer 0x{staticActorInfo:X}", _logger.ColorRed);
            return 0;
        }
        
        try
        {
            var info = (StaticActorInfo*)staticActorInfo;
            uint actorId = info->ActorId;
            
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] GetActorRef Debug:", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   StaticActorInfo: 0x{staticActorInfo:X}", _logger.ColorYellow);
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   +0x10 ActorId: 0x{actorId:X} ({actorId})", _logger.ColorYellow);
            
            // METHOD 1: Try GetActorByKey if ActorManager is available
            // This is what FaithFramework uses - the ActorReference pointer itself is the "ActorRef"
            if (ActorManager != null && _getActorByKeyFunc != null && actorId != 0)
            {
                var actorReference = _getActorByKeyFunc(ActorManager, actorId);
                if (actorReference != null)
                {
                    long actorRefPtr = (long)actorReference;
                    _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   METHOD 1 (GetActorByKey): ActorReference* = 0x{actorRefPtr:X}", _logger.ColorGreen);
                    return actorRefPtr;
                }
                else
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   METHOD 1 (GetActorByKey): returned null", _logger.ColorYellow);
                }
            }
            else
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   METHOD 1 (GetActorByKey): Not available - ActorManager=0x{(nint)ActorManager:X}, Func={_getActorByKeyFunc != null}", _logger.ColorYellow);
            }
            
            // METHOD 2: Try reading from StaticActorInfo offset 0x58
            long directActorRef = info->ActorRef;
            if (directActorRef != 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   METHOD 2 (+0x58 direct): ActorRef = 0x{directActorRef:X}", _logger.ColorGreen);
                return directActorRef;
            }
            else
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   METHOD 2 (+0x58 direct): ActorRef = 0x0 (not populated)", _logger.ColorYellow);
            }
            
            // METHOD 3: Try using the StaticActorInfo pointer itself as the ActorRef
            // Some game functions might accept this directly
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   METHOD 3: Using StaticActorInfo ptr as fallback = 0x{staticActorInfo:X}", _logger.ColorYellow);
            return staticActorInfo;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] GetActorRef Exception: {ex.Message}", _logger.ColorRed);
            return 0;
        }
    }
    
    /// <summary>
    /// Gets the BattleBehavior from a StaticActorInfo.
    /// </summary>
    public nint GetBattleBehavior(nint staticActorInfo)
    {
        if (staticActorInfo == 0 || staticActorInfo < 0x10000)
            return 0;
        
        var info = (StaticActorInfo*)staticActorInfo;
        return (nint)info->BattleBehavior;
    }
    
    /// <summary>
    /// Creates a TargetStruct from a StaticActorInfo's position.
    /// Uses position-only targeting (Type = 0).
    /// </summary>
    public TargetStruct? CreateTargetFromActor(nint staticActorInfo)
    {
        if (staticActorInfo == 0 || _getPositionFunc == null)
            return null;
        
        NodePositionPair position;
        var result = _getPositionFunc(staticActorInfo, &position);
        if (result == null)
            return null;
        
        var target = TargetStruct.FromPosition(position.Position);
        target.Node = position.ParentNode;
        return target;
    }
    
    /// <summary>
    /// Creates a TargetStruct from a StaticActorInfo that tracks the actor.
    /// Uses actor-based targeting (Type = 1) with ActorId set.
    /// The spell will follow/track this actor.
    /// </summary>
    public TargetStruct? CreateTargetFromActorWithTracking(nint staticActorInfo)
    {
        if (staticActorInfo == 0 || _getPositionFunc == null)
            return null;
        
        // Get position
        NodePositionPair position;
        var result = _getPositionFunc(staticActorInfo, &position);
        if (result == null)
            return null;
        
        // Get ActorId from StaticActorInfo
        var actorInfo = (StaticActorInfo*)staticActorInfo;
        int actorId = (int)actorInfo->ActorId;
        
        // Create with actor tracking
        var target = TargetStruct.FromActorId(actorId, position.Position);
        target.Node = position.ParentNode;
        
        _logger.WriteLine($"[EntityApi] CreateTargetFromActorWithTracking: ActorId={actorId}, Type={target.Type}, Pos=({position.Position.X:F2}, {position.Position.Y:F2}, {position.Position.Z:F2})", _logger.ColorYellow);
        
        return target;
    }
    
    /// <summary>
    /// Creates a TargetStruct from a world position.
    /// </summary>
    public TargetStruct CreateTargetFromPosition(Vector3 position)
    {
        return TargetStruct.FromPosition(position);
    }
    
    /// <summary>
    /// Creates a TargetStruct from a position and direction.
    /// </summary>
    public TargetStruct CreateTargetFromPositionAndDirection(Vector3 position, Vector3 direction)
    {
        return TargetStruct.FromPositionAndDirection(position, direction);
    }
    
    // ============================================================
    // PHYSICS API
    // ============================================================
    
    /// <summary>
    /// Update the vertical push recorded for an NPC (for airborne detection).
    /// </summary>
    public void UpdateNpcPhysics(long bnpcRow, float verticalPush)
    {
        _npcVerticalPush[bnpcRow] = verticalPush;
    }
    
    /// <summary>
    /// Detects if an entity is currently airborne.
    /// </summary>
    public bool IsAirborne(long bnpcRow)
    {
        if (bnpcRow < 0x10000 || bnpcRow > 0x00007FFFFFFFFFFF) return false;

        try
        {
            StaticActorInfo* info = (StaticActorInfo*)*(long*)(bnpcRow + BnpcRowOffsets.StaticActorInfoPtr);
            
            long actorPtr = 0;
            if (info != null && (long)info > 0x10000)
            {
                actorPtr = info->ActorRef;
            }
            
            if (actorPtr == 0) 
            {
                actorPtr = *(long*)bnpcRow;
            }

            if (actorPtr > 0x10000 && actorPtr < 0x00007FFFFFFFFFFF)
            {
                byte reactionState = *(byte*)(actorPtr + ActorOffsets.ReactionState);
                if (reactionState > 5) return true;
                
                if (reactionState == 2)
                {
                    if (_npcVerticalPush.TryGetValue(bnpcRow, out float push) && push > 0.1f)
                    {
                        _npcVerticalPush.TryRemove(bnpcRow, out _);
                        return false; 
                    }
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] IsAirborne error: {ex.Message}", _logger.ColorRed);
        }
        return false;
    }
    
    // ============================================================
    // DEBUG API
    // ============================================================
    
    /// <summary>
    /// Log current entity API state.
    /// </summary>
    public void LogState()
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi] === State ===", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   Initialized: {IsInitialized}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   HasPositionFunctions: {HasPositionFunctions}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   HasTargetingFunctions: {HasTargetingFunctions}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   UnkSingleton: 0x{UnkSingletonPlayerOrCameraRelated:X}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   StaticActorManager: 0x{StaticActorManager:X}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   ActorManager: 0x{(nint)ActorManager:X}", _logger.ColorYellow);
        
        var actorId = GetPlayerActorId();
        var position = GetPlayerPosition();
        var target = GetTargetedEnemy();
        
        _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   Player ActorId: 0x{actorId:X}", _logger.ColorYellow);
        
        if (position.HasValue)
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   Player Position: {position.Value:F2}", _logger.ColorYellow);
        
        if (target != null)
            _logger.WriteLine($"[{_modConfig.ModId}] [EntityApi]   Locked Target ActorId: 0x{target->ActorId:X}", _logger.ColorYellow);
    }
}
