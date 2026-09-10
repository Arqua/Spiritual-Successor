using System;
using System.Collections.Generic;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Presentation
{
    public enum NumeralStyle { Damage, Critical, Weak, Strong, Heal, Miss, Status, Aether }

    public enum BannerTone { Neutral, Good, Bad, Grand }

    public abstract class StepAction { }

    public sealed class PoseAction : StepAction { public string Pose = ""; }
    public sealed class LungeAction : StepAction { public string TowardId = ""; }
    public sealed class FlashAction : StepAction { public string Color = "#ffffff"; public double Intensity; }
    public sealed class ShakeAction : StepAction { public double Intensity; }
    public sealed class NumeralAction : StepAction { public string Text = ""; public NumeralStyle Style; }
    public sealed class BarAction : StepAction { public string Stat = "hp"; public double From; public double To; public double Max; }
    public sealed class HitstopAction : StepAction { }
    public sealed class BannerAction : StepAction { public string Text = ""; public BannerTone Tone; }
    public sealed class VfxAction : StepAction { public string Effect = ""; public Element? Element; }
    public sealed class CameraAction : StepAction { public string FocusId = ""; }
    public sealed class CollapseAction : StepAction { }
    public sealed class RiseAction : StepAction { }
    public sealed class ScreenFlashAction : StepAction { public string Color = "#ffffff"; }
    public sealed class SfxAction : StepAction { public string Cue = ""; }

    public sealed class AnimationStep
    {
        /// <summary>Entity id, or Timeline.GlobalTrack for scene-wide steps.</summary>
        public string Track = "";
        public double StartMs;
        public double DurationMs;
        public StepAction Action = new HitstopAction();
    }

    public sealed class Timeline
    {
        public List<AnimationStep> Steps = new List<AnimationStep>();
        public double DurationMs;
    }

    /// <summary>Beat lengths in milliseconds. Tune these to change the feel.</summary>
    public sealed class TimelineTiming
    {
        public double TurnStart = 160;
        public double WindUp = 240;
        public double Impact = 300;
        public double Hitstop = 90;
        public double NumeralFloat = 700;
        public double BetweenHits = 130;
        public double StatusApply = 320;
        public double MoteUnleash = 520;
        public double ClassChange = 600;
        public double SummonCutaway = 1400;
        public double Collapse = 520;
        public double Banner = 900;
        public double RoundGap = 120;

        public static TimelineTiming Default => new TimelineTiming();
    }

    /// <summary>Vitals at the start of the round, so bars animate from the right value.</summary>
    public sealed class VitalsSnapshot
    {
        public readonly Dictionary<string, (double Hp, double MaxHp, double Aether, double MaxAether)> Entries =
            new Dictionary<string, (double, double, double, double)>();

        public void Set(string id, double hp, double maxHp, double aether, double maxAether) =>
            Entries[id] = (hp, maxHp, aether, maxAether);
    }

    public sealed class TimelineOptions
    {
        public TimelineTiming Timing = TimelineTiming.Default;
        public VitalsSnapshot? Vitals;
        /// <summary>Display names for banners and numerals, keyed by id.</summary>
        public Dictionary<string, string> Labels = new Dictionary<string, string>();
    }

    /// <summary>
    /// Event stream to animation timeline.
    ///
    /// ResolveRound returns an entire round instantly as an ordered event list.
    /// This turns that list into a *timed* schedule: a flat array of steps, each
    /// with a start time, a duration and a track. Playback then becomes a matter
    /// of asking "which steps are active at time T", which any frame loop can
    /// drive.
    ///
    /// Deliberately pure - no clock, no engine types - so a schedule can be unit
    /// tested and ported without a renderer attached.
    ///
    /// Concurrency is expressed by giving steps overlapping time ranges. An
    /// all-foes spell hits every target in the same beat rather than
    /// sequentially, because the cursor only advances between beats.
    /// </summary>
    public static class TimelineBuilder
    {
        public const string GlobalTrack = "@global";

        public static Timeline Build(IReadOnlyList<GameEvent> events, TimelineOptions? options = null)
        {
            var opts = options ?? new TimelineOptions();
            var timing = opts.Timing;
            var timeline = new Timeline();

            // Track vitals as we walk so bar steps know their start value.
            var vitals = new Dictionary<string, (double Hp, double MaxHp, double Aether, double MaxAether)>();
            if (opts.Vitals != null)
            {
                foreach (var entry in opts.Vitals.Entries) vitals[entry.Key] = entry.Value;
            }

            double cursor = 0;

            void Emit(string track, double durationMs, StepAction action) =>
                timeline.Steps.Add(new AnimationStep { Track = track, StartMs = cursor, DurationMs = durationMs, Action = action });

            string Label(string id) => opts.Labels.TryGetValue(id, out var name) ? name : id;

            foreach (var e in events)
            {
                switch (e)
                {
                    case RoundStartEvent:
                        break;

                    case TurnStartEvent turn:
                        Emit(GlobalTrack, timing.TurnStart, new CameraAction { FocusId = turn.CombatantId });
                        cursor += timing.TurnStart;
                        break;

                    case TurnSkippedEvent skipped:
                        Emit(skipped.CombatantId, timing.NumeralFloat,
                            new NumeralAction { Text = ReadableReason(skipped.Reason), Style = NumeralStyle.Status });
                        cursor += timing.StatusApply;
                        break;

                    case ActionDeclaredEvent declared:
                    {
                        bool isAttack = declared.Action == "attack";
                        Emit(declared.CombatantId, timing.WindUp, new PoseAction { Pose = isAttack ? "attack" : "cast" });
                        if (isAttack && declared.TargetIds.Count > 0)
                            Emit(declared.CombatantId, timing.WindUp, new LungeAction { TowardId = declared.TargetIds[0] });
                        if (!isAttack && declared.Action.StartsWith("art:", StringComparison.Ordinal))
                            Emit(GlobalTrack, timing.Banner, new BannerAction { Text = Label(declared.Action.Substring(4)), Tone = BannerTone.Neutral });
                        cursor += timing.WindUp;
                        break;
                    }

                    case DamageEvent damage:
                    {
                        var style = DamageStyle(damage.Critical, damage.Effective);
                        Emit(damage.TargetId, timing.Impact, new FlashAction { Color = "#ffffff", Intensity = damage.Critical ? 1 : 0.75 });
                        Emit(damage.TargetId, timing.Impact, new ShakeAction { Intensity = damage.Critical ? 6 : 3 });
                        Emit(damage.TargetId, timing.Impact, new PoseAction { Pose = "hurt" });
                        Emit(damage.TargetId, timing.Impact, new VfxAction { Effect = "impact", Element = damage.Element });
                        Emit(damage.TargetId, timing.NumeralFloat, new NumeralAction { Text = FormatAmount(damage.Amount), Style = style });
                        Emit(GlobalTrack, timing.Hitstop, new HitstopAction());
                        Emit(GlobalTrack, 1, new SfxAction { Cue = damage.Critical ? "hit-critical" : "hit" });
                        EmitBar(timeline, vitals, damage.TargetId, "hp", -damage.Amount, cursor, timing.Impact);
                        cursor += timing.Impact + timing.BetweenHits;
                        break;
                    }

                    case HealEvent heal:
                        Emit(heal.TargetId, timing.Impact, new VfxAction { Effect = "heal" });
                        Emit(heal.TargetId, timing.NumeralFloat, new NumeralAction { Text = FormatAmount(heal.Amount), Style = NumeralStyle.Heal });
                        Emit(GlobalTrack, 1, new SfxAction { Cue = "heal" });
                        EmitBar(timeline, vitals, heal.TargetId, "hp", heal.Amount, cursor, timing.Impact);
                        cursor += timing.Impact;
                        break;

                    case MissEvent miss:
                        Emit(miss.TargetId, timing.NumeralFloat, new NumeralAction { Text = "MISS", Style = NumeralStyle.Miss });
                        Emit(GlobalTrack, 1, new SfxAction { Cue = "miss" });
                        cursor += timing.Impact;
                        break;

                    case AetherSpentEvent spent:
                        EmitBar(timeline, vitals, spent.CombatantId, "aether", -spent.Amount, cursor, timing.Impact);
                        break;

                    case AetherRestoredEvent restored:
                        Emit(restored.CombatantId, timing.NumeralFloat, new NumeralAction { Text = "+" + FormatAmount(restored.Amount), Style = NumeralStyle.Aether });
                        EmitBar(timeline, vitals, restored.CombatantId, "aether", restored.Amount, cursor, timing.Impact);
                        cursor += timing.Impact;
                        break;

                    case StatusAppliedEvent applied:
                        Emit(applied.TargetId, timing.StatusApply, new VfxAction { Effect = "status:" + applied.StatusId });
                        Emit(applied.TargetId, timing.NumeralFloat, new NumeralAction { Text = Label(applied.StatusId).ToUpperInvariant(), Style = NumeralStyle.Status });
                        cursor += timing.StatusApply;
                        break;

                    case StatusResistedEvent resisted:
                        Emit(resisted.TargetId, timing.NumeralFloat, new NumeralAction { Text = "RESIST", Style = NumeralStyle.Miss });
                        cursor += timing.StatusApply;
                        break;

                    case StatusExpiredEvent expired:
                        Emit(expired.TargetId, timing.StatusApply, new VfxAction { Effect = "status-clear" });
                        break;

                    case MoteUnleashedEvent unleashed:
                        Emit(unleashed.CombatantId, timing.MoteUnleash, new PoseAction { Pose = "cast" });
                        Emit(unleashed.CombatantId, timing.MoteUnleash, new VfxAction { Effect = "mote-unleash", Element = unleashed.Element });
                        Emit(GlobalTrack, timing.Banner, new BannerAction { Text = Label(unleashed.MoteId), Tone = BannerTone.Good });
                        Emit(GlobalTrack, 1, new SfxAction { Cue = "mote" });
                        cursor += timing.MoteUnleash;
                        break;

                    case MoteRecoveredEvent recovered:
                        Emit(recovered.CombatantId, timing.StatusApply, new VfxAction { Effect = "mote-return" });
                        break;

                    case ClassChangedEvent changed:
                        Emit(changed.CombatantId, timing.ClassChange, new VfxAction { Effect = "class-change" });
                        Emit(GlobalTrack, timing.Banner, new BannerAction { Text = Label(changed.ToClassId), Tone = BannerTone.Grand });
                        cursor += timing.ClassChange;
                        break;

                    case SummonCalledEvent summon:
                        Emit(GlobalTrack, timing.SummonCutaway, new BannerAction { Text = Label(summon.SummonId), Tone = BannerTone.Grand });
                        Emit(GlobalTrack, timing.SummonCutaway, new ScreenFlashAction { Color = "#ffffff" });
                        Emit(summon.CombatantId, timing.SummonCutaway, new PoseAction { Pose = "cast" });
                        Emit(GlobalTrack, 1, new SfxAction { Cue = "summon" });
                        cursor += timing.SummonCutaway;
                        break;

                    case DownedEvent downed:
                        Emit(downed.CombatantId, timing.Collapse, new PoseAction { Pose = "down" });
                        Emit(downed.CombatantId, timing.Collapse, new CollapseAction());
                        Emit(GlobalTrack, 1, new SfxAction { Cue = "down" });
                        cursor += timing.Collapse;
                        break;

                    case RevivedEvent revived:
                        Emit(revived.CombatantId, timing.Collapse, new RiseAction());
                        Emit(revived.CombatantId, timing.NumeralFloat, new NumeralAction { Text = FormatAmount(revived.Hp), Style = NumeralStyle.Heal });
                        SetVital(vitals, revived.CombatantId, "hp", revived.Hp);
                        cursor += timing.Collapse;
                        break;

                    case FleeSucceededEvent:
                        Emit(GlobalTrack, timing.Banner, new BannerAction { Text = "Escaped", Tone = BannerTone.Neutral });
                        cursor += timing.Banner;
                        break;

                    case FleeFailedEvent:
                        Emit(GlobalTrack, timing.Banner, new BannerAction { Text = "Couldn't escape", Tone = BannerTone.Bad });
                        cursor += timing.Banner;
                        break;

                    case BattleEndedEvent ended:
                    {
                        var tone = ended.Outcome switch
                        {
                            BattleOutcome.Victory => BannerTone.Good,
                            BattleOutcome.Defeat => BannerTone.Bad,
                            _ => BannerTone.Neutral,
                        };
                        Emit(GlobalTrack, timing.Banner, new BannerAction { Text = OutcomeText(ended.Outcome), Tone = tone });
                        cursor += timing.Banner;
                        break;
                    }

                    case RewardEvent reward:
                        Emit(GlobalTrack, timing.Banner, new BannerAction { Text = $"+{reward.Xp} XP   +{reward.Coin}", Tone = BannerTone.Good });
                        cursor += timing.Banner;
                        break;

                    case MessageEvent message:
                        Emit(GlobalTrack, timing.Banner, new BannerAction { Text = message.Text, Tone = BannerTone.Neutral });
                        cursor += timing.Banner;
                        break;

                    case RoundEndEvent:
                        cursor += timing.RoundGap;
                        break;
                }
            }

            double duration = 0;
            foreach (var step in timeline.Steps) duration = Math.Max(duration, step.StartMs + step.DurationMs);
            timeline.DurationMs = duration;
            return timeline;
        }

        private static void EmitBar(
            Timeline timeline,
            Dictionary<string, (double Hp, double MaxHp, double Aether, double MaxAether)> vitals,
            string id,
            string stat,
            double delta,
            double startMs,
            double durationMs)
        {
            if (!vitals.TryGetValue(id, out var entry)) return;

            double max = stat == "hp" ? entry.MaxHp : entry.MaxAether;
            double from = stat == "hp" ? entry.Hp : entry.Aether;
            double to = Math.Max(0, Math.Min(max, from + delta));

            vitals[id] = stat == "hp"
                ? (to, entry.MaxHp, entry.Aether, entry.MaxAether)
                : (entry.Hp, entry.MaxHp, to, entry.MaxAether);

            timeline.Steps.Add(new AnimationStep
            {
                Track = id,
                StartMs = startMs,
                DurationMs = durationMs,
                Action = new BarAction { Stat = stat, From = from, To = to, Max = max },
            });
        }

        private static void SetVital(
            Dictionary<string, (double Hp, double MaxHp, double Aether, double MaxAether)> vitals,
            string id,
            string stat,
            double value)
        {
            if (!vitals.TryGetValue(id, out var entry)) return;
            vitals[id] = stat == "hp"
                ? (value, entry.MaxHp, entry.Aether, entry.MaxAether)
                : (entry.Hp, entry.MaxHp, value, entry.MaxAether);
        }

        /// <summary>
        /// Numeral styling from the damage event's own data.
        ///
        /// Effective is the elemental multiplier the rules layer already worked
        /// out, which is exactly why it is on the event: the presentation layer
        /// should not be recomputing affinity to decide how a hit reads.
        /// </summary>
        public static NumeralStyle DamageStyle(bool critical, double effective)
        {
            if (critical) return NumeralStyle.Critical;
            if (effective >= 1.2) return NumeralStyle.Strong;
            if (effective <= 0.8) return NumeralStyle.Weak;
            return NumeralStyle.Damage;
        }

        /// <summary>Amounts are whole numbers on screen even though they are doubles.</summary>
        private static string FormatAmount(double amount) =>
            ((long)Math.Round(amount, MidpointRounding.AwayFromZero)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string OutcomeText(BattleOutcome outcome) => outcome switch
        {
            BattleOutcome.Victory => "Victory",
            BattleOutcome.Defeat => "Defeat",
            _ => "Escaped",
        };

        private static string ReadableReason(string reason) => reason switch
        {
            "insufficient-aether" => "NO AETHER",
            "insufficient-motes" => "NO MOTES",
            "arts-sealed" => "SEALED",
            _ => reason.Replace("-", " ").ToUpperInvariant(),
        };
    }
}
