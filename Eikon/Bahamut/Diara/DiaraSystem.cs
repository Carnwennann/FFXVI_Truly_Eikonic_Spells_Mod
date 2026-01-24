using System.Diagnostics;
using System.IO;
using Reloaded.Mod.Interfaces;
using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.GameApis;
using ff16.gameplay.truly_eikonic_spells.GameApis.Magic;
using ff16.gameplay.truly_eikonic_spells.Utils;

namespace ff16.gameplay.truly_eikonic_spells;

/// <summary>
/// Diara System: Charged shot buff that triggers Dia spells on perfect dodge
/// 
/// MECHANIC:
/// - Casting Diara (Charged Shot with Bahamut) activates a buff
/// - During buff, each Perfect Dodge fires 5 Dia spells at locked enemy
/// - Diara itself doesn't spawn its normal projectile
/// - Wings of Bahamut visual effect during buff (cosmetic)
/// 
/// SYNERGIES:
/// - Each spawned Dia triggers Satellites
/// - Enhances dodge gameplay while staying proactive
/// </summary>
public class DiaraSystem
{
    // Buff state
    private bool _isBuffActive = false;
    private readonly Stopwatch _buffTimer = new();
    
    // Logger for debug output
    private readonly ILogger? _logger;
    private readonly string _modId;
    public bool DebugLogging { get; set; } = true;
    
    // Configuration (settable for hot-reload)
    public float BuffDurationSeconds { get; set; }
    public int DiaSpellsPerDodge { get; set; }
    public int MagicID { get; set; }  // Dia magic ID for projectiles
    public float FanAngleStep { get; set; } // Degrees between spells in fan
    
    // Action IDs
    public const int CHARGED_SHOT_ACTION_ID = 227;
    public const int DIA_MAGIC_ID = 214;
    
    // Events for main mod to handle
    public event Action? OnBuffActivated;
    public event Action? OnBuffDeactivated;
    public event Action<int>? OnPerfectDodgeWithBuff;  // int = number of Dia spells to spawn
    
    // Legacy logging delegate (for backwards compatibility) - will be removed
    public Action<string>? Log;
    
    // Reference to MagicApi for spawning projectiles
    private IMagicApi? _magicApi;
    
    // Path to the configuration file
    private string? _configFilePath;
    
    public DiaraSystem(float buffDurationSeconds = 120.0f, int diaSpellsPerDodge = 5, int magicID = 214, float fanAngleStep = 15.0f, ILogger? logger = null, string modId = "")
    {
        BuffDurationSeconds = buffDurationSeconds;
        DiaSpellsPerDodge = diaSpellsPerDodge;
        MagicID = magicID;
        FanAngleStep = fanAngleStep;
        _logger = logger;
        _modId = modId;
    }
    
    public void SetMagicApi(IMagicApi magicApi)
    {
        _magicApi = magicApi;
        
        // Register charged shot handler
        _magicApi.RegisterChargedShotHandler(OnChargedShotCast);
        LogDebug("MagicApi linked, charged shot handler registered");
        
        // Determine path to config file
        _configFilePath = Path.Combine(
            Path.GetDirectoryName(typeof(DiaraSystem).Assembly.Location) ?? "",
            "Eikon", "Bahamut", "Diara", "DiaModifications.json");
        
        if (File.Exists(_configFilePath))
        {
            LogDebug($"Found spell config at: {_configFilePath}");
        }
        else
        {
            LogDebug($"Spell config not found at: {_configFilePath}");
            _configFilePath = null;
        }
    }
    
    #region Logging
    
    private void LogInfo(string message)
    {
        if (!DebugLogging) return;
        
        if (_logger != null)
            _logger.WriteLine($"[{_modId}] [DIARA] {message}", _logger.ColorGreen);
        else
            Log?.Invoke($"[DIARA] {message}");
    }
    
    private void LogDebug(string message)
    {
        if (!DebugLogging) return;
        
        if (_logger != null)
            _logger.WriteLine($"[{_modId}] [DIARA] {message}", _logger.ColorYellow);
        else
            Log?.Invoke($"[DIARA] {message}");
    }
    
    #endregion
    
    // Last target tracking for projectile spawning
    public long LastAttackedEnemyPtr { get; private set; }
    public long LastAttackR15 { get; private set; }
    
    /// <summary>
    /// Check if Diara buff is currently active
    /// </summary>
    public bool IsBuffActive => _isBuffActive && _buffTimer.Elapsed.TotalSeconds < BuffDurationSeconds;
    
    /// <summary>
    /// Get remaining buff time in seconds
    /// </summary>
    public float RemainingBuffTime => _isBuffActive 
        ? Math.Max(0, BuffDurationSeconds - (float)_buffTimer.Elapsed.TotalSeconds) 
        : 0;
    
    #region Main Hook Entry Points
    
    /// <summary>
    /// Called from TrulyEikonicSpells.OnHitImpl - handles all Diara logic
    /// </summary>
    public void OnHit(long targetPtr, int actionId, long R15, bool isEnabled)
    {
        // Always update timer (even if disabled, to properly expire buffs)
        Update();
        
        if (!isEnabled) return;
        
        // Track magic shot targets for projectile spawning
        if (ActionIds.IsMagicShot(actionId))
        {
            LastAttackedEnemyPtr = targetPtr;
            LastAttackR15 = R15;
            LogDebug($"Magic shot tracked: Target=0x{targetPtr:X}");
        }
    }
    
    #endregion
    
    /// <summary>
    /// Called when Clive casts Charged Shot (227) with Bahamut active
    /// Returns true if the normal projectile should be SUPPRESSED
    /// </summary>
    public bool OnChargedShotCast(int activeEikon)
    {
        // Only activate with Bahamut
        if (activeEikon != EikonUtils.EIKON_BAHAMUT)
            return false;
        
        // Activate Diara buff
        ActivateBuff();
        
        // Return true to indicate we want to suppress the normal projectile
        return true;
    }
    
    /// <summary>
    /// Called when a perfect dodge occurs.
    /// Spawns Dia projectiles using the MagicApi for modified properties.
    /// Returns the number of Dia spells spawned (0 if buff not active).
    /// </summary>
    public int OnPerfectDodge()
    {
        if (!IsBuffActive || _magicApi == null)
            return 0;
        
        if (!_magicApi.IsReady)
        {
            LogDebug("Failed to spawn Dia spells - MagicApi context not ready. (Try firing a normal shot first)");
            return 0;
        }
        
        LogInfo($"Perfect Dodge! Spawning {DiaSpellsPerDodge} Dia spells!");
        
        // Cast Dia spells in a fan pattern using the new API
        int successCount = CastFanPattern();
        
        if (successCount > 0)
        {
            LogInfo($"Successfully cast {successCount} Dia spells in a fan!");
            OnPerfectDodgeWithBuff?.Invoke(successCount);
        }
        else
        {
            LogDebug("Failed to spawn Dia spells");
        }
        
        return successCount;
    }
    
    /// <summary>
    /// Cast Dia spells in a fan pattern using the new MagicApi.
    /// </summary>
    private int CastFanPattern()
    {
        if (_magicApi == null) return 0;
        
        // Calculate start angle to center the fan
        float startAngle = -(DiaSpellsPerDodge - 1) * FanAngleStep / 2f;
        int successCount = 0;
        
        for (int i = 0; i < DiaSpellsPerDodge; i++)
        {
            float currentAngle = startAngle + (i * FanAngleStep);
            
            // Create spell with the builder and import base modifications from JSON
            var builder = _magicApi.CreateSpell(MagicID);
            
            // Import modifications from config file (API handles all parsing)
            if (_configFilePath != null)
            {
                try
                {
                    builder.ImportFromFile(_configFilePath);
                }
                catch (Exception ex)
                {
                    LogDebug($"Failed to import config: {ex.Message}");
                }
            }
            
            // For secondary projectiles (i > 0), add "slave" property to reduce audio/visual clutter
            // Op 51 (Initialize) Property 69 (SFXEnable) = 0
            if (i > 0)
            {
                builder.SetProperty(4338, 51, 69, 0);
            }

            // Override trajectory angle for fan pattern (this will replace the one from JSON)
            builder.SetProperty(4338, 2493, 2430, new System.Numerics.Vector3(-90f, 0f, currentAngle));
            
            LogDebug($"Projectile {i}: Angle {currentAngle:F2}");
            
            if (builder.Cast())
            {
                successCount++;
            }
        }
        
        return successCount;
    }
    
    /// <summary>
    /// Activate the Diara buff
    /// </summary>
    private void ActivateBuff()
    {
        _isBuffActive = true;
        _buffTimer.Restart();
        
        LogInfo($"Buff activated! ({BuffDurationSeconds}s duration)");
        OnBuffActivated?.Invoke();
    }
    
    /// <summary>
    /// Deactivate the Diara buff (called when timer expires or manually)
    /// </summary>
    public void DeactivateBuff()
    {
        if (_isBuffActive)
        {
            _isBuffActive = false;
            _buffTimer.Stop();
            
            LogInfo("Buff expired");
            OnBuffDeactivated?.Invoke();
        }
    }
    
    /// <summary>
    /// Check buff timer and deactivate if expired
    /// Should be called periodically (e.g., from a game loop hook)
    /// </summary>
    public void Update()
    {
        if (_isBuffActive && _buffTimer.Elapsed.TotalSeconds >= BuffDurationSeconds)
        {
            DeactivateBuff();
        }
    }
    
    /// <summary>
    /// Get number of Dia spells that would spawn per perfect dodge
    /// </summary>
    public int GetPendingSpellCount()
    {
        return IsBuffActive ? DiaSpellsPerDodge : 0;
    }
    
    /// <summary>
    /// Consume pending spells (used when spawn is disabled for debugging)
    /// </summary>
    public void ConsumeSpells()
    {
        // For now just log, the spells are "consumed" by not being spawned
        LogDebug("Spells consumed (debug mode)");
    }
    
    /// <summary>
    /// Reset system state (e.g., on level load)
    /// </summary>
    public void Reset()
    {
        _isBuffActive = false;
        _buffTimer.Reset();
    }

    public void UpdateConfiguration(Config configuration)
    {
        // Check for changes and log
        if (BuffDurationSeconds != configuration.DiaraBuffDuration)
        {
            LogDebug($"BuffDurationSeconds changed: {BuffDurationSeconds} -> {configuration.DiaraBuffDuration}");
            BuffDurationSeconds = configuration.DiaraBuffDuration;
        }
        if (DiaSpellsPerDodge != configuration.DiaSpellsPerDodge)
        {
            LogDebug($"DiaSpellsPerDodge changed: {DiaSpellsPerDodge} -> {configuration.DiaSpellsPerDodge}");
            DiaSpellsPerDodge = configuration.DiaSpellsPerDodge;
        }
        if (MagicID != configuration.DiaMagicID)
        {
            LogDebug($"MagicID changed: {MagicID} -> {configuration.DiaMagicID}");
            MagicID = configuration.DiaMagicID;
        }
        if (FanAngleStep != configuration.DiaFanAngleStep)
        {
            LogDebug($"FanAngleStep changed: {FanAngleStep} -> {configuration.DiaFanAngleStep}");
            FanAngleStep = configuration.DiaFanAngleStep;
        }
    }
}
