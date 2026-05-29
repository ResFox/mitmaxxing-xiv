using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Common.Math;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace MitStack.DeathRecap;

// Damage / healing / status capture ported from Kouzukii/ffxiv-deathrecap (MIT).
// The ActionEffect packet hook (stable FFXIVClientStructs address) supplies real
// damage amounts including overkill. ActorControl + EffectResult hooks are
// best-effort (byte signatures, may break across patches); death triggering is
// also done by per-frame polling so a recap always fires even when those
// signatures are stale.
public class CombatEventCapture : IDisposable
{
    private readonly Configuration _config;
    private readonly IPluginLog    _log;

    private readonly Dictionary<uint, List<CombatEvent>> _combatEvents = new();
    private readonly Dictionary<uint, bool>              _aliveState   = new();

    public List<DeathRecord> Deaths { get; } = [];
    public event Action<DeathRecord>? OnDeath;

    public bool   ActionEffectInstalled { get; private set; }
    public bool   ActorControlInstalled { get; private set; }
    public bool   EffectResultInstalled { get; private set; }
    public string InstallError { get; private set; } = "";
    public int    TrackedActorCount => _combatEvents.Count;
    public int    TotalCapturedEvents => _combatEvents.Sum(kv => kv.Value.Count);

    private unsafe delegate void ProcessActionEffectDelegate(
        uint casterEntityId, Character* casterPtr, Vector3* targetPos,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds);

    private delegate void ProcessActorControlDelegate(
        uint entityId, uint category,
        uint param1, uint param2, uint param3, uint param4,
        uint param5, uint param6, uint param7, uint param8,
        ulong targetId, byte param9);

    private delegate void ProcessEffectResultDelegate(uint targetId, IntPtr actionIntegrityData, byte isReplay);

    private Hook<ProcessActionEffectDelegate>? _actionEffectHook;

    [Signature("E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64", DetourName = nameof(ActorControlDetour))]
    private readonly Hook<ProcessActorControlDelegate>? _actorControlHook = null!;

    [Signature("48 8B C4 44 88 40 18 89 48 08", DetourName = nameof(EffectResultDetour))]
    private readonly Hook<ProcessEffectResultDelegate>? _effectResultHook = null!;

    public unsafe CombatEventCapture(Configuration config, IGameInteropProvider interop, IPluginLog log)
    {
        _config = config;
        _log    = log;

        try
        {
            interop.InitializeFromAttributes(this);

            _actionEffectHook = interop.HookFromSignature<ProcessActionEffectDelegate>(
                ActionEffectHandler.Addresses.Receive.String, ActionEffectDetour);
            _actionEffectHook.Enable();
            ActionEffectInstalled = true;

            _actorControlHook?.Enable();
            ActorControlInstalled = _actorControlHook != null;

            _effectResultHook?.Enable();
            EffectResultInstalled = _effectResultHook != null;

            log.Information(
                $"Death recap hooks: actionEffect={ActionEffectInstalled}, " +
                $"actorControl={ActorControlInstalled}, effectResult={EffectResultInstalled}");
        }
        catch (Exception ex)
        {
            InstallError = ex.Message;
            log.Error(ex, "Failed to install death recap hooks");
        }
    }

    public void Dispose()
    {
        try
        {
            _actionEffectHook?.Dispose();
            _actorControlHook?.Dispose();
            _effectResultHook?.Dispose();
        }
        catch { }
    }

    public void ClearHistory() => Deaths.Clear();

    // Injects a synthetic death so the recap layout can be previewed without
    // actually dying. Wired to "/mitstack testdeath".
    public void InjectTestDeath()
    {
        var now    = DateTime.Now;
        uint maxHp = 351_854;

        CombatEvent.EventSnapshot Snap(uint hp, double secsAgo, uint barrier, params (uint id, uint stacks)[] statuses)
            => new()
            {
                Time           = now.AddSeconds(-secsAgo),
                CurrentHp      = hp,
                MaxHp          = maxHp,
                BarrierPercent = barrier,
                StatusEffects  = statuses
                    .Select(s => new CombatEvent.StatusEffectSnapshot { Id = s.id, StackCount = s.stacks })
                    .ToList(),
            };

        var events = new List<CombatEvent>
        {
            new CombatEvent.StatusEffect
            {
                Snapshot = Snap(maxHp, 9, 0), Id = 1191, StackCount = 0, Source = "Res Fox",
                Icon = null, Duration = 20, Status = "Rampart", Description = "Reduces damage taken by 20%.",
                Category = StatusCategory.Beneficial,
            },
            new CombatEvent.DamageTaken
            {
                Snapshot = Snap(maxHp, 8, 0), Source = "Themis", Amount = 27_333, Action = "Ultimate Verdict",
                Icon = null, DamageType = DamageType.Magic, DisplayType = default,
            },
            new CombatEvent.Healed
            {
                Snapshot = Snap(324_521, 6, 0), Source = "Healer", Amount = 41_310, Action = "Cure III", Icon = null,
            },
            new CombatEvent.DamageTaken
            {
                Snapshot = Snap(maxHp, 3, 15), Source = "Themis", Amount = 70_371, Action = "Divine Judgment",
                Icon = null, DamageType = DamageType.Magic, DisplayType = default,
            },
            new CombatEvent.DamageTaken
            {
                Snapshot = Snap(281_483, 0, 0), Source = "Themis", Amount = 423_900, Action = "Heavenly Hell",
                Icon = null, DamageType = DamageType.Magic, DisplayType = default,
            },
        };

        var record = new DeathRecord
        {
            PlayerId  = 0,
            CharName  = "Test Dummy",
            JobId     = 0,
            JobAbbrev = "WAR",
            DiedAt    = now,
            Events    = events,
        };

        Deaths.Insert(0, record);
        if (Deaths.Count > 20) Deaths.RemoveAt(Deaths.Count - 1);
        try { OnDeath?.Invoke(record); } catch { }
    }

    public void CleanStaleEvents()
    {
        try
        {
            var entriesToRemove = new List<uint>();
            foreach (var (id, events) in _combatEvents)
            {
                if (events.Count == 0 ||
                    (DateTime.Now - events[^1].Snapshot.Time).TotalSeconds > _config.DeathRecapKeepEventsSeconds)
                {
                    entriesToRemove.Add(id);
                    continue;
                }

                var cut = DateTime.Now - TimeSpan.FromSeconds(_config.DeathRecapKeepEventsSeconds);
                for (var i = 0; i < events.Count; i++)
                {
                    if (events[i].Snapshot.Time > cut)
                    {
                        if (i > 0) events.RemoveRange(0, i);
                        break;
                    }
                }
            }

            foreach (var id in entriesToRemove) _combatEvents.Remove(id);

            var deathCut = DateTime.Now - TimeSpan.FromMinutes(_config.DeathRecapKeepDeathsMinutes);
            Deaths.RemoveAll(d => d.DiedAt < deathCut);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error while clearing events");
        }
    }

    // Per-frame death poll. Reliable trigger independent of the ActorControl
    // signature. Called from Plugin.OnFrameworkUpdate.
    public void Update(IBattleChara? _)
    {
        try
        {
            if (!_config.DeathRecapEnabled) return;
            if (_config.DeathRecapOnlyInDuty && !Plugin.DutyState.IsDutyStarted) return;

            PollActor(Plugin.PlayerState.EntityId);
            foreach (var member in Plugin.PartyList)
                if (member != null && member.EntityId != Plugin.PlayerState.EntityId)
                    PollActor(member.EntityId);
        }
        catch (Exception ex)
        {
            _log.Debug($"Death poll error: {ex.Message}");
        }
    }

    private void PollActor(uint entityId)
    {
        if (Plugin.ObjectTable.SearchById(entityId) is not IBattleChara bc) return;

        bool dead    = bc.IsDead || bc.CurrentHp == 0;
        bool wasAlive = !_aliveState.TryGetValue(entityId, out var prev) || prev;

        if (dead && wasAlive)
            FinalizeDeath(entityId, bc);

        _aliveState[entityId] = !dead;
    }

    private void FinalizeDeath(uint entityId, IBattleChara bc)
    {
        if (!_combatEvents.Remove(entityId, out var events) || events.Count == 0) return;

        var record = new DeathRecord
        {
            PlayerId  = entityId,
            CharName  = bc.Name.TextValue,
            JobId     = bc.ClassJob.RowId,
            JobAbbrev = bc.ClassJob.IsValid ? bc.ClassJob.Value.Abbreviation.ExtractText() : "",
            DiedAt    = DateTime.Now,
            Events    = events,
        };
        Deaths.Insert(0, record);
        if (Deaths.Count > 20) Deaths.RemoveAt(Deaths.Count - 1);

        try { OnDeath?.Invoke(record); }
        catch (Exception ex) { _log.Warning($"OnDeath handler threw: {ex.Message}"); }
    }

    private bool ShouldCapture(uint actorId)
    {
        if (!_config.DeathRecapEnabled) return false;
        if (_config.DeathRecapOnlyInDuty && !Plugin.DutyState.IsDutyStarted) return false;
        if (actorId == Plugin.PlayerState.EntityId) return true;
        foreach (var member in Plugin.PartyList)
            if (member?.EntityId == actorId) return true;
        return false;
    }

    private unsafe void ActionEffectDetour(
        uint casterEntityId, Character* casterPtr, Vector3* targetPos,
        ActionEffectHandler.Header* effectHeader, ActionEffectHandler.TargetEffects* effectArray,
        GameObjectId* targetEntityIds)
    {
        _actionEffectHook!.Original(casterEntityId, casterPtr, targetPos, effectHeader, effectArray, targetEntityIds);

        try
        {
            if (effectHeader->NumTargets == 0) return;

            var actionId = (ActionType)effectHeader->ActionType switch
            {
                ActionType.Mount => 0xD000000 + effectHeader->ActionId,
                ActionType.Item  => 0x2000000 + effectHeader->ActionId,
                _                => effectHeader->SpellId,
            };

            LuminaAction? action = null;
            string? source = null;
            List<uint>? additionalStatus = null;

            int targetCount = Math.Min((int)effectHeader->NumTargets, 32);
            for (var i = 0; i < targetCount; i++)
            {
                var actionTargetId = (uint)(targetEntityIds[i] & uint.MaxValue);
                if (!ShouldCapture(actionTargetId)) continue;
                if (Plugin.ObjectTable.SearchById(actionTargetId) is not IPlayerCharacter p) continue;

                for (var j = 0; j < 8; j++)
                {
                    ref var actionEffect = ref effectArray[i].Effects[j];
                    if (actionEffect.Type == 0) continue;

                    uint amount = actionEffect.Value;
                    if ((actionEffect.Param4 & 0x40) == 0x40)
                        amount += (uint)actionEffect.Param3 << 16;

                    action ??= Plugin.DataManager.GetExcelSheet<LuminaAction>().GetRowOrDefault(actionId);
                    source ??= casterPtr != null ? casterPtr->NameString : "";

                    switch ((ActionEffectType)actionEffect.Type)
                    {
                        case ActionEffectType.Miss:
                        case ActionEffectType.Damage:
                        case ActionEffectType.BlockedDamage:
                        case ActionEffectType.ParriedDamage:
                            if (additionalStatus == null)
                            {
                                additionalStatus = [];
                                if (casterPtr != null)
                                {
                                    var sm = casterPtr->GetStatusManager();
                                    if (sm != null)
                                    {
                                        foreach (ref var status in sm->Status)
                                            if (status.StatusId is 1203 or 1195 or 1193 or 860 or 1715 or 2115 or 3642)
                                                additionalStatus.Add(status.StatusId);
                                    }
                                }
                            }

                            _combatEvents.AddEntry(actionTargetId, new CombatEvent.DamageTaken
                            {
                                Snapshot    = p.Snapshot(true, additionalStatus),
                                Source      = source,
                                Amount      = amount,
                                Action      = action?.ActionCategory.RowId == 1
                                    ? "Auto-attack"
                                    : action?.Name.ExtractText() ?? "",
                                Icon        = action?.Icon,
                                Crit        = (actionEffect.Param0 & 0x20) == 0x20,
                                DirectHit   = (actionEffect.Param0 & 0x40) == 0x40,
                                DamageType  = (DamageType)(actionEffect.Param1 & 0xF),
                                Parried     = actionEffect.Type == (byte)ActionEffectType.ParriedDamage,
                                Blocked     = actionEffect.Type == (byte)ActionEffectType.BlockedDamage,
                                DisplayType = (ActionType)effectHeader->ActionType,
                            });
                            break;

                        case ActionEffectType.Heal:
                            _combatEvents.AddEntry(actionTargetId, new CombatEvent.Healed
                            {
                                Snapshot = p.Snapshot(true),
                                Source   = source,
                                Amount   = amount,
                                Action   = action?.Name.ExtractText() ?? "",
                                Icon     = action?.Icon,
                                Crit     = (actionEffect.Param1 & 0x20) == 0x20,
                            });
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ActionEffectDetour");
        }
    }

    private void ActorControlDetour(
        uint entityId, uint category,
        uint param1, uint param2, uint param3, uint param4,
        uint param5, uint param6, uint param7, uint param8,
        ulong targetId, byte param9)
    {
        _actorControlHook!.Original(entityId, category, param1, param2, param3, param4,
            param5, param6, param7, param8, targetId, param9);

        try
        {
            if (!ShouldCapture(entityId)) return;
            if (Plugin.ObjectTable.SearchById(entityId) is not IPlayerCharacter p) return;

            switch ((ActorControlCategory)category)
            {
                case ActorControlCategory.DoT:
                    _combatEvents.AddEntry(entityId, new CombatEvent.DoT
                    {
                        Snapshot = p.Snapshot(),
                        Amount   = param2,
                    });
                    break;

                case ActorControlCategory.HoT:
                    if (param1 != 0)
                    {
                        var status = Plugin.DataManager.GetExcelSheet<LuminaStatus>().GetRowOrDefault(param1);
                        _combatEvents.AddEntry(entityId, new CombatEvent.Healed
                        {
                            Snapshot = p.Snapshot(),
                            Source   = p.Name.TextValue,
                            Amount   = param2,
                            Action   = status?.Name.ExtractText() ?? "",
                            Icon     = status?.Icon,
                            Crit     = param4 == 1,
                        });
                    }
                    else
                    {
                        _combatEvents.AddEntry(entityId, new CombatEvent.HoT
                        {
                            Snapshot = p.Snapshot(),
                            Amount   = param2,
                        });
                    }
                    break;

                case ActorControlCategory.Death:
                    if (Plugin.ObjectTable.SearchById(entityId) is IBattleChara bc)
                    {
                        _aliveState[entityId] = false;
                        FinalizeDeath(entityId, bc);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ActorControlDetour");
        }
    }

    private unsafe void EffectResultDetour(uint targetId, IntPtr actionIntegrityData, byte isReplay)
    {
        _effectResultHook!.Original(targetId, actionIntegrityData, isReplay);

        try
        {
            if (!ShouldCapture(targetId)) return;
            if (Plugin.ObjectTable.SearchById(targetId) is not IPlayerCharacter p) return;

            var message     = (AddStatusEffect*)actionIntegrityData;
            var effects     = (StatusEffectAddEntry*)message->Effects;
            var effectCount = Math.Min(message->EffectCount, (byte)4);

            for (uint j = 0; j < effectCount; j++)
            {
                var effect   = effects[j];
                var effectId = effect.EffectId;
                if (effectId <= 0) continue;
                if (effect.Duration < 0) continue;

                var sourceName = Plugin.ObjectTable.SearchById(effect.SourceActorId)?.Name.TextValue;
                var status     = Plugin.DataManager.GetExcelSheet<LuminaStatus>().GetRowOrDefault(effectId);

                _combatEvents.AddEntry(targetId, new CombatEvent.StatusEffect
                {
                    Snapshot    = p.Snapshot(),
                    Id          = effectId,
                    StackCount  = effect.StackCount <= (status?.MaxStacks ?? 0) ? effect.StackCount : 0u,
                    Icon        = status?.Icon,
                    Status      = status?.Name.ExtractText(),
                    Description = status?.Description.ExtractText(),
                    Category    = (StatusCategory)(status?.StatusCategory ?? 0),
                    Source      = sourceName,
                    Duration    = effect.Duration,
                });
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "EffectResultDetour");
        }
    }
}
