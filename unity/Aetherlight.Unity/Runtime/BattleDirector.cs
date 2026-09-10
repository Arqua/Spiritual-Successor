using System;
using System.Collections.Generic;
using UnityEngine;
using Aetherlight.Core;
using Aetherlight.Domain;
using Aetherlight.Battle;
using Aetherlight.Field;
using Aetherlight.Presentation;
using UnityCamera = UnityEngine.Camera;

namespace Aetherlight.Unity
{
    /// <summary>
    /// Drives a battle: resolves rounds through the rules core, schedules the
    /// resulting events, and paces them out across frames.
    ///
    /// The shape of the loop is the whole contract. ResolveRound computes an
    /// entire round instantly - the fight is already decided before the first
    /// hit is drawn - and this component replays that decision at a watchable
    /// speed. Nothing here may write back into battle state; doing so would
    /// break determinism and, with it, replays and netplay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleDirector : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("The canonical pack, e.g. content/starter.json copied into Resources.")]
        [SerializeField] private TextAsset? contentPack = null;

        [Header("Scene")]
        [SerializeField] private Transform? cameraTransform = null;
        [SerializeField] private BattleHud? hud = null;
        [SerializeField] private CueDispatcher? cues = null;

        [Header("Layout")]
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private float heightUnit = 0.5f;
        [Tooltip("Spacing between formation slots.")]
        [SerializeField] private float slotSpacing = 1.15f;
        [Tooltip("How far each side stands from the centre.")]
        [SerializeField] private float sideOffset = 2.2f;

        [Header("Feel")]
        [SerializeField] private NumeralPalette numeralPalette = new NumeralPalette();
        [Tooltip("Playback rate. 2 is a fast-battle option.")]
        [SerializeField] private float playbackSpeed = 1f;
        [Tooltip("Pause between a round finishing and the next being requested.")]
        [SerializeField] private float roundGapSeconds = 0.45f;

        private readonly Dictionary<string, CombatantView> _views = new Dictionary<string, CombatantView>();
        private readonly Playback _playback = new Playback();
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();

        private ContentRegistry? _registry;
        private BattleState? _state;
        private List<ActorState> _party = new List<ActorState>();
        private double _elapsedMs;
        private float _idleSeconds;

        /// <summary>
        /// Raised when the previous round has finished animating and the battle
        /// needs commands for the next one. Return the party's commands; enemy
        /// commands are added automatically.
        /// </summary>
        public event Func<BattleState, IReadOnlyList<BattleCommand>>? OnCommandsNeeded;

        /// <summary>Raised once when the battle reaches an outcome.</summary>
        public event Action<BattleAftermath>? OnBattleEnded;

        public ContentRegistry Registry => _registry ?? throw new InvalidOperationException("Content has not been loaded yet.");
        public BattleState? State => _state;
        public bool IsAnimating => _playback.IsPlaying;

        private void Awake()
        {
            LoadContent();
        }

        private void LoadContent()
        {
            if (_registry != null) return;
            if (contentPack == null)
            {
                Debug.LogError("BattleDirector has no content pack assigned.");
                return;
            }

            _registry = ContentRegistry.From(ContentLoader.LoadPack(contentPack.text));
            _registry.AssertValid();

            // Display names for banners, pulled straight from content so the
            // renderer never hard-codes a string a designer owns.
            foreach (var pair in _registry.Arts) _labels[pair.Key] = pair.Value.Name;
            foreach (var pair in _registry.Motes) _labels[pair.Key] = pair.Value.Name;
            foreach (var pair in _registry.Summons) _labels[pair.Key] = pair.Value.Name;
            foreach (var pair in _registry.Classes) _labels[pair.Key] = pair.Value.Name;
            foreach (var pair in _registry.Statuses) _labels[pair.Key] = pair.Value.Name;
        }

        /// <summary>Register the view that represents a combatant id.</summary>
        public void RegisterView(CombatantView view)
        {
            if (string.IsNullOrEmpty(view.CombatantId)) return;
            _views[view.CombatantId] = view;
        }

        public void Begin(List<ActorState> party, EncounterDef encounter, string seed)
        {
            LoadContent();
            if (_registry == null) return;

            _party = party;
            _state = BattleFactory.Create(new BattleSetup { Party = party, Encounter = encounter, Seed = seed }, _registry);

            PlaceViews();
            _playback.Load(new Timeline());
            _idleSeconds = 0f;
        }

        /// <summary>
        /// Formation positions. Party on the near side, foes on the far side,
        /// spread along the slot axis.
        /// </summary>
        private void PlaceViews()
        {
            if (_state == null) return;

            int partyIndex = 0;
            int foeIndex = 0;
            int partyCount = 0;
            int foeCount = 0;
            foreach (var combatant in _state.Combatants)
            {
                if (combatant.Side == Side.Party) partyCount++;
                else foeCount++;
            }

            foreach (var combatant in _state.Combatants)
            {
                bool isParty = combatant.Side == Side.Party;
                int slot = isParty ? partyIndex++ : foeIndex++;
                int count = isParty ? partyCount : foeCount;

                double offset = (slot - (count - 1) / 2.0) * slotSpacing;
                double x = isParty ? sideOffset + offset : -sideOffset + offset;
                double z = isParty ? sideOffset - offset : -sideOffset - offset;

                if (_views.TryGetValue(combatant.Id, out var view))
                {
                    view.HomePosition = IsoPlacement.World(x, z, 0, tileSize, heightUnit);
                    view.SetName(combatant.Name);
                    view.ResetView();
                }
            }
        }

        private void Update()
        {
            if (_state == null || _registry == null) return;

            double dtMs = Time.deltaTime * 1000.0;
            _elapsedMs += dtMs;
            _playback.Speed = playbackSpeed;

            if (_playback.IsPlaying)
            {
                _playback.Advance(dtMs);

                // A hitstop step freezes the whole scene the moment it begins.
                // Reading it here, from the steps that just started, is what
                // keeps every entity frozen on the same frame.
                foreach (var step in _playback.JustStarted())
                {
                    switch (step.Action)
                    {
                        case HitstopAction:
                            _playback.RequestHitstop(step.DurationMs);
                            break;
                        case SfxAction sfx:
                            if (cues != null) cues.Play(sfx.Cue);
                            break;
                    }
                }

                _idleSeconds = 0f;
            }
            else
            {
                _idleSeconds += Time.deltaTime;
                if (_state.Phase != BattlePhase.Ended && _idleSeconds >= roundGapSeconds)
                {
                    _idleSeconds = 0f;
                    AdvanceRound();
                }
            }

            ApplyFrame();
        }

        private void AdvanceRound()
        {
            if (_state == null || _registry == null) return;

            // Snapshot before resolving: bars must animate from the values the
            // round started with, not the ones it ended with.
            var vitals = new VitalsSnapshot();
            foreach (var combatant in _state.Combatants)
            {
                vitals.Set(combatant.Id, combatant.Hp, combatant.MaxHp, combatant.Aether, combatant.MaxAether);
            }

            var commands = new List<BattleCommand>();
            var partyCommands = OnCommandsNeeded?.Invoke(_state);
            if (partyCommands != null) commands.AddRange(partyCommands);
            commands.AddRange(EnemyAi.CommandsFor(_state, _registry));

            var result = Resolver.ResolveRound(_state, commands, _registry);

            _playback.Load(TimelineBuilder.Build(result.Events, new TimelineOptions
            {
                Vitals = vitals,
                Labels = _labels,
            }));

            if (_state.Phase == BattlePhase.Ended)
            {
                var aftermath = BattleFactory.Conclude(_state, _party, _registry);
                OnBattleEnded?.Invoke(aftermath);
            }
        }

        private void ApplyFrame()
        {
            if (_state == null) return;

            var allActive = _playback.ActiveAt(_playback.Now);

            if (hud != null) hud.Apply(RenderFold.ForScene(allActive));

            foreach (var combatant in _state.Combatants)
            {
                if (!_views.TryGetValue(combatant.Id, out var view)) continue;

                var state = RenderFold.ForEntity(_playback.ActiveForTrack(combatant.Id));

                Vector3? lungeTarget = null;
                if (state.LungeToward != null && _views.TryGetValue(state.LungeToward, out var target))
                {
                    lungeTarget = target.HomePosition;
                }

                // Party faces away from the camera, foes toward it, which is the
                // convention the sprite direction helper expects.
                var facing = combatant.Side == Side.Party ? Facing.North : Facing.South;

                view.Apply(state, _elapsedMs, cameraTransform, lungeTarget, numeralPalette, facing);
            }
        }

        /// <summary>Skip the remaining animation for the current round.</summary>
        public void SkipAnimation() => _playback.Skip();

        private void Reset()
        {
            if (cameraTransform == null && UnityCamera.main != null) cameraTransform = UnityCamera.main.transform;
        }
    }
}
