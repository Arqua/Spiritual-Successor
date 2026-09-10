using System;
using System.Collections.Generic;
using Aetherlight.Field;

namespace Aetherlight.Presentation
{
    /// <summary>Eight compass directions, clockwise from south (toward the viewer).</summary>
    public enum Direction { S, SW, W, NW, N, NE, E, SE }

    public enum ActorPose { Idle, Walk, Attack, Cast, Hurt, Down, Victory }

    public sealed class AnimationClip
    {
        /// <summary>Frame indices into the sprite sheet row.</summary>
        public int[] Frames = System.Array.Empty<int>();
        public double FrameMs = 100;
        public bool Loop;
    }

    /// <summary>
    /// Sprite orientation and framing.
    ///
    /// Pixel-art characters in a projected world have to decide which way they
    /// face relative to the camera. This module owns that decision and the
    /// frame timing that goes with it.
    /// </summary>
    public static class Sprites
    {
        public static readonly Direction[] AllDirections =
        {
            Direction.S, Direction.SW, Direction.W, Direction.NW,
            Direction.N, Direction.NE, Direction.E, Direction.SE,
        };

        /// <summary>World-space angle in degrees for each cardinal facing.</summary>
        private static double FacingAngle(Facing facing) => facing switch
        {
            Facing.South => 0,
            Facing.West => 90,
            Facing.North => 180,
            Facing.East => 270,
            _ => 0,
        };

        /// <summary>
        /// Pick the sprite direction for an actor facing <paramref name="facing"/>
        /// when the camera is rotated by <paramref name="cameraYaw"/> degrees.
        ///
        /// With 8-directional art this keeps characters looking correct as the
        /// camera orbits. With a single-direction sheet, ignore the result and
        /// billboard instead.
        /// </summary>
        public static Direction SpriteDirection(Facing facing, double cameraYaw = 0)
        {
            double relative = ((FacingAngle(facing) - cameraYaw) % 360 + 360) % 360;
            int index = (int)Math.Round(relative / 45, MidpointRounding.AwayFromZero) % 8;
            return AllDirections[index];
        }

        public static bool FacesAway(Direction direction) =>
            direction == Direction.N || direction == Direction.NE || direction == Direction.NW;

        /// <summary>
        /// Y-axis billboarding angle: the rotation a sprite quad needs to face
        /// the camera while staying upright. This is the recommended mode -
        /// full billboarding makes sprites swivel and lifts their feet off the
        /// ground.
        /// </summary>
        public static double BillboardYaw(double cameraYaw) => -cameraYaw;

        public static readonly IReadOnlyDictionary<ActorPose, AnimationClip> DefaultClips =
            new Dictionary<ActorPose, AnimationClip>
            {
                [ActorPose.Idle] = new AnimationClip { Frames = new[] { 0, 1, 2, 1 }, FrameMs = 180, Loop = true },
                [ActorPose.Walk] = new AnimationClip { Frames = new[] { 0, 1, 2, 3 }, FrameMs = 110, Loop = true },
                [ActorPose.Attack] = new AnimationClip { Frames = new[] { 0, 1, 2 }, FrameMs = 70 },
                [ActorPose.Cast] = new AnimationClip { Frames = new[] { 0, 1 }, FrameMs = 120 },
                [ActorPose.Hurt] = new AnimationClip { Frames = new[] { 0 }, FrameMs = 200 },
                [ActorPose.Down] = new AnimationClip { Frames = new[] { 0 }, FrameMs = 1 },
                [ActorPose.Victory] = new AnimationClip { Frames = new[] { 0, 1 }, FrameMs = 240, Loop = true },
            };

        /// <summary>Current frame index for a clip at a given elapsed time.</summary>
        public static int FrameAt(AnimationClip clip, double elapsedMs)
        {
            if (clip.Frames.Length == 0) return 0;
            int step = (int)Math.Floor(Math.Max(0, elapsedMs) / clip.FrameMs);
            int index = clip.Loop ? step % clip.Frames.Length : Math.Min(step, clip.Frames.Length - 1);
            return clip.Frames[index];
        }

        public static double ClipDuration(AnimationClip clip) => clip.Frames.Length * clip.FrameMs;

        /// <summary>
        /// Idle bob offset in pixels. A tiny oscillation stops a static sprite
        /// from looking dead; the per-entity phase keeps a row of characters
        /// from bobbing in lockstep.
        /// </summary>
        public static double IdleBob(double timeMs, double phase, double amplitude = 1.5, double periodMs = 1600) =>
            Math.Sin((timeMs / periodMs + phase) * Math.PI * 2) * amplitude;

        /// <summary>Stable phase offset derived from an id, so it survives reloads.</summary>
        public static double PhaseFromId(string id)
        {
            uint hash = 0;
            unchecked
            {
                foreach (char c in id) hash = hash * 31 + c;
            }
            return (hash % 1000) / 1000.0;
        }
    }
}
