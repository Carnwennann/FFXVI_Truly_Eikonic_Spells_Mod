using ff16.gameplay.truly_eikonic_spells.Configuration;
using ff16.gameplay.truly_eikonic_spells.GameApis.Magic;
using ff16.gameplay.truly_eikonic_spells.GameApis.Magic.MagicFile;
using NenTools.ImGui.Interfaces;
using NenTools.ImGui.Interfaces.Shell;
using System.Numerics;

namespace ff16.gameplay.truly_eikonic_spells.Configuration;

[ImGuiMenu(Category = "Mods", Priority = 100, Owner = "Truly Eikonic Spells")]
public class ImGuiConfigurator : IImGuiComponent
{
    public bool IsOverlay => true;

    private readonly IImGui _imgui;
    private Config _config;
    private readonly Action<Config> _onConfigChanged;
    private readonly IMagicApi _magicApi;

    public ImGuiConfigurator(IImGui imgui, Config config, Action<Config> onConfigChanged, IMagicApi magicApi)
    {
        _imgui = imgui;
        _config = config;
        _onConfigChanged = onConfigChanged;
        _magicApi = magicApi;
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
}
