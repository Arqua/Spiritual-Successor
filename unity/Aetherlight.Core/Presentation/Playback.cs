using System;
using System.Collections.Generic;

namespace Aetherlight.Presentation
{
    public enum PlaybackState { Idle, Playing, Finished }

    public sealed class ActiveStep
    {
        public AnimationStep Step = new AnimationStep();
        /// <summary>Normalized progress through this step, 0..1.</summary>
        public double T;
        public double ElapsedMs;
    }

    /// <summary>
    /// Timeline playback: owns the clock.
    ///
    /// Advance it with the frame delta and ask what is active; the renderer
    /// draws whatever comes back. Nothing here touches an engine type, so the
    /// same scheduler drives a Unity renderer, a text log, or a test.
    ///
    /// Hitstop is handled here rather than in the renderer because it must
    /// freeze the whole scene consistently - if each entity decided on its own,
    /// a hit would smear.
    /// </summary>
    public sealed class Playback
    {
        private Timeline _timeline = new Timeline();
        private double _clockMs;

        /// <summary>
        /// Clock value before the last Advance. Starts below zero so steps
        /// scheduled at exactly time 0 are reported by the first JustStarted
        /// call rather than skipped by an exclusive lower bound.
        /// </summary>
        private double _previousClockMs = -1;

        /// <summary>Whether Advance has run since the timeline was loaded.</summary>
        private bool _hasAdvanced;

        private double _hitstopMs;
        private PlaybackState _state = PlaybackState.Idle;

        /// <summary>Playback rate. 2 is double speed, for a "fast battle" option.</summary>
        public double Speed { get; set; } = 1;

        public void Load(Timeline timeline)
        {
            _timeline = timeline;
            _clockMs = 0;
            _previousClockMs = -1;
            _hasAdvanced = false;
            _hitstopMs = 0;
            _state = timeline.Steps.Count > 0 ? PlaybackState.Playing : PlaybackState.Finished;
        }

        /// <summary>Advance the clock. Returns the steps active after advancing.</summary>
        public List<ActiveStep> Advance(double dtMs)
        {
            if (_state != PlaybackState.Playing) return new List<ActiveStep>();

            double delta = Math.Max(0, dtMs) * Speed;
            // On the very first advance the previous bound stays below zero, so
            // steps at exactly time 0 fall inside the (previous, current] window.
            _previousClockMs = _hasAdvanced ? _clockMs : -1;
            _hasAdvanced = true;

            // Hitstop eats frame time before the clock sees it, freezing everything.
            if (_hitstopMs > 0)
            {
                double consumed = Math.Min(_hitstopMs, delta);
                _hitstopMs -= consumed;
                delta -= consumed;
                if (delta <= 0) return ActiveAt(_clockMs);
            }

            _clockMs += delta;
            if (_clockMs >= _timeline.DurationMs)
            {
                _clockMs = _timeline.DurationMs;
                _state = PlaybackState.Finished;
            }
            return ActiveAt(_clockMs);
        }

        /// <summary>Steps overlapping a given time.</summary>
        public List<ActiveStep> ActiveAt(double timeMs)
        {
            var active = new List<ActiveStep>();
            foreach (var step in _timeline.Steps)
            {
                if (timeMs < step.StartMs) continue;
                double elapsed = timeMs - step.StartMs;
                if (elapsed > step.DurationMs) continue;
                double t = step.DurationMs <= 0 ? 1 : Math.Min(1, elapsed / step.DurationMs);
                active.Add(new ActiveStep { Step = step, T = t, ElapsedMs = elapsed });
            }
            return active;
        }

        /// <summary>Steps active for one entity, which is what a sprite needs each frame.</summary>
        public List<ActiveStep> ActiveForTrack(string track) => ActiveForTrack(track, _clockMs);

        public List<ActiveStep> ActiveForTrack(string track, double timeMs) =>
            ActiveAt(timeMs).FindAll(entry => entry.Step.Track == track);

        /// <summary>
        /// Steps that began during the most recent Advance, for one-shot
        /// triggers like sound cues.
        ///
        /// The window is (previous clock, current clock], measured from the
        /// actual last advance rather than a caller-supplied duration. That
        /// guarantees every step fires exactly once: a caller-supplied window
        /// either drops cues when shorter than the frame delta, or repeats them
        /// when longer.
        /// </summary>
        public List<AnimationStep> JustStarted() =>
            _timeline.Steps.FindAll(step => step.StartMs > _previousClockMs && step.StartMs <= _clockMs);

        /// <summary>Request a scene-wide freeze. Called when a hitstop step begins.</summary>
        public void RequestHitstop(double durationMs) => _hitstopMs = Math.Max(_hitstopMs, durationMs);

        /// <summary>Jump to the end, for a "skip animation" button.</summary>
        public void Skip()
        {
            _previousClockMs = _hasAdvanced ? _clockMs : -1;
            _hasAdvanced = true;
            _clockMs = _timeline.DurationMs;
            _hitstopMs = 0;
            _state = PlaybackState.Finished;
        }

        public bool IsFinished => _state == PlaybackState.Finished;
        public bool IsPlaying => _state == PlaybackState.Playing;
        public double Now => _clockMs;
        public double TotalMs => _timeline.DurationMs;

        /// <summary>0..1 through the whole timeline, for a progress indicator.</summary>
        public double Progress => _timeline.DurationMs <= 0 ? 1 : _clockMs / _timeline.DurationMs;
    }

    public sealed class NumeralView
    {
        public string Text = "";
        public NumeralStyle Style;
        public double T;
    }

    public sealed class BarView
    {
        public string Stat = "hp";
        public double Value;
        public double Max;
    }

    public sealed class VfxView
    {
        public string Effect = "";
        public Domain.Element? Element;
        public double T;
    }

    /// <summary>
    /// The values a sprite needs each frame, folded from every step currently
    /// targeting it. Several steps can overlap - a flash and a shake and a pose
    /// - so discrete values take the last writer and continuous ones accumulate.
    /// </summary>
    public sealed class EntityRenderState
    {
        public string Pose = "idle";
        public double FlashIntensity;
        public string FlashColor = "#ffffff";
        public double ShakeOffset;
        public double Opacity = 1;
        public string? LungeToward;
        public double LungeAmount;
        public List<NumeralView> Numerals = new List<NumeralView>();
        public List<BarView> Bars = new List<BarView>();
        public List<VfxView> Vfx = new List<VfxView>();
    }

    public sealed class SceneRenderState
    {
        public string? BannerText;
        public BannerTone BannerTone;
        public double BannerT;
        public bool HasBanner;
        public double ScreenFlash;
        public string? CameraFocusId;
        public bool HitstopActive;
    }

    public static class RenderFold
    {
        public static EntityRenderState ForEntity(IReadOnlyList<ActiveStep> active)
        {
            var outState = new EntityRenderState();

            foreach (var entry in active)
            {
                double t = entry.T;
                switch (entry.Step.Action)
                {
                    case PoseAction pose:
                        outState.Pose = pose.Pose;
                        break;

                    case FlashAction flash:
                        // Flash decays across its own duration.
                        outState.FlashIntensity = Math.Max(outState.FlashIntensity, flash.Intensity * (1 - t));
                        outState.FlashColor = flash.Color;
                        break;

                    case ShakeAction shake:
                        // Decaying oscillation, so the shake settles rather than cutting out.
                        outState.ShakeOffset += Math.Sin(t * Math.PI * 8) * shake.Intensity * (1 - t);
                        break;

                    case LungeAction lunge:
                        outState.LungeToward = lunge.TowardId;
                        outState.LungeAmount = Math.Sin(t * Math.PI);
                        break;

                    case CollapseAction:
                        outState.Opacity = Math.Min(outState.Opacity, 1 - t * 0.65);
                        outState.Pose = "down";
                        break;

                    case RiseAction:
                        outState.Opacity = Math.Max(outState.Opacity, t);
                        break;

                    case NumeralAction numeral:
                        outState.Numerals.Add(new NumeralView { Text = numeral.Text, Style = numeral.Style, T = t });
                        break;

                    case BarAction bar:
                        outState.Bars.Add(new BarView
                        {
                            Stat = bar.Stat,
                            Value = bar.From + (bar.To - bar.From) * EaseBar(t),
                            Max = bar.Max,
                        });
                        break;

                    case VfxAction vfx:
                        outState.Vfx.Add(new VfxView { Effect = vfx.Effect, Element = vfx.Element, T = t });
                        break;
                }
            }
            return outState;
        }

        /// <summary>Bars drain fast then settle, which reads better than a linear slide.</summary>
        private static double EaseBar(double t) => 1 - Math.Pow(1 - t, 3);

        public static SceneRenderState ForScene(IReadOnlyList<ActiveStep> active)
        {
            var outState = new SceneRenderState();
            foreach (var entry in active)
            {
                if (entry.Step.Track != TimelineBuilder.GlobalTrack) continue;
                switch (entry.Step.Action)
                {
                    case BannerAction banner:
                        outState.HasBanner = true;
                        outState.BannerText = banner.Text;
                        outState.BannerTone = banner.Tone;
                        outState.BannerT = entry.T;
                        break;
                    case ScreenFlashAction:
                        outState.ScreenFlash = Math.Max(outState.ScreenFlash, 1 - entry.T);
                        break;
                    case CameraAction camera:
                        outState.CameraFocusId = camera.FocusId;
                        break;
                    case HitstopAction:
                        outState.HitstopActive = true;
                        break;
                }
            }
            return outState;
        }
    }
}
