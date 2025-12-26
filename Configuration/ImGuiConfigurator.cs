using ff16.gameplay.truly_eikonic_spells.Configuration;
using NenTools.ImGui.Interfaces;
using NenTools.ImGui.Abstractions;
using System.Numerics;

namespace ff16.gameplay.truly_eikonic_spells.Configuration;

[ImGuiMenu(Category = "Mods", Priority = 100, Owner = "Truly Eikonic Spells")]
public class ImGuiConfigurator : IImGuiComponent
{
    public bool IsOverlay => true;

    private readonly IImGui _imgui;
    private Config _config;
    private readonly Action<Config> _onConfigChanged;

    public ImGuiConfigurator(IImGui imgui, Config config, Action<Config> onConfigChanged)
    {
        _imgui = imgui;
        _config = config;
        _onConfigChanged = onConfigChanged;
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
        if (!_isWindowOpen) return;

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
        _imgui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Magic Struct Overrides");
        bool enableStruct = _config.EnableMagicStructOverrides;
        if (_imgui.Checkbox("Enable Struct Overrides", ref enableStruct)) _config.EnableMagicStructOverrides = enableStruct;
        
        float timing = _config.MagicTimingOverride;
        if (_imgui.InputFloat("Timing Override", ref timing)) _config.MagicTimingOverride = timing;
        
        float scale = _config.MagicScaleOverride;
        if (_imgui.InputFloat("Scale Override", ref scale)) _config.MagicScaleOverride = scale;
        
        float angle = _config.MagicAimAngleOverride;
        if (_imgui.InputFloat("Aim Angle (deg)", ref angle)) _config.MagicAimAngleOverride = angle;

        _imgui.Separator();
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
        
        int propId = _config.FuzzerPropertyId;
        if (_imgui.InputInt("Target Property ID", ref propId)) _config.FuzzerPropertyId = propId;
        
        bool useFloat = _config.FuzzerUseFloat;
        if (_imgui.Checkbox("Use Float Value", ref useFloat)) _config.FuzzerUseFloat = useFloat;
        
        if (useFloat)
        {
            float fVal = _config.FuzzerFloatValue;
            if (_imgui.InputFloat("Float Value", ref fVal)) _config.FuzzerFloatValue = fVal;
        }
        else
        {
            int iVal = _config.FuzzerIntValue;
            if (_imgui.InputInt("Int Value", ref iVal)) _config.FuzzerIntValue = iVal;
        }

        _imgui.Separator();
        _imgui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "General Debug");
        bool debugLog = _config.DebugLogging;
        if (_imgui.Checkbox("Enable Debug Logging", ref debugLog)) _config.DebugLogging = debugLog;
    }

    private void RenderTriState(string label, Func<TriState> getter, Action<TriState> setter)
    {
        TriState current = getter();
        int index = (int)current;
        string[] items = { "Default", "On", "Off" };
        
        if (_imgui.BeginCombo(label, items[index], ImGuiComboFlags.ImGuiComboFlags_None))
        {
            for (int i = 0; i < items.Length; i++)
            {
                bool isSelected = (index == i);
                if (_imgui.SelectableEx(items[i], isSelected, ImGuiSelectableFlags.ImGuiSelectableFlags_None, Vector2.Zero))
                {
                    setter((TriState)i);
                }
                if (isSelected)
                {
                    _imgui.SetItemDefaultFocus();
                }
            }
            _imgui.EndCombo();
        }
    }
}
