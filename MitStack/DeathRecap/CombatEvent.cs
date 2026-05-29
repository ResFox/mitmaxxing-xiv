using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace MitStack.DeathRecap;

public enum StatusCategory : byte
{
    None         = 0,
    Beneficial   = 1,
    Detrimental  = 2,
}

public enum DamageType : byte
{
    None      = 0,
    Slashing  = 1,
    Piercing  = 2,
    Hitting   = 3,
    Shooting  = 4,
    Magic     = 5,
    Darkness  = 6,
}

public record CombatEvent
{
    public required EventSnapshot Snapshot { get; init; }

    public record EventSnapshot
    {
        public required DateTime Time { get; init; }
        public required uint     CurrentHp { get; init; }
        public required uint     MaxHp { get; init; }
        public List<StatusEffectSnapshot>? StatusEffects { get; init; }
        public uint BarrierPercent { get; init; }
    }

    public record struct StatusEffectSnapshot
    {
        public required uint Id;
        public required uint StackCount;
    }

    public record StatusEffect : CombatEvent
    {
        public required uint           Id { get; init; }
        public required uint           StackCount { get; init; }
        public required string?        Source { get; init; }
        public required uint?          Icon { get; init; }
        public required float          Duration { get; init; }
        public required string?        Status { get; init; }
        public required string?        Description { get; init; }
        public required StatusCategory Category { get; init; }
    }

    public record HoT : CombatEvent
    {
        public required uint Amount { get; init; }
    }

    public record DoT : CombatEvent
    {
        public required uint Amount { get; init; }
    }

    public record DamageTaken : CombatEvent
    {
        public required string?    Source { get; init; }
        public required uint     Amount { get; init; }
        public required string   Action { get; init; }
        public          bool     Crit { get; init; }
        public          bool     DirectHit { get; init; }
        public required DamageType DamageType { get; init; }
        public required ActionType DisplayType { get; init; }
        public          bool     Parried { get; init; }
        public          bool     Blocked { get; init; }
        public required uint?  Icon { get; init; }
    }

    public record Healed : CombatEvent
    {
        public required string?  Source { get; init; }
        public required uint     Amount { get; init; }
        public required string   Action { get; init; }
        public          bool     Crit { get; init; }
        public required uint?    Icon { get; init; }
    }
}

public class DeathRecord
{
    public uint     PlayerId;
    public string   CharName  = "";
    public string   JobAbbrev = "";
    public uint     JobId;
    public DateTime DiedAt;
    public List<CombatEvent> Events = [];

    public uint TotalDamage =>
        (uint)Events.Sum(e => e switch
        {
            CombatEvent.DamageTaken dt => dt.Amount,
            CombatEvent.DoT dot        => dot.Amount,
            _                          => 0u,
        });

    public uint MaxHp =>
        Events.Count > 0 ? Events[^1].Snapshot.MaxHp : 0u;

    /// <summary>Damage that exceeded remaining HP on the killing blow.</summary>
    public uint KillingBlowOverkill
    {
        get
        {
            for (int i = Events.Count - 1; i >= 0; i--)
            {
                switch (Events[i])
                {
                    case CombatEvent.DamageTaken dt when dt.Amount > dt.Snapshot.CurrentHp:
                        return dt.Amount - dt.Snapshot.CurrentHp;
                    case CombatEvent.DoT dot when dot.Amount > dot.Snapshot.CurrentHp:
                        return dot.Amount - dot.Snapshot.CurrentHp;
                }
            }
            return 0;
        }
    }

    /// <summary>Total damage minus max HP (accounts for healing during the window).</summary>
    public uint NetOverkill =>
        TotalDamage > MaxHp && MaxHp > 0 ? TotalDamage - MaxHp : 0u;
}
