using System.Diagnostics;

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
    
    // Configuration (settable for hot-reload)
    public float BuffDurationSeconds { get; set; }
    public int DiaSpellsPerDodge { get; set; }
    
    // Action IDs
    public const int CHARGED_SHOT_ACTION_ID = 227;
    
    // Events for main mod to handle
    public event Action? OnBuffActivated;
    public event Action? OnBuffDeactivated;
    public event Action<int>? OnPerfectDodgeWithBuff;  // int = number of Dia spells to spawn
    
    // Logging delegate (set by main mod) - simple string only
    public Action<string>? Log;
    
    public DiaraSystem(float buffDurationSeconds = 120.0f, int diaSpellsPerDodge = 5)
    {
        BuffDurationSeconds = buffDurationSeconds;
        DiaSpellsPerDodge = diaSpellsPerDodge;
    }
    
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
    /// Called when a perfect dodge occurs
    /// Returns the number of Dia spells to spawn (0 if buff not active)
    /// </summary>
    public int OnPerfectDodge()
    {
        if (!IsBuffActive)
            return 0;
        
        Log?.Invoke($"[DIARA] Perfect Dodge! Spawning {DiaSpellsPerDodge} Dia spells!");
        OnPerfectDodgeWithBuff?.Invoke(DiaSpellsPerDodge);
        
        return DiaSpellsPerDodge;
    }
    
    /// <summary>
    /// Activate the Diara buff
    /// </summary>
    private void ActivateBuff()
    {
        _isBuffActive = true;
        _buffTimer.Restart();
        
        Log?.Invoke($"[DIARA] Buff activated! ({BuffDurationSeconds}s duration)");
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
            
            Log?.Invoke("[DIARA] Buff expired");
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
        Log?.Invoke("[DIARA] Spells consumed (debug mode)");
    }
    
    /// <summary>
    /// Reset system state (e.g., on level load)
    /// </summary>
    public void Reset()
    {
        _isBuffActive = false;
        _buffTimer.Reset();
    }
}
