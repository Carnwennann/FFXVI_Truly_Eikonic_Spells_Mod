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
    public delegate nint StaticActorManager_GetOrCreateByActorIdDelegate(nint @this, nint** outEntityInfo, uint actorId);
    public delegate ActorReference* ActorManager_SetupEntityDelegate(nint @this, nint entityPtr);
    
    // Function delegates (wrapper only, no hook needed)
    public delegate ActorReference* ActorManager_GetActorByKeyDelegate(nint @this, uint actorId);
    public delegate nint StaticActorInfo_IsValidActorDelegate(nint pStaticEntityInfo);
    public delegate NodePositionPair* StaticActorInfo_GetPositionDelegate(nint pStaticEntityInfo, NodePositionPair* outPair);
    public delegate Vector3* StaticActorInfo_GetRotationDelegate(nint pStaticEntityInfo, Vector3* outPair);
    public delegate Vector3* StaticActorInfo_GetForwardVectorDelegate(nint pStaticEntityInfo, Vector3* outPair);

    /// <summary>
    /// a1: MagicFileInstance, a2: MagicFileSetupData, a3: byte flag
    /// </summary>
    public delegate byte MagicFileInstance_InitFromResourceFileDelegate(nint @this, nint data, byte a3);
    
    /// <summary>
    /// ActorLists::GetListEntryByListTypeAndActorId(listManager, actorRef)
    /// Returns the list entry (e.g., BattleBehaviorEntityEntry) for the given actor.
    /// </summary>
    public delegate nint ActorLists_GetListEntryByListTypeAndActorIdDelegate(nint listManager, nint actorRef);
    
    /// <summary>
    /// BattleBehaviorEntityEntry::GetPositionStructMaybe(this, flag)
    /// Returns position struct pointer.
    /// </summary>
    public delegate nint BattleBehaviorEntityEntry_GetPositionStructMaybeDelegate(nint @this, int flag);
    
    // ============================================================
    // HOOKS
    // ============================================================
    
    private IHook<UnkSingletonPlayerOrCameraRelated_CtorDelegate>? _unkSingletonCtorHook;
    private IHook<StaticActorManager_GetOrCreateDelegate>? _staticActorManagerGetOrCreateHook;
    private IHook<ActorManager_SetupEntityDelegate>? _actorManagerSetupEntityHook;
    
    // ============================================================
    // FUNCTION WRAPPERS
    // ============================================================
    private StaticActorInfo_IsValidActorDelegate? _isValidActorFunc;
    private StaticActorInfo_GetPositionDelegate? _getPositionFunc;
    private StaticActorInfo_GetRotationDelegate? _getRotationFunc;
    private StaticActorInfo_GetForwardVectorDelegate? _getForwardVectorFunc;
    private StaticActorManager_GetOrCreateByActorIdDelegate? _getOrCreateByActorIdFunc;
    private MagicFileInstance_InitFromResourceFileDelegate? _initFromResourceFunc;
    private ActorLists_GetListEntryByListTypeAndActorIdDelegate? _getListEntryFunc;
    private BattleBehaviorEntityEntry_GetPositionStructMaybeDelegate? _getPositionStructFunc;
    private ActorManager_GetActorByKeyDelegate? _getActorByKeyFunc;
    
    // ============================================================
    // CAPTURED SINGLETONS & FALLBACKS
    // ============================================================
    
    private nint _unkSingleton;
    private nint _staticActorManager;
    private nint _actorManager;
    private bool _levelLoaded; // Only capture ActorManager after level loads
    private nint _battleBehaviorListManager; // g_BattleBehaviorEntityListManager_Id30
    private nint _battleBehaviorListManagerGlobalAddress; // Address of the global pointer (parsed from instruction)
    private long _baseAddress;

    private const long UNK_SINGLETON_OFFSET = 0x1816608;
    private const long STATIC_ACTOR_MANAGER_OFFSET = 0x1816CD0;

    public nint UnkSingletonPlayerOrCameraRelated 
    { 
        get {
            if (_unkSingleton != 0) return _unkSingleton;
            if (_baseAddress == 0) return 0;
            nint ptr = *(nint*)(_baseAddress + UNK_SINGLETON_OFFSET);
            if (ptr > 0x10000 && ptr < 0x0000700000000000)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Resolved UnkSingleton from global: 0x{ptr:X}", _logger.ColorGreen);
                _unkSingleton = ptr;
                return ptr;
            }
            return 0;
        }
        private set => _unkSingleton = value;
    }

    public nint StaticActorManager 
    { 
        get {
            if (_staticActorManager != 0) return _staticActorManager;
            if (_baseAddress == 0) return 0;
            nint ptr = *(nint*)(_baseAddress + STATIC_ACTOR_MANAGER_OFFSET);
            if (ptr > 0x10000 && ptr < 0x0000700000000000)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Resolved StaticActorManager from global: 0x{ptr:X}", _logger.ColorGreen);
                _staticActorManager = ptr;
                return ptr;
            }
            return 0;
        }
        private set => _staticActorManager = value;
    }

    /// <summary>
    /// ActorManager (Handles actor lookup by ID). 
    /// Captured from GetActorByKey hook (first parameter).
    /// </summary>
    public nint ActorManager => _actorManager;

    /// <summary>
    /// BattleBehaviorEntityListManager (g_BattleBehaviorEntityListManager_Id30).
    /// Used to get BattleBehaviorEntityEntry for actors.
    /// </summary>
    public nint BattleBehaviorListManager
    {
        get {
            if (_battleBehaviorListManager != 0) return _battleBehaviorListManager;
            
            // Try reading from the global address we parsed during signature scan
            if (_battleBehaviorListManagerGlobalAddress != 0)
            {
                try
                {
                    nint ptr = *(nint*)_battleBehaviorListManagerGlobalAddress;
                    if (ptr > 0x10000 && ptr < 0x0000700000000000)
                    {
                        _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Resolved BattleBehaviorListManager from parsed global: 0x{ptr:X}", _logger.ColorGreen);
                        _battleBehaviorListManager = ptr;
                        return ptr;
                    }
                }
                catch (Exception ex)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Failed to read BattleBehaviorListManager from global address 0x{_battleBehaviorListManagerGlobalAddress:X}: {ex.Message}", _logger.ColorRed);
                }
            }
            else
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] BattleBehaviorListManager global address not resolved - signature scan may have failed", _logger.ColorRed);
            }
            
            return 0;
        }
        private set => _battleBehaviorListManager = value;
    }
    
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private IReloadedHooks? _hooks;
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    /// <summary>
    /// Gets the Actor ID of the currently controlled character (usually Clive = 1).
    /// Read from [UnkSingleton + 0xC8]
    /// </summary>
    public uint CurrentActorId
    {
        get
        {
            nint singleton = UnkSingletonPlayerOrCameraRelated;
            if (singleton == 0) return 0;
            try { return *(uint*)(singleton + 0xC8); } catch { return 0; }
        }
    }

    /// <summary>
    /// Returns true if all required singletons have been captured or resolved.
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
        _baseAddress = System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress;
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
        _hooks = hooks; // Save for lazy loading
        
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
        
        // ActorManager_GetActorByKey
        // This function receives ActorManager as first parameter (rcx)
        scans.AddMainModuleScan("89 54 24 ?? 4C 8B D1 85 D2", result =>
        {
            if (result.Found)
            {
                var addr = GetAddressFromResult(result.Offset);
                _getActorByKeyFunc = hooks.CreateWrapper<ActorManager_GetActorByKeyDelegate>(addr, out _);
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Resolved ActorManager_GetActorByKey at 0x{addr:X}", _logger.ColorGreen);
            }
        });
        
        scans.AddMainModuleScan("48 83 EC 28 8B D2 48 8B C1 E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 00", result =>
        {
            if (result.Found && _getActorByKeyFunc == null)
            {
                var addr = GetAddressFromResult(result.Offset);
                _getActorByKeyFunc = hooks.CreateWrapper<ActorManager_GetActorByKeyDelegate>(addr, out _);
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Resolved ActorManager_GetActorByKey (Sig 2) at 0x{addr:X}", _logger.ColorGreen);
            }
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

        // MagicFileInstance_InitFromResourceFile
        // RVA provided by user: 0x6DD2D4
        {
            var addr = _baseAddress + 0x6DD2D4;
            _initFromResourceFunc = hooks.CreateWrapper<MagicFileInstance_InitFromResourceFileDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Resolved MagicFileInstance_InitFromResourceFile at 0x{addr:X} (RVA 0x6DD2D4)", _logger.ColorGreen);
        }

        // StaticActorManager_GetOrCreateByActorId
        // RVA provided by user: 0x4A3464
        {
            var addr = _baseAddress + 0x4A3464;
            _getOrCreateByActorIdFunc = hooks.CreateWrapper<StaticActorManager_GetOrCreateByActorIdDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Resolved StaticActorManager_GetOrCreateByActorId at 0x{addr:X} (RVA 0x4A3464)", _logger.ColorGreen);
        }

        // ActorLists::GetListEntryByListTypeAndActorId - signature scan
        // Pattern: 48 83 EC 28 45 33 C0 4C 8B C9 48 85 D2 74 3F [4C 8B 15 ?? ?? ?? ??] 48 8D 41 FF 48 83 F8 60
        //                                                        ^^^^^^^^^^^^^^^^^^^^ 
        //                                                        mov r10, [rip+offset] -> g_BattleBehaviorEntityListManager_Id30
        scans.AddMainModuleScan("48 83 EC 28 45 33 C0 4C 8B C9 48 85 D2 74 3F 4C 8B 15 ?? ?? ?? ?? 48 8D 41 FF 48 83 F8 60", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find ActorLists::GetListEntryByListTypeAndActorId", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getListEntryFunc = hooks.CreateWrapper<ActorLists_GetListEntryByListTypeAndActorIdDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Found ActorLists::GetListEntryByListTypeAndActorId at 0x{addr:X}", _logger.ColorGreen);
            
            // Parse the "mov r10, [rip+offset]" instruction to find g_BattleBehaviorEntityListManager_Id30
            // Search for pattern "4C 8B 15" within first 200 bytes of the function
            try
            {
                byte* functionStart = (byte*)addr;
                byte* instructionPtr = null;
                
                // Search for "4C 8B 15" (mov r10, [rip+offset]) in the function
                for (int i = 0; i < 200; i++)
                {
                    if (functionStart[i] == 0x4C && functionStart[i+1] == 0x8B && functionStart[i+2] == 0x15)
                    {
                        instructionPtr = functionStart + i;
                        _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Found 'mov r10, [rip+offset]' at offset +0x{i:X} from function start", _logger.ColorYellow);
                        break;
                    }
                }
                
                if (instructionPtr == null)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] WARNING: Could not find 'mov r10, [rip+offset]' instruction in function", _logger.ColorYellow);
                    return;
                }
                
                // Read the 4-byte RIP-relative offset (little endian)
                int ripRelativeOffset = *(int*)(instructionPtr + 3); // +3 to skip "4C 8B 15"
                
                // Calculate absolute address: instructionPtr + 7 (instruction size) + ripRelativeOffset
                nint globalAddress = (nint)(instructionPtr + 7 + ripRelativeOffset);
                
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Parsed g_BattleBehaviorEntityListManager_Id30 from instruction", _logger.ColorGreen);
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi]   RIP-relative offset: 0x{ripRelativeOffset:X}", _logger.ColorYellow);
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi]   Global address: 0x{globalAddress:X}", _logger.ColorYellow);
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi]   Offset from base: 0x{(globalAddress - _baseAddress):X}", _logger.ColorYellow);
                
                // Save the global address for later use
                _battleBehaviorListManagerGlobalAddress = globalAddress;
                
                // Try to cache the pointer value (may not be initialized yet during startup)
                nint listManagerPtr = *(nint*)globalAddress;
                if (listManagerPtr > 0x10000 && listManagerPtr < 0x0000700000000000)
                {
                    _battleBehaviorListManager = listManagerPtr;
                    _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi]   -> BattleBehaviorListManager resolved: 0x{listManagerPtr:X}", _logger.ColorGreen);
                }
                else
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi]   -> WARNING: Invalid pointer value 0x{listManagerPtr:X} (may not be initialized yet)", _logger.ColorYellow);
                }
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] WARNING: Failed to parse g_BattleBehaviorEntityListManager_Id30 from instruction: {ex.Message}", _logger.ColorYellow);
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Will retry reading at runtime (may not be initialized during startup)", _logger.ColorYellow);
            }
        });

        // BattleBehaviorEntityEntry::GetPositionStructMaybe - signature scan
        scans.AddMainModuleScan("48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 8B 81 ?? ?? ?? ?? 40 8A F2 C1 E8 12", result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] FAILED to find BattleBehaviorEntityEntry::GetPositionStructMaybe", _logger.ColorRed);
                return;
            }
            var addr = GetAddressFromResult(result.Offset);
            _getPositionStructFunc = hooks.CreateWrapper<BattleBehaviorEntityEntry_GetPositionStructMaybeDelegate>(addr, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Found BattleBehaviorEntityEntry::GetPositionStructMaybe at 0x{addr:X}", _logger.ColorGreen);
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
        if (ActorManager == 0)
        {
            _actorManager = @this;
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Captured ActorManager from SetupEntity: 0x{@this:X}", _logger.ColorGreen);
        }
        return _actorManagerSetupEntityHook!.OriginalFunction(@this, entityPtr);
    }
    
    /// <summary>
    /// Call this when level loads to allow ActorManager capture
    /// </summary>
    public void OnLevelLoaded()
    {
        _levelLoaded = true;
        _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Level loaded", _logger.ColorYellow);
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
        if (StaticActorManager == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetStaticActorInfo FAIL: StaticActorManager is 0", _logger.ColorRed);
            return 0;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetStaticActorInfo: Manager=0x{StaticActorManager:X}, ActorId={actorId}", _logger.ColorBlue);
            
        nint* staticActorInfo = null;
        if (_getOrCreateByActorIdFunc != null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Calling GetOrCreateByActorId via RVA function...", _logger.ColorBlue);
            _getOrCreateByActorIdFunc(StaticActorManager, &staticActorInfo, actorId);
        }
        else if (_staticActorManagerGetOrCreateHook != null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] Calling GetOrCreate via hooked function...", _logger.ColorBlue);
            _staticActorManagerGetOrCreateHook.OriginalFunction(StaticActorManager, &staticActorInfo, actorId);
        }
        else
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetStaticActorInfo FAIL: No function available!", _logger.ColorRed);
            return 0;
        }
        
        if (staticActorInfo == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetStaticActorInfo returned NULL pointer", _logger.ColorRed);
            return 0;
        }
        
        nint result = *staticActorInfo;
        _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetStaticActorInfo result: 0x{result:X}", _logger.ColorGreen);
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
    /// Initializes a MagicFileInstance from a resource file data buffer.
    /// </summary>
    public byte InitFromResourceFile(nint magicFileInstance, nint data, byte a3)
    {
        if (_initFromResourceFunc == null) return 0;
        return _initFromResourceFunc(magicFileInstance, data, a3);
    }

    /// <summary>
    /// Get the player's battle context (ActorReference and Position) using the game's proper API.
    /// This is the CORRECT way to get player context, matching how the game does it internally.
    /// Returns: (actorRef, positionStruct) or (0, 0) if failed.
    /// </summary>
    public (nint actorRef, nint positionStruct) GetPlayerBattleContext(uint playerActorId = 0)
    {
        if (playerActorId == 0)
        {
            playerActorId = 1; // Default to 1 for Clive
        }
        
        // Check if required functions are initialized
        if (_getListEntryFunc == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext FAIL: ActorLists::GetListEntryByListTypeAndActorId not found via signature scan", _logger.ColorRed);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] This function is REQUIRED. Check if signature is correct for your game version.", _logger.ColorYellow);
            return (0, 0);
        }
        
        if (_getPositionStructFunc == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext FAIL: BattleBehaviorEntityEntry::GetPositionStructMaybe not found via signature scan", _logger.ColorRed);
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] This function is REQUIRED. Check if signature is correct for your game version.", _logger.ColorYellow);
            return (0, 0);
        }
        
        // Get BattleBehaviorListManager (g_BattleBehaviorEntityListManager_Id30)
        nint listManager = BattleBehaviorListManager;
        if (listManager == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext FAIL: BattleBehaviorListManager not resolved", _logger.ColorRed);
            if (_battleBehaviorListManagerGlobalAddress == 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi]   -> Global address was not parsed from instruction (signature scan failed?)", _logger.ColorYellow);
            }
            else
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi]   -> Global address: 0x{_battleBehaviorListManagerGlobalAddress:X} (pointer may not be initialized yet)", _logger.ColorYellow);
            }
            return (0, 0);
        }

        // Get ActorReference for player (ActorId = 1 for Clive)
        var actorRef = GetActorByKey(playerActorId);
        if (actorRef == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext FAIL: GetActorByKey returned null for ActorId {playerActorId}", _logger.ColorRed);
            return (0, 0);
        }

        // Get BattleBehaviorEntityEntry using ActorLists::GetListEntryByListTypeAndActorId
        if (_getListEntryFunc == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext FAIL: GetListEntryFunc not initialized", _logger.ColorRed);
            return (0, 0);
        }

        nint btlBehaviorEntry = _getListEntryFunc(listManager, (nint)actorRef);
        if (btlBehaviorEntry == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext FAIL: GetListEntry returned 0", _logger.ColorRed);
            return (0, 0);
        }

        // Get position struct using BattleBehaviorEntityEntry::GetPositionStructMaybe
        if (_getPositionStructFunc == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext FAIL: GetPositionStructFunc not initialized", _logger.ColorRed);
            return (0, 0);
        }

        nint positionStruct = _getPositionStructFunc(btlBehaviorEntry, 0);
        if (positionStruct == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext FAIL: GetPositionStruct returned 0", _logger.ColorRed);
            return (0, 0);
        }

        _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPlayerBattleContext SUCCESS: ActorRef=0x{(nint)actorRef:X}, Pos=0x{positionStruct:X}", _logger.ColorGreen);
        return ((nint)actorRef, positionStruct);
    }

    /// <summary>
    /// Gets the position struct for a given actor reference using the BattleBehavior system.
    /// This is safer than manual pointer math.
    /// </summary>
    public nint GetPositionStructFromActorRef(nint actorRef)
    {
        if (actorRef == 0) return 0;

        nint listManager = BattleBehaviorListManager;
        if (listManager == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPositionStruct FAIL: ListManager not found", _logger.ColorYellow);
            return 0;
        }

        if (_getListEntryFunc == null || _getPositionStructFunc == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPositionStruct FAIL: Functions not initialized", _logger.ColorYellow);
            return 0;
        }

        nint btlBehaviorEntry = _getListEntryFunc(listManager, actorRef);
        if (btlBehaviorEntry == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [FunctionApi] GetPositionStruct FAIL: List entry not found for ActorRef 0x{actorRef:X}", _logger.ColorYellow);
            return 0;
        }

        return _getPositionStructFunc(btlBehaviorEntry, 0);
    }
}
