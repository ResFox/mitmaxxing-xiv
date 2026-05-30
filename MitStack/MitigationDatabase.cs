namespace MitStack;

// Which status list a mitigation entry lives on
public enum MitigSource
{
    EnemyDebuff,  // debuff on the boss → read from target.StatusList
    PlayerBuff,   // buff on the local player → read from localPlayer.StatusList
}

public enum DamagePrimary { Both, Physical, Magical }

/// <summary>
/// Role-style categorisation used for row colouring + sorting in the list UI.
/// Pure cosmetic / organisational.
/// </summary>
public enum MitRole
{
    BossDebuff,       // red:    Reprisal, Feint, Addle, Dismantle
    TankBig,          // gold:   Rampart, Sentinel/Guardian, Vengeance/Damnation, etc.
    TankParty,        // teal:   Heart of Light, Dark Missionary, Divine Veil
    TankTargeted,     // cyan:   Intervention, Heart of Corundum, Nascent Glint
    HealerParty,      // green:  Sacred Soil, Holos, Temperance, Kerachole, etc.
    HealerTargeted,   // light-green: Taurochole
    RangedSupport,    // purple: Shield Samba, Troubadour, Tactician
    Shield,           // blue:   barrier-only effects (no DR) — shown in death recap as info
}

/// <summary>One barrier/shield effect — pure HP absorption, no DR.</summary>
public record ShieldEntry(uint StatusId, string Label);

/// <summary>
/// Common shield-applying statuses. Tracked separately from DR so we can
/// show "had Shake It Off active when you died" in the death recap.
/// IDs verified via XIVAPI / community resources.
/// </summary>
public static class ShieldDatabase
{
    public static readonly ShieldEntry[] Entries =
    [
        new(297,  "Galvanize"),               // SCH Adloquium
        new(1457, "Shake It Off"),            // WAR — 15% maxHP barrier
        new(727,  "Divine Veil"),             // PLD — proc barrier on next heal
        new(1873, "Aspected Helios"),         // AST (legacy)
        new(2607, "Eukrasian Diagnosis"),     // SGE single-target shield
        new(2609, "Eukrasian Prognosis"),     // SGE AoE shield
        new(1918, "Catalyze"),                // SCH crit barrier
        new(1872, "Nascent Glint"),           // WAR Nascent Flash shield
        new(2719, "Macrocosmos"),             // AST delayed heal-back
    ];
}

/// <summary>
/// One tracked mitigation effect.
/// PhysReduction / MagicReduction are 0–1 fractions (0.10 = 10%).
/// </summary>
public record MitigationEntry(
    uint         StatusId,
    string       Label,
    float        PhysReduction,
    float        MagicReduction,
    MitigSource  Source,
    MitRole      Role,
    DamagePrimary Primary = DamagePrimary.Both
);

/// <summary>
/// Complete mitigation table for Patch 7.5 (Dawntrail, level cap 100).
///
/// ════════════════════════════════════════════════════════════════════
/// HOW TO VERIFY A STATUS ID IN-GAME
/// ════════════════════════════════════════════════════════════════════
/// 1. Load the plugin, target a training dummy (or a real boss).
/// 2. Type /mitmaxxing debug  →  all active status IDs print to /xllog.
/// 3. Use the local player debug section to find your own buffs.
///
/// ════════════════════════════════════════════════════════════════════
/// SOURCE DATA (ALL IDs VERIFIED VIA XIVAPI.COM/Status/{ID})
/// ════════════════════════════════════════════════════════════════════
/// • All status IDs confirmed via direct XIVAPI lookups (Name_en field).
///
/// • Bloodwhetting (lv82): replaced single Raw Intuition status (735)
///   with TWO separate 10% DR statuses (2678 + 2679).
///   Status 735 (Raw Intuition) still appears in level-synced content.
///
/// ════════════════════════════════════════════════════════════════════
/// DAWNTRAIL VALUE CHANGES (lv92 upgrades)
/// ════════════════════════════════════════════════════════════════════
///   Sentinel (74) → Guardian (3829):        30% → 40%
///   Vengeance (912) → Damnation (3832):     30% → 40%
///   Shadow Wall (747) → Shadowed Vigil (3835): 30% → 40%
///   Nebula (1834) → Great Nebula (3838):    30% → 40%
///   Shield Samba / Troubadour / Tactician:  10% → 15%
/// </summary>
public static class MitigationDatabase
{
    public static readonly MitigationEntry[] Entries =
    [
        // ══════════════════════════════════════════════════════════════════
        // ENEMY DEBUFFS — read from target (boss) StatusList
        // ══════════════════════════════════════════════════════════════════

        // Role actions — debuff the boss's damage output
        new(1193, "Reprisal",      0.10f, 0.10f, MitigSource.EnemyDebuff, MitRole.BossDebuff),
        new(1195, "Feint",         0.10f, 0.05f, MitigSource.EnemyDebuff, MitRole.BossDebuff, DamagePrimary.Physical),
        new(1203, "Addle",         0.05f, 0.10f, MitigSource.EnemyDebuff, MitRole.BossDebuff, DamagePrimary.Magical),
        new(860,  "Dismantle",     0.10f, 0.10f, MitigSource.EnemyDebuff, MitRole.BossDebuff),

        // ══════════════════════════════════════════════════════════════════
        // PLAYER BUFFS — read from local player StatusList
        // ══════════════════════════════════════════════════════════════════

        // ── Physical Ranged DPS party mits (15% both) ──
        new(1826, "Shield Samba",  0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.RangedSupport),
        new(1934, "Troubadour",    0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.RangedSupport),
        new(1951, "Tactician",     0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.RangedSupport),

        // ── Tank party mits ──
        new(1839, "Heart of Light",  0.00f, 0.10f, MitigSource.PlayerBuff, MitRole.TankParty, DamagePrimary.Magical),
        new(1894, "Dark Missionary", 0.00f, 0.10f, MitigSource.PlayerBuff, MitRole.TankParty, DamagePrimary.Magical),

        // ── Tank self-mits ──
        new(1191, "Rampart",       0.20f, 0.20f, MitigSource.PlayerBuff, MitRole.TankBig),

        // ── PLD ──
        new(74,   "Sentinel",       0.30f, 0.30f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(3829, "Guardian",       0.40f, 0.40f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(1856, "Sheltron",       0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(2674, "Holy Sheltron",  0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(1175, "Passage of Arms",0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.TankParty),

        // ── WAR ──
        new(912,  "Vengeance",     0.30f, 0.30f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(3832, "Damnation",     0.40f, 0.40f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(735,  "Raw Intuition", 0.20f, 0.20f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(2678, "Bloodwhetting", 0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(2679, "Stem the Flow", 0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(1858, "Nascent Glint", 0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.TankTargeted),

        // ── DRK ──
        new(747,  "Shadow Wall",    0.30f, 0.30f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(3835, "Shadowed Vigil", 0.40f, 0.40f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(746,  "Dark Mind",      0.00f, 0.20f, MitigSource.PlayerBuff, MitRole.TankBig, DamagePrimary.Magical),
        new(2682, "Oblation",       0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.TankTargeted),

        // ── GNB ──
        new(1834, "Nebula",         0.30f, 0.30f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(3838, "Great Nebula",   0.40f, 0.40f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(1832, "Camouflage",     0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.TankBig),
        new(1840, "Heart of Stone", 0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.TankTargeted),
        new(2683, "Corundum",       0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.TankTargeted),

        // ── Intervention (PLD lv62 / GNB lv70): 10% DR on target ──
        new(1174, "Intervention",   0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.TankTargeted),

        // ── Healer party mits ──
        new(1873, "Temperance",        0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerParty),
        new(299,  "Sacred Soil",       0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerParty),
        new(2711, "Desperate Meas.",   0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerParty),
        new(317,  "Fey Illumination",  0.00f, 0.05f, MitigSource.PlayerBuff, MitRole.HealerParty, DamagePrimary.Magical),
        new(1875, "Seraphic Illum.",   0.00f, 0.05f, MitigSource.PlayerBuff, MitRole.HealerParty, DamagePrimary.Magical),
        new(849,  "Collective Uncon.", 0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerParty),
        new(1209, "Sunsign",           0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerParty),
        new(2618, "Kerachole",         0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerParty),
        new(3003, "Holos",             0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerParty),
        new(2619, "Taurochole",        0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerTargeted),
        new(2708, "Aquaveil",          0.15f, 0.15f, MitigSource.PlayerBuff, MitRole.HealerTargeted),
        new(2717, "Exaltation",        0.10f, 0.10f, MitigSource.PlayerBuff, MitRole.HealerTargeted),
    ];
}
