using Xunit;
using Aetherlight.Domain;

namespace Aetherlight.Tests
{
    /// <summary>Ported from test/elements.test.ts and the growth-curve rules.</summary>
    public class ElementAndStatTests
    {
        [Fact]
        public void PairsOppositesSymmetrically()
        {
            foreach (var element in Elements.All)
                Assert.Equal(element, Elements.Opposed(Elements.Opposed(element)));
        }

        [Fact]
        public void IsNeutralWhenPowerAndResistanceMatch()
            => Assert.Equal(1.0, Elements.AffinityFactor(Element.Pyre, 50, 50));

        [Fact]
        public void RewardsPowerAdvantageAndPunishesResistance()
        {
            Assert.True(Elements.AffinityFactor(Element.Pyre, 100, 50) > 1);
            Assert.True(Elements.AffinityFactor(Element.Pyre, 20, 90) < 1);
        }

        [Fact]
        public void GrantsAnEdgeAgainstAnOpposedDefender()
        {
            double neutral = Elements.AffinityFactor(Element.Pyre, 50, 50, Element.Terra);
            double opposed = Elements.AffinityFactor(Element.Pyre, 50, 50, Element.Rime);
            Assert.True(opposed > neutral);
        }

        [Fact]
        public void ClampsEffectivenessIntoTheConfiguredBand()
        {
            Assert.True(Elements.AffinityFactor(Element.Rime, 10000, 0) <= 2.0);
            Assert.True(Elements.AffinityFactor(Element.Rime, 0, 10000) >= 0.25);
        }

        [Fact]
        public void GrowthCurveIsLinearWhenExponentIsOne()
        {
            var curve = new GrowthCurve(10, 2);
            Assert.Equal(10, Stats.CurveAt(curve, 1));
            Assert.Equal(12, Stats.CurveAt(curve, 2));
            Assert.Equal(20, Stats.CurveAt(curve, 6));
        }

        [Fact]
        public void NormalizeKeepsStatsAboveTheirFloors()
        {
            var block = new StatBlock { MaxHp = -50, Attack = -3, Agility = 0, Defense = -9 }.Normalized();
            Assert.Equal(1, block.MaxHp);
            Assert.Equal(1, block.Attack);
            Assert.Equal(1, block.Agility);
            Assert.Equal(0, block.Defense);
        }

        [Fact]
        public void XpCurveIsMonotonicAndStartsAtZero()
        {
            Assert.Equal(0, Stats.XpForLevel(1));
            for (int level = 2; level < 60; level++)
                Assert.True(Stats.XpForLevel(level) > Stats.XpForLevel(level - 1));
        }

        [Fact]
        public void LevelForXpInvertsXpForLevel()
        {
            for (int level = 1; level <= 40; level++)
                Assert.Equal(level, Stats.LevelForXp(Stats.XpForLevel(level)));
        }
    }
}
