using System.Collections.Generic;
using UnityEngine;
using Aetherlight.Core;
using Aetherlight.Domain;
using Aetherlight.Battle;

namespace Aetherlight.Unity
{
    /// <summary>
    /// A stand-in for player input, so a scene runs end to end before any UI
    /// exists.
    ///
    /// Replace it by handing your own commands to BattleDirector.OnCommandsNeeded;
    /// this component is a worked example of that hook, not a design for how a
    /// real game should choose actions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScriptedPartyBrain : MonoBehaviour
    {
        [SerializeField] private BattleDirector? director = null;

        [Header("Behaviour")]
        [Tooltip("Heal an ally below this fraction of max HP when able.")]
        [Range(0f, 1f)]
        [SerializeField] private float healBelow = 0.55f;

        [Tooltip("Actor def id that acts as the healer.")]
        [SerializeField] private string healerDefId = "maren";

        [SerializeField] private string healArtId = "mend";
        [SerializeField] private string summonId = "conflagrant";

        [Tooltip("Unleash a set mote every Nth round to build toward a summon.")]
        [SerializeField] private int unleashEveryRounds = 3;

        private void OnEnable()
        {
            if (director == null) director = GetComponent<BattleDirector>();
            if (director != null) director.OnCommandsNeeded += ChooseCommands;
        }

        private void OnDisable()
        {
            if (director != null) director.OnCommandsNeeded -= ChooseCommands;
        }

        private IReadOnlyList<BattleCommand> ChooseCommands(BattleState state)
        {
            var commands = new List<BattleCommand>();
            if (director == null) return commands;

            var registry = director.Registry;
            var foes = Battles.Living(state, Side.Foe);
            if (foes.Count == 0) return commands;

            Combatant weakestFoe = foes[0];
            foreach (var foe in foes) if (foe.Hp < weakestFoe.Hp) weakestFoe = foe;

            var allies = Battles.Living(state, Side.Party);
            Combatant? hurtAlly = null;
            double lowest = double.MaxValue;
            foreach (var ally in allies)
            {
                double fraction = ally.MaxHp > 0 ? ally.Hp / ally.MaxHp : 0;
                if (fraction < lowest) { lowest = fraction; hurtAlly = ally; }
            }

            foreach (var member in allies)
            {
                var actor = member.Actor;
                if (actor == null) continue;

                var healArt = registry.ArtDef(healArtId);
                if (actor.DefId == healerDefId
                    && hurtAlly != null
                    && lowest < healBelow
                    && healArt != null
                    && member.Aether >= healArt.Cost)
                {
                    commands.Add(BattleCommand.Art(member.Id, healArtId, hurtAlly.Id));
                    continue;
                }

                var summon = registry.SummonDef(summonId);
                if (summon != null && Summons.CanAfford(summon, actor.Motes, registry.MoteDef))
                {
                    commands.Add(BattleCommand.Summon(member.Id, summonId));
                    continue;
                }

                if (unleashEveryRounds > 0 && state.Round % unleashEveryRounds == 0)
                {
                    var setMote = actor.Motes.Find(m => m.State == MoteState.Set);
                    if (setMote != null)
                    {
                        commands.Add(BattleCommand.Unleash(member.Id, setMote.DefId, weakestFoe.Id));
                        continue;
                    }
                }

                commands.Add(BattleCommand.Attack(member.Id, weakestFoe.Id));
            }

            return commands;
        }
    }
}
