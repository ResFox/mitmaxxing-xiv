using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace MitStack.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("Mitmaxxing Settings###MitStackConfig")
    {
        _plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 360),
            MaximumSize = new Vector2(700, 800),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg     = _plugin.Configuration;
        bool changed = false;

        // ─── Mit Tracker (main list window) ────────────────────────────────
        SectionHeader("Mit Tracker (main list window)");

        if (ImGui.Button("Open / Close Mit Tracker"))
            _plugin.ToggleListWindow();

        bool listLocked = cfg.ListLocked;
        if (ImGui.Checkbox("Lock position & size", ref listLocked))
        {
            cfg.ListLocked = listLocked;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("Removes title bar and disables move/resize.\nRight-click the window to toggle.");

        bool listSimple = cfg.ListSimpleMode;
        if (ImGui.Checkbox("Use simple (vanilla) mode", ref listSimple))
        {
            cfg.ListSimpleMode = listSimple;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("Strips backgrounds, role stripes and time bars.\nClean single-line rows — looks closer to a chat log.\nGreat for small screens.");

        float listAlpha = cfg.ListBackgroundAlpha;
        if (ImGui.SliderFloat("Background opacity##list", ref listAlpha, 0f, 1f, "%.2f"))
        {
            cfg.ListBackgroundAlpha = listAlpha;
            changed = true;
        }

        float listScale = cfg.ListScale;
        if (ImGui.SliderFloat("UI scale##list", ref listScale, 1.0f, 1.8f, "%.2f"))
        {
            cfg.ListScale = listScale;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("Multiplies icon + bar sizes inside the Mit Tracker.\n1.0 = default. (Smaller than 1.0 disabled — use the Simple mode for a compact look.)");

        bool autoFit = cfg.ListAutoFit;
        if (ImGui.Checkbox("Auto-fit height to content", ref autoFit))
        {
            cfg.ListAutoFit = autoFit;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("Window grows / shrinks vertically as mits come and go.\nDisable for a fixed-size scrollable list.");

        ImGui.Spacing();

        // ─── 2-icon mini overlay ───────────────────────────────────────────
        SectionHeader("2-icon mini overlay");

        if (ImGui.Button("Open / Close mini overlay"))
            _plugin.ToggleOverlay();

        bool locked = cfg.Locked;
        if (ImGui.Checkbox("Lock position##overlay", ref locked))
        {
            cfg.Locked = locked;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("Right-click the overlay to toggle.");

        bool overlaySimple = cfg.OverlaySimpleMode;
        if (ImGui.Checkbox("Use simple in-game-style mode", ref overlaySimple))
        {
            cfg.OverlaySimpleMode = overlaySimple;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("Game-style: just two small status icons with a number next to each.\nNo progress bars, no padding.\nFits nicely above the boss bar.");

        float alpha = cfg.BackgroundAlpha;
        if (ImGui.SliderFloat("Background opacity##overlay", ref alpha, 0f, 1f, "%.2f"))
        {
            cfg.BackgroundAlpha = alpha;
            changed = true;
        }

        float iconSize = cfg.IconSize;
        if (ImGui.SliderFloat("Icon size (px)##overlay", ref iconSize, 14f, 48f, "%.0f"))
        {
            cfg.IconSize = iconSize;
            changed = true;
        }

        bool showTip = cfg.ShowTooltip;
        if (ImGui.Checkbox("Show breakdown tooltip on hover", ref showTip))
        {
            cfg.ShowTooltip = showTip;
            changed = true;
        }

        ImGui.Spacing();

        // ─── Targeting ─────────────────────────────────────────────────────
        SectionHeader("Targeting");

        bool useFocus = cfg.UseFocusTarget;
        if (ImGui.Checkbox("Prefer focus target", ref useFocus))
        {
            cfg.UseFocusTarget = useFocus;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("If a focus target is set, track its debuffs.\nFalls back to your current target otherwise.");

        ImGui.TextDisabled("Show mitigations:");
        ImGui.SameLine();
        HelpMarker("Always: show whenever a mit is active.\nIn combat: only while you're in combat.\nIn instance: only inside a duty.\n\nYour own buffs (Rampart etc.) always show even when the boss isn't targetable.");

        int vis = (int)cfg.ShowMitsWhen;
        if (ImGui.RadioButton("Always", ref vis, (int)MitVisibility.Always)) { cfg.ShowMitsWhen = MitVisibility.Always; changed = true; }
        ImGui.SameLine();
        if (ImGui.RadioButton("In combat", ref vis, (int)MitVisibility.InCombat)) { cfg.ShowMitsWhen = MitVisibility.InCombat; changed = true; }
        ImGui.SameLine();
        if (ImGui.RadioButton("In instance", ref vis, (int)MitVisibility.InDuty)) { cfg.ShowMitsWhen = MitVisibility.InDuty; changed = true; }

        ImGui.Spacing();

        // ─── Death Recap ───────────────────────────────────────────────────
        SectionHeader("Death Recap");

        if (ImGui.Button("Open / Close Death Recap"))
            _plugin.ToggleDeathRecapWindow();

        bool drEnabled = cfg.DeathRecapEnabled;
        if (ImGui.Checkbox("Enable death recap (packet hooks)", ref drEnabled))
        {
            cfg.DeathRecapEnabled = drEnabled;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("Uses the same packet hooks as the DeathRecap plugin.\nTracks damage, heals, and ALL status effects.");

        bool drAuto = cfg.DeathRecapAutoOpen;
        if (ImGui.Checkbox("Notify on death", ref drAuto))
        {
            cfg.DeathRecapAutoOpen = drAuto;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("When ON, you get a notification each time someone dies.");

        // ── Popup vs. full window ──
        if (drAuto)
        {
            ImGui.Indent(20f);
            bool usePopup = cfg.DeathPopupEnabled;
            if (ImGui.Checkbox("Use small popup (click to expand)", ref usePopup))
            {
                cfg.DeathPopupEnabled = usePopup;
                changed = true;
            }
            ImGui.SameLine();
            HelpMarker("Small notification with a 'View full recap' button.\nUncheck to open the full window directly on every death.");

            if (usePopup)
            {
                float autoClose = cfg.DeathPopupAutoCloseSeconds;
                if (ImGui.SliderFloat("Auto-close after (s)##popup", ref autoClose, 0f, 60f, "%.0f"))
                {
                    cfg.DeathPopupAutoCloseSeconds = autoClose;
                    changed = true;
                }
                ImGui.SameLine();
                HelpMarker("0 = stay open until dismissed.");

                bool popupLocked = cfg.DeathPopupLocked;
                if (ImGui.Checkbox("Lock popup position##popup", ref popupLocked))
                {
                    cfg.DeathPopupLocked = popupLocked;
                    changed = true;
                }
                ImGui.SameLine();
                HelpMarker("Drag the popup to set a position, then lock it here.\nLocked = click-through, no title bar.");

                float popupAlpha = cfg.DeathPopupBackgroundAlpha;
                if (ImGui.SliderFloat("Popup opacity##popup", ref popupAlpha, 0f, 1f, "%.2f"))
                {
                    cfg.DeathPopupBackgroundAlpha = popupAlpha;
                    changed = true;
                }
            }
            ImGui.Unindent(20f);
        }

        bool drDuty = cfg.DeathRecapOnlyInDuty;
        if (ImGui.Checkbox("Only track while in a duty", ref drDuty))
        {
            cfg.DeathRecapOnlyInDuty = drDuty;
            changed = true;
        }

        ImGui.TextDisabled("Event filters:");
        bool showDmg = cfg.DeathRecapShowDamage;
        if (ImGui.Checkbox("Show damage", ref showDmg)) { cfg.DeathRecapShowDamage = showDmg; changed = true; }
        ImGui.SameLine();
        bool showHeal = cfg.DeathRecapShowHealing;
        if (ImGui.Checkbox("Show healing", ref showHeal)) { cfg.DeathRecapShowHealing = showHeal; changed = true; }

        bool showBuff = cfg.DeathRecapShowBuffs;
        if (ImGui.Checkbox("Show buffs (Shake It Off, Bloodwhetting…)", ref showBuff)) { cfg.DeathRecapShowBuffs = showBuff; changed = true; }
        bool showDebuff = cfg.DeathRecapShowDebuffs;
        if (ImGui.Checkbox("Show debuffs", ref showDebuff)) { cfg.DeathRecapShowDebuffs = showDebuff; changed = true; }

        bool drLocked = cfg.DeathRecapLocked;
        if (ImGui.Checkbox("Lock position & size##death", ref drLocked))
        {
            cfg.DeathRecapLocked = drLocked;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("Removes title bar and makes the window click-through.");

        float drAlpha = cfg.DeathRecapBackgroundAlpha;
        if (ImGui.SliderFloat("Background opacity##death", ref drAlpha, 0f, 1f, "%.2f"))
        {
            cfg.DeathRecapBackgroundAlpha = drAlpha;
            changed = true;
        }

        ImGui.Spacing();

        // ─── Commands cheat sheet ──────────────────────────────────────────
        SectionHeader("Commands");
        ImGui.BulletText("/mitmaxxing           — toggle the 2-icon overlay");
        ImGui.BulletText("/mitmaxxing list      — toggle the Mit Tracker");
        ImGui.BulletText("/mitmaxxing deaths    — toggle Death Recap");
        ImGui.BulletText("/mitmaxxing testdeath — preview the recap with fake data");
        ImGui.BulletText("/mitmaxxing config    — open this window");
        ImGui.BulletText("/mitmaxxing debug     — print all status IDs to /xllog");

        ImGui.Spacing();
        SectionHeader("Maintenance");
        if (ImGui.Button("Preview death recap"))
            _plugin.CombatCapture.InjectTestDeath();
        ImGui.SameLine();
        if (ImGui.Button("Reset all settings"))
            ImGui.OpenPopup("##mitstack_reset_confirm");

        using (var popup = Dalamud.Interface.Utility.Raii.ImRaii.Popup("##mitstack_reset_confirm"))
        {
            if (popup)
            {
                ImGui.Text("Reset every Mitmaxxing setting to defaults?");
                if (ImGui.Button("Yes, reset"))
                {
                    _plugin.ResetConfiguration();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                    ImGui.CloseCurrentPopup();
            }
        }

        if (changed)
            cfg.Save();
    }

    // -----------------------------------------------------------------------
    private static void SectionHeader(string text)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.75f, 0.85f, 1.0f, 1f), text);
        ImGui.Separator();
    }

    private static void HelpMarker(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 22f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
