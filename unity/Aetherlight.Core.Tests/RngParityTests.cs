using System;
using Xunit;
using Aetherlight.Core;

namespace Aetherlight.Tests
{
    /// <summary>
    /// Cross-language parity for the generator.
    ///
    /// The expected values below were produced by running the TypeScript
    /// implementation (src/core/rng.ts) and captured verbatim. If this file
    /// fails, the two implementations have diverged and no seed, replay, or
    /// recorded battle transfers between them - which is a far worse failure
    /// than a wrong number, because everything downstream still *looks* fine.
    ///
    /// Regenerate with tools/rng-fixture.ts if the generator is ever changed
    /// deliberately, and change both sides together.
    /// </summary>
    public class RngParityTests
    {

        [Fact]
        public void SeedDemo_MatchesTypeScript()
        {
            var state = Rng.Seed("demo");
            Assert.Equal(1070530927u, state.A);
            Assert.Equal(470820821u, state.B);
            Assert.Equal(3741457244u, state.C);
            Assert.Equal(3424574197u, state.D);

            uint[] expected = { 1911601976u, 1828456857u, 1347499133u, 2517064688u, 1546128411u, 2839736958u, 551818970u, 173138417u };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], Rng.NextUInt32(ref state));
            }
        }

        [Fact]
        public void SeedBattle42_MatchesTypeScript()
        {
            var state = Rng.Seed("battle-42");
            Assert.Equal(2437566561u, state.A);
            Assert.Equal(4156946991u, state.B);
            Assert.Equal(2288041187u, state.C);
            Assert.Equal(499806559u, state.D);

            uint[] expected = { 58603389u, 3173970850u, 4228691854u, 3543376773u, 2685320993u, 3427046095u, 2697508791u, 3254561410u };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], Rng.NextUInt32(ref state));
            }
        }

        [Fact]
        public void SingleCharSeed_MatchesTypeScript()
        {
            var state = Rng.Seed("x");
            Assert.Equal(3381320821u, state.A);
            Assert.Equal(2749523633u, state.B);
            Assert.Equal(2714567181u, state.C);
            Assert.Equal(195986234u, state.D);

            uint[] expected = { 280866533u, 2145612313u, 892307538u, 893059336u, 82078821u, 4172536949u, 2285256899u, 14874126u };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], Rng.NextUInt32(ref state));
            }
        }

        [Fact]
        public void EmptySeed_MatchesTypeScript()
        {
            var state = Rng.Seed("");
            Assert.Equal(1541420728u, state.A);
            Assert.Equal(454851044u, state.B);
            Assert.Equal(2900350524u, state.C);
            Assert.Equal(3942498910u, state.D);

            uint[] expected = { 553317054u, 1884961304u, 4170540610u, 3094077806u, 1081180018u, 678833777u, 4247122458u, 949170562u };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], Rng.NextUInt32(ref state));
            }
        }

        [Fact]
        public void LongSeed_MatchesTypeScript()
        {
            var state = Rng.Seed("a-longer-seed-string-0123456789");
            Assert.Equal(4266497476u, state.A);
            Assert.Equal(399757069u, state.B);
            Assert.Equal(3053060961u, state.C);
            Assert.Equal(420470198u, state.D);

            uint[] expected = { 3521202070u, 1564411442u, 382880645u, 3272321173u, 714323668u, 2342770138u, 2497994033u, 170533228u };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], Rng.NextUInt32(ref state));
            }
        }

        [Fact]
        public void Doubles_MatchTypeScript()
        {
            var state = Rng.Seed("floats");
            double[] expected = { 0.2422300693579018, 0.8453282862901688, 0.48053174116648734, 0.7280915617011487, 0.490569363348186, 0.4566485658288002 };
            for (int i = 0; i < expected.Length; i++)
            {
                // Exact equality: both sides divide the same uint32 by 2^32,
                // which is representable, so there is no rounding to tolerate.
                Assert.Equal(expected[i], Rng.NextDouble(ref state));
            }
        }

        [Fact]
        public void Ints_MatchTypeScript()
        {
            var state = Rng.Seed("ints");
            int[] expected = { 4, 5, 8, 9, 9, 6, 7, 6, 8, 7 };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], Rng.NextInt(ref state, 3, 9));
            }
        }

        [Fact]
        public void NumericSeed_MatchesTypeScript()
        {
            // The TypeScript side stringifies a numeric seed before hashing, so
            // Seed(12345) and Seed("12345") must agree here too.
            var state = Rng.Seed(12345);
            Assert.Equal(3041794202u, state.A);
            Assert.Equal(2298467661u, state.B);
            Assert.Equal(3922281909u, state.C);
            Assert.Equal(3940992694u, state.D);

            uint[] expected = { 2549915051u, 855498384u, 4258001277u, 3762124182u };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], Rng.NextUInt32(ref state));
            }
            Assert.Equal(Rng.Seed("12345"), Rng.Seed(12345));
        }
    }
}
