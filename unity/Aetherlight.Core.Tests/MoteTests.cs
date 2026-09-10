using System.Collections.Generic;
using System.Linq;
using Xunit;
using Aetherlight.Domain;

namespace Aetherlight.Tests
{
    /// <summary>Ported from test/motes.test.ts.</summary>
    public class MoteTests
    {
        private static readonly Dictionary<string, MoteDef> Defs = new()
        {
            ["boulder"] = new MoteDef { Id = "boulder", Name = "Boulder", Element = Element.Terra },
            ["loam"] = new MoteDef { Id = "loam", Name = "Loam", Element = Element.Terra },
            ["cinder"] = new MoteDef { Id = "cinder", Name = "Cinder", Element = Element.Pyre },
        };

        private static MoteDef? DefOf(string id) => Defs.TryGetValue(id, out var def) ? def : null;

        [Fact]
        public void CountsOnlySetMotesTowardClass()
        {
            var motes = new List<MoteInstance>
            {
                new MoteInstance("boulder"),
                new MoteInstance("cinder", MoteState.Standby),
            };
            Assert.Equal(1, Motes.SetCounts(motes, DefOf)[Element.Terra]);
            Assert.Equal(0, Motes.SetCounts(motes, DefOf)[Element.Pyre]);
            Assert.Equal(1, Motes.StandbyCounts(motes, DefOf)[Element.Pyre]);
        }

        [Fact]
        public void RefusesToUnleashAMoteThatIsNotSet()
        {
            var motes = new List<MoteInstance> { new MoteInstance("boulder", MoteState.Standby) };
            Assert.False(Motes.Unleash(motes, "boulder"));
        }

        [Fact]
        public void UnleashMovesSetMoteToStandby()
        {
            var motes = new List<MoteInstance> { new MoteInstance("boulder") };
            Assert.True(Motes.Unleash(motes, "boulder"));
            Assert.Equal(MoteState.Standby, motes[0].State);
        }

        [Fact]
        public void SpendsStandbyMotesAndRecoversThemOverRounds()
        {
            var motes = new List<MoteInstance>
            {
                new MoteInstance("boulder", MoteState.Standby),
                new MoteInstance("loam", MoteState.Standby),
            };
            var spent = Motes.SpendStandby(motes, Element.Terra, 2, DefOf);
            Assert.Equal(2, spent.Count);
            Assert.All(motes, m => Assert.Equal(MoteState.Recovering, m.State));

            // Default recovery is three rounds.
            Assert.Empty(Motes.TickRecovery(motes));
            Assert.Empty(Motes.TickRecovery(motes));
            Assert.Equal(2, Motes.TickRecovery(motes).Count);
            Assert.All(motes, m => Assert.Equal(MoteState.Set, m.State));
        }

        [Fact]
        public void WillNotSpendMoreMotesThanAreOnStandby()
        {
            var motes = new List<MoteInstance> { new MoteInstance("boulder", MoteState.Standby) };
            Assert.Empty(Motes.SpendStandby(motes, Element.Terra, 2, DefOf));
            // Nothing was mutated by the failed attempt.
            Assert.Equal(MoteState.Standby, motes[0].State);
        }

        [Fact]
        public void RestoreAllReturnsEveryMoteBetweenBattles()
        {
            var motes = new List<MoteInstance>
            {
                new MoteInstance("boulder", MoteState.Standby),
                new MoteInstance("cinder", MoteState.Recovering) { RecoveryLeft = 2 },
            };
            Motes.RestoreAll(motes);
            Assert.All(motes, m => { Assert.Equal(MoteState.Set, m.State); Assert.Equal(0, m.RecoveryLeft); });
        }
    }
}
