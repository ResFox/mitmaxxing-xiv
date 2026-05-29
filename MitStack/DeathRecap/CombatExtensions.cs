using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace MitStack.DeathRecap;

internal static class CombatExtensions
{
    public static unsafe byte Barrier(this IPlayerCharacter player)
        => ((Character*)player.Address)->CharacterData.ShieldValue;

    public static CombatEvent.EventSnapshot Snapshot(
        this IPlayerCharacter player,
        bool snapEffects = false,
        IReadOnlyCollection<uint>? additionalStatus = null)
    {
        List<CombatEvent.StatusEffectSnapshot>? statusEffects = snapEffects
            ? player.StatusList
                .Select(s => new CombatEvent.StatusEffectSnapshot { Id = s.StatusId, StackCount = s.Param })
                .ToList()
            : null;

        if (additionalStatus != null && statusEffects != null)
        {
            statusEffects.AddRange(additionalStatus.Select(s =>
                new CombatEvent.StatusEffectSnapshot { Id = s, StackCount = 0 }));
        }

        return new CombatEvent.EventSnapshot
        {
            Time           = DateTime.Now,
            CurrentHp      = player.CurrentHp,
            MaxHp          = player.MaxHp,
            StatusEffects  = statusEffects,
            BarrierPercent = player.Barrier(),
        };
    }

    public static void AddEntry<TKey, TValue>(this Dictionary<TKey, List<TValue>> dict, TKey key, TValue val)
        where TKey : notnull
    {
        if (dict.TryGetValue(key, out var list))
            list.Add(val);
        else
            dict[key] = [val];
    }
}
