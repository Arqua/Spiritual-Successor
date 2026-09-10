using System;
using System.Collections.Generic;

namespace Aetherlight.Core
{
    /// <summary>
    /// Deterministic, serializable pseudo-random number generator.
    ///
    /// This is a bit-exact port of the TypeScript original. That exactness is
    /// the whole point: seeds, replays, and balance-test failures are only
    /// portable between the two implementations if the generators agree on
    /// every bit. <c>RngParityTests</c> pins that against a fixture captured
    /// from the TypeScript side.
    ///
    /// Porting notes, since JavaScript's integer semantics do not survive a
    /// naive translation:
    ///   * <c>Math.imul(a, b)</c> is a 32-bit multiply that wraps. C# <c>uint</c>
    ///     multiplication inside <c>unchecked</c> produces the same bit pattern.
    ///   * <c>x >>> 0</c> coerces to uint32; using <c>uint</c> throughout makes
    ///     it implicit.
    ///   * Addition and XOR agree between the two languages once both sides are
    ///     mod-2^32, because two's complement addition is sign-agnostic.
    /// </summary>
    public struct RngState : IEquatable<RngState>
    {
        public uint A;
        public uint B;
        public uint C;
        public uint D;

        public RngState(uint a, uint b, uint c, uint d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public bool Equals(RngState other) => A == other.A && B == other.B && C == other.C && D == other.D;

        public override bool Equals(object? obj) => obj is RngState other && Equals(other);

        public override int GetHashCode() => unchecked((int)(A ^ B ^ C ^ D));

        public override string ToString() => $"RngState({A}, {B}, {C}, {D})";
    }

    public static class Rng
    {
        private const double Uint32Range = 4294967296.0;

        private static uint Rotl(uint x, int k) => unchecked((x << k) | (x >> (32 - k)));

        /// <summary>Build a generator state from a string or numeric seed.</summary>
        public static RngState Seed(string seed)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));

            uint h = 0x9e3779b9u;
            unchecked
            {
                for (int i = 0; i < seed.Length; i++)
                {
                    h = (h ^ seed[i]) * 0x85ebca6bu;
                    h = h ^ (h >> 13);
                }
            }

            uint Next()
            {
                unchecked
                {
                    h += 0x6d2b79f5u;
                    uint t = h;
                    t = (t ^ (t >> 15)) * (t | 1u);
                    t = t ^ (t + (t ^ (t >> 7)) * (t | 61u));
                    return t ^ (t >> 14);
                }
            }

            var state = new RngState(Next(), Next(), Next(), Next());
            // The all-zero state is a fixed point; nudge it off.
            if (state.A == 0 && state.B == 0 && state.C == 0 && state.D == 0) state.A = 0x1a2b3c4du;
            return state;
        }

        public static RngState Seed(long seed) => Seed(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>Advance the state and return a uint32.</summary>
        public static uint NextUInt32(ref RngState state)
        {
            unchecked
            {
                uint t = state.B << 9;
                state.C ^= state.A;
                state.D ^= state.B;
                state.B ^= state.C;
                state.A ^= state.D;
                state.C ^= t;
                state.D = Rotl(state.D, 11);
                return Rotl(state.B * 5u, 7) * 9u;
            }
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public static double NextDouble(ref RngState state) => NextUInt32(ref state) / Uint32Range;

        /// <summary>Uniform integer in [min, max], inclusive.</summary>
        public static int NextInt(ref RngState state, int min, int max)
        {
            if (max < min) throw new ArgumentOutOfRangeException(nameof(max), $"NextInt: max ({max}) < min ({min})");
            long span = (long)max - min + 1;
            return min + (int)Math.Floor(NextDouble(ref state) * span);
        }

        /// <summary>True with the given probability (0..1).</summary>
        public static bool Chance(ref RngState state, double probability)
        {
            if (probability <= 0) return false;
            if (probability >= 1) return true;
            return NextDouble(ref state) < probability;
        }

        /// <summary>Pick one element, or default for an empty list.</summary>
        public static T? Pick<T>(ref RngState state, IReadOnlyList<T> items) where T : class
        {
            if (items == null || items.Count == 0) return null;
            return items[NextInt(ref state, 0, items.Count - 1)];
        }

        /// <summary>Fisher-Yates into a new list; the input is left untouched.</summary>
        public static List<T> Shuffle<T>(ref RngState state, IReadOnlyList<T> items)
        {
            var outList = new List<T>(items);
            for (int i = outList.Count - 1; i > 0; i--)
            {
                int j = NextInt(ref state, 0, i);
                (outList[i], outList[j]) = (outList[j], outList[i]);
            }
            return outList;
        }

        /// <summary>
        /// Weighted pick. Weights must be non-negative; zero-weight entries are
        /// never chosen. Returns false if every weight is zero or the list is empty.
        /// </summary>
        public static bool TryWeightedPick<T>(ref RngState state, IReadOnlyList<T> items, Func<T, double> weightOf, out T result)
        {
            result = default!;
            if (items == null || items.Count == 0) return false;

            double total = 0;
            foreach (var item in items) total += Math.Max(0, weightOf(item));
            if (total <= 0) return false;

            double roll = NextDouble(ref state) * total;
            foreach (var item in items)
            {
                roll -= Math.Max(0, weightOf(item));
                if (roll < 0)
                {
                    result = item;
                    return true;
                }
            }
            result = items[items.Count - 1];
            return true;
        }

        /// <summary>Symmetric variance multiplier: spread 0.1 gives [0.9, 1.1).</summary>
        public static double Variance(ref RngState state, double spread) => 1 + (NextDouble(ref state) * 2 - 1) * spread;
    }
}
