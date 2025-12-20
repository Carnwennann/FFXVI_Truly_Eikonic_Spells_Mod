using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;

namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Handles magic spell casting and projectile spawning.
/// 
/// The magic system works with two approaches:
/// 
/// 1. MagicExecute + CastMagic flow:
///    - MagicExecute prepares the spell struct with magicId
///    - CastMagic actually spawns the spell using the prepared struct
///    - Used for: general magic spells by ID
/// 
/// 2. FireMagicProjectile flow:
///    - Takes a MagicManager pointer + ProjectileData
///    - MagicManager+0x38 points to MagicInputConfig
///    - MagicInputConfig+0x10 = Shot Type (1=Normal, 2=Charged, 3=Precision, 4=Burst)
///    - Used for: magic projectiles (Dia/Diara type shots)
/// 
/// Key structures:
/// - MagicManager: Main manager structure (~512 bytes)
///   +0x28 = Timeline pointer
///   +0x38 = MagicInputConfig pointer
/// 
/// - MagicInputConfig: Shot configuration (~256 bytes)
///   +0x10 = Shot Type (1=Normal/Dia, 2=Charged/Diara, 3=Precision Counter, 4=Burst)
///   +0x18 = Burst ID?
///   +0x1C = Charged ID?
/// 
/// - UnkMagicStruct: Magic spell setup structure (264 bytes)
///   +0x00 = ptr1 (pointer to some data)
///   +0x08 = array of 32 longs (256 bytes)
/// </summary>
public unsafe class MagicCastSystem
{
    // ============================================================
    // DELEGATES
    // ============================================================
    
    /// <summary>
    /// Prepares a magic spell to be cast. Sets up the magic struct.
    /// Signature: 48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long MagicExecuteDelegate(long unkMagicStructPtr, int magicId, long a3, long a4, int a5, int a6, int a7);
    
    /// <summary>
    /// Actually spawns the magic spell using the prepared struct.
    /// Signature: 48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate char CastMagicDelegate(long a1, long unkMagicStructPtr);
    
    /// <summary>
    /// Fires a magic projectile (Dia/Diara shots).
    /// Offset: 0x56E0F0 (hardcoded, signature scan was matching wrong function)
    /// </summary>
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    public delegate long FireMagicProjectileDelegate(long magicManager, long projectileData);
    
    // ============================================================
    // CONSTANTS
    // ============================================================
    
    // Function offsets (relative to base address)
    private const int FIRE_MAGIC_PROJECTILE_OFFSET = 0x56E0F0;
    
    // Shot types for MagicInputConfig+0x10
    public const int SHOT_TYPE_NORMAL = 1;    // Normal shot (Dia)
    public const int SHOT_TYPE_CHARGED = 2;   // Charged shot (Diara)
    public const int SHOT_TYPE_PRECISION = 3; // Precision Counter
    public const int SHOT_TYPE_BURST = 4;     // Magic Burst
    
    // Buffer sizes
    private const int MAGIC_STRUCT_SIZE = 0x108;      // 264 bytes (8 + 32*8)
    private const int MAGIC_MANAGER_SIZE = 0x200;     // 512 bytes
    private const int MAGIC_INPUT_CONFIG_SIZE = 0x100; // 256 bytes
    private const int PROJECTILE_DATA_SIZE = 0x500;   // 1280 bytes
    
    // ============================================================
    // HOOKS & FUNCTION POINTERS
    // ============================================================
    
    private IHook<MagicExecuteDelegate>? _magicExecuteHook;
    private IHook<CastMagicDelegate>? _castMagicHook;
    private IHook<FireMagicProjectileDelegate>? _fireMagicProjectileHook;
    
    private MagicExecuteDelegate? _magicExecuteWrapper;
    private CastMagicDelegate? _castMagicWrapper;
    private FireMagicProjectileDelegate? _fireMagicProjectileWrapper;
    
    // ============================================================
    // CACHED CONTEXT
    // ============================================================
    
    // MagicExecute/CastMagic system cache
    private IntPtr _magicStructBuffer = IntPtr.Zero;
    private long _magicExecute_a3 = 0;
    private long _magicExecute_a4 = 0;
    private int _magicExecute_a5 = 0;
    private int _magicExecute_a6 = 0;
    private int _magicExecute_a7 = 0;
    private long _castMagic_a1 = 0;
    private bool _hasMagicContext = false;
    
    // FireMagicProjectile system cache (deep copy)
    private IntPtr _magicManagerCopy = IntPtr.Zero;
    private IntPtr _magicInputConfigCopy = IntPtr.Zero;
    private IntPtr _projectileDataBuffer = IntPtr.Zero;
    private long _cachedMagicManager = 0;  // Original pointer for debugging
    private long _cachedValidVTable = 0;
    private bool _hasCachedProjectileContext = false;
    
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private Config _configuration;
    
    // ============================================================
    // CALLBACKS
    // ============================================================
    
    /// <summary>
    /// Called when a charged shot (Diara) is detected and should be suppressed.
    /// Parameters: (activeEikon, magicManager, projectileData)
    /// Return true to suppress the shot, false to let it through.
    /// </summary>
    public Func<int, long, long, bool>? OnChargedShotDetected { get; set; }
    
    /// <summary>
    /// Called when a normal shot (Dia) is successfully fired.
    /// Can be used to cache context for later use.
    /// </summary>
    public Action<long, long>? OnNormalShotFired { get; set; }
    
    /// <summary>
    /// Called to get the current active Eikon.
    /// </summary>
    public Func<int>? GetActiveEikon { get; set; }
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    /// <summary>
    /// True if we have valid cached MagicExecute/CastMagic context.
    /// </summary>
    public bool HasMagicContext => _hasMagicContext;
    
    /// <summary>
    /// True if we have valid cached FireMagicProjectile context (deep copy).
    /// </summary>
    public bool HasProjectileContext => _hasCachedProjectileContext;
    
    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public MagicCastSystem(ILogger logger, IModConfig modConfig, Config configuration)
    {
        _logger = logger;
        _modConfig = modConfig;
        _configuration = configuration;
        
        // Allocate buffers
        _magicStructBuffer = Marshal.AllocHGlobal(MAGIC_STRUCT_SIZE);
        _magicManagerCopy = Marshal.AllocHGlobal(MAGIC_MANAGER_SIZE);
        _magicInputConfigCopy = Marshal.AllocHGlobal(MAGIC_INPUT_CONFIG_SIZE);
        _projectileDataBuffer = Marshal.AllocHGlobal(PROJECTILE_DATA_SIZE);
        
        // Zero-initialize
        for (int i = 0; i < MAGIC_STRUCT_SIZE; i++) *((byte*)_magicStructBuffer + i) = 0;
        for (int i = 0; i < MAGIC_MANAGER_SIZE; i++) *((byte*)_magicManagerCopy + i) = 0;
        for (int i = 0; i < MAGIC_INPUT_CONFIG_SIZE; i++) *((byte*)_magicInputConfigCopy + i) = 0;
        for (int i = 0; i < PROJECTILE_DATA_SIZE; i++) *((byte*)_projectileDataBuffer + i) = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Allocated buffers: MagicStruct=0x{(long)_magicStructBuffer:X}, MagicManager=0x{(long)_magicManagerCopy:X}, Config=0x{(long)_magicInputConfigCopy:X}, ProjData=0x{(long)_projectileDataBuffer:X}", _logger.ColorGreen);
    }
    
    // ============================================================
    // INITIALIZATION
    // ============================================================
    
    /// <summary>
    /// Initialize hooks using signature scanning.
    /// Call from SetupScans in the main mod.
    /// </summary>
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        // MagicExecute - Prepares the magic spell
        scans.AddScan("48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8", address =>
        {
            _magicExecuteHook = hooks.CreateHook<MagicExecuteDelegate>(MagicExecuteImpl, address).Activate();
            _magicExecuteWrapper = hooks.CreateWrapper<MagicExecuteDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Hooked MagicExecute at 0x{address:X}", _logger.ColorGreen);
        });
        
        // CastMagic - Actually spawns the spell
        scans.AddScan("48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D", address =>
        {
            _castMagicHook = hooks.CreateHook<CastMagicDelegate>(CastMagicImpl, address).Activate();
            _castMagicWrapper = hooks.CreateWrapper<CastMagicDelegate>(address, out _);
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Hooked CastMagic at 0x{address:X}", _logger.ColorGreen);
        });
    }
    
    /// <summary>
    /// Initialize FireMagicProjectile hook using hardcoded offset.
    /// Call from main mod after SetupScans.
    /// </summary>
    public void InitializeFireMagicProjectile(IReloadedHooks hooks, long baseAddress)
    {
        var fireMagicAddr = baseAddress + FIRE_MAGIC_PROJECTILE_OFFSET;
        _fireMagicProjectileHook = hooks.CreateHook<FireMagicProjectileDelegate>(FireMagicProjectileImpl, fireMagicAddr).Activate();
        _fireMagicProjectileWrapper = hooks.CreateWrapper<FireMagicProjectileDelegate>(fireMagicAddr, out _);
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Hooked FireMagicProjectile at 0x{fireMagicAddr:X}", _logger.ColorGreen);
    }
    
    // ============================================================
    // PUBLIC API - CAST MAGIC
    // ============================================================
    
    /// <summary>
    /// Spawn a magic spell by ID using the MagicExecute/CastMagic system.
    /// Requires HasMagicContext to be true (fire a normal spell first to capture context).
    /// </summary>
    public bool CastMagicSpell(int magicId)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] === ENTRY === magicId={magicId}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] State: _hasMagicContext={_hasMagicContext}, _magicStructBuffer=0x{(long)_magicStructBuffer:X}, _castMagic_a1=0x{_castMagic_a1:X}", _logger.ColorYellow);
        
        if (!_hasMagicContext)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: No magic context! Fire a normal shot first.", _logger.ColorRed);
            return false;
        }
        
        if (_magicStructBuffer == IntPtr.Zero || _castMagic_a1 == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: Missing buffers or a1!", _logger.ColorRed);
            return false;
        }
        
        if (_magicExecuteHook == null || _castMagicHook == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] FAIL: Hooks not initialized! MagicExecute={_magicExecuteHook != null}, CastMagic={_castMagicHook != null}", _logger.ColorRed);
            return false;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] All checks passed, calling MagicExecute...", _logger.ColorGreen);
        _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Params: a3=0x{_magicExecute_a3:X}, a4=0x{_magicExecute_a4:X}, a5={_magicExecute_a5}, a6={_magicExecute_a6}, a7={_magicExecute_a7}", _logger.ColorYellow);
        
        try
        {
            // Call MagicExecute with our cached struct and new magicId
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Calling MagicExecute.OriginalFunction...", _logger.ColorYellow);
            var execResult = _magicExecuteHook.OriginalFunction(
                (long)_magicStructBuffer, 
                magicId, 
                _magicExecute_a3, 
                _magicExecute_a4, 
                _magicExecute_a5, 
                _magicExecute_a6, 
                _magicExecute_a7
            );
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] MagicExecute returned: 0x{execResult:X}", _logger.ColorGreen);
            
            // Call CastMagic to actually spawn the spell
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Calling CastMagic.OriginalFunction with a1=0x{_castMagic_a1:X}...", _logger.ColorYellow);
            var castResult = _castMagicHook.OriginalFunction(_castMagic_a1, (long)_magicStructBuffer);
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] CastMagic returned: {(int)castResult}", _logger.ColorGreen);
            
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] === SUCCESS === Magic spell {magicId} cast!", _logger.ColorGreen);
            return true;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] === CRASH === {ex.Message}", _logger.ColorRed);
            _logger.WriteLine($"[{_modConfig.ModId}] [CastMagicSpell] Stack: {ex.StackTrace}", _logger.ColorRed);
            return false;
        }
    }
    
    /// <summary>
    /// Fire a magic projectile (Dia-type shot) using deep-copied context.
    /// Requires HasProjectileContext to be true (fire a normal shot first to capture context).
    /// </summary>
    /// <param name="shotType">Shot type: 1=Normal/Dia, 2=Charged/Diara, 3=Precision, 4=Burst</param>
    /// <param name="count">Number of projectiles to fire</param>
    public bool FireMagicProjectiles(int shotType, int count = 1)
    {
        if (!_hasCachedProjectileContext)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Cannot fire projectiles - no cached context! Fire a normal shot first.", _logger.ColorRed);
            return false;
        }
        
        if (_magicManagerCopy == IntPtr.Zero || _magicInputConfigCopy == IntPtr.Zero)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Cannot fire projectiles - buffers not allocated!", _logger.ColorRed);
            return false;
        }
        
        if (_fireMagicProjectileHook == null)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Cannot fire projectiles - hook not initialized!", _logger.ColorRed);
            return false;
        }
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Firing {count} projectiles (shotType={shotType})...", _logger.ColorGreen);
        
        try
        {
            // Patch MagicManager+0x38 to point to our copied config
            long* configPtrLocation = (long*)((long)_magicManagerCopy + 0x38);
            *configPtrLocation = (long)_magicInputConfigCopy;
            
            // Set the shot type
            int* shotTypePtr = (int*)((long)_magicInputConfigCopy + 0x10);
            *shotTypePtr = shotType;
            
            // Fire the projectiles
            int successCount = 0;
            for (int i = 0; i < count; i++)
            {
                long result = _fireMagicProjectileHook.OriginalFunction((long)_magicManagerCopy, 0);
                if (result != 0) successCount++;
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Fired {successCount}/{count} projectiles!", 
                successCount == count ? _logger.ColorGreen : _logger.ColorYellow);
            
            return successCount > 0;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] CRASH firing projectiles: {ex.Message}", _logger.ColorRed);
            return false;
        }
    }
    
    /// <summary>
    /// Fire Normal (Dia) projectiles. Shorthand for FireMagicProjectiles(SHOT_TYPE_NORMAL, count).
    /// </summary>
    public bool FireDiaProjectiles(int count = 1) => FireMagicProjectiles(SHOT_TYPE_NORMAL, count);
    
    /// <summary>
    /// Fire Charged (Diara) projectiles. Shorthand for FireMagicProjectiles(SHOT_TYPE_CHARGED, count).
    /// </summary>
    public bool FireDiaraProjectiles(int count = 1) => FireMagicProjectiles(SHOT_TYPE_CHARGED, count);
    
    /// <summary>
    /// Cast Dia spell using MagicExecute/CastMagic system (more stable than FireMagicProjectile).
    /// magicId for Dia needs to be discovered - try common values or check logs.
    /// </summary>
    public bool CastSpells(int magicID = 1, int count = 1)
    {   
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Attempting to cast {count} Dia spells via CastMagicSpell...", _logger.ColorGreen);
        
        int successCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (CastMagicSpell(magicID))
                successCount++;
        }
        
        return successCount > 0;
    }
    
    // ============================================================
    // HOOK IMPLEMENTATIONS
    // ============================================================
    
    private long MagicExecuteImpl(long unkMagicStructPtr, int magicId, long a3, long a4, int a5, int a6, int a7)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_EXECUTE] Called! structPtr=0x{unkMagicStructPtr:X}, magicId={magicId}", _logger.ColorGreen);
        
        // Deep copy the magic struct
        if (_magicStructBuffer != IntPtr.Zero && unkMagicStructPtr != 0)
        {
            try
            {
                long ptr1Value = *(long*)unkMagicStructPtr;
                *(long*)_magicStructBuffer = ptr1Value;
                
                for (int i = 0; i < 32; i++)
                {
                    long value = *(long*)(unkMagicStructPtr + 0x8 + 0x8 * i);
                    *(long*)((long)_magicStructBuffer + 0x8 + 0x8 * i) = value;
                }
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [MAGIC_EXECUTE] Copy failed: {ex.Message}", _logger.ColorRed);
            }
        }
        
        // Cache parameters
        _magicExecute_a3 = a3;
        _magicExecute_a4 = a4;
        _magicExecute_a5 = a5;
        _magicExecute_a6 = a6;
        _magicExecute_a7 = a7;
        
        return _magicExecuteHook!.OriginalFunction(unkMagicStructPtr, magicId, a3, a4, a5, a6, a7);
    }
    
    private char CastMagicImpl(long a1, long unkMagicStructPtr)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] [CAST_MAGIC] Called! a1=0x{a1:X}", _logger.ColorGreen);
        
        _castMagic_a1 = a1;
        _hasMagicContext = true;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [CAST_MAGIC] === MAGIC CONTEXT READY! ===", _logger.ColorGreen);
        
        return _castMagicHook!.OriginalFunction(a1, unkMagicStructPtr);
    }
    
    private long FireMagicProjectileImpl(long magicManager, long projectileData)
    {
        // Get shot type
        int shotType = 0;
        long ptr38 = *(long*)(magicManager + 0x38);
        if (ptr38 != 0)
        {
            shotType = *(int*)(ptr38 + 0x10);
        }
        
        // Check for Charged Shot suppression
        if (shotType == SHOT_TYPE_CHARGED && OnChargedShotDetected != null)
        {
            int activeEikon = GetActiveEikon?.Invoke() ?? 0;
            if (OnChargedShotDetected(activeEikon, magicManager, projectileData))
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [FIRE_MAGIC] Charged shot SUPPRESSED by callback", _logger.ColorYellow);
                return 0; // Suppress
            }
        }
        
        // Call original
        long result = _fireMagicProjectileHook!.OriginalFunction(magicManager, projectileData);
        
        // Cache on successful NORMAL shot
        if (result != 0 && shotType == SHOT_TYPE_NORMAL)
        {
            CacheProjectileContext(magicManager, projectileData);
            OnNormalShotFired?.Invoke(magicManager, projectileData);
        }
        
        return result;
    }
    
    // ============================================================
    // INTERNAL HELPERS
    // ============================================================
    
    private void CacheProjectileContext(long magicManager, long projectileData)
    {
        try
        {
            _cachedMagicManager = magicManager;
            
            // Deep copy MagicManager
            Buffer.MemoryCopy((void*)magicManager, (void*)_magicManagerCopy, MAGIC_MANAGER_SIZE, MAGIC_MANAGER_SIZE);
            
            // Deep copy MagicInputConfig
            long configPtr = *(long*)(magicManager + 0x38);
            if (configPtr != 0)
            {
                Buffer.MemoryCopy((void*)configPtr, (void*)_magicInputConfigCopy, MAGIC_INPUT_CONFIG_SIZE, MAGIC_INPUT_CONFIG_SIZE);
            }
            
            // Copy projectile data
            if (projectileData != 0)
            {
                Buffer.MemoryCopy((void*)projectileData, (void*)_projectileDataBuffer, PROJECTILE_DATA_SIZE, PROJECTILE_DATA_SIZE);
            }
            
            // Cache VTable
            long timelinePtr = *(long*)(magicManager + 0x28);
            if (timelinePtr != 0)
            {
                _cachedValidVTable = *(long*)timelinePtr;
            }
            
            _hasCachedProjectileContext = true;
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Context cached from 0x{magicManager:X}", _logger.ColorBlue);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Cache failed: {ex.Message}", _logger.ColorRed);
        }
    }
    
    // ============================================================
    // STATE MANAGEMENT
    // ============================================================
    
    /// <summary>
    /// Reset all cached context. Call on level load.
    /// </summary>
    public void Reset()
    {
        _hasMagicContext = false;
        _hasCachedProjectileContext = false;
        _castMagic_a1 = 0;
        _cachedMagicManager = 0;
        _cachedValidVTable = 0;
        _magicExecute_a3 = 0;
        _magicExecute_a4 = 0;
        _magicExecute_a5 = 0;
        _magicExecute_a6 = 0;
        _magicExecute_a7 = 0;
        
        _logger.WriteLine($"[{_modConfig.ModId}] [MagicCastSystem] Reset", _logger.ColorYellow);
    }
    
    /// <summary>
    /// Update configuration reference.
    /// </summary>
    public void UpdateConfiguration(Config configuration)
    {
        _configuration = configuration;
    }
    
    /// <summary>
    /// Clean up allocated buffers.
    /// </summary>
    public void Dispose()
    {
        if (_magicStructBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_magicStructBuffer);
        if (_magicManagerCopy != IntPtr.Zero) Marshal.FreeHGlobal(_magicManagerCopy);
        if (_magicInputConfigCopy != IntPtr.Zero) Marshal.FreeHGlobal(_magicInputConfigCopy);
        if (_projectileDataBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_projectileDataBuffer);
        
        _magicStructBuffer = IntPtr.Zero;
        _magicManagerCopy = IntPtr.Zero;
        _magicInputConfigCopy = IntPtr.Zero;
        _projectileDataBuffer = IntPtr.Zero;
    }
}
