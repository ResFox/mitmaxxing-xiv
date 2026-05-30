using System;
using System.Numerics;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace MitStack.Windows;

/// <summary>
/// Compact "at-a-glance" overlay: two damage-type icons + their additive %.
/// Pretty styling: rounded panels, colour-graded numbers, time-remaining bar.
/// </summary>
public class OverlayWindow : Window, IDisposable
{
    private readonly Plugin _plugin;

    // 060011 = Physical damage type, 060012 = Magical damage type (in-game HUD icons)
    private ISharedImmediateTexture? _physTex;
    private ISharedImmediateTexture? _magicTex;

    private static readonly Vector4 ColPhys   = new(1.00f, 0.55f, 0.30f, 1f);
    private static readonly Vector4 ColMagic  = new(0.55f, 0.70f, 1.00f, 1f);
    private static readonly Vector4 ColGood   = new(0.45f, 0.95f, 0.50f, 1f);
    private static readonly Vector4 ColWarn   = new(1.00f, 0.85f, 0.10f, 1f);
    private static readonly Vector4 ColDim    = new(0.55f, 0.55f, 0.55f, 1f);

    private const float WarningTime = 5f;

    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoScrollbar       |
        ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.AlwaysAutoResize;

    private const ImGuiWindowFlags LockedExtra =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoMove     |
        ImGuiWindowFlags.NoResize   |
        ImGuiWindowFlags.NoInputs;   // click-through when locked

    public OverlayWindow(Plugin plugin) : base("Mitmaxxing##overlay")
    {
        _plugin = plugin;
        IsOpen  = true;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(10, 10),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        LoadIcons();
    }

    private void LoadIcons()
    {
        _physTex  = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(60011));
        _magicTex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(60012));
    }

    public void Dispose() { }

    public override bool DrawConditions() => _plugin.ShouldShowMits();

    public override void PreDraw()
    {
        Flags = BaseFlags;
        if (_plugin.Configuration.Locked)
            Flags |= LockedExtra;

        ImGui.SetNextWindowBgAlpha(_plugin.Configuration.BackgroundAlpha);
    }

    public override void Draw()
    {
        var cfg    = _plugin.Configuration;
        var result = _plugin.Calculator.Current;

        float iconPx = cfg.IconSize * ImGuiHelpers.GlobalScale;

        if (cfg.OverlaySimpleMode)
            DrawSimpleRow(result, iconPx, cfg);
        else
        {
            DrawSide(_physTex,  result.PhysSum,  iconPx, result.MinRemaining, ColPhys,  physical: true,  cfg);

            ImGui.SameLine(0, 12f * ImGuiHelpers.GlobalScale);
            ImGui.TextDisabled("│");
            ImGui.SameLine(0, 12f * ImGuiHelpers.GlobalScale);

            DrawSide(_magicTex, result.MagicSum, iconPx, result.MinRemaining, ColMagic, physical: false, cfg);
        }

        // Right-click context menu
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup("##mitstack_ctx");

        using var popup = ImRaii.Popup("##mitstack_ctx");
        if (popup)
        {
            if (ImGui.MenuItem(cfg.Locked ? "Unlock Overlay" : "Lock Overlay"))
                cfg.ToggleLock();
            if (ImGui.MenuItem("Open Mit Tracker"))
                _plugin.ToggleListWindow();
            ImGui.Separator();
            if (ImGui.MenuItem("Settings"))
                _plugin.ToggleConfigUi();
        }
    }

    // -----------------------------------------------------------------------
    // Compact "in-game style" row: [icon][number]   [icon][number]
    // No progress bars, no fancy spacing — just like a vanilla status display.
    // -----------------------------------------------------------------------
    // Vanilla in-game style: icon on top, big number directly below it.
    // Two pairs side-by-side.  Looks exactly like the game's status display.
    private void DrawSimpleRow(MitigationResult result, float iconPx, Configuration cfg)
    {
        DrawSimplePair(_physTex,  result.PhysSum,  result.MinRemaining, iconPx, physical: true,  cfg);
        ImGui.SameLine(0, 6f * ImGuiHelpers.GlobalScale);
        DrawSimplePair(_magicTex, result.MagicSum, result.MinRemaining, iconPx, physical: false, cfg);
    }

    private void DrawSimplePair(
        ISharedImmediateTexture? tex,
        float sum, float minRemaining, float iconPx,
        bool physical, Configuration cfg)
    {
        bool hasAny = sum > 0.0001f;
        int  pct    = (int)MathF.Round(sum * 100f);

        var col = !hasAny ? ColDim :
                  minRemaining > 0f && minRemaining < WarningTime ? ColWarn : ColGood;

        // Stack icon + number vertically as a tight group
        ImGui.BeginGroup();

        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
            ImGui.Image(wrap.Handle, new Vector2(iconPx, iconPx));
        else
            ImGui.Dummy(new Vector2(iconPx, iconPx));

        // Tighten the gap between icon and number
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 4f);

        // Centre the number under the icon
        string text = hasAny ? $"{pct}" : "–";
        var    sz   = ImGui.CalcTextSize(text);
        float offsetX = MathF.Max(0f, (iconPx - sz.X) * 0.5f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

        ImGui.PushStyleColor(ImGuiCol.Text, col);
        ImGui.Text(text);
        ImGui.PopStyleColor();

        ImGui.EndGroup();

        if (cfg.ShowTooltip && hasAny && ImGui.IsItemHovered())
            DrawTooltip(physical);
    }

    // -----------------------------------------------------------------------
    private void DrawSide(
        ISharedImmediateTexture? tex,
        float                    sum,
        float                    iconPx,
        float                    minRemaining,
        Vector4                  accent,
        bool                     physical,
        Configuration            cfg)
    {
        bool hasAny = sum > 0.0001f;
        int  pct    = (int)MathF.Round(sum * 100f);

        var col = !hasAny ? ColDim :
                  minRemaining > 0f && minRemaining < WarningTime ? ColWarn : ColGood;

        var groupStart = ImGui.GetCursorScreenPos();
        ImGui.BeginGroup();

        // ── Icon ──
        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
            ImGui.Image(wrap.Handle, new Vector2(iconPx, iconPx));
        else
            ImGui.Dummy(new Vector2(iconPx, iconPx));

        ImGui.SameLine(0, 6f * ImGuiHelpers.GlobalScale);

        // ── %  +  thin time bar underneath ──
        ImGui.BeginGroup();

        ImGui.PushStyleColor(ImGuiCol.Text, col);
        ImGui.Text(hasAny ? $"{pct}%" : "—");
        ImGui.PopStyleColor();

        // Thin progress bar under the number showing min remaining time as a fraction
        // (uses 30s as a comfortable scale — never overflows beyond bar).
        if (hasAny && minRemaining > 0f)
        {
            var draw = ImGui.GetWindowDrawList();
            var p0   = ImGui.GetCursorScreenPos();
            float barW = MathF.Max(iconPx + 38f, 56f);
            float barH = 3f * ImGuiHelpers.GlobalScale;
            float frac = MathF.Min(minRemaining / 30f, 1f);
            var p1 = new Vector2(p0.X + barW,           p0.Y + barH);
            var pf = new Vector2(p0.X + barW * frac,    p0.Y + barH);
            draw.AddRectFilled(p0, p1, ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.20f, 0.8f)), 1.5f);
            draw.AddRectFilled(p0, pf, ImGui.GetColorU32(col),                                    1.5f);
            ImGui.Dummy(new Vector2(barW, barH));
        }

        ImGui.EndGroup();
        ImGui.EndGroup();

        if (cfg.ShowTooltip && hasAny && ImGui.IsItemHovered())
            DrawTooltip(physical);
    }

    // -----------------------------------------------------------------------
    private void DrawTooltip(bool physical)
    {
        using var tt = ImRaii.Tooltip();
        var result = _plugin.Calculator.Current;

        if (result.EnemyDebuffs.Count > 0)
        {
            ImGui.TextDisabled("Boss debuffs");
            foreach (var d in result.EnemyDebuffs)
            {
                float val = physical ? d.Entry.PhysReduction : d.Entry.MagicReduction;
                if (val < 0.001f) continue;
                ImGui.Text($"  {d.Entry.Label,-20}  {val * 100f:F0}%  ({d.RemainingTime:F1}s)");
            }
        }

        if (result.PlayerBuffs.Count > 0)
        {
            if (result.EnemyDebuffs.Count > 0) ImGui.Separator();
            ImGui.TextDisabled("Your buffs");
            foreach (var d in result.PlayerBuffs)
            {
                float val = physical ? d.Entry.PhysReduction : d.Entry.MagicReduction;
                if (val < 0.001f) continue;
                ImGui.Text($"  {d.Entry.Label,-20}  {val * 100f:F0}%  ({d.RemainingTime:F1}s)");
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("/mitmaxxing list — full Mit Tracker");
    }
}
