using System.Collections.Generic;
using System.Linq;
using Xunit;
using Aetherlight.Core;
using Aetherlight.Domain;
using Aetherlight.Presentation;

namespace Aetherlight.Tests
{
    /// <summary>Ported from test/camera.test.ts.</summary>
    public class CameraTests
    {
        private static CameraConfig Cam() => CameraConfig.Default(640, 360);

        [Fact]
        public void PutsTheFocusPointAtTheViewportCentre()
        {
            var point = Camera.Project(new Vec3(0, 0, 0), Cam());
            Assert.Equal(320, point.X);
            Assert.Equal(180, point.Y);
        }

        [Fact]
        public void SeparatesTheTwoGroundAxesInOppositeDirections()
        {
            var east = Camera.Project(new Vec3(1, 0, 0), Cam());
            var south = Camera.Project(new Vec3(0, 1, 0), Cam());
            Assert.True(east.X > 320);
            Assert.True(south.X < 320);
            // Both move down the screen by the same amount.
            Assert.Equal(east.Y, south.Y);
        }

        [Fact]
        public void RaisesElevatedPointsUpTheScreen()
        {
            Assert.True(Camera.Project(new Vec3(0, 0, 2), Cam()).Y < Camera.Project(new Vec3(0, 0, 0), Cam()).Y);
        }

        [Fact]
        public void SortsNearerTilesInFront()
        {
            Assert.True(Camera.Project(new Vec3(3, 3, 0), Cam()).Depth > Camera.Project(new Vec3(0, 0, 0), Cam()).Depth);
        }

        [Fact]
        public void BreaksDepthTiesByElevation()
        {
            Assert.True(Camera.Project(new Vec3(1, 1, 3), Cam()).Depth > Camera.Project(new Vec3(1, 1, 0), Cam()).Depth);
        }

        [Fact]
        public void RoundTripsThroughUnprojectOnTheGroundPlane()
        {
            var camera = Cam();
            var screen = Camera.Project(new Vec3(4, -2, 0), camera);
            var back = Camera.Unproject(screen.X, screen.Y, camera, 0);
            Assert.Equal(4, back.X, 6);
            Assert.Equal(-2, back.Y, 6);
        }

        [Fact]
        public void RoundTripsOnAnElevatedPlane()
        {
            var camera = Cam();
            var screen = Camera.Project(new Vec3(2, 5, 3), camera);
            var back = Camera.Unproject(screen.X, screen.Y, camera, 3);
            Assert.Equal(2, back.X, 6);
            Assert.Equal(5, back.Y, 6);
        }

        [Fact]
        public void SnapsToWholePixels()
        {
            var snapped = Camera.SnapToPixel(new ScreenPoint { X = 10.4, Y = 20.6, Depth = 5 });
            Assert.Equal(10, snapped.X);
            Assert.Equal(21, snapped.Y);
        }

        [Fact]
        public void FollowsATargetWithoutOvershooting()
        {
            var camera = Cam();
            for (int i = 0; i < 200; i++) camera = Camera.Follow(camera, new Vec3(10, 10, 0), 0.1, 16.667);
            Assert.Equal(10, camera.Focus.X, 1);
            Assert.Equal(10, camera.Focus.Y, 1);
        }

        [Fact]
        public void FollowsAtTheSameRateRegardlessOfFramerate()
        {
            var slow = Camera.Follow(Cam(), new Vec3(10, 0, 0), 0.1, 100);
            var fast = Cam();
            for (int i = 0; i < 6; i++) fast = Camera.Follow(fast, new Vec3(10, 0, 0), 0.1, 16.667);
            Assert.Equal(slow.Focus.X, fast.Focus.X, 1);
        }

        [Fact]
        public void CullsPointsOutsideTheViewport()
        {
            var camera = Cam();
            Assert.True(Camera.IsVisible(Camera.Project(new Vec3(0, 0, 0), camera), camera));
            Assert.False(Camera.IsVisible(Camera.Project(new Vec3(500, 500, 0), camera), camera));
        }

        [Fact]
        public void FramingOffsetShiftsTheWorldWithoutMovingTheFocus()
        {
            var camera = Cam();
            var shifted = camera.Clone();
            shifted.OffsetY = 40;
            var b = Camera.Project(new Vec3(0, 0, 0), camera);
            var m = Camera.Project(new Vec3(0, 0, 0), shifted);
            Assert.Equal(40, m.Y - b.Y);
            Assert.Equal(b.X, m.X);
        }

        [Fact]
        public void StillRoundTripsWithAnOffsetApplied()
        {
            var shifted = Cam();
            shifted.OffsetY = 40;
            var screen = Camera.Project(new Vec3(3, -1, 0), shifted);
            var back = Camera.Unproject(screen.X, screen.Y, shifted, 0);
            Assert.Equal(3, back.X, 6);
            Assert.Equal(-1, back.Y, 6);
        }
    }

    /// <summary>Ported from test/timeline.test.ts.</summary>
    public class TimelineTests
    {
        private static VitalsSnapshot Vitals()
        {
            var v = new VitalsSnapshot();
            v.Set("party:rell", 100, 100, 30, 30);
            v.Set("foe:slime:0", 80, 80, 0, 0);
            return v;
        }

        private static List<AnimationStep> Of<T>(Timeline timeline) where T : StepAction =>
            timeline.Steps.FindAll(s => s.Action is T);

        [Fact]
        public void ProducesNothingForAnEmptyEventList()
        {
            var timeline = TimelineBuilder.Build(new List<GameEvent>());
            Assert.Empty(timeline.Steps);
            Assert.Equal(0, timeline.DurationMs);
        }

        [Fact]
        public void NeverRunsTimeBackwards()
        {
            var events = new List<GameEvent>
            {
                new TurnStartEvent { CombatantId = "party:rell" },
                new ActionDeclaredEvent { CombatantId = "party:rell", Action = "attack", TargetIds = { "foe:slime:0" } },
                new DamageEvent { SourceId = "party:rell", TargetId = "foe:slime:0", Amount = 12, Effective = 1 },
            };
            var timeline = TimelineBuilder.Build(events, new TimelineOptions { Vitals = Vitals() });
            for (int i = 1; i < timeline.Steps.Count; i++)
                Assert.True(timeline.Steps[i].StartMs >= timeline.Steps[i - 1].StartMs);
        }

        [Fact]
        public void EmitsAnImpactClusterOnTheSameBeat()
        {
            var events = new List<GameEvent>
            {
                new DamageEvent { SourceId = "a", TargetId = "foe:slime:0", Amount = 12, Element = Element.Pyre, Effective = 1 },
            };
            var timeline = TimelineBuilder.Build(events, new TimelineOptions { Vitals = Vitals() });
            var atZero = timeline.Steps.FindAll(s => s.StartMs == 0).Select(s => s.Action.GetType()).ToList();

            Assert.Contains(typeof(FlashAction), atZero);
            Assert.Contains(typeof(ShakeAction), atZero);
            Assert.Contains(typeof(NumeralAction), atZero);
            Assert.Contains(typeof(HitstopAction), atZero);
            Assert.Contains(typeof(BarAction), atZero);
        }

        [Fact]
        public void AnimatesTheHpBarFromThePreHitValue()
        {
            var events = new List<GameEvent>
            {
                new DamageEvent { SourceId = "a", TargetId = "foe:slime:0", Amount = 30, Effective = 1 },
            };
            var bar = (BarAction)Of<BarAction>(TimelineBuilder.Build(events, new TimelineOptions { Vitals = Vitals() }))[0].Action;
            Assert.Equal("hp", bar.Stat);
            Assert.Equal(80, bar.From);
            Assert.Equal(50, bar.To);
            Assert.Equal(80, bar.Max);
        }

        [Fact]
        public void ChainsSuccessiveHitsFromTheRunningValue()
        {
            var events = new List<GameEvent>
            {
                new DamageEvent { TargetId = "foe:slime:0", Amount = 30, Effective = 1 },
                new DamageEvent { TargetId = "foe:slime:0", Amount = 20, Effective = 1 },
            };
            var bars = Of<BarAction>(TimelineBuilder.Build(events, new TimelineOptions { Vitals = Vitals() }));
            Assert.Equal(50, ((BarAction)bars[0].Action).To);
            Assert.Equal(50, ((BarAction)bars[1].Action).From);
            Assert.Equal(30, ((BarAction)bars[1].Action).To);
        }

        [Fact]
        public void NeverDrivesABarBelowZeroOrAboveMax()
        {
            var events = new List<GameEvent>
            {
                new DamageEvent { TargetId = "foe:slime:0", Amount = 9999, Effective = 1 },
                new HealEvent { TargetId = "foe:slime:0", Amount = 9999 },
            };
            var bars = Of<BarAction>(TimelineBuilder.Build(events, new TimelineOptions { Vitals = Vitals() }));
            Assert.Equal(0, ((BarAction)bars[0].Action).To);
            Assert.Equal(80, ((BarAction)bars[1].Action).To);
        }

        [Fact]
        public void DoesNotEmitBarStepsForUnknownCombatants()
        {
            var events = new List<GameEvent> { new DamageEvent { TargetId = "nobody", Amount = 5, Effective = 1 } };
            Assert.Empty(Of<BarAction>(TimelineBuilder.Build(events, new TimelineOptions { Vitals = Vitals() })));
        }

        [Fact]
        public void GivesASummonALongCutawayAndAScreenFlash()
        {
            var events = new List<GameEvent>
            {
                new SummonCalledEvent { CombatantId = "party:rell", SummonId = "stonewarden", MotesSpent = 2 },
            };
            var options = new TimelineOptions { Vitals = Vitals() };
            options.Labels["stonewarden"] = "Stonewarden";
            var timeline = TimelineBuilder.Build(events, options);

            Assert.Single(Of<ScreenFlashAction>(timeline));
            var banner = (BannerAction)Of<BannerAction>(timeline)[0].Action;
            Assert.Equal("Stonewarden", banner.Text);
            Assert.Equal(BannerTone.Grand, banner.Tone);
            Assert.True(timeline.DurationMs > 1000);
        }

        [Fact]
        public void RoutesSceneWideStepsOntoTheGlobalTrack()
        {
            var timeline = TimelineBuilder.Build(new List<GameEvent> { new BattleEndedEvent { Outcome = BattleOutcome.Victory } });
            Assert.All(timeline.Steps, s => Assert.Equal(TimelineBuilder.GlobalTrack, s.Track));
        }

        [Fact]
        public void ReportsADurationCoveringTheLastStep()
        {
            var events = new List<GameEvent> { new DamageEvent { TargetId = "foe:slime:0", Amount = 5, Effective = 1 } };
            var timeline = TimelineBuilder.Build(events, new TimelineOptions { Vitals = Vitals() });
            double last = timeline.Steps.Max(s => s.StartMs + s.DurationMs);
            Assert.Equal(last, timeline.DurationMs);
        }

        [Fact]
        public void HonoursCustomTiming()
        {
            var events = new List<GameEvent> { new DamageEvent { TargetId = "foe:slime:0", Amount = 5, Effective = 1 } };
            var fast = TimelineBuilder.Build(events, new TimelineOptions
            {
                Vitals = Vitals(),
                Timing = new TimelineTiming { Impact = 50, NumeralFloat = 60 },
            });
            var slow = TimelineBuilder.Build(events, new TimelineOptions { Vitals = Vitals() });
            Assert.True(fast.DurationMs < slow.DurationMs);
        }

        [Fact]
        public void ReadsEffectivenessOffTheEventRatherThanRecomputingIt()
        {
            Assert.Equal(NumeralStyle.Critical, TimelineBuilder.DamageStyle(true, 1));
            Assert.Equal(NumeralStyle.Strong, TimelineBuilder.DamageStyle(false, 1.5));
            Assert.Equal(NumeralStyle.Weak, TimelineBuilder.DamageStyle(false, 0.5));
            Assert.Equal(NumeralStyle.Damage, TimelineBuilder.DamageStyle(false, 1));
            // Critical wins over effectiveness.
            Assert.Equal(NumeralStyle.Critical, TimelineBuilder.DamageStyle(true, 0.4));
        }
    }

    /// <summary>Ported from test/playback.test.ts.</summary>
    public class PlaybackTests
    {
        private static Timeline Make(params AnimationStep[] steps)
        {
            var timeline = new Timeline { Steps = steps.ToList() };
            timeline.DurationMs = steps.Length == 0 ? 0 : steps.Max(s => s.StartMs + s.DurationMs);
            return timeline;
        }

        private static AnimationStep Step(string track, double start, double duration, StepAction action) =>
            new AnimationStep { Track = track, StartMs = start, DurationMs = duration, Action = action };

        [Fact]
        public void StartsFinishedWhenThereIsNothingToPlay()
        {
            var playback = new Playback();
            playback.Load(Make());
            Assert.True(playback.IsFinished);
        }

        [Fact]
        public void ReportsStepsActiveNowAndNotBefore()
        {
            var playback = new Playback();
            playback.Load(Make(Step("a", 100, 100, new PoseAction { Pose = "attack" })));
            Assert.Empty(playback.Advance(50));
            Assert.Single(playback.Advance(100));
        }

        [Fact]
        public void DropsAStepOnceElapsed()
        {
            var playback = new Playback();
            playback.Load(Make(Step("a", 0, 100, new PoseAction())));
            playback.Advance(50);
            Assert.Empty(playback.ActiveAt(150));
        }

        [Fact]
        public void ReportsNormalizedProgress()
        {
            var playback = new Playback();
            playback.Load(Make(Step("a", 0, 200, new PoseAction())));
            Assert.Equal(0.5, playback.Advance(100)[0].T, 5);
        }

        [Fact]
        public void ClampsToTheEndAndFinishes()
        {
            var playback = new Playback();
            playback.Load(Make(Step("a", 0, 100, new PoseAction())));
            playback.Advance(10000);
            Assert.True(playback.IsFinished);
            Assert.Equal(100, playback.Now);
            Assert.Equal(1, playback.Progress);
        }

        [Fact]
        public void FreezesTheClockDuringHitstopThenResumes()
        {
            var playback = new Playback();
            playback.Load(Make(Step("a", 0, 1000, new PoseAction())));
            playback.RequestHitstop(100);

            playback.Advance(60);
            // The whole frame was eaten by the freeze.
            Assert.Equal(0, playback.Now);

            playback.Advance(60);
            // 40ms of freeze left, so 20ms reaches the clock.
            Assert.Equal(20, playback.Now, 5);
        }

        [Fact]
        public void DoesNotStackHitstopBeyondTheLongest()
        {
            var playback = new Playback();
            playback.Load(Make(Step("a", 0, 1000, new PoseAction())));
            playback.RequestHitstop(50);
            playback.RequestHitstop(80);
            playback.Advance(80);
            Assert.Equal(0, playback.Now);
            playback.Advance(10);
            Assert.Equal(10, playback.Now, 5);
        }

        [Fact]
        public void HonoursTheSpeedMultiplier()
        {
            var playback = new Playback();
            playback.Load(Make(Step("a", 0, 1000, new PoseAction())));
            playback.Speed = 2;
            playback.Advance(100);
            Assert.Equal(200, playback.Now);
        }

        [Fact]
        public void SkipsStraightToTheEnd()
        {
            var playback = new Playback();
            playback.Load(Make(Step("a", 0, 5000, new PoseAction())));
            playback.Skip();
            Assert.True(playback.IsFinished);
            Assert.Equal(5000, playback.Now);
        }

        [Fact]
        public void FiltersStepsByTrack()
        {
            var playback = new Playback();
            playback.Load(Make(
                Step("a", 0, 100, new PoseAction()),
                Step("b", 0, 100, new PoseAction())));
            playback.Advance(50);
            Assert.Single(playback.ActiveForTrack("a"));
        }

        [Fact]
        public void FiresACueScheduledAtTimeZeroOnTheFirstFrame()
        {
            // An exclusive lower bound here would silently swallow the opening
            // cue of every round, since timelines almost always start at t=0.
            var playback = new Playback();
            playback.Load(Make(
                Step(TimelineBuilder.GlobalTrack, 0, 1, new SfxAction { Cue = "hit" }),
                Step(TimelineBuilder.GlobalTrack, 500, 1, new SfxAction { Cue = "down" })));
            playback.Advance(20);
            Assert.Equal(new[] { "hit" }, playback.JustStarted().Select(s => ((SfxAction)s.Action).Cue));
        }

        [Fact]
        public void FiresEachCueExactlyOnceAcrossManyFrames()
        {
            var playback = new Playback();
            playback.Load(Make(
                Step(TimelineBuilder.GlobalTrack, 0, 1, new SfxAction { Cue = "a" }),
                Step(TimelineBuilder.GlobalTrack, 30, 1, new SfxAction { Cue = "b" }),
                Step(TimelineBuilder.GlobalTrack, 200, 1, new SfxAction { Cue = "c" })));

            var fired = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                playback.Advance(16.667);
                fired.AddRange(playback.JustStarted().Select(s => ((SfxAction)s.Action).Cue));
            }
            Assert.Equal(new[] { "a", "b", "c" }, fired);
        }

        [Fact]
        public void FoldsConcurrentStepsIntoOneState()
        {
            var state = RenderFold.ForEntity(new List<ActiveStep>
            {
                new ActiveStep { Step = Step("a", 0, 100, new FlashAction { Intensity = 1 }), T = 0 },
                new ActiveStep { Step = Step("a", 0, 100, new ShakeAction { Intensity = 5 }), T = 0.25 },
                new ActiveStep { Step = Step("a", 0, 100, new PoseAction { Pose = "hurt" }), T = 0 },
            });
            Assert.Equal("hurt", state.Pose);
            Assert.True(state.FlashIntensity > 0);
            Assert.NotEqual(0, state.ShakeOffset);
        }

        [Fact]
        public void DecaysAFlashToNothingByTheEnd()
        {
            double At(double t) => RenderFold.ForEntity(new List<ActiveStep>
            {
                new ActiveStep { Step = Step("a", 0, 100, new FlashAction { Intensity = 1 }), T = t },
            }).FlashIntensity;
            Assert.Equal(1, At(0), 5);
            Assert.Equal(0, At(1), 5);
        }

        [Fact]
        public void SettlesAShakeAsItCompletes()
        {
            double At(double t) => System.Math.Abs(RenderFold.ForEntity(new List<ActiveStep>
            {
                new ActiveStep { Step = Step("a", 0, 100, new ShakeAction { Intensity = 8 }), T = t },
            }).ShakeOffset);
            Assert.Equal(0, At(1), 5);
            Assert.True(At(0.99) < 1);
        }

        [Fact]
        public void InterpolatesABarBetweenItsEndpoints()
        {
            double At(double t) => RenderFold.ForEntity(new List<ActiveStep>
            {
                new ActiveStep { Step = Step("a", 0, 100, new BarAction { Stat = "hp", From = 100, To = 50, Max = 100 }), T = t },
            }).Bars[0].Value;
            Assert.Equal(100, At(0));
            Assert.Equal(50, At(1));
            Assert.True(At(0.5) < 100 && At(0.5) > 50);
        }

        [Fact]
        public void FadesOpacityWhileCollapsing()
        {
            var state = RenderFold.ForEntity(new List<ActiveStep>
            {
                new ActiveStep { Step = Step("a", 0, 100, new CollapseAction()), T = 1 },
            });
            Assert.True(state.Opacity < 1);
            Assert.Equal("down", state.Pose);
        }

        [Fact]
        public void ReturnsANeutralStateWhenNothingIsActive()
        {
            var state = RenderFold.ForEntity(new List<ActiveStep>());
            Assert.Equal("idle", state.Pose);
            Assert.Equal(1, state.Opacity);
            Assert.Empty(state.Numerals);
        }

        [Fact]
        public void FoldsTheGlobalTrackIntoSceneState()
        {
            var scene = RenderFold.ForScene(new List<ActiveStep>
            {
                new ActiveStep { Step = Step(TimelineBuilder.GlobalTrack, 0, 100, new BannerAction { Text = "Victory", Tone = BannerTone.Good }), T = 0.5 },
                new ActiveStep { Step = Step(TimelineBuilder.GlobalTrack, 0, 100, new HitstopAction()), T = 0 },
                new ActiveStep { Step = Step("a", 0, 100, new PoseAction()), T = 0 },
            });
            Assert.Equal("Victory", scene.BannerText);
            Assert.Equal(BannerTone.Good, scene.BannerTone);
            Assert.True(scene.HitstopActive);
        }
    }
}
