using System;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System.Linq;
using MitStack.DeathRecap;

namespace MitStack.Windows;

public class DeathPopupWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private DeathRecord? _current;
    private DateTime _shownAt;

    private static readonly Vector4 ColDanger = new(1.00f, 0.40f, 0.35f, 1f);
    private static readonly Vector4 ColAccent = new(1.00f, 0.85f, 0.10f, 1f);

    public DeathPopupWindow(Plugin plugin)
        : base("Mitmaxxing — Death###MitStackDeathPopup",
               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
               ImGuiWindowFlags.AlwaysAutoResize)
    {
        _plugin = plugin;
        IsOpen  = false;
        RespectCloseHotkey = false;
    }

    public void Dispose() { }

    public void Show(DeathRecord record)
    {
        _current = record;
        _shownAt = DateTime.UtcNow;
        IsOpen   = true;

        var cfg = _plugin.Configuration;
        if (cfg.DeathPopupPosX > 0 && cfg.DeathPopupPosY > 0)
        {
            Position          = new Vector2(cfg.DeathPopupPosX, cfg.DeathPopupPosY);
            PositionCondition = ImGuiCond.Always;
        }
        else
        {
            PositionCondition = ImGuiCond.FirstUseEver;
        }
    }

    public override void PreDraw()
    {
        var cfg = _plugin.Configuration;
        BgAlpha = cfg.DeathPopupBackgroundAlpha;

        ImGuiWindowFlags extra = ImGuiWindowFlags.None;
        if (cfg.DeathPopupLocked)
            extra |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove |
                     ImGuiWindowFlags.NoInputs;
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.AlwaysAutoResize | extra;
    }

    public override void Draw()
    {
        if (_current is null) { IsOpen = false; return; }

        var cfg = _plugin.Configuration;
        if (cfg.DeathPopupAutoCloseSeconds > 0 &&
            (DateTime.UtcNow - _shownAt).TotalSeconds > cfg.DeathPopupAutoCloseSeconds)
        {
            IsOpen = false;
            return;
        }

        if (!cfg.DeathPopupLocked)
        {
            var pos = ImGui.GetWindowPos();
            if (Math.Abs(pos.X - cfg.DeathPopupPosX) > 1f ||
                Math.Abs(pos.Y - cfg.DeathPopupPosY) > 1f)
            {
                cfg.DeathPopupPosX = pos.X;
                cfg.DeathPopupPosY = pos.Y;
                cfg.Save();
            }
        }

        var d = _current;
        string title = string.IsNullOrEmpty(d.JobAbbrev) ? d.CharName : $"[{d.JobAbbrev}] {d.CharName}";

        ImGui.TextColored(ColDanger, "DEATH");
        ImGui.SameLine();
        ImGui.TextColored(ColAccent, title);

        ImGui.Text("Damage taken: ");
        ImGui.SameLine();
        ImGui.TextColored(ColDanger, $"{d.TotalDamage:N0}");
        if (d.NetOverkill > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"(+{d.NetOverkill:N0} overkill)");
        }

        var killer = d.Events.LastOrDefault(e => e is CombatEvent.DamageTaken) as CombatEvent.DamageTaken;
        if (killer != null)
        {
            ImGui.TextDisabled("Killed by:");
            ImGui.SameLine();
            ImGui.TextColored(ColAccent, killer.Action);
            if (!string.IsNullOrEmpty(killer.Source))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({killer.Source})");
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("View full recap", new Vector2(150f * ImGuiHelpers.GlobalScale, 0)))
        {
            _plugin.OpenDeathRecapWindow();
            IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Dismiss", new Vector2(80f * ImGuiHelpers.GlobalScale, 0)))
            IsOpen = false;
    }
}
