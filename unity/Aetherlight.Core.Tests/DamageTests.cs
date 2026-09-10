using Xunit;
using Aetherlight.Core;
using Aetherlight.Domain;
using Aetherlight.Battle;

namespace Aetherlight.Tests
{
    /// <summary>Ported from test/damage.test.ts.</summary>
    public class DamageTests
    {
        private static StatBlock Stats(double attack = 60, double defense = 30, double agility = 40, double luck = 10)
        {
            var block = new StatBlock
            {
                MaxHp = 200, MaxAether = 100,
                Attack = attack, Defense = defense, Agility = agility, Luck = luck,
                Power = ElementTable<double>.Filled(20),
                Resist = ElementTable<double>.Filled(20),
            };
            return block.Normalized();
        }

        /// <summary>No variance, no crits - isolates the formula from the dice.</summary>
        private static DamageConfig Flat() => new DamageConfig
        {
            Variance = 0,
            BaseCritChance = 0,
            CritLuckDivisor = double.MaxValue,
        };

        [Fact]
        public void NeverDealsLessThanTheMinimum()
        {
            var rng = Rng.Seed("min");
            var result = Damage.Roll(ref rng, new DamageInput
            {
                Attacker = Stats(attack: 1),
                Defender = Stats(defense: 9999),
                Power = 1,
                Kind = DamageKind.Physical,
                Defending = true,
            }, Flat());
            Assert.True(result.Amount >= DamageConfig.Default.MinimumDamage);
        }

        [Fact]
        public void ScalesWithAttackerAttack()
        {
            var rng = Rng.Seed("gap");
            var weak = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(attack: 40), Defender = Stats(), Power = 1, Kind = DamageKind.Physical }, Flat());
            var strong = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(attack: 120), Defender = Stats(), Power = 1, Kind = DamageKind.Physical }, Flat());
            Assert.True(strong.Amount > weak.Amount);
        }

        [Fact]
        public void DefenseBluntsPhysicalFarMoreThanAetheric()
        {
            var rng = Rng.Seed("armor");
            var cfg = Flat();
            double physLight = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(), Defender = Stats(defense: 10), Power = 1, Kind = DamageKind.Physical }, cfg).Amount;
            double physHeavy = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(), Defender = Stats(defense: 50), Power = 1, Kind = DamageKind.Physical }, cfg).Amount;
            double magicLight = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(), Defender = Stats(defense: 10), Power = 60, Kind = DamageKind.Aetheric }, cfg).Amount;
            double magicHeavy = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(), Defender = Stats(defense: 50), Power = 60, Kind = DamageKind.Aetheric }, cfg).Amount;

            double physReduction = 1 - physHeavy / physLight;
            double magicReduction = 1 - magicHeavy / magicLight;
            Assert.True(physReduction > magicReduction);
        }

        [Fact]
        public void ArmourIsNeverAWall()
        {
            // The regression that motivated asymptotic mitigation: with a
            // subtractive formula, defense above attack collapsed damage to the
            // 1-point floor and made a starting party unkillable by accident.
            var rng = Rng.Seed("wall");
            var cfg = Flat();
            double atLowDefense = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(attack: 34), Defender = Stats(defense: 10), Power = 1, Kind = DamageKind.Physical }, cfg).Amount;
            double atHighDefense = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(attack: 34), Defender = Stats(defense: 60), Power = 1, Kind = DamageKind.Physical }, cfg).Amount;

            Assert.True(atHighDefense > cfg.MinimumDamage);
            Assert.True(atHighDefense < atLowDefense);
        }

        [Fact]
        public void MitigationIsMonotonicAndNeverReachesZero()
        {
            double previous = Damage.Mitigation(0);
            Assert.Equal(1.0, previous);
            for (double defense = 10; defense <= 10000; defense *= 2)
            {
                double current = Damage.Mitigation(defense);
                Assert.True(current < previous);
                Assert.True(current > 0);
                previous = current;
            }
        }

        [Fact]
        public void AppliesElementalAffinity()
        {
            var rng = Rng.Seed("affinity");
            var cfg = Flat();

            var strongAttacker = Stats();
            strongAttacker.Power = ElementTable<double>.Filled(0);
            strongAttacker.Power[Element.Pyre] = 90;
            var bareDefender = Stats();
            bareDefender.Resist = ElementTable<double>.Filled(0);

            var weakAttacker = Stats();
            weakAttacker.Power = ElementTable<double>.Filled(0);
            var wardedDefender = Stats();
            wardedDefender.Resist = ElementTable<double>.Filled(0);
            wardedDefender.Resist[Element.Pyre] = 90;

            var strong = Damage.Roll(ref rng, new DamageInput { Attacker = strongAttacker, Defender = bareDefender, Element = Element.Pyre, Power = 60, Kind = DamageKind.Aetheric }, cfg);
            var weak = Damage.Roll(ref rng, new DamageInput { Attacker = weakAttacker, Defender = wardedDefender, Element = Element.Pyre, Power = 60, Kind = DamageKind.Aetheric }, cfg);

            Assert.True(strong.Effectiveness > 1);
            Assert.True(weak.Effectiveness < 1);
            Assert.True(strong.Amount > weak.Amount);
        }

        [Fact]
        public void HalvesDamageAgainstADefendingTarget()
        {
            var rng = Rng.Seed("defend");
            var cfg = Flat();
            double open = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(), Defender = Stats(), Power = 1, Kind = DamageKind.Physical, Defending = false }, cfg).Amount;
            double guarded = Damage.Roll(ref rng, new DamageInput { Attacker = Stats(), Defender = Stats(), Power = 1, Kind = DamageKind.Physical, Defending = true }, cfg).Amount;
            Assert.True(guarded < open);
        }

        [Fact]
        public void CapsEvasionSoAFastDefenderIsNeverUntouchable()
        {
            var rng = Rng.Seed("evade");
            int evaded = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (Damage.RollEvaded(ref rng, Stats(agility: 1), Stats(agility: 100000))) evaded++;
            }
            Assert.True(evaded / 1000.0 <= DamageConfig.Default.MaxEvasion + 0.05);
        }

        [Fact]
        public void HealsMoreWithHigherElementalPower()
        {
            var rng = Rng.Seed("heal");
            var cfg = new DamageConfig { Variance = 0 };
            double low = Damage.RollHeal(ref rng, 40, 0, 200, 0, cfg);
            double high = Damage.RollHeal(ref rng, 40, 200, 200, 0, cfg);
            Assert.True(high > low);
        }
    }
}
