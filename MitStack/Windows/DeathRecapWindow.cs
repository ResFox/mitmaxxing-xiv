using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaStatus = Lumina.Excel.Sheets.Status;
using MitStack.DeathRecap;

namespace MitStack.Windows;

public class DeathRecapWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private readonly Dictionary<uint, ISharedImmediateTexture?> _iconCache = new();

    private int _selectedDeath = 0;

    // Mitmaxxing palette
    private static readonly Vector4 ColPhys     = new(1.00f, 0.55f, 0.30f, 1f);
    private static readonly Vector4 ColMagic    = new(0.55f, 0.70f, 1.00f, 1f);
    private static readonly Vector4 ColHeal     = new(0.45f, 0.95f, 0.50f, 1f);
    private static readonly Vector4 ColAction   = new(1.00f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 ColDim      = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 ColDanger   = new(1.00f, 0.40f, 0.35f, 1f);
    private static readonly Vector4 ColHp       = new(0.02f, 0.53f, 0.22f, 1f);
    private static readonly Vector4 ColBarrier  = new(1.00f, 0.90f, 0.25f, 0.95f);
    private static readonly Vector4 ColDmgOver  = new(0.80f, 0.15f, 0.15f, 0.55f);
    private static readonly Vector4 ColHealOver = new(0.15f, 0.80f, 0.30f, 0.45f);

    public DeathRecapWindow(Plugin plugin)
        : base("Mitmaxxing — Death Recap###MitStackDeathRecap2")
    {
        _plugin = plugin;
        Size = new Vector2(820, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 320),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var cfg = _plugin.Configuration;
        BgAlpha = Math.Clamp(cfg.DeathRecapBackgroundAlpha, 0f, 1f);

        ImGuiWindowFlags flags = ImGuiWindowFlags.None;
        if (cfg.DeathRecapLocked)
            flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove |
                     ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoInputs;
        Flags = flags;
    }

    public override void Draw()
    {
        var deaths = _plugin.CombatCapture.Deaths;
        if (deaths.Count == 0)
        {
            DrawEmpty();
            DrawContextMenu();
            return;
        }

        if (_selectedDeath >= deaths.Count) _selectedDeath = 0;

        DrawToolbar(deaths.Count);
        ImGui.Separator();

        // Sidebar: death list
        float listW = 160f * ImGuiHelpers.GlobalScale;
        if (ImGui.BeginChild("##dr_sidebar", new Vector2(listW, 0), true))
        {
            for (int i = 0; i < deaths.Count; i++)
            {
                var d = deaths[i];
                bool sel = i == _selectedDeath;
                string label = $"{d.DiedAt:HH:mm:ss}";
                if (ImGui.Selectable($"{label}##dr{i}", sel, ImGuiSelectableFlags.None, new Vector2(0, 28f)))
                    _selectedDeath = i;

                var pos = ImGui.GetItemRectMin();
                ImGui.GetWindowDrawList().AddText(
                    new Vector2(pos.X + 4f, pos.Y + 14f),
                    ImGui.GetColorU32(ColDim),
                    string.IsNullOrEmpty(d.JobAbbrev) ? d.CharName : $"{d.JobAbbrev} {d.CharName}");
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("##dr_detail", new Vector2(0, 0), false))
            DrawDeathDetail(deaths[_selectedDeath]);

        ImGui.EndChild();
        DrawContextMenu();
    }

    private void DrawToolbar(int count)
    {
        ImGui.TextColored(new Vector4(0.85f, 0.85f, 1f, 1f), $"Deaths: {count}");
        ImGui.SameLine(0, 16f);
        if (ImGui.SmallButton("Clear##dr"))
        {
            _plugin.CombatCapture.ClearHistory();
            _selectedDeath = 0;
        }
    }

    private static void DrawEmpty()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.85f, 0.85f, 1f, 1f), "Death Recap");
        ImGui.Separator();
        ImGui.TextDisabled("No deaths recorded yet.");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Records damage taken, healing received, shields, and active buffs for you and " +
            "your party. When someone dies in a duty the recap appears automatically.");
    }

    private void DrawDeathDetail(DeathRecord d)
    {
        ImGui.TextColored(ColAction,
            string.IsNullOrEmpty(d.JobAbbrev) ? d.CharName : $"[{d.JobAbbrev}]  {d.CharName}");
        ImGui.SameLine();
        ImGui.TextDisabled($"   died {d.DiedAt:HH:mm:ss}");

        ImGui.Spacing();
        DrawKillingBlowCard(d);
        ImGui.Spacing();
        DrawEventTable(d);
    }

    private void DrawKillingBlowCard(DeathRecord d)
    {
        CombatEvent.DamageTaken? kb = null;
        CombatEvent.DoT?         kbDot = null;
        for (int i = d.Events.Count - 1; i >= 0; i--)
        {
            if (d.Events[i] is CombatEvent.DamageTaken dt) { kb = dt; break; }
            if (d.Events[i] is CombatEvent.DoT dot)        { kbDot = dot; break; }
        }

        uint hpBefore = kb?.Snapshot.CurrentHp ?? kbDot?.Snapshot.CurrentHp ?? d.MaxHp;
        uint maxHp    = kb?.Snapshot.MaxHp     ?? kbDot?.Snapshot.MaxHp     ?? d.MaxHp;
        uint amount   = kb?.Amount             ?? kbDot?.Amount             ?? 0;
        string abil   = kb?.Action ?? (kbDot != null ? "Damage over time" : "Unknown");
        string src    = kb?.Source ?? "";
        uint? icon    = kb?.Icon;
        int overkill  = (int)(amount > hpBefore ? amount - hpBefore : 0);

        if (icon is { } iconId) { DrawInlineIcon(iconId); ImGui.SameLine(0, 6f); }

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Killed by");
        ImGui.SameLine(0, 4f);
        ImGui.TextColored(ColAction, string.IsNullOrEmpty(src) ? abil : $"{src} — {abil}");

        ImGui.SameLine(0, 16f);
        ImGui.TextDisabled("for");
        ImGui.SameLine(0, 4f);
        ImGui.TextColored(ColDanger, $"{amount:N0}");
        ImGui.SameLine(0, 4f);
        ImGui.TextDisabled($"vs {hpBefore:N0} HP");

        if (overkill > 0)
        {
            ImGui.SameLine(0, 8f);
            ImGui.TextColored(ColDanger, $"  •  Overkill {overkill:N0}");
        }

        uint physTotal = 0, magicTotal = 0;
        foreach (var e in d.Events)
        {
            if (e is CombatEvent.DamageTaken dmg)
            {
                if (dmg.DamageType == DamageType.Magic) magicTotal += dmg.Amount;
                else                                    physTotal  += dmg.Amount;
            }
            else if (e is CombatEvent.DoT dt) magicTotal += dt.Amount;
        }

        ImGui.TextDisabled($"Events: {d.Events.Count}   •   Window damage: {d.TotalDamage:N0}");
        ImGui.SameLine(0, 6f);
        ImGui.TextColored(ColPhys, $"({physTotal:N0} phys");
        ImGui.SameLine(0, 4f);
        ImGui.TextColored(ColMagic, $"/ {magicTotal:N0} magic)");
        ImGui.SameLine(0, 6f);
        ImGui.TextDisabled($"•   Max HP: {maxHp:N0}");
    }

    private void DrawEventTable(DeathRecord death)
    {
        var cfg = _plugin.Configuration;

        if (!ImGui.BeginTable("##dr_events", 6,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 52f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 90f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Ability");
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 100f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("HP Before", ImGuiTableColumnFlags.WidthFixed, 130f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Status Effects");
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        for (int i = death.Events.Count - 1; i >= 0; i--)
        {
            switch (death.Events[i])
            {
                case CombatEvent.HoT hot when cfg.DeathRecapShowHealing:
                    DrawHoTRow(hot, death, ref i);
                    break;
                case CombatEvent.DoT dot when cfg.DeathRecapShowDamage:
                    DrawDoTRow(dot, death);
                    break;
                case CombatEvent.DamageTaken dt when cfg.DeathRecapShowDamage:
                    DrawDamageRow(dt, death);
                    break;
                case CombatEvent.Healed h when cfg.DeathRecapShowHealing:
                    DrawHealRow(h, death);
                    break;
                case CombatEvent.StatusEffect s:
                    if (s.Category == StatusCategory.Beneficial && !cfg.DeathRecapShowBuffs) break;
                    if (s.Category == StatusCategory.Detrimental && !cfg.DeathRecapShowDebuffs) break;
                    DrawStatusRow(s, death);
                    break;
            }
        }

        ImGui.EndTable();
    }

    private void DrawHoTRow(CombatEvent.HoT hot, DeathRecord death, ref int i)
    {
        ImGui.TableNextRow();
        DrawTimeCol(hot, death);

        uint total = hot.Amount;
        while (i > 0 && death.Events[i - 1] is CombatEvent.HoT h)
        {
            hot = h;
            total += h.Amount;
            i--;
        }

        ImGui.TableNextColumn();
        ImGui.TextColored(ColHeal, $"+{total:N0}");

        ImGui.TableNextColumn();
        ImGui.Text("Regen");

        ImGui.TableNextColumn(); // source empty

        DrawHpCol(hot, healAmount: total);
        DrawStatusCol(hot);
    }

    private void DrawDoTRow(CombatEvent.DoT dot, DeathRecord death)
    {
        ImGui.TableNextRow();
        DrawTimeCol(dot, death);
        ImGui.TableNextColumn();
        ImGui.TextColored(ColDanger, $"-{dot.Amount:N0}");
        ImGui.TableNextColumn();
        ImGui.TextDisabled("DoT");
        ImGui.TableNextColumn();
        DrawHpCol(dot, damageAmount: dot.Amount);
        DrawStatusCol(dot);
    }

    private void DrawDamageRow(CombatEvent.DamageTaken dt, DeathRecord death)
    {
        ImGui.TableNextRow();
        DrawTimeCol(dt, death);

        ImGui.TableNextColumn();
        var suffix = dt.Crit ? dt.DirectHit ? "!!" : "!" : "";
        var col = dt.DamageType == DamageType.Magic ? ColMagic : ColPhys;
        ImGui.TextColored(col, $"-{dt.Amount:N0}{suffix}");

        ImGui.TableNextColumn();
        if (dt.Icon is { } iconId)
        {
            DrawInlineIcon(iconId);
            ImGui.SameLine(0, 4f);
        }
        ImGui.TextColored(ColAction, dt.Action);

        ImGui.TableNextColumn();
        ImGui.Text(dt.Source ?? "");

        DrawHpCol(dt, damageAmount: dt.Amount);
        DrawStatusCol(dt);
    }

    private void DrawHealRow(CombatEvent.Healed h, DeathRecord death)
    {
        ImGui.TableNextRow();
        DrawTimeCol(h, death);

        ImGui.TableNextColumn();
        ImGui.TextColored(ColHeal, $"+{h.Amount:N0}{(h.Crit ? "!" : "")}");

        ImGui.TableNextColumn();
        if (h.Icon is { } iconId)
        {
            DrawInlineIcon(iconId);
            ImGui.SameLine(0, 4f);
        }
        ImGui.TextColored(ColAction, h.Action);

        ImGui.TableNextColumn();
        ImGui.Text(h.Source ?? "");

        DrawHpCol(h, healAmount: h.Amount);
        DrawStatusCol(h);
    }

    private void DrawStatusRow(CombatEvent.StatusEffect s, DeathRecord death)
    {
        ImGui.TableNextRow();
        DrawTimeCol(s, death);

        ImGui.TableNextColumn();
        ImGui.TextDisabled($"{s.Duration:N0}s");

        ImGui.TableNextColumn();
        if (s.Icon is { } iconId)
        {
            DrawInlineIcon(iconId, s.StackCount);
            ImGui.SameLine(0, 4f);
        }
        ImGui.Text(s.Status ?? "");
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(s.Description))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            ImGui.TextUnformatted(s.Description);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        ImGui.TableNextColumn();
        ImGui.Text(s.Source ?? "");

        DrawHpCol(s);
        DrawStatusCol(s);
    }

    private static void DrawTimeCol(CombatEvent e, DeathRecord death)
    {
        ImGui.TableNextColumn();
        var secs = (e.Snapshot.Time - death.DiedAt).TotalSeconds;
        ImGui.TextColored(ColDim, $"{secs:N1}s");
    }

    private void DrawHpCol(CombatEvent e, uint damageAmount = 0, uint healAmount = 0)
    {
        ImGui.TableNextColumn();

        var snap   = e.Snapshot;
        var maxHp  = snap.MaxHp;
        var curHp  = snap.CurrentHp;
        var shield = maxHp > 0 ? (uint)(maxHp * snap.BarrierPercent / 100f) : 0;

        float hpFrac     = maxHp > 0 ? (float)curHp / maxHp : 0f;
        float shieldFrac = maxHp > 0 ? (float)shield / maxHp : 0f;

        int overkill = 0;
        float changeFrac = 0f;
        if (damageAmount > 0)
        {
            overkill   = (int)(damageAmount > curHp ? damageAmount - curHp : 0);
            changeFrac = maxHp > 0 ? -(float)damageAmount / maxHp : 0f;
        }
        else if (healAmount > 0)
        {
            changeFrac = maxHp > 0 ? (float)healAmount / maxHp : 0f;
        }

        var   draw = ImGui.GetWindowDrawList();
        var   p0   = ImGui.GetCursorScreenPos();
        float w    = ImGui.GetContentRegionAvail().X;
        float h    = 18f * ImGuiHelpers.GlobalScale;
        var   p1   = new Vector2(p0.X + w, p0.Y + h);

        draw.AddRectFilled(p0, p1, ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.10f, 1f)), 3f);

        float hpEnd = p0.X + w * hpFrac;
        draw.AddRectFilled(p0, new Vector2(hpEnd, p0.Y + h), ImGui.GetColorU32(ColHp), 3f);

        if (shield > 0)
        {
            float shieldEnd = MathF.Min(p0.X + w, hpEnd + w * shieldFrac);
            draw.AddRectFilled(
                new Vector2(hpEnd, p0.Y),
                new Vector2(shieldEnd, p0.Y + h),
                ImGui.GetColorU32(ColBarrier), 3f);
            draw.AddRect(
                new Vector2(hpEnd, p0.Y),
                new Vector2(shieldEnd, p0.Y + h),
                ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.10f, 1f)), 3f, 0, 1f);
        }

        if (changeFrac < 0)
        {
            float dmgW = w * MathF.Min(MathF.Abs(changeFrac), hpFrac);
            draw.AddRectFilled(
                new Vector2(hpEnd - dmgW, p0.Y),
                new Vector2(hpEnd, p0.Y + h),
                ImGui.GetColorU32(ColDmgOver), 3f);
        }
        else if (changeFrac > 0)
        {
            float healW = w * changeFrac;
            draw.AddRectFilled(
                new Vector2(hpEnd, p0.Y),
                new Vector2(hpEnd + healW, p0.Y + h),
                ImGui.GetColorU32(ColHealOver), 3f);
        }

        string text = shield > 0 ? $"{curHp:N0}  +{shield:N0}" : $"{curHp:N0}";
        var ts = ImGui.CalcTextSize(text);
        draw.AddText(new Vector2(p0.X + 4f, p0.Y + (h - ts.Y) * 0.5f), 0xFFFFFFFF, text);

        ImGui.Dummy(new Vector2(w, h));

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            ImGui.TextUnformatted($"HP: {curHp:N0} / {maxHp:N0}");
            if (shield > 0) ImGui.TextUnformatted($"Shield: {shield:N0}  ({snap.BarrierPercent}%)");
            if (overkill > 0) ImGui.TextColored(ColDanger, $"Overkill by {overkill:N0}");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    // All active status icons for this event
    private void DrawStatusCol(CombatEvent e)
    {
        ImGui.TableNextColumn();
        if (e.Snapshot.StatusEffects is not { Count: > 0 } effects) return;

        var sheet = Plugin.DataManager.GetExcelSheet<LuminaStatus>();
        bool sep = false;

        foreach (var group in effects
            .Select(s => (Status: sheet.GetRow(s.Id), s.StackCount))
            .Where(s => s.Status.RowId != 0)
            .Reverse()
            .GroupBy(s => s.Status.StatusCategory)
            .OrderByDescending(g => g.Key))
        {
            if (group.Key == 0) continue;
            if (sep) { ImGui.TextDisabled("|"); ImGui.SameLine(0, 4f); }
            sep = true;

            foreach (var s in group)
            {
                uint stacks = s.StackCount <= s.Status.MaxStacks ? s.StackCount : 0;
                DrawInlineIcon(s.Status.Icon, stacks);
                ImGui.SameLine(0, 2f);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
                    ImGui.TextUnformatted(s.Status.Name.ExtractText());
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }
            }
        }
    }

    private void DrawInlineIcon(uint iconId, uint stacks = 0)
    {
        if (stacks > 1) iconId += stacks - 1;
        float size = 18f * ImGuiHelpers.GlobalScale;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size, size));

        if (!_iconCache.TryGetValue(iconId, out var tex))
        {
            try
            {
                tex = Plugin.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(iconId));
            }
            catch
            {
                tex = null;
            }
            _iconCache[iconId] = tex;
        }

        if (tex == null) return;
        var wrap = tex.GetWrapOrEmpty();
        if (wrap.Handle != IntPtr.Zero)
            ImGui.GetWindowDrawList().AddImage(wrap.Handle, pos, pos + new Vector2(size, size));
    }

    private void DrawContextMenu()
    {
        if (!ImGui.BeginPopupContextWindow("##dr_ctx")) return;

        var cfg = _plugin.Configuration;
        bool locked = cfg.DeathRecapLocked;
        if (ImGui.MenuItem("Lock window", "", locked))
        {
            cfg.DeathRecapLocked = !locked;
            cfg.Save();
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Clear history"))
        {
            _plugin.CombatCapture.ClearHistory();
            _selectedDeath = 0;
        }
        if (ImGui.MenuItem("Settings")) _plugin.ToggleConfigUi();
        if (ImGui.MenuItem("Close")) IsOpen = false;
        ImGui.EndPopup();
    }
}
