using System.Collections.Generic;
using Xunit;
using Aetherlight.Domain;

namespace Aetherlight.Tests
{
    /// <summary>
    /// Ported from test/classes.test.ts. The assertions are deliberately the
    /// same as the TypeScript suite: if both pass, the two implementations
    /// agree about class derivation, which is the rule most likely to drift
    /// silently in a port.
    /// </summary>
    public class ClassResolutionTests
    {
        private static ElementTable<int> Counts(int terra = 0, int pyre = 0, int aeris = 0, int rime = 0)
        {
            var table = ElementTable<int>.Filled(0);
            table[Element.Terra] = terra;
            table[Element.Pyre] = pyre;
            table[Element.Aeris] = aeris;
            table[Element.Rime] = rime;
            return table;
        }

        private static ClassRequirement Req(
            (Element element, int count)[]? min = null,
            (Element element, int count)[]? max = null,
            int? minTotal = null)
        {
            var requirement = new ClassRequirement { MinTotal = minTotal };
            foreach (var (element, count) in min ?? System.Array.Empty<(Element, int)>())
                requirement.MinMotes[element] = count;
            foreach (var (element, count) in max ?? System.Array.Empty<(Element, int)>())
                requirement.MaxMotes[element] = count;
            return requirement;
        }

        private static List<ClassDef> Table() => new List<ClassDef>
        {
            new ClassDef { Id = "base", Name = "Base", Requires = Req(minTotal: 0), Priority = 0 },
            new ClassDef
            {
                Id = "pure-terra", Name = "Pure Terra", Priority = 10,
                Innate = { Element.Terra },
                Requires = Req(
                    min: new[] { (Element.Terra, 1) },
                    max: new[] { (Element.Pyre, 0), (Element.Aeris, 0), (Element.Rime, 0) }),
            },
            new ClassDef
            {
                Id = "dual", Name = "Dual", Priority = 20,
                Innate = { Element.Terra },
                Requires = Req(min: new[] { (Element.Terra, 1), (Element.Pyre, 1) }),
            },
            new ClassDef
            {
                Id = "all-four", Name = "All Four", Priority = 50,
                Requires = Req(min: new[] { (Element.Terra, 1), (Element.Pyre, 1), (Element.Aeris, 1), (Element.Rime, 1) }),
            },
        };

        [Fact]
        public void FallsBackToBaseline_WithNoMotes()
            => Assert.Equal("base", Classes.Resolve(Table(), Element.Terra, Counts())?.Id);

        [Fact]
        public void PicksPureClass_WhenOnlyInnateElementBound()
            => Assert.Equal("pure-terra", Classes.Resolve(Table(), Element.Terra, Counts(terra: 2))?.Id);

        [Fact]
        public void PrefersHigherPriorityDual_WhenSecondElementAppears()
            => Assert.Equal("dual", Classes.Resolve(Table(), Element.Terra, Counts(terra: 1, pyre: 1))?.Id);

        [Fact]
        public void ReachesCapstone_OnlyWithAllFourElements()
            => Assert.Equal("all-four", Classes.Resolve(Table(), Element.Terra, Counts(1, 1, 1, 1))?.Id);

        [Fact]
        public void RespectsInnateGating()
            => Assert.Equal("base", Classes.Resolve(Table(), Element.Pyre, Counts(terra: 2))?.Id);

        [Fact]
        public void EnforcesMaximumRequirements()
        {
            Assert.False(Classes.Matches(Req(max: new[] { (Element.Pyre, 0) }), Counts(terra: 1, pyre: 1)));
            Assert.True(Classes.Matches(Req(max: new[] { (Element.Pyre, 1) }), Counts(terra: 1, pyre: 1)));
        }

        [Fact]
        public void IsDeterministic_WhenPriorityAndSpecificityTie()
        {
            var tied = new List<ClassDef>
            {
                new ClassDef { Id = "zebra", Name = "Z", Priority = 5, Requires = Req(min: new[] { (Element.Terra, 1) }) },
                new ClassDef { Id = "alpha", Name = "A", Priority = 5, Requires = Req(min: new[] { (Element.Terra, 1) }) },
            };
            Assert.Equal("alpha", Classes.Resolve(tied, Element.Terra, Counts(terra: 1))?.Id);
        }
    }
}
