using System.Collections.Generic;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Battle
{
    /// <summary>
    /// Deliberately simple: a weighted roll over the moves whose gating
    /// conditions are met, with target selection biased toward low-HP party
    /// members so fights have some bite without needing a behaviour tree. A
    /// project wanting boss scripting hooks BehaviorTag and overrides Choose.
    /// </summary>
    public static class EnemyAi
    {
        /// <summary>Probability the AI picks the weakest target rather than a random one.</summary>
        public const double FocusFireChance = 0.45;

        public static BattleCommand? Choose(BattleState state, Combatant enemy, ContentRegistry registry)
        {
            var targets = Battles.Living(state, Battles.Opposing(enemy.Side));
            if (targets.Count == 0) return null;

            var def = enemy.EnemyId != null ? registry.EnemyDef(enemy.EnemyId) : null;
            double hpFraction = enemy.MaxHp > 0 ? enemy.Hp / enemy.MaxHp : 0;

            var usable = new List<EnemyMove>();
            foreach (var move in def?.Moves ?? new List<EnemyMove>())
            {
                if (IsUsable(move, enemy, state.Round, hpFraction, registry)) usable.Add(move);
            }

            EnemyMove? move2 = null;
            if (Rng.TryWeightedPick(ref state.Rng, usable, m => m.Weight, out var picked)) move2 = picked;

            if (move2 == null || move2.ArtId == "attack")
                return BattleCommand.Attack(enemy.Id, ChooseTarget(ref state.Rng, targets).Id);

            var art = registry.ArtDef(move2.ArtId);
            if (art == null)
                return BattleCommand.Attack(enemy.Id, ChooseTarget(ref state.Rng, targets).Id);

            if (move2.Cooldown > 0) enemy.Cooldowns[move2.ArtId] = move2.Cooldown;

            if (Arts.IsOffensive(art))
                return BattleCommand.Art(enemy.Id, art.Id, ChooseTarget(ref state.Rng, targets).Id);

            // Support and healing arts point back at the enemy's own side.
            var allies = Battles.Living(state, enemy.Side);
            Combatant ally = enemy;
            double lowest = double.MaxValue;
            foreach (var candidate in allies)
            {
                double fraction = candidate.MaxHp > 0 ? candidate.Hp / candidate.MaxHp : 0;
                if (fraction < lowest) { lowest = fraction; ally = candidate; }
            }
            return BattleCommand.Art(enemy.Id, art.Id, ally.Id);
        }

        private static bool IsUsable(EnemyMove move, Combatant enemy, int round, double hpFraction, ContentRegistry registry)
        {
            if (move.HpBelow.HasValue && hpFraction > move.HpBelow.Value) return false;
            if (move.FromRound.HasValue && round < move.FromRound.Value) return false;
            if (enemy.Cooldowns.ContainsKey(move.ArtId)) return false;
            if (move.ArtId != "attack")
            {
                var art = registry.ArtDef(move.ArtId);
                if (art == null) return false;
                if (art.Cost > enemy.Aether) return false;
            }
            return true;
        }

        private static Combatant ChooseTarget(ref RngState rng, IReadOnlyList<Combatant> targets)
        {
            if (Rng.NextDouble(ref rng) < FocusFireChance)
            {
                Combatant weakest = targets[0];
                double lowest = double.MaxValue;
                foreach (var candidate in targets)
                {
                    double fraction = candidate.MaxHp > 0 ? candidate.Hp / candidate.MaxHp : 0;
                    if (fraction < lowest) { lowest = fraction; weakest = candidate; }
                }
                return weakest;
            }
            return targets[Rng.NextInt(ref rng, 0, targets.Count - 1)];
        }

        /// <summary>Commands for every living enemy in one call.</summary>
        public static List<BattleCommand> CommandsFor(BattleState state, ContentRegistry registry)
        {
            var commands = new List<BattleCommand>();
            foreach (var enemy in Battles.Living(state, Side.Foe))
            {
                var command = Choose(state, enemy, registry);
                if (command != null) commands.Add(command);
            }
            return commands;
        }
    }
}
