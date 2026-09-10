using System;
using System.Collections.Generic;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Battle
{
    /// <summary>
    /// Round resolution.
    ///
    /// A round runs in three phases: build the turn order from agility and
    /// action priority; execute each combatant's command in that order,
    /// skipping anyone who went down before their turn came up; then tick
    /// statuses, cooldowns, mote recovery, and check for an ending.
    ///
    /// Deliberately long-form rather than clever: an RPG resolver is read far
    /// more often than it is written, and every branch here maps to a rule a
    /// designer will want to find.
    /// </summary>
    public static class Resolver
    {
        public sealed class RoundResult
        {
            public BattleState State = new BattleState();
            public List<GameEvent> Events = new List<GameEvent>();
        }

        /// <summary>Resolve one full round. Mutates and returns the state.</summary>
        public static RoundResult ResolveRound(
            BattleState state,
            IReadOnlyList<BattleCommand> commands,
            ContentRegistry registry,
            DamageConfig? damageConfig = null)
        {
            var config = damageConfig ?? DamageConfig.Default;
            var log = new EventLog();

            if (state.Phase == BattlePhase.Ended) return new RoundResult { State = state, Events = new List<GameEvent>() };

            state.Phase = BattlePhase.Resolving;
            log.Push(new RoundStartEvent { Round = state.Round });

            foreach (var combatant in state.Combatants) combatant.Defending = false;

            var byCombatant = new Dictionary<string, BattleCommand>();
            foreach (var command in commands) byCombatant[command.CombatantId] = command;

            var orderInputs = new List<OrderInput>();
            foreach (var combatant in state.Combatants)
            {
                if (combatant.Downed) continue;
                byCombatant.TryGetValue(combatant.Id, out var command);
                orderInputs.Add(new OrderInput
                {
                    CombatantId = combatant.Id,
                    Agility = StatsOf(combatant, registry).Agility,
                    Priority = CommandPriority(command, registry),
                });
            }
            var order = TurnOrder.Build(ref state.Rng, orderInputs);

            // Defend resolves before anything else so the reduction is live.
            foreach (var entry in order)
            {
                if (!byCombatant.TryGetValue(entry.CombatantId, out var command)) continue;
                if (command.Kind != CommandKind.Defend) continue;
                var combatant = Battles.Find(state, entry.CombatantId);
                if (combatant != null && !combatant.Downed) combatant.Defending = true;
            }

            foreach (var entry in order)
            {
                var combatant = Battles.Find(state, entry.CombatantId);
                if (combatant == null || combatant.Downed) continue;
                if (Battles.IsOver(state)) break;
                if (!byCombatant.TryGetValue(entry.CombatantId, out var command)) continue;

                var blocker = ActionBlocker(combatant, registry);
                if (blocker != null)
                {
                    log.Push(new TurnSkippedEvent { CombatantId = combatant.Id, Reason = blocker });
                    continue;
                }

                log.Push(new TurnStartEvent { CombatantId = combatant.Id });
                ExecuteCommand(state, combatant, command, registry, config, log);
                ReapDowned(state, log);
            }

            EndOfRound(state, registry, log);

            if (state.Outcome == null && Battles.IsOver(state)) FinishBattle(state, registry, log);

            log.Push(new RoundEndEvent { Round = state.Round });
            state.Round += 1;
            // Outcome is set exactly when the fight ends, by FinishBattle or a
            // successful flee, so it is the reliable signal here.
            if (state.Outcome == null) state.Phase = BattlePhase.Command;

            return new RoundResult { State = state, Events = log.Drain() };
        }

        private static int CommandPriority(BattleCommand? command, ContentRegistry registry)
        {
            if (command == null) return 0;
            switch (command.Kind)
            {
                case CommandKind.Defend: return 100;
                case CommandKind.Flee: return 90;
                case CommandKind.Item: return 50;
                case CommandKind.Unleash: return 20;
                case CommandKind.Summon: return 15;
                case CommandKind.Art: return registry.ArtDef(command.ArtId ?? "")?.Priority ?? 0;
                default: return 0;
            }
        }

        private static void ExecuteCommand(BattleState state, Combatant actor, BattleCommand command, ContentRegistry registry, DamageConfig config, EventLog log)
        {
            switch (command.Kind)
            {
                case CommandKind.Attack:
                    DoAttack(state, actor, command.TargetId ?? "", registry, config, log);
                    break;
                case CommandKind.Art:
                    DoArt(state, actor, command.ArtId ?? "", command.TargetId, registry, config, log);
                    break;
                case CommandKind.Unleash:
                    DoUnleash(state, actor, command.MoteId ?? "", command.TargetId, registry, config, log);
                    break;
                case CommandKind.Summon:
                    DoSummon(state, actor, command.SummonId ?? "", registry, config, log);
                    break;
                case CommandKind.Flee:
                    DoFlee(state, actor, registry, log);
                    break;
                case CommandKind.Defend:
                    log.Push(new ActionDeclaredEvent { CombatantId = actor.Id, Action = "defend" });
                    break;
                case CommandKind.Item:
                    // Items are project-specific; emit the intent and let the
                    // host game apply its own inventory rules.
                    log.Push(new ActionDeclaredEvent
                    {
                        CombatantId = actor.Id,
                        Action = $"item:{command.ItemId}",
                        TargetIds = command.TargetId != null ? new List<string> { command.TargetId } : new List<string>(),
                    });
                    break;
            }
        }

        private static void DoAttack(BattleState state, Combatant actor, string targetId, ContentRegistry registry, DamageConfig config, EventLog log)
        {
            var target = ResolveTarget(state, actor, targetId);
            if (target == null) return;

            log.Push(new ActionDeclaredEvent { CombatantId = actor.Id, Action = "attack", TargetIds = { target.Id } });

            // A worn weapon may convert the swing into an art instead.
            var proc = RollGearProc(ref state.Rng, actor, registry);
            if (proc != null)
            {
                var procArt = registry.ArtDef(proc.ArtId);
                if (procArt != null)
                {
                    ApplyArtToTargets(state, actor, procArt, new List<Combatant> { target }, registry, config, log, proc.PowerScale);
                    return;
                }
            }

            var attackerStats = StatsOf(actor, registry);
            var defenderStats = StatsOf(target, registry);

            if (Damage.RollEvaded(ref state.Rng, attackerStats, defenderStats, 1, config))
            {
                log.Push(new MissEvent { SourceId = actor.Id, TargetId = target.Id });
                return;
            }

            var element = WeaponElement(actor, registry);
            var result = Damage.Roll(ref state.Rng, new DamageInput
            {
                Attacker = attackerStats,
                Defender = defenderStats,
                Element = element,
                DefenderInnate = target.Innate,
                Power = 1,
                Kind = DamageKind.Physical,
                Defending = target.Defending,
            }, config);

            DealDamage(state, actor, target, result.Amount, element, result.Critical, result.Effectiveness, log, registry);
        }

        private static void DoArt(BattleState state, Combatant actor, string artId, string? targetId, ContentRegistry registry, DamageConfig config, EventLog log)
        {
            var art = registry.ArtDef(artId);
            if (art == null || art.FieldOnly) return;

            if (ArtsBlocked(actor, registry))
            {
                log.Push(new TurnSkippedEvent { CombatantId = actor.Id, Reason = "arts-sealed" });
                return;
            }
            if (actor.Aether < art.Cost)
            {
                log.Push(new TurnSkippedEvent { CombatantId = actor.Id, Reason = "insufficient-aether" });
                return;
            }

            actor.Aether -= art.Cost;
            if (art.Cost > 0) log.Push(new AetherSpentEvent { CombatantId = actor.Id, Amount = art.Cost });

            var targets = SelectTargets(state, actor, art, targetId);
            var declared = new ActionDeclaredEvent { CombatantId = actor.Id, Action = $"art:{art.Id}" };
            foreach (var t in targets) declared.TargetIds.Add(t.Id);
            log.Push(declared);

            ApplyArtToTargets(state, actor, art, targets, registry, config, log, 1);
        }

        private static void ApplyArtToTargets(BattleState state, Combatant actor, ArtDef art, IReadOnlyList<Combatant> targets, ContentRegistry registry, DamageConfig config, EventLog log, double powerScale)
        {
            var primary = targets.Count > 0 ? targets[0] : null;
            var attackerStats = StatsOf(actor, registry);

            foreach (var target in targets)
            {
                int distance = primary != null ? Battles.SlotDistance(primary, target) : 0;
                double falloff = art.Targeting == ArtTargeting.SpreadFoes ? Arts.SpreadMultiplier(art, distance) : 1;
                if (falloff <= 0) continue;

                if (art.Kind == ArtKind.Heal)
                {
                    ApplyHeal(state, actor, target, art, attackerStats, registry, log, powerScale * falloff);
                    continue;
                }
                if (art.Kind == ArtKind.Support)
                {
                    ApplySupport(state, actor, target, art, registry, log);
                    continue;
                }

                var defenderStats = StatsOf(target, registry);
                if (Damage.RollEvaded(ref state.Rng, attackerStats, defenderStats, art.Accuracy, config))
                {
                    log.Push(new MissEvent { SourceId = actor.Id, TargetId = target.Id });
                    continue;
                }

                int hits = Math.Max(1, art.Effect.Hits);
                double dealtTotal = 0;
                for (int hit = 0; hit < hits; hit++)
                {
                    if (target.Downed) break;
                    var result = Damage.Roll(ref state.Rng, new DamageInput
                    {
                        Attacker = attackerStats,
                        Defender = defenderStats,
                        Element = art.Element,
                        DefenderInnate = target.Innate,
                        Power = art.Effect.Power * powerScale,
                        Kind = art.Kind == ArtKind.Physical ? DamageKind.Physical : DamageKind.Aetheric,
                        Defending = target.Defending,
                        Multiplier = falloff,
                    }, config);

                    dealtTotal += result.Amount;
                    DealDamage(state, actor, target, result.Amount, art.Element, result.Critical, result.Effectiveness, log, registry);
                }

                if (art.Effect.Drain > 0 && dealtTotal > 0)
                {
                    double healed = Math.Max(1, Math.Round(dealtTotal * art.Effect.Drain, MidpointRounding.AwayFromZero));
                    ApplyRawHeal(actor, actor, healed, log);
                }

                foreach (var status in art.Effect.Statuses)
                {
                    TryApplyStatus(state, actor, target, status.StatusId, status.Chance, registry, log);
                }
            }
        }

        private static void ApplyHeal(BattleState state, Combatant actor, Combatant target, ArtDef art, StatBlock attackerStats, ContentRegistry registry, EventLog log, double scale)
        {
            if (art.Effect.ReviveFraction > 0 && target.Downed)
            {
                double hp = Math.Max(1, Math.Round(target.MaxHp * art.Effect.ReviveFraction, MidpointRounding.AwayFromZero));
                target.Downed = false;
                target.Hp = hp;
                if (target.Actor != null) target.Actor.Hp = hp;
                log.Push(new RevivedEvent { CombatantId = target.Id, Hp = hp });
                return;
            }
            if (target.Downed) return;

            if (art.Effect.RestoreAether > 0)
            {
                double amount = Math.Min(art.Effect.RestoreAether, target.MaxAether - target.Aether);
                if (amount > 0)
                {
                    target.Aether += amount;
                    if (target.Actor != null) target.Actor.Aether = target.Aether;
                    log.Push(new AetherRestoredEvent { CombatantId = target.Id, Amount = amount });
                }
            }

            double power = art.Effect.Power * scale;
            if (power > 0 || art.Effect.HealFraction > 0)
            {
                double healed = Damage.RollHeal(ref state.Rng, power, attackerStats.Power[art.Element], target.MaxHp, art.Effect.HealFraction);
                ApplyRawHeal(actor, target, healed, log);
            }

            // On a healing art, listed statuses are cures rather than afflictions.
            foreach (var status in art.Effect.Statuses)
            {
                int before = target.Statuses.Count;
                target.Statuses.RemoveAll(s => s.StatusId == status.StatusId);
                if (target.Statuses.Count != before)
                    log.Push(new StatusExpiredEvent { TargetId = target.Id, StatusId = status.StatusId });
            }

            if (art.Effect.Buff != null) ApplyBuff(target, art.Effect.Buff.Modifier, art.Effect.Buff.Rounds, log);
        }

        private static void ApplySupport(BattleState state, Combatant actor, Combatant target, ArtDef art, ContentRegistry registry, EventLog log)
        {
            if (art.Effect.Buff != null) ApplyBuff(target, art.Effect.Buff.Modifier, art.Effect.Buff.Rounds, log);

            if (art.Effect.RestoreAether > 0)
            {
                double amount = Math.Min(art.Effect.RestoreAether, target.MaxAether - target.Aether);
                if (amount > 0)
                {
                    target.Aether += amount;
                    if (target.Actor != null) target.Actor.Aether = target.Aether;
                    log.Push(new AetherRestoredEvent { CombatantId = target.Id, Amount = amount });
                }
            }

            foreach (var status in art.Effect.Statuses)
            {
                TryApplyStatus(state, actor, target, status.StatusId, status.Chance, registry, log);
            }
        }

        private static void ApplyBuff(Combatant target, StatModifier modifier, int rounds, EventLog log)
        {
            target.TempModifiers.Add(new TimedModifier { Modifier = modifier, Rounds = rounds });
            if (modifier.Attack.HasValue) log.Push(new StatStageChangedEvent { TargetId = target.Id, Stat = "attack", Delta = modifier.Attack.Value });
            if (modifier.Defense.HasValue) log.Push(new StatStageChangedEvent { TargetId = target.Id, Stat = "defense", Delta = modifier.Defense.Value });
            if (modifier.Agility.HasValue) log.Push(new StatStageChangedEvent { TargetId = target.Id, Stat = "agility", Delta = modifier.Agility.Value });
        }

        private static void DoUnleash(BattleState state, Combatant actor, string moteId, string? targetId, ContentRegistry registry, DamageConfig config, EventLog log)
        {
            if (actor.Actor == null) return;
            var moteDef = registry.MoteDef(moteId);
            if (moteDef == null) return;

            string? classBefore = Actors.DeriveClass(actor.Actor, registry)?.Id;
            if (!Motes.Unleash(actor.Actor.Motes, moteId)) return;

            log.Push(new MoteUnleashedEvent { CombatantId = actor.Id, MoteId = moteId, Element = moteDef.Element });

            // Losing a set mote can drop the actor out of their class, which
            // changes their stats mid-round. Re-sync vitals and announce it.
            SyncActorVitals(actor, registry);
            string? classAfter = Actors.DeriveClass(actor.Actor, registry)?.Id;
            if (classAfter != classBefore && classAfter != null)
                log.Push(new ClassChangedEvent { CombatantId = actor.Id, FromClassId = classBefore, ToClassId = classAfter });

            ApplyMoteEffect(state, actor, moteDef, targetId, registry, config, log);
        }

        private static void ApplyMoteEffect(BattleState state, Combatant actor, MoteDef moteDef, string? targetId, ContentRegistry registry, DamageConfig config, EventLog log)
        {
            var effect = moteDef.Effect;
            var targeting = effect.Kind == MoteEffectKind.Utility ? MoteTargeting.Self : effect.Targeting;
            var targets = SelectMoteTargets(state, actor, targeting, targetId);
            var attackerStats = StatsOf(actor, registry);

            foreach (var target in targets)
            {
                switch (effect.Kind)
                {
                    case MoteEffectKind.Damage:
                    {
                        var result = Damage.Roll(ref state.Rng, new DamageInput
                        {
                            Attacker = attackerStats,
                            Defender = StatsOf(target, registry),
                            Element = moteDef.Element,
                            DefenderInnate = target.Innate,
                            Power = effect.Power,
                            Kind = effect.IgnoresDefense ? DamageKind.Fixed : DamageKind.Aetheric,
                            Defending = target.Defending,
                        }, config);
                        DealDamage(state, actor, target, result.Amount, moteDef.Element, result.Critical, result.Effectiveness, log, registry);
                        break;
                    }
                    case MoteEffectKind.Heal:
                    {
                        double healed = Damage.RollHeal(ref state.Rng, effect.Power, attackerStats.Power[moteDef.Element], target.MaxHp);
                        ApplyRawHeal(actor, target, healed, log);
                        break;
                    }
                    case MoteEffectKind.Revive:
                    {
                        if (!target.Downed) break;
                        double hp = Math.Max(1, Math.Round(target.MaxHp * effect.HpFraction, MidpointRounding.AwayFromZero));
                        target.Downed = false;
                        target.Hp = hp;
                        if (target.Actor != null) target.Actor.Hp = hp;
                        log.Push(new RevivedEvent { CombatantId = target.Id, Hp = hp });
                        break;
                    }
                    case MoteEffectKind.Status:
                        TryApplyStatus(state, actor, target, effect.StatusId ?? "", effect.Chance, registry, log);
                        break;
                    case MoteEffectKind.Buff:
                        if (effect.Modifier != null) ApplyBuff(target, effect.Modifier, effect.Rounds, log);
                        break;
                    case MoteEffectKind.RestoreAether:
                    {
                        double amount = Math.Min(effect.Amount, target.MaxAether - target.Aether);
                        if (amount > 0)
                        {
                            target.Aether += amount;
                            if (target.Actor != null) target.Actor.Aether = target.Aether;
                            log.Push(new AetherRestoredEvent { CombatantId = target.Id, Amount = amount });
                        }
                        break;
                    }
                    case MoteEffectKind.Utility:
                        log.Push(new MessageEvent { Text = $"{moteDef.Name}: {effect.Tag}" });
                        break;
                }
            }
        }

        private static void DoSummon(BattleState state, Combatant actor, string summonId, ContentRegistry registry, DamageConfig config, EventLog log)
        {
            if (actor.Actor == null) return;
            var summon = registry.SummonDef(summonId);
            if (summon == null) return;

            var spent = Domain.Summons.PayCost(summon, actor.Actor.Motes, registry.MoteDef);
            if (spent.Count == 0)
            {
                log.Push(new TurnSkippedEvent { CombatantId = actor.Id, Reason = "insufficient-motes" });
                return;
            }

            SyncActorVitals(actor, registry);
            log.Push(new SummonCalledEvent { CombatantId = actor.Id, SummonId = summonId, MotesSpent = spent.Count });

            var foes = Battles.Living(state, Battles.Opposing(actor.Side));
            var targets = summon.HitsAll ? foes : (foes.Count > 0 ? new List<Combatant> { foes[0] } : new List<Combatant>());
            var attackerStats = StatsOf(actor, registry);
            int totalCost = Domain.Summons.CostTotal(summon);

            foreach (var target in targets)
            {
                double basePower = Domain.Summons.BaseDamage(summon, totalCost, target.MaxHp);
                var result = Damage.Roll(ref state.Rng, new DamageInput
                {
                    Attacker = attackerStats,
                    Defender = StatsOf(target, registry),
                    Element = summon.Element,
                    DefenderInnate = target.Innate,
                    Power = basePower,
                    Kind = DamageKind.Aetheric,
                    Defending = target.Defending,
                    CanCrit = false,
                }, config);
                DealDamage(state, actor, target, result.Amount, summon.Element, false, result.Effectiveness, log, registry);
            }

            if (summon.AfterglowPower > 0 && summon.AfterglowRounds > 0)
            {
                var modifier = new StatModifier();
                modifier.Power[summon.Element] = summon.AfterglowPower;
                ApplyBuff(actor, modifier, summon.AfterglowRounds, log);
            }
        }

        private static void DoFlee(BattleState state, Combatant actor, ContentRegistry registry, EventLog log)
        {
            string sideName = actor.Side.ToString();
            if (state.NoFlee)
            {
                log.Push(new FleeFailedEvent { Side = sideName });
                return;
            }

            var allies = Battles.Living(state, actor.Side);
            var foes = Battles.Living(state, Battles.Opposing(actor.Side));
            double ourAgility = AverageAgility(allies, registry);
            double theirAgility = AverageAgility(foes, registry);

            double resistance = 0;
            foreach (var foe in foes)
            {
                if (foe.EnemyId == null) continue;
                resistance = Math.Max(resistance, registry.EnemyDef(foe.EnemyId)?.FleeResistance ?? 0);
            }

            double odds = Math.Max(0.05, Math.Min(0.95, ourAgility / Math.Max(1, ourAgility + theirAgility) * 1.5 - resistance));

            if (Rng.Chance(ref state.Rng, odds))
            {
                log.Push(new FleeSucceededEvent { Side = sideName });
                state.Outcome = BattleOutcome.Fled;
                state.Phase = BattlePhase.Ended;
                log.Push(new BattleEndedEvent { Outcome = BattleOutcome.Fled });
            }
            else
            {
                log.Push(new FleeFailedEvent { Side = sideName });
            }
        }

        private static void DealDamage(BattleState state, Combatant? source, Combatant target, double amount, Element? element, bool critical, double effectiveness, EventLog log, ContentRegistry registry)
        {
            if (target.Downed) return;

            double applied = Math.Min(amount, target.Hp);
            target.Hp -= applied;
            if (target.Actor != null) target.Actor.Hp = target.Hp;

            log.Push(new DamageEvent
            {
                SourceId = source?.Id,
                TargetId = target.Id,
                Amount = applied,
                Element = element,
                Critical = critical,
                Effective = effectiveness,
            });

            // Some statuses break the moment their bearer is struck.
            var survivors = new List<StatusInstance>();
            foreach (var status in target.Statuses)
            {
                if (registry.StatusDef(status.StatusId)?.BreaksOnDamage == true)
                    log.Push(new StatusExpiredEvent { TargetId = target.Id, StatusId = status.StatusId });
                else survivors.Add(status);
            }
            target.Statuses = survivors;
        }

        private static void ApplyRawHeal(Combatant? source, Combatant target, double amount, EventLog log)
        {
            if (target.Downed) return;
            double applied = Math.Min(amount, target.MaxHp - target.Hp);
            if (applied <= 0) return;
            target.Hp += applied;
            if (target.Actor != null) target.Actor.Hp = target.Hp;
            log.Push(new HealEvent { SourceId = source?.Id, TargetId = target.Id, Amount = applied });
        }

        private static void TryApplyStatus(BattleState state, Combatant? source, Combatant target, string statusId, double baseChance, ContentRegistry registry, EventLog log)
        {
            if (target.Downed) return;

            var def = registry.StatusDef(statusId);
            double luck = StatsOf(target, registry).Luck;
            double landChance = Statuses.LandChance(baseChance, def, luck);

            if (!Rng.Chance(ref state.Rng, landChance))
            {
                log.Push(new StatusResistedEvent { TargetId = target.Id, StatusId = statusId });
                return;
            }

            if (def?.ExclusiveGroup != null)
            {
                target.Statuses.RemoveAll(s => registry.StatusDef(s.StatusId)?.ExclusiveGroup == def.ExclusiveGroup);
            }
            if (target.Statuses.Exists(s => s.StatusId == statusId)) return;

            double duration = def != null && def.Duration.Kind == StatusDurationKind.Rounds
                ? def.Duration.Rounds
                : double.PositiveInfinity;

            target.Statuses.Add(new StatusInstance { StatusId = statusId, Remaining = duration, SourceId = source?.Id });
            log.Push(new StatusAppliedEvent
            {
                TargetId = target.Id,
                StatusId = statusId,
                Duration = double.IsInfinity(duration) ? -1 : duration,
            });

            if (def?.Incapacitates == true)
            {
                target.Downed = true;
                log.Push(new DownedEvent { CombatantId = target.Id });
            }
        }

        private static void EndOfRound(BattleState state, ContentRegistry registry, EventLog log)
        {
            foreach (var combatant in state.Combatants)
            {
                if (combatant.Downed) continue;

                foreach (var status in new List<StatusInstance>(combatant.Statuses))
                {
                    var def = registry.StatusDef(status.StatusId);
                    if (def == null) continue;
                    if (def.DegenPerRound > 0)
                    {
                        double amount = Math.Max(1, Math.Round(combatant.MaxHp * def.DegenPerRound, MidpointRounding.AwayFromZero));
                        DealDamage(state, null, combatant, amount, def.Element, false, 1, log, registry);
                    }
                    if (def.RegenPerRound > 0)
                    {
                        ApplyRawHeal(null, combatant, Math.Max(1, Math.Round(combatant.MaxHp * def.RegenPerRound, MidpointRounding.AwayFromZero)), log);
                    }
                }

                foreach (var expired in Statuses.Tick(combatant.Statuses))
                {
                    log.Push(new StatusExpiredEvent { TargetId = combatant.Id, StatusId = expired });
                }

                // Decrement explicitly rather than inside a RemoveAll predicate:
                // that would rely on the predicate visiting each element exactly
                // once, which is true today but is not a contract worth betting
                // the buff durations on.
                foreach (var mod in combatant.TempModifiers) mod.Rounds -= 1;
                combatant.TempModifiers.RemoveAll(mod => mod.Rounds <= 0);

                foreach (var artId in new List<string>(combatant.Cooldowns.Keys))
                {
                    if (combatant.Cooldowns[artId] <= 1) combatant.Cooldowns.Remove(artId);
                    else combatant.Cooldowns[artId] -= 1;
                }

                if (combatant.Actor != null)
                {
                    foreach (var moteId in Motes.TickRecovery(combatant.Actor.Motes))
                    {
                        log.Push(new MoteRecoveredEvent { CombatantId = combatant.Id, MoteId = moteId });
                    }
                    SyncActorVitals(combatant, registry);
                }
            }
            ReapDowned(state, log);
        }

        private static void ReapDowned(BattleState state, EventLog log)
        {
            foreach (var combatant in state.Combatants)
            {
                if (combatant.Downed || combatant.Hp > 0) continue;
                combatant.Downed = true;
                combatant.Hp = 0;
                if (combatant.Actor != null) combatant.Actor.Hp = 0;
                log.Push(new DownedEvent { CombatantId = combatant.Id });
            }
        }

        private static void FinishBattle(BattleState state, ContentRegistry registry, EventLog log)
        {
            bool partyAlive = Battles.Living(state, Side.Party).Count > 0;
            state.Outcome = partyAlive ? BattleOutcome.Victory : BattleOutcome.Defeat;
            state.Phase = BattlePhase.Ended;

            if (state.Outcome == BattleOutcome.Victory)
            {
                long xp = 0, coin = 0;
                var itemIds = new List<string>();
                foreach (var combatant in state.Combatants)
                {
                    if (combatant.Side != Side.Foe || combatant.EnemyId == null) continue;
                    var def = registry.EnemyDef(combatant.EnemyId);
                    if (def == null) continue;
                    xp += def.Xp;
                    coin += def.Coin;
                    foreach (var drop in def.Drops)
                    {
                        if (Rng.Chance(ref state.Rng, drop.Chance)) itemIds.Add(drop.ItemId);
                    }
                }
                state.Rewards = new BattleRewards { Xp = xp, Coin = coin, ItemIds = itemIds };
                log.Push(new RewardEvent { Xp = xp, Coin = coin, ItemIds = itemIds });
            }

            log.Push(new BattleEndedEvent { Outcome = state.Outcome.Value });
        }

        // --- helpers ---------------------------------------------------------

        /// <summary>Current stats, derived fresh for party members.</summary>
        public static StatBlock StatsOf(Combatant combatant, ContentRegistry registry)
        {
            var baseStats = combatant.Actor != null
                ? Actors.DeriveStats(combatant.Actor, registry)
                : combatant.EnemyStats?.Clone() ?? new StatBlock { MaxHp = 1, Attack = 1, Agility = 1 }.Normalized();

            if (combatant.TempModifiers.Count == 0) return baseStats;

            var mods = new StatModifier?[combatant.TempModifiers.Count];
            for (int i = 0; i < combatant.TempModifiers.Count; i++) mods[i] = combatant.TempModifiers[i].Modifier;
            return Stats.ApplyAll(baseStats, mods).Normalized();
        }

        /// <summary>Re-read max HP/aether after a class or mote change and clamp.</summary>
        private static void SyncActorVitals(Combatant combatant, ContentRegistry registry)
        {
            if (combatant.Actor == null) return;
            var stats = StatsOf(combatant, registry);
            combatant.MaxHp = stats.MaxHp;
            combatant.MaxAether = stats.MaxAether;
            combatant.Hp = Math.Min(combatant.Hp, stats.MaxHp);
            combatant.Aether = Math.Min(combatant.Aether, stats.MaxAether);
            combatant.Actor.Hp = combatant.Hp;
            combatant.Actor.Aether = combatant.Aether;
        }

        private static string? ActionBlocker(Combatant combatant, ContentRegistry registry)
        {
            foreach (var status in combatant.Statuses)
            {
                if (registry.StatusDef(status.StatusId)?.PreventsAction == true) return status.StatusId;
            }
            return null;
        }

        private static bool ArtsBlocked(Combatant combatant, ContentRegistry registry) =>
            combatant.Statuses.Exists(s => registry.StatusDef(s.StatusId)?.PreventsArts == true);

        private static Element? WeaponElement(Combatant combatant, ContentRegistry registry)
        {
            var weaponId = combatant.Actor?.Equipment[EquipSlot.Weapon];
            if (weaponId == null) return null;
            return registry.GearDef(weaponId)?.Element;
        }

        private static GearProc? RollGearProc(ref RngState rng, Combatant combatant, ContentRegistry registry)
        {
            if (combatant.Actor == null) return null;
            foreach (var proc in Equipment.Procs(combatant.Actor.Equipment, registry.GearDef))
            {
                if (Rng.Chance(ref rng, proc.Chance)) return proc;
            }
            return null;
        }

        private static Combatant? ResolveTarget(BattleState state, Combatant actor, string targetId)
        {
            var target = Battles.Find(state, targetId);
            if (target != null && !target.Downed) return target;
            // Retarget rather than waste the turn when the choice already fell.
            var living = Battles.Living(state, Battles.Opposing(actor.Side));
            return Rng.Pick(ref state.Rng, living);
        }

        private static List<Combatant> SelectTargets(BattleState state, Combatant actor, ArtDef art, string? targetId)
        {
            var foes = Battles.Living(state, Battles.Opposing(actor.Side));
            var allies = Battles.Living(state, actor.Side);

            switch (art.Targeting)
            {
                case ArtTargeting.Self:
                    return new List<Combatant> { actor };
                case ArtTargeting.AllFoes:
                    return foes;
                case ArtTargeting.AllAllies:
                    return allies;
                case ArtTargeting.DownedAlly:
                {
                    var downed = state.Combatants.FindAll(c => c.Side == actor.Side && c.Downed);
                    var chosen = targetId != null ? downed.Find(c => c.Id == targetId) : (downed.Count > 0 ? downed[0] : null);
                    return chosen != null ? new List<Combatant> { chosen } : new List<Combatant>();
                }
                case ArtTargeting.OneAlly:
                {
                    var chosen = (targetId != null ? allies.Find(c => c.Id == targetId) : null) ?? Rng.Pick(ref state.Rng, allies);
                    return chosen != null ? new List<Combatant> { chosen } : new List<Combatant>();
                }
                case ArtTargeting.SpreadFoes:
                {
                    var primary = (targetId != null ? foes.Find(c => c.Id == targetId) : null) ?? Rng.Pick(ref state.Rng, foes);
                    if (primary == null) return new List<Combatant>();
                    // Primary first so falloff distances measure from it.
                    var ordered = new List<Combatant> { primary };
                    foreach (var foe in foes) if (foe.Id != primary.Id) ordered.Add(foe);
                    return ordered;
                }
                default:
                {
                    var chosen = (targetId != null ? foes.Find(c => c.Id == targetId) : null) ?? Rng.Pick(ref state.Rng, foes);
                    return chosen != null ? new List<Combatant> { chosen } : new List<Combatant>();
                }
            }
        }

        private static List<Combatant> SelectMoteTargets(BattleState state, Combatant actor, MoteTargeting targeting, string? targetId)
        {
            var foes = Battles.Living(state, Battles.Opposing(actor.Side));
            var allies = Battles.Living(state, actor.Side);

            switch (targeting)
            {
                case MoteTargeting.AllFoes: return foes;
                case MoteTargeting.AllAllies: return allies;
                case MoteTargeting.Self: return new List<Combatant> { actor };
                case MoteTargeting.OneAlly:
                {
                    var chosen = (targetId != null ? allies.Find(c => c.Id == targetId) : null) ?? actor;
                    return new List<Combatant> { chosen };
                }
                default:
                {
                    var chosen = (targetId != null ? foes.Find(c => c.Id == targetId) : null) ?? Rng.Pick(ref state.Rng, foes);
                    return chosen != null ? new List<Combatant> { chosen } : new List<Combatant>();
                }
            }
        }

        private static double AverageAgility(List<Combatant> combatants, ContentRegistry registry)
        {
            if (combatants.Count == 0) return 0;
            double total = 0;
            foreach (var combatant in combatants) total += StatsOf(combatant, registry).Agility;
            return total / combatants.Count;
        }
    }
}
