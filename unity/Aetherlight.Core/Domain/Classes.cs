using System;
using System.Collections.Generic;

namespace Aetherlight.Domain
{
    /// <summary>
    /// Classes are derived, never assigned.
    ///
    /// A class is a pure function of (innate element, set-mote counts). Nothing
    /// stores it, so it can never fall out of sync with the motes, and
    /// mid-battle class changes fall out of the system for free.
    ///
    /// Resolution: collect every class whose requirements are satisfied, take
    /// the highest priority. Ties break toward the more specific requirement,
    /// then by id so the result is deterministic.
    /// </summary>
    public sealed class ClassRequirement
    {
        /// <summary>Minimum set motes per element. Absent means none required.</summary>
        public ElementTable<int?> MinMotes = ElementTable<int?>.Filled(null);

        /// <summary>Maximum set motes per element, for classes wanting purity.</summary>
        public ElementTable<int?> MaxMotes = ElementTable<int?>.Filled(null);

        /// <summary>Minimum total set motes across all elements.</summary>
        public int? MinTotal;
    }

    public sealed class ClassArtGrant
    {
        public string ArtId = "";
        public int Level;
    }

    public sealed class ClassDef
    {
        public string Id = "";
        public string Name = "";
        public string? Description;
        /// <summary>Innate elements this class is available to. Empty means any.</summary>
        public List<Element> Innate = new List<Element>();
        public ClassRequirement Requires = new ClassRequirement();
        /// <summary>Higher wins when several classes match.</summary>
        public int Priority;
        public ClassMultipliers? StatMultipliers;
        public List<ClassArtGrant> Arts = new List<ClassArtGrant>();
    }

    public static class Classes
    {
        public static int RequirementSpecificity(ClassRequirement requirement)
        {
            int total = requirement.MinTotal ?? 0;
            foreach (var element in Elements.All) total += requirement.MinMotes[element] ?? 0;
            return total;
        }

        public static bool Matches(ClassRequirement requirement, ElementTable<int> counts)
        {
            int total = 0;
            foreach (var element in Elements.All)
            {
                int have = counts[element];
                total += have;

                var min = requirement.MinMotes[element];
                if (min.HasValue && have < min.Value) return false;

                var max = requirement.MaxMotes[element];
                if (max.HasValue && have > max.Value) return false;
            }
            if (requirement.MinTotal.HasValue && total < requirement.MinTotal.Value) return false;
            return true;
        }

        /// <summary>Pick the class for an actor, or null if nothing matches.</summary>
        public static ClassDef? Resolve(IEnumerable<ClassDef> candidates, Element innate, ElementTable<int> counts)
        {
            ClassDef? best = null;
            int bestSpecificity = -1;

            foreach (var candidate in candidates)
            {
                if (candidate.Innate.Count > 0 && !candidate.Innate.Contains(innate)) continue;
                if (!Matches(candidate.Requires, counts)) continue;

                int specificity = RequirementSpecificity(candidate.Requires);
                if (best == null)
                {
                    best = candidate;
                    bestSpecificity = specificity;
                    continue;
                }

                if (candidate.Priority > best.Priority)
                {
                    best = candidate;
                    bestSpecificity = specificity;
                }
                else if (candidate.Priority == best.Priority)
                {
                    bool moreSpecific = specificity > bestSpecificity;
                    bool tieBrokenById = specificity == bestSpecificity
                        && string.CompareOrdinal(candidate.Id, best.Id) < 0;
                    if (moreSpecific || tieBrokenById)
                    {
                        best = candidate;
                        bestSpecificity = specificity;
                    }
                }
            }
            return best;
        }

        /// <summary>Arts a class grants at or below the given level, in grant order.</summary>
        public static List<string> ArtsAtLevel(ClassDef? classDef, int level)
        {
            var arts = new List<string>();
            if (classDef == null) return arts;
            foreach (var grant in classDef.Arts)
            {
                if (grant.Level <= level) arts.Add(grant.ArtId);
            }
            return arts;
        }
    }
}
