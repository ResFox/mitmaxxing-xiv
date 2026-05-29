using Dalamud.Game.ClientState.Statuses;
using System;
using System.Collections.Generic;

namespace MitStack;

public record ActiveDebuff(
    MitigationEntry Entry,
    float           RemainingTime,
    uint            SourceId,
    uint            IconId);

public class MitigationResult
{
    public static readonly MitigationResult Empty = new();

    // ── Additive sums (the "listed" numbers the user wants as main display) ──
    public float PhysSum  { get; init; }
    public float MagicSum { get; init; }

    // ── Multiplicative effective DR (shown in parentheses) ──
    public float PhysEffective  { get; init; }
    public float MagicEffective { get; init; }

    // ── Active entries, split by source for tooltip display ──
    public List<ActiveDebuff> EnemyDebuffs { get; init; } = [];
    public List<ActiveDebuff> PlayerBuffs  { get; init; } = [];

    // All combined
    public List<ActiveDebuff> All
    {
        get
        {
            var all = new List<ActiveDebuff>(EnemyDebuffs.Count + PlayerBuffs.Count);
            all.AddRange(EnemyDebuffs);
            all.AddRange(PlayerBuffs);
            return all;
        }
    }

    public bool HasAny => EnemyDebuffs.Count > 0 || PlayerBuffs.Count > 0;
    public int  TotalCount => EnemyDebuffs.Count + PlayerBuffs.Count;

    /// <summary>Remaining time of the debuff/buff expiring soonest.</summary>
    public float MinRemaining
    {
        get
        {
            float min = float.MaxValue;
            foreach (var d in EnemyDebuffs) if (d.RemainingTime < min) min = d.RemainingTime;
            foreach (var d in PlayerBuffs)  if (d.RemainingTime < min) min = d.RemainingTime;
            return min == float.MaxValue ? 0f : min;
        }
    }
}

public class MitigationCalculator
{
    public MitigationResult Current { get; private set; } = MitigationResult.Empty;

    /// <summary>
    /// Scans the boss's status list for enemy debuffs AND the local player's
    /// status list for party/self buffs, then computes the combined totals.
    /// </summary>
    public void Update(StatusList? targetList, StatusList? playerList)
    {
        var enemyDebuffs = ScanList(targetList, MitigSource.EnemyDebuff);
        var playerBuffs  = ScanList(playerList, MitigSource.PlayerBuff);

        if (enemyDebuffs.Count == 0 && playerBuffs.Count == 0)
        {
            Current = MitigationResult.Empty;
            return;
        }

        float physSum  = 0f, magicSum  = 0f;
        float physMult = 1f, magicMult = 1f;

        foreach (var d in enemyDebuffs)
        {
            physSum   += d.Entry.PhysReduction;
            magicSum  += d.Entry.MagicReduction;
            physMult  *= 1f - d.Entry.PhysReduction;
            magicMult *= 1f - d.Entry.MagicReduction;
        }
        foreach (var d in playerBuffs)
        {
            physSum   += d.Entry.PhysReduction;
            magicSum  += d.Entry.MagicReduction;
            physMult  *= 1f - d.Entry.PhysReduction;
            magicMult *= 1f - d.Entry.MagicReduction;
        }

        Current = new MitigationResult
        {
            PhysSum        = physSum,
            MagicSum       = magicSum,
            PhysEffective  = 1f - physMult,
            MagicEffective = 1f - magicMult,
            EnemyDebuffs   = enemyDebuffs,
            PlayerBuffs    = playerBuffs,
        };
    }

    public void Clear() => Current = MitigationResult.Empty;

    // -----------------------------------------------------------------------
    // Scan one StatusList for all known entries of a given source category.
    // Deduplication by StatusId: the same status can only contribute once
    // (e.g. two melee using Feint = still 1 Feint instance on the boss).
    // -----------------------------------------------------------------------
    private static List<ActiveDebuff> ScanList(StatusList? list, MitigSource wantSource)
    {
        var result = new List<ActiveDebuff>();
        if (list == null) return result;

        var seen = new HashSet<uint>();

        foreach (var status in list)
        {
            if (seen.Contains(status.StatusId)) continue;

            foreach (var entry in MitigationDatabase.Entries)
            {
                if (entry.Source     != wantSource)     continue;
                if (entry.StatusId   != status.StatusId) continue;

                seen.Add(status.StatusId);

                uint iconId = 0;
                if (status.GameData.IsValid)
                    iconId = (uint)status.GameData.Value.Icon;

                result.Add(new ActiveDebuff(
                    entry,
                    status.RemainingTime,
                    status.SourceId,
                    iconId));
                break;
            }
        }

        return result;
    }
}
