using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.GameApis;
using ff16.gameplay.truly_eikonic_spells.GameApis.EikonGauges;
using ff16.gameplay.truly_eikonic_spells.GameApis.Magic;
using ff16.gameplay.truly_eikonic_spells.GameApis.Magic.MagicFile;
using NenTools.ImGui.Interfaces;
using NenTools.ImGui.Interfaces.Shell;
using System.Numerics;

namespace ff16.gameplay.truly_eikonic_spells.Configuration;

/// <summary>
/// Container for all Eikon APIs for the ImGui tester.
/// </summary>
public class EikonApiContainer
{
    public BlindJusticeApi? BlindJustice { get; set; }
    public AbyssalTearApi? AbyssalTear { get; set; }
    public SerpentsCryApi? SerpentsCry { get; set; }
    public ZantetsukenApi? Zantetsuken { get; set; }
    public MegaflareApi? Megaflare { get; set; }
}

[ImGuiMenu(Category = "Mods", Priority = 100, Owner = "Truly Eikonic Spells")]
public class ImGuiConfigurator : IImGuiComponent
{
    public bool IsOverlay => true;

    private readonly IImGui _imgui;
    private Config _config;
    private readonly Action<Config> _onConfigChanged;
    private readonly IMagicApi _magicApi;
    private EikonApiContainer? _eikonApis;

    public ImGuiConfigurator(IImGui imgui, Config config, Action<Config> onConfigChanged, IMagicApi magicApi)
    {
        _imgui = imgui;
        _config = config;
        _onConfigChanged = onConfigChanged;
        _magicApi = magicApi;
    }
    
    /// <summary>
    /// Set the Eikon APIs for the API tester tab.
    /// </summary>
    public void SetEikonApis(EikonApiContainer apis)
    {
        _eikonApis = apis;
    }

    public void RenderMenu(IImGuiShell imGuiShell)
    {
        if (_imgui.MenuItem("Truly Eikonic Spells"))
        {
            _isWindowOpen = true;
        }
    }

    private bool _isWindowOpen = false;

    public void Render(IImGuiShell imguiShell)
    {
        //if (!_isWindowOpen) return;

        _imgui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.ImGuiCond_FirstUseEver);
        if (_imgui.Begin("Truly Eikonic Spells Configuration", ref _isWindowOpen, ImGuiWindowFlags.ImGuiWindowFlags_None))
        {
            if (_imgui.BeginTabBar("MainTabs", ImGuiTabBarFlags.ImGuiTabBarFlags_None))
            {
                bool dummy = true;
                if (_imgui.BeginTabItem("Bahamut (Dia/Diara)", ref dummy, ImGuiTabItemFlags.ImGuiTabItemFlags_None))
                {
                    RenderBahamutTab();
                    _imgui.EndTabItem();
                }

                if (_imgui.BeginTabItem("Odin (Darkra)", ref dummy, ImGuiTabItemFlags.ImGuiTabItemFlags_None))
                {
                    RenderOdinTab();
                    _imgui.EndTabItem();
                }

                if (_imgui.BeginTabItem("Physics & Reactions", ref dummy, ImGuiTabItemFlags.ImGuiTabItemFlags_None))
                {
                    RenderPhysicsTab();
                    _imgui.EndTabItem();
                }

                if (_imgui.BeginTabItem("Magic Overrides", ref dummy, ImGuiTabItemFlags.ImGuiTabItemFlags_None))
                {
                    RenderMagicOverridesTab();
                    _imgui.EndTabItem();
                }

                if (_imgui.BeginTabItem("Magic Tester", ref dummy, ImGuiTabItemFlags.ImGuiTabItemFlags_None))
                {
                    RenderMagicTesterTab();
                    _imgui.EndTabItem();
                }

                if (_imgui.BeginTabItem("VFX Tester", ref dummy, ImGuiTabItemFlags.ImGuiTabItemFlags_None))
                {
                    RenderVfxTesterTab();
                    _imgui.EndTabItem();
                }

                if (_imgui.BeginTabItem("Eikon API Tester", ref dummy, ImGuiTabItemFlags.ImGuiTabItemFlags_None))
                {
                    RenderEikonApiTesterTab();
                    _imgui.EndTabItem();
                }

                if (_imgui.BeginTabItem("Debug & Fuzzer", ref dummy, ImGuiTabItemFlags.ImGuiTabItemFlags_None))
                {
                    RenderDebugTab();
                    _imgui.EndTabItem();
                }

                _imgui.EndTabBar();
            }

            _imgui.Separator();
            if (_imgui.Button("Save Configuration"))
            {
                _onConfigChanged(_config);
            }
        }
        _imgui.End();
    }

    private void RenderBahamutTab()
    {
        _imgui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Dia System");
        bool enableDia = _config.EnableDiaSystem;
        if (_imgui.Checkbox("Enable Dia System", ref enableDia)) _config.EnableDiaSystem = enableDia;
        
        int maxStacks = _config.MaxDiaStacks;
        if (_imgui.InputInt("Max Dia Stacks", ref maxStacks)) _config.MaxDiaStacks = maxStacks;
        
        float dmgPerStack = _config.DiaDamagePerStack;
        if (_imgui.InputFloat("Damage Per Stack", ref dmgPerStack)) _config.DiaDamagePerStack = dmgPerStack;

        _imgui.Separator();
        _imgui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Diara System");
        bool enableDiara = _config.EnableDiaraSystem;
        if (_imgui.Checkbox("Enable Diara System", ref enableDiara)) _config.EnableDiaraSystem = enableDiara;
        
        float diaraDuration = _config.DiaraBuffDuration;
        if (_imgui.InputFloat("Diara Buff Duration (s)", ref diaraDuration)) _config.DiaraBuffDuration = diaraDuration;
        
        int spellsPerDodge = _config.DiaSpellsPerDodge;
        if (_imgui.InputInt("Dia Spells Per Dodge", ref spellsPerDodge)) _config.DiaSpellsPerDodge = spellsPerDodge;
        
        int diaMagicId = _config.DiaMagicID;
        if (_imgui.InputInt("Dia Magic ID", ref diaMagicId)) _config.DiaMagicID = diaMagicId;
    }

    private void RenderOdinTab()
    {
        _imgui.TextColored(new Vector4(0.8f, 0.4f, 1.0f, 1.0f), "Darkra System");
        bool enableDarkra = _config.EnableDarkraSystem;
        if (_imgui.Checkbox("Enable Darkra System", ref enableDarkra)) _config.EnableDarkraSystem = enableDarkra;
        
        float shadowMult = _config.ShadowHitMultiplier;
        if (_imgui.InputFloat("Shadow Hit Multiplier", ref shadowMult)) _config.ShadowHitMultiplier = shadowMult;
        
        float shadowDuration = _config.ShadowDebuffDuration;
        if (_imgui.InputFloat("Shadow Debuff Duration (s)", ref shadowDuration)) _config.ShadowDebuffDuration = shadowDuration;
        
        int shadowDelay = _config.ShadowHitDelayMs;
        if (_imgui.InputInt("Shadow Hit Delay (ms)", ref shadowDelay)) _config.ShadowHitDelayMs = shadowDelay;

        _imgui.Separator();
        _imgui.TextColored(new Vector4(0.8f, 0.4f, 1.0f, 1.0f), "Shadow Hit Reactions");
        int reactionType = _config.ShadowHitReactionType;
        if (_imgui.InputInt("Reaction Animation ID", ref reactionType)) _config.ShadowHitReactionType = reactionType;
        
        int reactionIntensity = _config.ShadowHitReactionIntensity;
        if (_imgui.InputInt("Push Direction ID", ref reactionIntensity)) _config.ShadowHitReactionIntensity = reactionIntensity;

        _imgui.Separator();
        _imgui.TextColored(new Vector4(0.8f, 0.4f, 1.0f, 1.0f), "Juggle Physics");
        bool juggleEnabled = _config.ShadowHitJuggleEnabled;
        if (_imgui.Checkbox("Enable Shadow Juggle", ref juggleEnabled)) _config.ShadowHitJuggleEnabled = juggleEnabled;
        
        int juggleAnim = _config.ShadowHitJuggleAnimId;
        if (_imgui.InputInt("Juggle Anim ID", ref juggleAnim)) _config.ShadowHitJuggleAnimId = juggleAnim;
        
        float vPush = _config.ShadowHitJuggleVerticalPush;
        if (_imgui.InputFloat("Vertical Push", ref vPush)) _config.ShadowHitJuggleVerticalPush = vPush;
        
        float fPush = _config.ShadowHitJuggleForwardPush;
        if (_imgui.InputFloat("Forward Push", ref fPush)) _config.ShadowHitJuggleForwardPush = fPush;

        _imgui.Separator();
        _imgui.TextColored(new Vector4(0.8f, 0.4f, 1.0f, 1.0f), "Zantetsuken Ticks");
        bool ticksEnabled = _config.ShadowHitZantetsukenTicksEnabled;
        if (_imgui.Checkbox("Enable Ticks on Shadow Hit", ref ticksEnabled)) _config.ShadowHitZantetsukenTicksEnabled = ticksEnabled;
        
        int tickAmount = _config.ShadowHitZantetsukenTickAmount;
        if (_imgui.InputInt("Gauge Per Hit", ref tickAmount)) _config.ShadowHitZantetsukenTickAmount = tickAmount;
    }

    private void RenderPhysicsTab()
    {
        _imgui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), "Global Physics Overrides");
        bool enablePhys = _config.EnablePhysicsModification;
        if (_imgui.Checkbox("Enable Physics Modification", ref enablePhys)) _config.EnablePhysicsModification = enablePhys;

        int forcePush = _config.PhysicsForcePushDirection;
        if (_imgui.InputInt("Force Push Direction (-1=Off)", ref forcePush)) _config.PhysicsForcePushDirection = forcePush;

        _imgui.Separator();
        _imgui.Text("SystemMove Column Overrides (-999 = Default)");
        float fPush = _config.PhysicsForwardPushOverride;
        if (_imgui.InputFloat("Forward Push", ref fPush)) _config.PhysicsForwardPushOverride = fPush;
        
        float fDur = _config.PhysicsForwardDurationOverride;
        if (_imgui.InputFloat("Forward Duration", ref fDur)) _config.PhysicsForwardDurationOverride = fDur;
        
        float vPush = _config.PhysicsVerticalPushOverride;
        if (_imgui.InputFloat("Vertical Push", ref vPush)) _config.PhysicsVerticalPushOverride = vPush;
        
        float vInt = _config.PhysicsVerticalInterpolationOverride;
        if (_imgui.InputFloat("Vertical Interpolation", ref vInt)) _config.PhysicsVerticalInterpolationOverride = vInt;

        _imgui.Separator();
        _imgui.Text("Physics Flags");
        RenderTriState("Flag 0x2800 (Reaction)", () => _config.PhysicsFlag0x2800, (v) => _config.PhysicsFlag0x2800 = v);
        RenderTriState("Flag 0x10 (bit4)", () => _config.PhysicsFlagBit4, (v) => _config.PhysicsFlagBit4 = v);
        RenderTriState("Flag 0x1000 (bit12 - Stagger)", () => _config.PhysicsFlagBit12, (v) => _config.PhysicsFlagBit12 = v);
    }

    private void RenderMagicOverridesTab()
    {
        _imgui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Position Overrides");
        bool enablePos = _config.EnablePositionOverrides;
        if (_imgui.Checkbox("Enable Position Overrides", ref enablePos)) _config.EnablePositionOverrides = enablePos;
        
        float posX = _config.PositionXOffset;
        if (_imgui.InputFloat("X Offset", ref posX)) _config.PositionXOffset = posX;
        
        float posY = _config.PositionYOffset;
        if (_imgui.InputFloat("Y Offset", ref posY)) _config.PositionYOffset = posY;
        
        float posZ = _config.PositionZOffset;
        if (_imgui.InputFloat("Z Offset", ref posZ)) _config.PositionZOffset = posZ;
    }

    private void RenderDebugTab()
    {
        _imgui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Universal Property Fuzzer");
        bool enableFuzzer = _config.EnableUniversalFuzzer;
        if (_imgui.Checkbox("Enable Universal Fuzzer", ref enableFuzzer)) _config.EnableUniversalFuzzer = enableFuzzer;
        
        bool enableLogging = _config.EnablePropertyLogging;
        if (_imgui.Checkbox("Enable Property Logging", ref enableLogging)) _config.EnablePropertyLogging = enableLogging;
        _imgui.SameLine();
        _imgui.TextDisabled("(?)");
        if (_imgui.IsItemHovered(ImGuiHoveredFlags.ImGuiHoveredFlags_None)) _imgui.SetTooltip("Logs all magic property values. Can impact performance.");
        
        if (_imgui.Button("Add New Magic Mod Entry"))
        {
            if (_config.MagicModEntries.Count > 0)
            {
                // Copy the last entry
                var lastEntry = _config.MagicModEntries[_config.MagicModEntries.Count - 1];
                _config.MagicModEntries.Add(lastEntry.Clone());
            }
            else
            {
                _config.MagicModEntries.Add(new MagicModEntry());
            }
        }

        for (int i = 0; i < _config.MagicModEntries.Count; i++)
        {
            var entry = _config.MagicModEntries[i];
            
            if (_imgui.CollapsingHeader($"Entry {i}: Prop {entry.PropertyId}##Header_{i}", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen))
            {
                bool enabled = entry.Enabled;
                if (_imgui.Checkbox($"Enabled##{i}", ref enabled)) entry.Enabled = enabled;
                
                _imgui.SameLine();
                if (!entry.DisableOp)
                {
                    bool isInjection = entry.IsInjection;
                    if (_imgui.Checkbox($"Inject##{i}", ref isInjection)) entry.IsInjection = isInjection;
                    _imgui.SameLine();
                }

                if (!entry.IsInjection)
                {
                    bool disableOp = entry.DisableOp;
                    if (_imgui.Checkbox($"Disable##{i}", ref disableOp)) entry.DisableOp = disableOp;
                    _imgui.SameLine();
                }

                if (_imgui.Button($"Remove##{i}"))
                {
                    _config.MagicModEntries.RemoveAt(i);
                    break;
                }

                int targetId = entry.TargetMagicId;
                if (_imgui.InputInt($"Target Magic ID (-1=All)##{i}", ref targetId)) entry.TargetMagicId = targetId;

                int opType = entry.OpType;
                if (_imgui.InputInt($"Op Type (-1=Any)##{i}", ref opType)) entry.OpType = opType;

                int occurrence = entry.Occurrence;
                if (_imgui.InputInt($"Occurrence (-1=All)##{i}", ref occurrence)) entry.Occurrence = occurrence;

                if (entry.IsInjection)
                {
                    int afterOp = entry.InjectAfterOp;
                    if (_imgui.InputInt($"Inject After Op (-1=End)##{i}", ref afterOp)) entry.InjectAfterOp = afterOp;
                }

                int propId = entry.PropertyId;
                string propLabel = entry.DisableOp ? $"Property ID (-1=All Op)##{i}" : $"Property ID##{i}";
                if (_imgui.InputInt(propLabel, ref propId)) entry.PropertyId = propId;

                if (!entry.DisableOp)
                {
                    // Value Type (radio buttons to avoid popup asserts)
                    int currentType = entry.UseVec3 ? 2 : (entry.UseFloat ? 1 : 0);
                    _imgui.Text("Value Type:");
                    _imgui.SameLine();
                    if (_imgui.RadioButton($"Int##{i}", currentType == 0))
                    {
                        entry.UseFloat = false;
                        entry.UseVec3 = false;
                    }
                    _imgui.SameLine();
                    if (_imgui.RadioButton($"Float##{i}", currentType == 1))
                    {
                        entry.UseFloat = true;
                        entry.UseVec3 = false;
                    }
                    _imgui.SameLine();
                    if (_imgui.RadioButton($"Vec3##{i}", currentType == 2))
                    {
                        entry.UseFloat = false;
                        entry.UseVec3 = true;
                    }

                    if (entry.UseVec3)
                    {
                        float vX = entry.Vec3X;
                        float vY = entry.Vec3Y;
                        float vZ = entry.Vec3Z;

                        _imgui.Text("Vector3:");
                        _imgui.SetNextItemWidth(100);
                        if (_imgui.InputFloat($"X##{i}", ref vX)) entry.Vec3X = vX;
                        _imgui.SameLine();
                        _imgui.SetNextItemWidth(100);
                        if (_imgui.InputFloat($"Y##{i}", ref vY)) entry.Vec3Y = vY;
                        _imgui.SameLine();
                        _imgui.SetNextItemWidth(100);
                        if (_imgui.InputFloat($"Z##{i}", ref vZ)) entry.Vec3Z = vZ;
                    }
                    else if (entry.UseFloat)
                    {
                        float fVal = entry.FloatValue;
                        if (_imgui.InputFloat($"Float Value##{i}", ref fVal)) entry.FloatValue = fVal;
                    }
                    else
                    {
                        int iVal = entry.IntValue;
                        if (_imgui.InputInt($"Int Value##{i}", ref iVal)) entry.IntValue = iVal;
                    }
                }
            }
            
            _imgui.Separator();
        }

        _imgui.Separator();
        _imgui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "General Debug");
        bool debugLog = _config.DebugLogging;
        if (_imgui.Checkbox("Enable Debug Logging", ref debugLog)) _config.DebugLogging = debugLog;
    }

    private void RenderMagicTesterTab()
    {
        _imgui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Magic Spawning Tester (New API)");
        _imgui.TextWrapped("Use this panel to test the new Magic API. Fire at least one spell in-game to capture context.");
        
        _imgui.Separator();

        // Show API status
        _imgui.Text("API Status:");
        _imgui.SameLine();
        if (_magicApi.IsReady)
        {
            _imgui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "READY");
        }
        else
        {
            _imgui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "NO CONTEXT - Fire a spell first!");
        }
        
        _imgui.Separator();

        int testId = _config.TestMagicID;
        if (_imgui.InputInt("Magic ID To Spawn", ref testId)) _config.TestMagicID = testId;
        
        int testCount = _config.TestMagicCount;
        if (_imgui.InputInt("Number of Projectiles", ref testCount)) _config.TestMagicCount = testCount;

        _imgui.Separator();
        _imgui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), "Cast Methods:");

        // Cast targeting locked enemy (uses game's targeting for correct body position)
        if (_imgui.Button("Cast (Player -> Enemy)"))
        {
            if (_magicApi.IsReady)
            {
                for (int i = 0; i < _config.TestMagicCount; i++)
                {
                    nint playerActor = _magicApi.GetPlayerActor();
                    _magicApi.CastWithGameTarget(_config.TestMagicID, sourceActor: playerActor);
                }
            }
        }
        if (_imgui.IsItemHovered(ImGuiHoveredFlags.ImGuiHoveredFlags_None))
        {
            _imgui.SetTooltip("Casts from player TO locked enemy (uses game's targeting for body position).");
        }
        _imgui.SameLine();

        // Cast from player, no target (spell goes forward)
        if (_imgui.Button("Cast (Player -> No Target)"))
        {
            if (_magicApi.IsReady)
            {
                for (int i = 0; i < _config.TestMagicCount; i++)
                {
                    nint playerActor = _magicApi.GetPlayerActor();
                    _magicApi.Cast(_config.TestMagicID, sourceActor: playerActor, targetActor: nint.Zero);
                }
            }
        }
        if (_imgui.IsItemHovered(ImGuiHoveredFlags.ImGuiHoveredFlags_None))
        {
            _imgui.SetTooltip("Casts from player with NO target (spell goes straight ahead).");
        }
        _imgui.SameLine();

        // Cast from enemy targeting player
        if (_imgui.Button("Cast (Enemy -> Player)"))
        {
            if (_magicApi.IsReady)
            {
                nint enemyActor = _magicApi.GetLockedTarget();
                nint playerActor = _magicApi.GetPlayerActor();
                if (enemyActor != nint.Zero && playerActor != nint.Zero)
                {
                    for (int i = 0; i < _config.TestMagicCount; i++)
                    {
                        _magicApi.Cast(_config.TestMagicID, sourceActor: enemyActor, targetActor: playerActor);
                    }
                }
            }
        }
        if (_imgui.IsItemHovered(ImGuiHoveredFlags.ImGuiHoveredFlags_None))
        {
            _imgui.SetTooltip("Casts the spell FROM enemy TO player (reversed targeting).");
        }
        _imgui.SameLine();

        // Cast from enemy (soft-locked target as source)
        if (_imgui.Button("Cast (Enemy -> No Target)"))
        {
            if (_magicApi.IsReady)
            {
                nint enemyActor = _magicApi.GetLockedTarget();
                if (enemyActor != nint.Zero)
                {
                    for (int i = 0; i < _config.TestMagicCount; i++)
                    {
                        _magicApi.Cast(_config.TestMagicID, sourceActor: enemyActor, targetActor: null);
                    }
                }
            }
        }
        if (_imgui.IsItemHovered(ImGuiHoveredFlags.ImGuiHoveredFlags_None))
        {
            _imgui.SetTooltip("Casts FROM the soft-locked enemy (enemy as source, no explicit target).");
        }

        _imgui.Separator();
        _imgui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Identified Magic IDs (Click to set ID):");
        
        RenderEikonMagicGroup("Phoenix", MagicIds.Phoenix.All);
        RenderEikonMagicGroup("Garuda", MagicIds.Garuda.All);
        RenderEikonMagicGroup("Titan", MagicIds.Titan.All);
        RenderEikonMagicGroup("Ramuh", MagicIds.Ramuh.All);
        RenderEikonMagicGroup("Bahamut", MagicIds.Bahamut.All);
        RenderEikonMagicGroup("Shiva", MagicIds.Shiva.All);
        RenderEikonMagicGroup("Odin", MagicIds.Odin.All);
        RenderEikonMagicGroup("Leviathan", MagicIds.Leviathan.All);
        RenderEikonMagicGroup("Ultima", MagicIds.Ultima.All);
        RenderEikonMagicGroup("Ifrit", MagicIds.Ifrit.All);
        
        _imgui.Separator();
        RenderEikonMagicGroup("RESEARCH / UNKNOWN", MagicIds.Unknown.All);
    }

    private void RenderEikonMagicGroup(string eikonName, List<MagicIds.MagicEntry> entries)
    {
        if (_imgui.CollapsingHeader($"{eikonName}##Group", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_None))
        {
            foreach (var entry in entries)
            {
                if (_imgui.Button($"{entry.Name} (ID: {entry.Id})##{eikonName}_{entry.Id}"))
                {
                    _config.TestMagicID = entry.Id;
                    
                    // Auto-cast using API if available
                    if (_magicApi.IsReady)
                    {
                        for (int i = 0; i < _config.TestMagicCount; i++)
                        {
                            _magicApi.Cast(entry.Id);
                        }
                    }
                }
            }
        }
    }

    private void RenderTriState(string label, Func<TriState> getter, Action<TriState> setter)
    {
        TriState current = getter();
        _imgui.Text(label + ":");
        _imgui.SameLine();
        
        if (_imgui.RadioButton($"Default##{label}", current == TriState.Default)) setter(TriState.Default);
        _imgui.SameLine();
        if (_imgui.RadioButton($"On##{label}", current == TriState.On)) setter(TriState.On);
        _imgui.SameLine();
        if (_imgui.RadioButton($"Off##{label}", current == TriState.Off)) setter(TriState.Off);
    }

    // --- VFX Tester State ---
    private int _vfxTestId = 2880;
    private int _vfxTargetMode = 0; // 0=Player, 1=LockedTarget, 2=Coordinates
    private float _vfxX = 0f;
    private float _vfxY = 0f;
    private float _vfxZ = 0f;
    private string _vfxLastResult = "";

    private void RenderVfxTesterTab()
    {
        _imgui.TextColored(new Vector4(0.4f, 1.0f, 0.8f, 1.0f), "VFX Spawning Tester");
        _imgui.TextWrapped("Test the VfxApi by spawning visual effects. Uses Clive's BattleBehavior factory.");
        
        _imgui.Separator();

        // VFX ID Input
        if (_imgui.InputInt("VFX ID", ref _vfxTestId)) { }
        
        _imgui.Separator();
        _imgui.Text("Spawn Target:");
        
        // Target mode selection
        if (_imgui.RadioButton("On Player", _vfxTargetMode == 0)) _vfxTargetMode = 0;
        _imgui.SameLine();
        if (_imgui.RadioButton("On Locked Target", _vfxTargetMode == 1)) _vfxTargetMode = 1;
        _imgui.SameLine();
        if (_imgui.RadioButton("At Coordinates", _vfxTargetMode == 2)) _vfxTargetMode = 2;

        // Show coordinate inputs if mode is coordinates
        if (_vfxTargetMode == 2)
        {
            _imgui.SetNextItemWidth(100);
            if (_imgui.InputFloat("X", ref _vfxX)) { }
            _imgui.SameLine();
            _imgui.SetNextItemWidth(100);
            if (_imgui.InputFloat("Y", ref _vfxY)) { }
            _imgui.SameLine();
            _imgui.SetNextItemWidth(100);
            if (_imgui.InputFloat("Z", ref _vfxZ)) { }
        }

        _imgui.Separator();
        
        // Spawn button
        _imgui.TextColored(new Vector4(1.0f, 1.0f, 0.4f, 1.0f), ">>> ");
        _imgui.SameLine();
        if (_imgui.Button("SPAWN VFX"))
        {
            try
            {
                switch (_vfxTargetMode)
                {
                    case 0: // Player
                        VfxApi.SpawnVFX((uint)_vfxTestId, 0);
                        _vfxLastResult = $"Spawned VFX {_vfxTestId} on Player";
                        break;
                    case 1: // Locked Target
                        nint target = _magicApi.GetLockedTarget();
                        if (target != nint.Zero)
                        {
                            VfxApi.SpawnVFX((uint)_vfxTestId, target);
                            _vfxLastResult = $"Spawned VFX {_vfxTestId} on Locked Target (0x{target:X})";
                        }
                        else
                        {
                            _vfxLastResult = "No locked target!";
                        }
                        break;
                    case 2: // Coordinates
                        VfxApi.SpawnVFX((uint)_vfxTestId, _vfxX, _vfxY, _vfxZ);
                        _vfxLastResult = $"Spawned VFX {_vfxTestId} at ({_vfxX:F1}, {_vfxY:F1}, {_vfxZ:F1})";
                        break;
                }
            }
            catch (Exception ex)
            {
                _vfxLastResult = $"Error: {ex.Message}";
            }
        }

        // Show last result
        if (!string.IsNullOrEmpty(_vfxLastResult))
        {
            _imgui.Separator();
            bool isError = _vfxLastResult.StartsWith("Error") || _vfxLastResult.Contains("No locked");
            _imgui.TextColored(
                isError ? new Vector4(1.0f, 0.4f, 0.4f, 1.0f) : new Vector4(0.4f, 1.0f, 0.4f, 1.0f),
                _vfxLastResult);
        }

        _imgui.Separator();
        _imgui.TextColored(new Vector4(0.8f, 0.8f, 0.4f, 1.0f), "Common VFX IDs (Click to set):");
        
        // Common VFX quick buttons
        if (_imgui.CollapsingHeader("Bahamut VFX", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_None))
        {
            RenderVfxButton("Light Orb (Dia)", 2880);
            RenderVfxButton("Light Burst", 2881);
            RenderVfxButton("Megaflare Charge", 2890);
            RenderVfxButton("Gigaflare Explosion", 2895);
        }
        
        if (_imgui.CollapsingHeader("Phoenix VFX", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_None))
        {
            RenderVfxButton("Fire Burst", 1001);
            RenderVfxButton("Flames of Rebirth", 1010);
            RenderVfxButton("Rising Flames", 1015);
        }
        
        if (_imgui.CollapsingHeader("Odin VFX", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_None))
        {
            RenderVfxButton("Dark Slash", 3001);
            RenderVfxButton("Shadow Trail", 3010);
            RenderVfxButton("Zantetsuken Flash", 3020);
        }
        
        if (_imgui.CollapsingHeader("Titan VFX", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_None))
        {
            RenderVfxButton("Earth Impact", 2001);
            RenderVfxButton("Rock Shatter", 2010);
            RenderVfxButton("Titan Block", 2020);
        }
    }

    private void RenderVfxButton(string name, int vfxId)
    {
        if (_imgui.Button($"{name} ({vfxId})##vfx_{vfxId}"))
        {
            _vfxTestId = vfxId;
        }
        _imgui.SameLine();
        if (_imgui.SmallButton($"Spawn##spawn_{vfxId}"))
        {
            _vfxTestId = vfxId;
            VfxApi.SpawnVFX((uint)vfxId, 0); // Spawn on player
            _vfxLastResult = $"Spawned VFX {vfxId} on Player";
        }
    }
    
    // ================================================================
    // EIKON API TESTER TAB
    // ================================================================
    
    // Eikon API Tester state
    private int _apiTestBlindJusticeStacks = 1;
    private int _apiTestZantetsukenLevel = 1;
    private int _apiTestZantetsukenUnits = 0;
    private int _apiTestMegaflareUnits = 0;
    private int _apiTestMegaflareLevel = 0;
    private int _apiTestAbyssalTearUnits = 0;
    private int _apiTestAbyssalTearLevel = 1;
    private int _apiTestTidalGauge = 0;
    private string _apiTestLastResult = "";
    
    private void RenderEikonApiTesterTab()
    {
        if (_eikonApis == null)
        {
            _imgui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Eikon APIs not initialized!");
            _imgui.Text("Call SetEikonApis() to enable this tab.");
            return;
        }
        
        // Show last result
        if (!string.IsNullOrEmpty(_apiTestLastResult))
        {
            bool isError = _apiTestLastResult.StartsWith("Error") || _apiTestLastResult.Contains("not active");
            _imgui.TextColored(
                isError ? new Vector4(1.0f, 0.4f, 0.4f, 1.0f) : new Vector4(0.4f, 1.0f, 0.4f, 1.0f),
                _apiTestLastResult);
            _imgui.Separator();
        }
        
        // ================================================================
        // RAMUH - BLIND JUSTICE
        // ================================================================
        if (_imgui.CollapsingHeader("Ramuh - Blind Justice", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen))
        {
            var bj = _eikonApis.BlindJustice;
            if (bj == null)
            {
                _imgui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "BlindJusticeApi not available");
            }
            else
            {
                // Status
                bool isActive = bj.IsRamuhActive;
                _imgui.TextColored(
                    isActive ? new Vector4(0.4f, 1.0f, 0.4f, 1.0f) : new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    $"Ramuh Active: {isActive}");
                _imgui.SameLine();
                _imgui.Text($"| Stacks: {bj.GetUnits()}/{bj.GetMaxUnits()}");
                
                _imgui.Separator();
                
                // Stacks
                _imgui.Text("Stack Count:");
                _imgui.InputInt("Stacks##bj", ref _apiTestBlindJusticeStacks);
                _imgui.SameLine();
                if (_imgui.Button("Set##bjstacks"))
                {
                    bj.SetUnits(_apiTestBlindJusticeStacks);
                    int actualStacks = bj.GetUnits();
                    string clampInfo = actualStacks != _apiTestBlindJusticeStacks ? $" (clamped to max {bj.GetMaxUnits()})" : "";
                    _apiTestLastResult = $"Blind Justice Stacks set to {actualStacks}{clampInfo}";
                }
                
                // Quick buttons
                if (_imgui.Button("Fill Stacks##bj"))
                {
                    bj.FillGauge();
                    _apiTestLastResult = $"Blind Justice Stacks filled to {bj.GetMaxUnits()}";
                }
                _imgui.SameLine();
                if (_imgui.Button("Empty Stacks##bj"))
                {
                    bj.EmptyGauge();
                    _apiTestLastResult = "Blind Justice Stacks emptied";
                }
                _imgui.SameLine();
                if (_imgui.Button("+1##bjstack"))
                {
                    bj.AddUnits(1);
                    _apiTestLastResult = $"Blind Justice: Added 1 stack (now {bj.GetUnits()})";
                }
                _imgui.SameLine();
                if (_imgui.Button("-1##bjstack"))
                {
                    bj.AddUnits(-1);
                    _apiTestLastResult = $"Blind Justice: Removed 1 stack (now {bj.GetUnits()})";
                }
            }
        }
        
        // ================================================================
        // ODIN - ZANTETSUKEN
        // ================================================================
        if (_imgui.CollapsingHeader("Odin - Zantetsuken", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen))
        {
            var zk = _eikonApis.Zantetsuken;
            if (zk == null)
            {
                _imgui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "ZantetsukenApi not available");
            }
            else
            {
                // Status
                bool isActive = zk.IsOdinActive;
                int currentUnits = zk.GetUnits();
                int currentLevel = zk.GetLevel();
                int maxUnits = zk.GetMaxUnits();
                int maxLevel = zk.GetMaxLevel();
                
                _imgui.TextColored(
                    isActive ? new Vector4(0.4f, 1.0f, 0.4f, 1.0f) : new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    $"Odin Active: {isActive}");
                _imgui.SameLine();
                _imgui.Text($"| Units: {currentUnits}/{maxUnits} | Level: {currentLevel}/{maxLevel}");
                
                _imgui.Separator();
                
                // Units input
                _imgui.Text($"Gauge Units ({ZantetsukenApi.UnitsPerLevel} = 1 Level):");
                _imgui.InputInt("Units##zk", ref _apiTestZantetsukenUnits);
                _imgui.SameLine();
                if (_imgui.Button("Set##zkunits"))
                {
                    zk.SetUnits(_apiTestZantetsukenUnits);
                    _apiTestLastResult = $"Zantetsuken Units set to {_apiTestZantetsukenUnits}";
                }
                _imgui.SameLine();
                if (_imgui.Button("Add##zkunits"))
                {
                    zk.AddUnits(_apiTestZantetsukenUnits);
                    _apiTestLastResult = $"Zantetsuken: Added {_apiTestZantetsukenUnits} units (now {zk.GetUnits()})";
                }
                _imgui.SameLine();
                if (_imgui.Button("Set Current Lvl##zk"))
                {
                    // Add input units to the start of current level (1-based: level 2 starts at 1500)
                    int levelBaseUnits = (zk.GetLevel() - ZantetsukenApi.MinLevel) * ZantetsukenApi.UnitsPerLevel;
                    int newUnits = levelBaseUnits + _apiTestZantetsukenUnits;
                    zk.SetUnits(newUnits);
                    _apiTestLastResult = $"Zantetsuken set to {newUnits} units (Level {zk.GetLevel()} base + {_apiTestZantetsukenUnits})";
                }
                
                // Level input
                _imgui.Text($"Level ({ZantetsukenApi.MinLevel}-{maxLevel}):");;
                _imgui.InputInt("Level##zk", ref _apiTestZantetsukenLevel);
                _imgui.SameLine();
                if (_imgui.Button("Set##zklevel"))
                {
                    zk.SetLevel(_apiTestZantetsukenLevel);
                    _apiTestLastResult = $"Zantetsuken Level set to {_apiTestZantetsukenLevel}";
                }
                _imgui.SameLine();
                if (_imgui.Button("+1 Lvl##zk"))
                {
                    zk.AddLevels(1);
                    _apiTestLastResult = $"Zantetsuken: Added 1 level (now {zk.GetLevel()})";
                }
                _imgui.SameLine();
                if (_imgui.Button("-1 Lvl##zk"))
                {
                    zk.AddLevels(-1);
                    _apiTestLastResult = $"Zantetsuken: Removed 1 level (now {zk.GetLevel()})";
                }
                
                // Quick buttons
                if (_imgui.Button($"Fill##zk"))
                {
                    zk.FillGauge();
                    _apiTestLastResult = $"Zantetsuken filled to Level {maxLevel}";
                }
                _imgui.SameLine();
                if (_imgui.Button("Empty##zk"))
                {
                    zk.EmptyGauge();
                    _apiTestLastResult = "Zantetsuken emptied";
                }
            }
        }
        
        // ================================================================
        // BAHAMUT - MEGAFLARE
        // ================================================================
        if (_imgui.CollapsingHeader("Bahamut - Megaflare", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen))
        {
            var mf = _eikonApis.Megaflare;
            if (mf == null)
            {
                _imgui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "MegaflareApi not available");
            }
            else
            {
                // Status
                bool isActive = mf.IsBahamutActive;
                int currentUnits = mf.GetUnits();
                int currentLevel = mf.GetLevel();
                int maxLevel = mf.GetMaxLevel();
                int maxUnits = mf.GetMaxUnits();
                
                _imgui.TextColored(
                    isActive ? new Vector4(0.4f, 1.0f, 0.4f, 1.0f) : new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    $"Bahamut Active: {isActive}");
                _imgui.SameLine();
                _imgui.Text($"| Units: {currentUnits}/{maxUnits} | Level: {currentLevel}/{maxLevel}");
                
                _imgui.Separator();
                
                // Units input
                _imgui.Text("Gauge Units (4000 = 1 Level):");
                _imgui.InputInt("Units##mf", ref _apiTestMegaflareUnits);
                _imgui.SameLine();
                if (_imgui.Button("Set##mfunits"))
                {
                    mf.SetUnits(_apiTestMegaflareUnits);
                    _apiTestLastResult = $"Megaflare Units set to {_apiTestMegaflareUnits}";
                }
                _imgui.SameLine();
                if (_imgui.Button("Add##mfunits"))
                {
                    mf.AddUnits(_apiTestMegaflareUnits);
                    _apiTestLastResult = $"Megaflare: Added {_apiTestMegaflareUnits} units (now {mf.GetUnits()})";
                }
                _imgui.SameLine();
                if (_imgui.Button("Set Current Lvl##mf"))
                {
                    // Add input units to the start of current level (0-based: level 0 starts at 0)
                    int levelBaseUnits = mf.GetLevel() * MegaflareApi.UnitsPerLevel;
                    int newUnits = levelBaseUnits + _apiTestMegaflareUnits;
                    mf.SetUnits(newUnits);
                    _apiTestLastResult = $"Megaflare set to {newUnits} units (Level {mf.GetLevel()} base + {_apiTestMegaflareUnits})";
                }
                
                // Level input
                _imgui.Text($"Level (0-{maxLevel}):");
                _imgui.InputInt("Level##mf", ref _apiTestMegaflareLevel);
                _imgui.SameLine();
                if (_imgui.Button("Set##mflevel"))
                {
                    mf.SetLevel(_apiTestMegaflareLevel);
                    _apiTestLastResult = $"Megaflare Level set to {_apiTestMegaflareLevel}";
                }
                _imgui.SameLine();
                if (_imgui.Button("+1 Lvl##mf"))
                {
                    mf.AddLevels(1);
                    _apiTestLastResult = $"Megaflare: Added 1 level (now {mf.GetLevel()})";
                }
                _imgui.SameLine();
                if (_imgui.Button("-1 Lvl##mf"))
                {
                    mf.AddLevels(-1);
                    _apiTestLastResult = $"Megaflare: Removed 1 level (now {mf.GetLevel()})";
                }
                
                // Quick buttons
                if (_imgui.Button($"Fill##mf"))
                {
                    mf.FillGauge();
                    _apiTestLastResult = $"Megaflare filled to Level {maxLevel}";
                }
                _imgui.SameLine();
                if (_imgui.Button("Empty##mf"))
                {
                    mf.EmptyGauge();
                    _apiTestLastResult = "Megaflare emptied";
                }
            }
        }
        
        // ================================================================
        // LEVIATHAN - ABYSSAL TEAR (Always available)
        // ================================================================
        if (_imgui.CollapsingHeader("Leviathan - Abyssal Tear", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen))
        {
            var at = _eikonApis.AbyssalTear;
            if (at == null)
            {
                _imgui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "AbyssalTearApi not available");
            }
            else
            {
                // Status
                bool available = at.IsAvailable;
                int atMaxLevel = at.GetMaxLevel();
                _imgui.TextColored(
                    available ? new Vector4(0.4f, 1.0f, 0.4f, 1.0f) : new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    $"Available: {available}");
                _imgui.SameLine();
                _imgui.Text($"| Units: {at.GetUnits()} | Level: {at.GetLevel()}/{atMaxLevel}");
                
                _imgui.Separator();
                
                // Units input (seconds)
                _imgui.Text($"Units (seconds, {AbyssalTearApi.SecondsPerLevel}s = 1 Level):");
                _imgui.InputInt("Units##at", ref _apiTestAbyssalTearUnits);
                _imgui.SameLine();
                if (_imgui.Button("Set##atunits"))
                {
                    at.SetUnits(_apiTestAbyssalTearUnits);
                    _apiTestLastResult = $"Abyssal Tear Units set to {at.GetUnits()} seconds";
                }
                
                // Level input - second, with +1/-1 on same row
                _imgui.Text($"Level ({AbyssalTearApi.MinLevel}-{atMaxLevel}):");
                _imgui.InputInt("Level##at", ref _apiTestAbyssalTearLevel);
                _imgui.SameLine();
                if (_imgui.Button("Set##atlevel"))
                {
                    at.SetLevel(_apiTestAbyssalTearLevel);
                    _apiTestLastResult = $"Abyssal Tear Level set to {_apiTestAbyssalTearLevel} ({(int)(_apiTestAbyssalTearLevel * AbyssalTearApi.SecondsPerLevel)}s)";
                }
                _imgui.SameLine();
                if (_imgui.Button("+1 Lvl##at"))
                {
                    at.AddLevels(1);
                    _apiTestLastResult = $"Abyssal Tear: Added 1 level (now {at.GetLevel()})";
                }
                _imgui.SameLine();
                if (_imgui.Button("-1 Lvl##at"))
                {
                    at.AddLevels(-1);
                    _apiTestLastResult = $"Abyssal Tear: Removed 1 level (now {at.GetLevel()})";
                }
                
                // Quick buttons
                if (_imgui.Button("Fill##at"))
                {
                    at.FillGauge();
                    _apiTestLastResult = $"Abyssal Tear filled to max level ({atMaxLevel})";
                }
                _imgui.SameLine();
                if (_imgui.Button("Empty##at"))
                {
                    at.EmptyGauge();
                    _apiTestLastResult = "Abyssal Tear emptied";
                }
            }
        }
        
        // ================================================================
        // LEVIATHAN - SERPENT'S CRY (Leviathan mode only)
        // ================================================================
        if (_imgui.CollapsingHeader("Leviathan - Serpent's Cry", ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen))
        {
            var sc = _eikonApis.SerpentsCry;
            if (sc == null)
            {
                _imgui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "SerpentsCryApi not available");
            }
            else
            {
                // Status
                bool levActive = sc.IsLeviathanActive;
                _imgui.TextColored(
                    levActive ? new Vector4(0.4f, 1.0f, 0.4f, 1.0f) : new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    $"Leviathan Active: {levActive}");
                
                if (levActive)
                {
                    int tidalAvailable = sc.GetUnits();
                    int tidalMax = sc.GetMaxUnits();
                    
                    _imgui.Text($"Tidal Available: {tidalAvailable}/{tidalMax}");
                    
                    _imgui.Separator();
                    
                    // Tidal Gauge
                    _imgui.InputInt("Gauge Amount##tidal", ref _apiTestTidalGauge);
                    _imgui.SameLine();
                    if (_imgui.Button("Add##tidal"))
                    {
                        sc.AddUnits(_apiTestTidalGauge);
                        _apiTestLastResult = $"Tidal: Added {_apiTestTidalGauge} to gauge";
                    }
                    _imgui.SameLine();
                    if (_imgui.Button("Subtract##tidal"))
                    {
                        sc.AddUnits(-_apiTestTidalGauge);
                        _apiTestLastResult = $"Tidal: Subtracted {_apiTestTidalGauge} from gauge";
                    }
                    
                    // Quick buttons
                    if (_imgui.Button("Fill##tidal"))
                    {
                        sc.FillGauge();
                        _apiTestLastResult = "Tidal Gauge filled";
                    }
                    _imgui.SameLine();
                    if (_imgui.Button("Empty##tidal"))
                    {
                        sc.EmptyGauge();
                        _apiTestLastResult = "Tidal Gauge emptied";
                    }
                }
                else
                {
                    _imgui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "(Activate Leviathan mode to use Serpent's Cry)");
                }
            }
        }
    }
}
