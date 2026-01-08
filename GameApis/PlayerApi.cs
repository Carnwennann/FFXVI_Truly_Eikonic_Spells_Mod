using System.Numerics;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// High-level API for player information.
/// Provides easy access to player position, rotation, entity IDs, etc.
/// </summary>
public unsafe class PlayerApi
{
    // ============================================================
    // DEPENDENCIES
    // ============================================================
    
    private readonly FunctionApi _hooks;
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    
    // ============================================================
    // PROPERTIES
    // ============================================================
    
    /// <summary>
    /// Returns true if the player API is fully initialized and ready to use.
    /// </summary>
    public bool IsInitialized => _hooks.IsInitialized;
    
    /// <summary>
    /// Returns true if position/rotation functions are available.
    /// </summary>
    public bool HasPositionFunctions => _hooks.HasPositionFunctions;
    
    // ============================================================
    // CONSTRUCTOR
    // ============================================================
    
    public PlayerApi(ILogger logger, IModConfig modConfig)
    {
        _logger = logger;
        _modConfig = modConfig;
        _hooks = new FunctionApi(logger, modConfig);
    }
    
    // ============================================================
    // INITIALIZATION
    // ============================================================
    
    /// <summary>
    /// Set up all required hooks. Call this during mod initialization.
    /// </summary>
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        _hooks.SetupScans(scans, hooks);
    }
    
    // ============================================================
    // PLAYER ID API
    // ============================================================
    
    /// <summary>
    /// Get current player's actor ID from the singleton.
    /// Returns 1 (Clive) as fallback if not available.
    /// </summary>
    public uint GetPlayerActorId()
    {
        try {
            if (_hooks.UnkSingletonPlayerOrCameraRelated == 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi] UnkSingleton is 0, using fallback ActorId=1", _logger.ColorYellow);
                return 1; // Fallback to Clive
            }
            
            uint id = *(uint*)(_hooks.UnkSingletonPlayerOrCameraRelated + 0xC8);
            if (id == 0)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi] ActorId at +0xC8 is 0, using fallback ActorId=1", _logger.ColorYellow);
                return 1;
            }
            
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi] Current ActorId: {id}", _logger.ColorBlue);
            return id;
        } catch (Exception ex) {
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi] GetPlayerActorId failed: {ex.Message}", _logger.ColorRed);
            return 1;
        }
    }
    
    /// <summary>
    /// Get current player's entity ID.
    /// Returns 0 if not available.
    /// </summary>
    public uint GetPlayerEntityId()
    {
        var actorRef = GetPlayerActorReference();
        if (actorRef == null)
            return 0;
        
        return actorRef->EntityID;
    }
    
    /// <summary>
    /// Get the ActorReference pointer for the player.
    /// This requires ActorManager to be captured first.
    /// </summary>
    public FunctionApi.ActorReference* GetPlayerActorReference()
    {
        uint actorId = GetPlayerActorId();
        if (actorId == 0)
            return null;
        
        // This will return null if ActorManager hasn't been captured yet
        return _hooks.GetActorByKey(actorId);
    }
    
    /// <summary>
    /// Get the static actor info pointer for the player.
    /// Use this to pass to SetupMagic's position parameter.
    /// </summary>
    public nint GetPlayerStaticActorInfo()
    {
        uint actorId = GetPlayerActorId();
        if (actorId == 0)
            return 0;
        
        var staticInfo = _hooks.GetStaticActorInfo(actorId);
        
        if (staticInfo == 0)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi] GetStaticActorInfo returned 0 for ActorId={actorId}", _logger.ColorRed);
        }
        else if (staticInfo > 0x0000700000000000)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi] WARNING: StaticActorInfo (0x{staticInfo:X}) looks like a code address! ActorId={actorId}", _logger.ColorRed);
        }
        else
        {
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi] StaticActorInfo: 0x{staticInfo:X} for ActorId={actorId}", _logger.ColorBlue);
        }
        
        return staticInfo;
    }
    
    // ============================================================
    // PLAYER POSITION API
    // ============================================================
    
    /// <summary>
    /// Get current player position.
    /// Returns null if not available.
    /// </summary>
    public Vector3? GetPlayerPosition()
    {
        if (!IsInitialized)
            return null;
        
        nint staticActorInfo = GetPlayerStaticActorInfo();
        if (staticActorInfo == 0)
            return null;
        
        if (!_hooks.IsValidActor(staticActorInfo))
            return null;
        
        FunctionApi.NodePositionPair position;
        var result = _hooks.GetPosition(staticActorInfo, &position);
        if (result == null)
            return null;
        
        return position.Position;
    }
    
    /// <summary>
    /// Get current player rotation (Euler angles).
    /// Returns null if not available.
    /// </summary>
    public Vector3? GetPlayerRotation()
    {
        if (!IsInitialized)
            return null;
        
        nint staticActorInfo = GetPlayerStaticActorInfo();
        if (staticActorInfo == 0)
            return null;
        
        Vector3 rotation;
        var result = _hooks.GetRotation(staticActorInfo, &rotation);
        if (result == null)
            return null;
        
        return rotation;
    }
    
    /// <summary>
    /// Get current player forward vector (normalized direction the player is facing).
    /// Returns null if not available.
    /// </summary>
    public Vector3? GetPlayerForwardVector()
    {
        if (!IsInitialized)
            return null;
        
        nint staticActorInfo = GetPlayerStaticActorInfo();
        if (staticActorInfo == 0)
            return null;
        
        Vector3 forward;
        var result = _hooks.GetForwardVector(staticActorInfo, &forward);
        if (result == null)
            return null;
        
        return forward;
    }
    
    // ============================================================
    // UTILITY METHODS
    // ============================================================
    
    /// <summary>
    /// Get a position in front of the player at a specified distance.
    /// Useful for spawning projectiles.
    /// </summary>
    /// <param name="distance">Distance in front of the player.</param>
    /// <returns>World position, or null if player data unavailable.</returns>
    public Vector3? GetPositionInFront(float distance)
    {
        var position = GetPlayerPosition();
        var forward = GetPlayerForwardVector();
        
        if (!position.HasValue || !forward.HasValue)
            return null;
        
        return position.Value + forward.Value * distance;
    }
    
    /// <summary>
    /// Calculate direction from player to a target position.
    /// </summary>
    /// <param name="targetPosition">Target world position.</param>
    /// <returns>Normalized direction vector, or null if player position unavailable.</returns>
    public Vector3? GetDirectionTo(Vector3 targetPosition)
    {
        var position = GetPlayerPosition();
        if (!position.HasValue)
            return null;
        
        var direction = targetPosition - position.Value;
        if (direction.LengthSquared() < 0.0001f)
            return null;
        
        return Vector3.Normalize(direction);
    }
    
    /// <summary>
    /// Calculate distance from player to a target position.
    /// </summary>
    /// <param name="targetPosition">Target world position.</param>
    /// <returns>Distance, or -1 if player position unavailable.</returns>
    public float GetDistanceTo(Vector3 targetPosition)
    {
        var position = GetPlayerPosition();
        if (!position.HasValue)
            return -1f;
        
        return Vector3.Distance(position.Value, targetPosition);
    }
    
    // ============================================================
    // DEBUG API
    // ============================================================
    
    /// <summary>
    /// Log current player state for debugging.
    /// </summary>
    public void LogPlayerState()
    {
        var actorId = GetPlayerActorId();
        var entityId = GetPlayerEntityId();
        var position = GetPlayerPosition();
        var rotation = GetPlayerRotation();
        var forward = GetPlayerForwardVector();
        
        _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi] === Player State ===", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   Initialized: {IsInitialized}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   ActorId: {actorId}", _logger.ColorYellow);
        _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   EntityId: {entityId}", _logger.ColorYellow);
        
        if (position.HasValue)
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   Position: ({position.Value.X:F2}, {position.Value.Y:F2}, {position.Value.Z:F2})", _logger.ColorYellow);
        else
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   Position: N/A", _logger.ColorYellow);
        
        if (rotation.HasValue)
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   Rotation: ({rotation.Value.X:F2}, {rotation.Value.Y:F2}, {rotation.Value.Z:F2})", _logger.ColorYellow);
        else
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   Rotation: N/A", _logger.ColorYellow);
        
        if (forward.HasValue)
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   Forward: ({forward.Value.X:F2}, {forward.Value.Y:F2}, {forward.Value.Z:F2})", _logger.ColorYellow);
        else
            _logger.WriteLine($"[{_modConfig.ModId}] [PlayerApi]   Forward: N/A", _logger.ColorYellow);
    }
}
