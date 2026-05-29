using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace MitStack.Windows;

/// <summary>
/// The main MitStack window — a polished, raid-leader-friendly active-mit tracker.
///
/// Features:
///  • Big summary header with phys + mag percentages and a danger meter.
///  • Per-mit row with status icon, name, P/M %, animated time bar, and source.
///  • Source resolves to "[JOB-ICON] Player Name".
///  • Rows colour-coded by role (boss debuff / tank big cd / heal party / etc).
///  • Sections grouped + sorted by remaining time.
///  • Pulse highlight on newly applied mits.
/// </summary>
public class MitListWindow : Window, IDisposable
{
    private readonly Plugin _plugin;

    // Icon caches
    private readonly Dictionary<uint, ISharedImmediateTexture> _statusIconCache = new();
    private readonly Dictionary<uint, ISharedImmediateTexture> _jobIconCache    = new();

    // Track first-seen remaining time per status to drive the time progress bar
    private readonly Dictionary<uint, float> _initialDuration = new();

    // Pulse highlight: status id → time when first appeared
    private readonly Dictionary<uint, DateTime> _firstAppeared = new();
    private const float PulseSeconds = 1.5f;

    // ───── Palette ──────────────────────────────────────────────────────────
    private static readonly Vector4 ColPhys      = new(1.00f, 0.55f, 0.30f, 1f);
    private static readonly Vector4 ColMagic     = new(0.55f, 0.70f, 1.00f, 1f);
    private static readonly Vector4 ColDim       = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 ColGood      = new(0.45f, 0.95f, 0.50f, 1f);
    private static readonly Vector4 ColWarn      = new(1.00f, 0.85f, 0.10f, 1f);
    private static readonly Vector4 ColDanger    = new(1.00f, 0.40f, 0.35f, 1f);
    private static readonly Vector4 ColWhite     = new(1.00f, 1.00f, 1.00f, 1f);
    private static readonly Vector4 ColWhiteDim  = new(0.85f, 0.85f, 0.85f, 1f);

    // Role tint colours (used for the left "stripe" on each row)
    private static Vector4 RoleColour(MitRole r) => r switch
    {
        MitRole.BossDebuff      => new(0.85f, 0.30f, 0.30f, 1f),  // red
        MitRole.TankBig         => new(1.00f, 0.65f, 0.20f, 1f),  // gold
        MitRole.TankParty       => new(0.35f, 0.85f, 0.85f, 1f),  // teal
        MitRole.TankTargeted    => new(0.30f, 0.70f, 1.00f, 1f),  // cyan
        MitRole.HealerParty     => new(0.45f, 0.95f, 0.50f, 1f),  // green
        MitRole.HealerTargeted  => new(0.65f, 1.00f, 0.70f, 1f),  // light-green
        MitRole.RangedSupport   => new(0.78f, 0.55f, 1.00f, 1f),  // purple
        _                       => new(0.60f, 0.60f, 0.60f, 1f),
    };

    private const float WarningTime = 5f;

    private static readonly uint ShadowCol = 0xE6000000; // near-opaque black

    // 8-direction outline offsets for a solid stroke around text.
    private static readonly Vector2[] OutlineOffsets =
    [
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1,  0),             new(1,  0),
        new(-1,  1), new(0,  1), new(1,  1),
    ];

    // Outlined text via the window draw-list (for draw-list positioned text).
    private static void OutlinedText(ImDrawListPtr draw, Vector2 pos, uint col, string text)
    {
        foreach (var o in OutlineOffsets)
            draw.AddText(new Vector2(pos.X + o.X, pos.Y + o.Y), ShadowCol, text);
        draw.AddText(pos, col, text);
    }

    // Outlined text at the current ImGui cursor (advances layout like ImGui.Text).
    private static void OutlinedTextCursor(Vector4 col, string text)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos  = ImGui.GetCursorScreenPos();
        foreach (var o in OutlineOffsets)
            draw.AddText(new Vector2(pos.X + o.X, pos.Y + o.Y), ShadowCol, text);
        ImGui.TextColored(col, text);
    }

    public MitListWindow(Plugin plugin) : base("MitStack — Mit Tracker###MitStackList2")
    {
        _plugin = plugin;

        // Generous default size – this is now the main window
        Size          = new Vector2(540, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 200),
            MaximumSize = new Vector2(1600, 1600),
        };
    }

    public void Dispose() { }

    public override bool DrawConditions() => _plugin.ShouldShowMits();

    // -----------------------------------------------------------------------
    public override void PreDraw()
    {
        var cfg = _plugin.Configuration;

        var flags = ImGuiWindowFlags.None;
        if (cfg.ListLocked)
            flags |= ImGuiWindowFlags.NoTitleBar |
                     ImGuiWindowFlags.NoMove     |
                     ImGuiWindowFlags.NoResize   |
                     ImGuiWindowFlags.NoDocking  |
                     ImGuiWindowFlags.NoInputs;   // click-through when locked

        // Grow/shrink to fit content rather than crop with a scrollbar.
        // The user can still drag the right edge to widen — only height auto-fits.
        if (cfg.ListAutoFit)
            flags |= ImGuiWindowFlags.AlwaysAutoResize;

        Flags = flags;
        ImGui.SetNextWindowBgAlpha(cfg.ListBackgroundAlpha);
    }

    // -----------------------------------------------------------------------
    // Combined scale: Dalamud global × user-configured list scale.
    // Clamp the user scale to >= 1.0 — going below clips text against the
    // panel chrome (hard-coded internal offsets stay fixed-pixel).
    private float UiScale =>
        ImGuiHelpers.GlobalScale * MathF.Max(1.0f, _plugin.Configuration.ListScale);

    public override void Draw()
    {
        var result = _plugin.Calculator.Current;

        // Right-click anywhere in the window for quick menu
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup)
            && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup("##mitlist_ctx");

        using (var popup = ImRaii.Popup("##mitlist_ctx"))
        {
            if (popup)
            {
                var cfg = _plugin.Configuration;
                if (ImGui.MenuItem(cfg.ListLocked ? "Unlock window" : "Lock window"))
                    cfg.ToggleListLock();
                ImGui.Separator();
                if (ImGui.MenuItem("Settings"))
                    _plugin.ToggleConfigUi();
                if (ImGui.MenuItem("Close"))
                    _plugin.ToggleListWindow();
            }
        }

        TrackAppearances(result);

        if (_plugin.Configuration.ListSimpleMode)
        {
            DrawSimpleMode(result);
            return;
        }

        DrawHeader(result);
        ImGui.Separator();

        if (!result.HasAny)
        {
            ImGui.Spacing();
            OutlinedTextCursor(ColWhite, "No active mitigation on target or self.");
            ImGui.Spacing();
            OutlinedTextCursor(ColWhiteDim, "Target a boss / training dummy and have your");
            OutlinedTextCursor(ColWhiteDim, "party use mits — they will appear here in real-time.");
            return;
        }

        ImGui.Spacing();

        // Sort each section by remaining time (soonest-expiring first)
        var bossDebuffs = result.EnemyDebuffs.OrderBy(d => d.RemainingTime).ToList();
        var playerBuffs = result.PlayerBuffs .OrderBy(d => d.RemainingTime).ToList();

        DrawSection("BOSS DEBUFFS",   bossDebuffs, isBoss: true);
        if (bossDebuffs.Count > 0 && playerBuffs.Count > 0)
            ImGui.Spacing();
        DrawSection("PARTY / SELF BUFFS", playerBuffs, isBoss: false);
    }

    // -----------------------------------------------------------------------
    // Simple "vanilla" mode — clean single-line entries, no backgrounds,
    // no role stripes, no progress bars.  Just icon + name + % + time + src.
    // -----------------------------------------------------------------------
    private void DrawSimpleMode(MitigationResult result)
    {
        int physPct = (int)MathF.Round(result.PhysSum  * 100f);
        int magPct  = (int)MathF.Round(result.MagicSum * 100f);

        OutlinedTextCursor(ColPhys, "Phys");
        ImGui.SameLine(0, 4f);
        OutlinedTextCursor(ColWhite, $"{physPct}%");
        ImGui.SameLine(0, 18f);
        OutlinedTextCursor(ColMagic, "Mag");
        ImGui.SameLine(0, 4f);
        OutlinedTextCursor(ColWhite, $"{magPct}%");

        if (!result.HasAny)
        {
            ImGui.Spacing();
            OutlinedTextCursor(ColWhite, "— no active mitigation —");
            return;
        }

        ImGui.Separator();

        var all = result.EnemyDebuffs
            .Select(d => (d, isBoss: true))
            .Concat(result.PlayerBuffs.Select(d => (d, isBoss: false)))
            .OrderBy(t => t.isBoss ? 0 : 1)
            .ThenBy(t => t.d.RemainingTime)
            .ToList();

        float iconSize = 20f * UiScale;

        foreach (var (d, isBoss) in all)
        {
            DrawStatusIcon(d.IconId, iconSize);
            ImGui.SameLine(0, 6f);
            ImGui.AlignTextToFramePadding();

            // marker dot — red boss debuff, blue buff
            OutlinedTextCursor(isBoss ? new Vector4(0.95f, 0.40f, 0.40f, 1f)
                                      : new Vector4(0.55f, 0.80f, 1.00f, 1f), "•");
            ImGui.SameLine(0, 4f);

            int p = (int)MathF.Round(d.Entry.PhysReduction  * 100f);
            int m = (int)MathF.Round(d.Entry.MagicReduction * 100f);
            string pctStr = p == m ? $"{p}%" : $"{p}/{m}%";

            OutlinedTextCursor(ColWhite, $"{d.Entry.Label}  ");
            ImGui.SameLine(0, 0);
            OutlinedTextCursor(ColWhite, pctStr);
            ImGui.SameLine(0, 8f);

            var tCol = d.RemainingTime < WarningTime ? ColWarn : ColWhite;
            OutlinedTextCursor(tCol, $"{d.RemainingTime:F1}s");

            // source on the right side
            var (name, job, _) = ResolveSource(d.SourceId);
            if (name.Length > 0 && name != "—")
            {
                ImGui.SameLine();
                string src = job.Length > 0 ? $"  ({job}  {name})" : $"  ({name})";
                OutlinedTextCursor(ColWhiteDim, src);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Big top header: total %, summary bars, mit-count chip
    // -----------------------------------------------------------------------
    private void DrawHeader(MitigationResult r)
    {
        int physPct = (int)MathF.Round(r.PhysSum  * 100f);
        int magPct  = (int)MathF.Round(r.MagicSum * 100f);
        int physEff = (int)MathF.Round(r.PhysEffective  * 100f);
        int magEff  = (int)MathF.Round(r.MagicEffective * 100f);

        var avail = ImGui.GetContentRegionAvail().X;
        float colW = (avail - 8f) * 0.5f;

        DrawBigStat("Physical", physPct, physEff, ColPhys,  colW);
        ImGui.SameLine(0, 8f);
        DrawBigStat("Magical",  magPct,  magEff,  ColMagic, colW);
    }

    private void DrawBigStat(string label, int pct, int effective, Vector4 accent, float width)
    {
        float height = 54f * UiScale;

        var draw  = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var end   = new Vector2(start.X + width, start.Y + height);

        // Panel background + border
        draw.AddRectFilled(start, end, ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.12f, 1f)), 4f);
        draw.AddRect      (start, end, ImGui.GetColorU32(accent with { W = 0.35f }),            4f);

        var col = pct >= 30 ? ColGood :
                  pct >= 15 ? ColWarn :
                  pct >  0  ? new Vector4(1f, 0.65f, 0.30f, 1f) :
                              ColDim;

        // Draw labels straight onto the draw-list so ImGui layout is preserved
        OutlinedText(draw, new Vector2(start.X + 10f, start.Y + 6f),
            ImGui.GetColorU32(accent), label);

        OutlinedText(draw, new Vector2(start.X + 10f, start.Y + 22f),
            ImGui.GetColorU32(col),    $"{pct}%");

        // Real (multiplicative) DR in small text to the right of the big number
        if (effective > 0 && effective != pct)
        {
            string effText = $"real {effective}%";
            var effSize = ImGui.CalcTextSize(effText);
            OutlinedText(draw, new Vector2(end.X - 10f - effSize.X, start.Y + 26f),
                ImGui.GetColorU32(ColDim), effText);
        }

        // Progress bar at bottom
        var bp0 = new Vector2(start.X + 10f, end.Y - 12f);
        var bp1 = new Vector2(end.X   - 10f, end.Y - 6f);
        var bpf = new Vector2(bp0.X + (bp1.X - bp0.X) * Math.Clamp(pct / 100f, 0f, 1f), bp1.Y);
        draw.AddRectFilled(bp0, bp1, ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.20f, 1f)), 3f);
        draw.AddRectFilled(bp0, bpf, ImGui.GetColorU32(col),                                  3f);

        // Advance ImGui's layout
        ImGui.Dummy(new Vector2(width, height));
    }

    // -----------------------------------------------------------------------
    private void DrawSection(string title, List<ActiveDebuff> list, bool isBoss)
    {
        if (list.Count == 0) return;

        // Section header bar
        var draw  = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        float w   = ImGui.GetContentRegionAvail().X;
        float h   = ImGui.GetTextLineHeightWithSpacing() + 4f;

        var bg = isBoss
            ? new Vector4(0.30f, 0.10f, 0.10f, 0.65f)
            : new Vector4(0.10f, 0.20f, 0.30f, 0.65f);

        draw.AddRectFilled(start, new Vector2(start.X + w, start.Y + h),
            ImGui.GetColorU32(bg), 3f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f);
        OutlinedTextCursor(ColWhite, title);
        ImGui.SameLine();
        OutlinedTextCursor(ColWhiteDim, $"   ({list.Count})");

        ImGui.Dummy(new Vector2(0, 4f));

        foreach (var d in list)
            DrawMitRow(d);
    }

    // -----------------------------------------------------------------------
    private void DrawMitRow(ActiveDebuff d)
    {
        float rowH     = 38f * UiScale;
        var   draw     = ImGui.GetWindowDrawList();
        var   rowStart = ImGui.GetCursorScreenPos();
        float fullW    = ImGui.GetContentRegionAvail().X;

        // Pulse alpha for newly-applied mits
        float pulse = ComputePulse(d.Entry.StatusId);

        // Background tint
        var rowBg = new Vector4(0.16f, 0.16f, 0.18f, 0.55f);
        if (pulse > 0f)
        {
            var glow = RoleColour(d.Entry.Role);
            rowBg = Vector4.Lerp(rowBg, glow with { W = 0.45f }, pulse);
        }
        draw.AddRectFilled(rowStart, new Vector2(rowStart.X + fullW, rowStart.Y + rowH),
            ImGui.GetColorU32(rowBg), 4f);

        // Left role stripe
        var stripeCol = RoleColour(d.Entry.Role);
        draw.AddRectFilled(rowStart, new Vector2(rowStart.X + 4f, rowStart.Y + rowH),
            ImGui.GetColorU32(stripeCol), 2f);

        // Layout cursor
        float pad     = 10f;
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + pad, rowStart.Y + 4f));

        // ── Icon ──
        DrawStatusIcon(d.IconId, 30f * UiScale);

        ImGui.SameLine();
        float textColX = ImGui.GetCursorScreenPos().X;

        // ── Name + percent on first line ──
        ImGui.BeginGroup();

        ImGui.SetCursorScreenPos(new Vector2(textColX, rowStart.Y + 4f));
        OutlinedTextCursor(ColWhite, d.Entry.Label);

        // % chips right after name
        ImGui.SameLine(0, 10f);
        DrawPctChips(d.Entry);

        // ── Second line: source + time bar ──
        ImGui.SetCursorScreenPos(new Vector2(textColX, rowStart.Y + rowH * 0.5f + 2f));

        DrawSourceInline(d.SourceId);

        ImGui.EndGroup();

        // ── Time progress bar + numeric — right side ──
        DrawTimeBlock(d, rowStart, fullW, rowH);

        // Move cursor to below the row
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + rowH + 3f));
    }

    // -----------------------------------------------------------------------
    private static void DrawPctChips(MitigationEntry e)
    {
        int p = (int)MathF.Round(e.PhysReduction  * 100f);
        int m = (int)MathF.Round(e.MagicReduction * 100f);

        if (p > 0 && p == m)
        {
            DrawChip($"{p}%", new Vector4(0.85f, 0.85f, 0.85f, 1f));
            return;
        }

        if (p > 0) DrawChip($"{p}%", ColPhys);
        if (p > 0 && m > 0) ImGui.SameLine(0, 4f);
        if (m > 0) DrawChip($"{m}%", ColMagic);
    }

    private static void DrawChip(string text, Vector4 col)
    {
        var draw = ImGui.GetWindowDrawList();
        var sz   = ImGui.CalcTextSize(text);
        var p0   = ImGui.GetCursorScreenPos();
        float padX = 6f, padY = 1f;
        var p1   = new Vector2(p0.X + sz.X + padX * 2, p0.Y + sz.Y + padY * 2);

        var bg = col;
        bg.W   = 0.20f;
        draw.AddRectFilled(p0, p1, ImGui.GetColorU32(bg), 3f);
        draw.AddRect      (p0, p1, ImGui.GetColorU32(col with { W = 0.55f }), 3f);

        ImGui.SetCursorScreenPos(new Vector2(p0.X + padX, p0.Y + padY));
        ImGui.TextColored(col, text);
        ImGui.SameLine(0, 0);
        ImGui.Dummy(new Vector2(padX, 0));  // pad after
    }

    // -----------------------------------------------------------------------
    private void DrawSourceInline(uint sourceId)
    {
        var (name, jobAbbrev, jobId) = ResolveSource(sourceId);

        if (jobId != 0)
        {
            DrawJobIcon(jobId, 18f * UiScale);
            ImGui.SameLine(0, 4f);
        }

        if (jobAbbrev.Length > 0)
        {
            OutlinedTextCursor(ColWhiteDim, $"{jobAbbrev}");
            ImGui.SameLine(0, 6f);
        }

        OutlinedTextCursor(ColWhite, name);
    }

    // -----------------------------------------------------------------------
    private void DrawTimeBlock(ActiveDebuff d, Vector2 rowStart, float fullW, float rowH)
    {
        float blockW = 110f * UiScale;
        float blockX = rowStart.X + fullW - blockW - 10f;

        // Compute progress bar fraction
        float maxDur = _initialDuration.TryGetValue(d.Entry.StatusId, out var stored)
            ? stored : Math.Max(d.RemainingTime, 1f);
        if (d.RemainingTime > maxDur) maxDur = d.RemainingTime;
        float frac = Math.Clamp(d.RemainingTime / maxDur, 0f, 1f);

        var col = d.RemainingTime < WarningTime ? ColWarn : ColGood;

        // Bar
        var draw = ImGui.GetWindowDrawList();
        var p0   = new Vector2(blockX, rowStart.Y + rowH * 0.55f);
        var p1   = new Vector2(blockX + blockW, p0.Y + 8f * UiScale);
        var pf   = new Vector2(blockX + blockW * frac, p1.Y);

        draw.AddRectFilled(p0, p1, ImGui.GetColorU32(new Vector4(0.13f, 0.13f, 0.13f, 1f)), 3f);
        draw.AddRectFilled(p0, pf, ImGui.GetColorU32(col), 3f);

        // Numeric remaining time above the bar
        string txt = $"{d.RemainingTime:F1}s";
        var sz = ImGui.CalcTextSize(txt);
        ImGui.SetCursorScreenPos(new Vector2(p1.X - sz.X, rowStart.Y + 5f));
        OutlinedTextCursor(col, txt);
    }

    // -----------------------------------------------------------------------
    // Track when each status first appeared (for pulse + initial-duration bar)
    // -----------------------------------------------------------------------
    private void TrackAppearances(MitigationResult r)
    {
        var nowActive = new HashSet<uint>();

        foreach (var d in r.EnemyDebuffs) nowActive.Add(d.Entry.StatusId);
        foreach (var d in r.PlayerBuffs)  nowActive.Add(d.Entry.StatusId);

        // New appearances
        foreach (var d in r.EnemyDebuffs.Concat(r.PlayerBuffs))
        {
            if (!_firstAppeared.ContainsKey(d.Entry.StatusId))
            {
                _firstAppeared[d.Entry.StatusId] = DateTime.UtcNow;
                _initialDuration[d.Entry.StatusId] = Math.Max(d.RemainingTime, 1f);
            }
            else if (_initialDuration.TryGetValue(d.Entry.StatusId, out var prev) && d.RemainingTime > prev)
            {
                // Refresh — update max if a longer one came in
                _initialDuration[d.Entry.StatusId] = d.RemainingTime;
            }
        }

        // Drop stale entries
        var toRemove = _firstAppeared.Keys.Where(k => !nowActive.Contains(k)).ToList();
        foreach (var k in toRemove)
        {
            _firstAppeared.Remove(k);
            _initialDuration.Remove(k);
        }
    }

    private float ComputePulse(uint statusId)
    {
        if (!_firstAppeared.TryGetValue(statusId, out var t)) return 0f;
        double age = (DateTime.UtcNow - t).TotalSeconds;
        if (age >= PulseSeconds) return 0f;
        return (float)(1.0 - age / PulseSeconds);
    }

    // -----------------------------------------------------------------------
    private void DrawStatusIcon(uint iconId, float size)
    {
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        if (!_statusIconCache.TryGetValue(iconId, out var tex))
        {
            if (_statusIconCache.Count > 256) _statusIconCache.Clear();
            tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            _statusIconCache[iconId] = tex;
        }

        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
            ImGui.Image(wrap.Handle, new Vector2(size, size));
        else
            ImGui.Dummy(new Vector2(size, size));
    }

    private void DrawJobIcon(uint jobId, float size)
    {
        if (jobId == 0) { ImGui.Dummy(new Vector2(size, size)); return; }

        // Standard FF14 job-icon ID convention: 062100 + classJobId
        uint iconId = 62100u + jobId;

        if (!_jobIconCache.TryGetValue(iconId, out var tex))
        {
            tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            _jobIconCache[iconId] = tex;
        }

        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
            ImGui.Image(wrap.Handle, new Vector2(size, size));
        else
            ImGui.Dummy(new Vector2(size, size));
    }

    // -----------------------------------------------------------------------
    private static (string name, string job, uint jobId) ResolveSource(uint sourceId)
    {
        if (sourceId == 0 || sourceId == 0xE0000000)
            return ("—", "", 0);

        var actor = Plugin.ObjectTable.SearchById(sourceId);
        if (actor == null)
            return ("(out of range)", "", 0);

        string name = actor.Name.TextValue;
        string job  = "";
        uint   jobId = 0;

        if (actor is IBattleChara bc && bc.ClassJob.IsValid)
        {
            job   = bc.ClassJob.Value.Abbreviation.ExtractText();
            jobId = bc.ClassJob.RowId;
        }

        return (name, job, jobId);
    }
}
