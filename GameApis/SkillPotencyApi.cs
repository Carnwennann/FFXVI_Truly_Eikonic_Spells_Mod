using System.Collections.Concurrent;
using System.Diagnostics;
using Reloaded.Mod.Interfaces;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace ff16.gameplay.truly_eikonic_spells.GameApis;

/// <summary>
/// Centralized API for hooking Skill::GetPotencyParameter and caching skill potency values.
/// This allows all Eikon gauge APIs to share a single hook instead of each having their own.
/// 
/// Known Skill IDs:
/// - 0x1D (29) = BlindJustice max stacks
/// - 0x31 (49) = Zantetsuken max level  
/// - 0x36 (54) = SerpentsCry max tidal units
/// - 0x32 (50) = Megaflare max level (needs verification)
/// - 0x34 (52) = AbyssalTear max level (needs verification)
/// </summary>
public unsafe class SkillPotencyApi
{
    #region Signatures
    
    /// <summary>
    /// Signature for Skill::GetPotencyParameter function.
    /// __int64 __fastcall Skill::GetPotencyParameter(__int64 a1, unsigned int a2)
    /// Returns the potency value for a skill based on upgrade level.
    /// </summary>
    private const string SIG_GET_POTENCY_PARAMETER = 
        "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B FA E8";
    
    #endregion
    
    #region Skill IDs
    
    /// <summary>
    /// Skill ID for BlindJustice max stacks (Ramuh).
    /// Confirmed from GetBlindJusticeMaxGaugeMaxLevel: GetPotencyParameter(0x1D)
    /// </summary>
    public const uint SKILL_BLIND_JUSTICE = 0x1D; // 29
    
    /// <summary>
    /// Skill ID for Zantetsuken max level (Odin).
    /// Confirmed working.
    /// </summary>
    public const uint SKILL_ZANTETSUKEN = 0x31; // 49
    
    /// <summary>
    /// Skill ID for Megaflare max level (Bahamut).
    /// Confirmed from BattleUi::UpdateEikonGauges: GetPotencyParameter(0x27)
    /// </summary>
    public const uint SKILL_MEGAFLARE = 0x27; // 39
    
    /// <summary>
    /// Skill ID for AbyssalTear max level (Leviathan).
    /// Based on command table pattern - needs runtime verification.
    /// </summary>
    public const uint SKILL_ABYSSAL_TEAR = 0x39;   // 57 - estimated based on skill sequence
    
    /// <summary>
    /// Skill ID for SerpentsCry max tidal units (Leviathan).
    /// Confirmed working.
    /// </summary>
    public const uint SKILL_SERPENTS_CRY = 0x36;   // 54
    
    #endregion
    
    #region Delegates
    
    /// <summary>
    /// Delegate for Skill::GetPotencyParameter function.
    /// </summary>
    /// <param name="globalPtr">Global pointer</param>
    /// <param name="skillId">Skill ID</param>
    /// <returns>Potency value for the skill</returns>
    [Function(CallingConventions.Microsoft)]
    private delegate long GetPotencyParameterDelegate(long globalPtr, uint skillId);
    
    #endregion
    
    #region Fields
    
    private readonly ILogger? _logger;
    private readonly string _modId;
    
    private IHook<GetPotencyParameterDelegate>? _getPotencyHook;
    private nint _baseAddress;
    
    // Thread-safe cache for skill potency values
    private readonly ConcurrentDictionary<uint, int> _cachedPotencies = new();
    
    // Default values for each skill
    private static readonly Dictionary<uint, int> _defaultValues = new()
    {
        { SKILL_BLIND_JUSTICE, 9 },    // Default max stacks (upgraded)
        { SKILL_ZANTETSUKEN, 4 },      // Default max level (upgraded)
        { SKILL_MEGAFLARE, 4 },        // Default max level (upgraded)
        { SKILL_ABYSSAL_TEAR, 4 },     // Default max level (upgraded)
        { SKILL_SERPENTS_CRY, 150 },   // Default max tidal (upgraded)
    };
    
    // Track which skills we're interested in
    private static readonly HashSet<uint> _trackedSkills = new()
    {
        SKILL_BLIND_JUSTICE,
        SKILL_ZANTETSUKEN,
        SKILL_MEGAFLARE,
        SKILL_ABYSSAL_TEAR,
        SKILL_SERPENTS_CRY,
    };
    
    #endregion
    
    #region Constructor
    
    public SkillPotencyApi(ILogger? logger = null, string modId = "")
    {
        _logger = logger;
        _modId = modId;
        _baseAddress = Process.GetCurrentProcess().MainModule!.BaseAddress;
    }
    
    #endregion
    
    #region Setup
    
    /// <summary>
    /// Set up signature scans and hooks for Skill::GetPotencyParameter.
    /// </summary>
    public void SetupScans(IStartupScanner scans, IReloadedHooks hooks)
    {
        scans.AddMainModuleScan(SIG_GET_POTENCY_PARAMETER, result =>
        {
            if (!result.Found)
            {
                _logger?.WriteLine($"[{_modId}] [SkillPotency] FAILED to find Skill::GetPotencyParameter", _logger.ColorRed);
                return;
            }
            var addr = (nint)(_baseAddress + result.Offset);
            _getPotencyHook = hooks.CreateHook<GetPotencyParameterDelegate>(GetPotencyImpl, addr).Activate();
            _logger?.WriteLine($"[{_modId}] [SkillPotency] Hooked Skill::GetPotencyParameter at 0x{addr:X}", _logger.ColorGreen);
        });
    }
    
    /// <summary>
    /// Hook implementation for Skill::GetPotencyParameter.
    /// Caches the result for tracked skill IDs.
    /// </summary>
    private long GetPotencyImpl(long globalPtr, uint skillId)
    {
        // Call original function
        long result = _getPotencyHook!.OriginalFunction(globalPtr, skillId);
        
        // DEBUG: Log ALL skill IDs being queried (to find AbyssalTear)
        if (result > 0 && result <= 20 && !_trackedSkills.Contains(skillId))
        {
            _logger?.WriteLine($"[{_modId}] [SkillPotency] UNKNOWN skill 0x{skillId:X} ({skillId}): {result}", _logger.ColorYellow);
        }
        
        // Cache if this is a tracked skill and result is valid
        if (_trackedSkills.Contains(skillId) && result > 0 && result <= 1000)
        {
            int oldValue = _cachedPotencies.GetValueOrDefault(skillId, -1);
            _cachedPotencies[skillId] = (int)result;
            
            // Log only when value changes or first time captured
            if (oldValue != (int)result)
            {
                _logger?.WriteLine($"[{_modId}] [SkillPotency] Captured skill 0x{skillId:X} ({skillId}): {result}", _logger.ColorGreen);
            }
        }
        
        return result;
    }
    
    #endregion
    
    #region Public API
    
    /// <summary>
    /// Get the cached potency value for a skill.
    /// Returns the default value if not yet cached.
    /// </summary>
    /// <param name="skillId">The skill ID to look up</param>
    /// <returns>The cached potency value or default</returns>
    public int GetPotency(uint skillId)
    {
        if (_cachedPotencies.TryGetValue(skillId, out int cached))
        {
            return cached;
        }
        
        if (_defaultValues.TryGetValue(skillId, out int defaultVal))
        {
            return defaultVal;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Get BlindJustice max stacks.
    /// </summary>
    public int BlindJusticeMaxStacks => GetPotency(SKILL_BLIND_JUSTICE);
    
    /// <summary>
    /// Get Zantetsuken max level.
    /// </summary>
    public int ZantetsukenMaxLevel => GetPotency(SKILL_ZANTETSUKEN);
    
    /// <summary>
    /// Get Megaflare max level.
    /// </summary>
    public int MegaflareMaxLevel => GetPotency(SKILL_MEGAFLARE);
    
    /// <summary>
    /// Get AbyssalTear max level.
    /// </summary>
    public int AbyssalTearMaxLevel => GetPotency(SKILL_ABYSSAL_TEAR);
    
    /// <summary>
    /// Get SerpentsCry max tidal units.
    /// </summary>
    public int SerpentsCryMaxUnits => GetPotency(SKILL_SERPENTS_CRY);
    
    /// <summary>
    /// Check if the hook is active.
    /// </summary>
    public bool IsHookActive => _getPotencyHook != null;
    
    /// <summary>
    /// Log all cached potency values.
    /// </summary>
    public void LogState()
    {
        if (_logger == null) return;
        
        _logger.WriteLine($"[{_modId}] [SkillPotency] Hook Active: {IsHookActive}");
        _logger.WriteLine($"[{_modId}] [SkillPotency] Cached values:");
        _logger.WriteLine($"[{_modId}]   BlindJustice (0x1D): {BlindJusticeMaxStacks}");
        _logger.WriteLine($"[{_modId}]   Zantetsuken (0x31): {ZantetsukenMaxLevel}");
        _logger.WriteLine($"[{_modId}]   Megaflare (0x27): {MegaflareMaxLevel}");
        _logger.WriteLine($"[{_modId}]   AbyssalTear (0x35): {AbyssalTearMaxLevel}");
        _logger.WriteLine($"[{_modId}]   SerpentsCry (0x36): {SerpentsCryMaxUnits}");
    }
    
    #endregion
}
