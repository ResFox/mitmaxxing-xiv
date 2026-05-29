using Dalamud.Configuration;
using System;

namespace MitStack;

/// <summary>When the mitigation overlay / tracker should be visible.</summary>
public enum MitVisibility
{
    Always   = 0,
    InCombat = 1,
    InDuty   = 2,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;

    /// <summary>Controls when the mit overlay + tracker are shown.</summary>
    public MitVisibility ShowMitsWhen { get; set; } = MitVisibility.Always;

    /// <summary>2-icon overlay position is locked — no title bar, no dragging.</summary>
    public bool Locked { get; set; } = false;

    /// <summary>Mit-tracker list window is locked (no title bar, no move/resize).</summary>
    public bool ListLocked { get; set; } = false;

    /// <summary>Mit-tracker list window background opacity.</summary>
    public float ListBackgroundAlpha { get; set; } = 0.85f;

    /// <summary>Default content scale for the list window (1.0 = normal).</summary>
    public float ListScale { get; set; } = 1.0f;

    /// <summary>Mit Tracker auto-resizes vertically to fit current content (no scrolling).</summary>
    public bool ListAutoFit { get; set; } = true;

    /// <summary>List window uses a stripped-down "vanilla" row layout.</summary>
    public bool ListSimpleMode { get; set; } = false;

    /// <summary>Mini overlay uses a game-style icon+number layout (no progress bar, no padding).</summary>
    public bool OverlaySimpleMode { get; set; } = false;

    /// <summary>Prefer focus target; fall back to current target if no focus target is set.</summary>
    public bool UseFocusTarget { get; set; } = true;

    /// <summary>Show per-debuff breakdown in a tooltip on hover.</summary>
    public bool ShowTooltip { get; set; } = true;

    /// <summary>Persisted open/closed state of the detailed mit list window.</summary>
    public bool ListWindowOpen { get; set; } = true;

    /// <summary>Icon size in pixels (before HiDPI scaling).</summary>
    public float IconSize { get; set; } = 24f;

    /// <summary>Window background opacity.</summary>
    public float BackgroundAlpha { get; set; } = 0.70f;

    /// <summary>Only show the overlay while in a duty (instance).</summary>
    public bool OnlyInDuty { get; set; } = false;

    // ── Death Recap ──────────────────────────────────────────────────────
    /// <summary>Master switch for packet-hook death tracking.</summary>
    public bool DeathRecapEnabled { get; set; } = true;

    /// <summary>Seconds of combat events to keep per player before death.</summary>
    public float DeathRecapKeepEventsSeconds { get; set; } = 30f;

    /// <summary>Minutes to keep death records in memory.</summary>
    public int DeathRecapKeepDeathsMinutes { get; set; } = 60;

    /// <summary>Show damage rows in the recap table.</summary>
    public bool DeathRecapShowDamage { get; set; } = true;

    /// <summary>Show healing rows in the recap table.</summary>
    public bool DeathRecapShowHealing { get; set; } = true;

    /// <summary>Show beneficial status applications (Shake It Off, Bloodwhetting, etc.).</summary>
    public bool DeathRecapShowBuffs { get; set; } = true;

    /// <summary>Show detrimental status applications.</summary>
    public bool DeathRecapShowDebuffs { get; set; } = true;

    /// <summary>Auto-open the recap window when someone dies.</summary>
    public bool DeathRecapAutoOpen { get; set; } = true;

    /// <summary>Persisted open/closed state of the death recap window.</summary>
    public bool DeathRecapWindowOpen { get; set; } = false;

    /// <summary>Lock position/size of death recap window (also enables click-through).</summary>
    public bool DeathRecapLocked { get; set; } = false;

    /// <summary>Background opacity for death recap window.</summary>
    public float DeathRecapBackgroundAlpha { get; set; } = 0.92f;

    /// <summary>Only track deaths while in a duty.</summary>
    public bool DeathRecapOnlyInDuty { get; set; } = true;

    // ── Death notification popup ─────────────────────────────────────────
    /// <summary>Use a small popup notification on death instead of opening the full window.</summary>
    public bool DeathPopupEnabled { get; set; } = true;

    /// <summary>Auto-close the popup after this many seconds (0 = stay open forever).</summary>
    public float DeathPopupAutoCloseSeconds { get; set; } = 12f;

    /// <summary>Lock popup position (and make click-through).</summary>
    public bool DeathPopupLocked { get; set; } = false;

    /// <summary>Background opacity for popup.</summary>
    public float DeathPopupBackgroundAlpha { get; set; } = 0.92f;

    /// <summary>Saved popup window X position.</summary>
    public float DeathPopupPosX { get; set; } = 0f;
    /// <summary>Saved popup window Y position.</summary>
    public float DeathPopupPosY { get; set; } = 0f;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public void ToggleLock()
    {
        Locked = !Locked;
        Save();
    }

    public void ToggleListLock()
    {
        ListLocked = !ListLocked;
        Save();
    }
}
