using System;
using System.Collections.Generic;

namespace Aetherlight.Domain
{
    public enum Element
    {
        Terra = 0,
        Pyre = 1,
        Aeris = 2,
        Rime = 3,
    }

    /// <summary>
    /// The four elemental affinities and how they interact.
    ///
    /// Affinity is not a rock-paper-scissors chart. Every actor carries a power
    /// and a resist value per element; effectiveness is the gap between the
    /// attacker's power and the defender's resistance. Opposed elements add a
    /// flat separation on top, so an actor aligned to one element is naturally
    /// soft against its opposite with no pairwise table.
    /// </summary>
    public static class Elements
    {
        public static readonly Element[] All = { Element.Terra, Element.Pyre, Element.Aeris, Element.Rime };

        public static Element Opposed(Element element) => element switch
        {
            Element.Terra => Element.Aeris,
            Element.Aeris => Element.Terra,
            Element.Pyre => Element.Rime,
            Element.Rime => Element.Pyre,
            _ => throw new ArgumentOutOfRangeException(nameof(element)),
        };

        public static bool TryParse(string? value, out Element element)
        {
            switch (value?.ToLowerInvariant())
            {
                case "terra": element = Element.Terra; return true;
                case "pyre": element = Element.Pyre; return true;
                case "aeris": element = Element.Aeris; return true;
                case "rime": element = Element.Rime; return true;
                default: element = default; return false;
            }
        }

        public static string ToId(this Element element) => element switch
        {
            Element.Terra => "terra",
            Element.Pyre => "pyre",
            Element.Aeris => "aeris",
            Element.Rime => "rime",
            _ => throw new ArgumentOutOfRangeException(nameof(element)),
        };

        public static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        /// <summary>
        /// Effectiveness multiplier for an attack of <paramref name="element"/>.
        /// 1.0 is neutral. <paramref name="defenderInnate"/> is the defender's own
        /// alignment; striking the element that opposes it counts as extra power.
        /// </summary>
        public static double AffinityFactor(
            Element element,
            double attackerPower,
            double defenderResist,
            Element? defenderInnate = null,
            AffinityConfig? config = null)
        {
            var cfg = config ?? AffinityConfig.Default;
            double opposedEdge = defenderInnate.HasValue && Opposed(defenderInnate.Value) == element
                ? cfg.OpposedBonus
                : 0;
            double gap = attackerPower + opposedEdge - defenderResist;
            return Clamp(1 + gap / cfg.Divisor, cfg.MinFactor, cfg.MaxFactor);
        }
    }

    /// <summary>
    /// Tuning constants for affinity maths.
    ///
    /// These are plain settable properties rather than init-only ones on
    /// purpose: `init` requires System.Runtime.CompilerServices.IsExternalInit,
    /// which netstandard2.1 does not define. The usual workaround is to declare
    /// the shim yourself, but inside Unity that risks colliding with a
    /// definition from another assembly depending on the editor version. Plain
    /// setters cost nothing here and keep the library portable across every
    /// Unity release that consumes netstandard2.1.
    /// </summary>
    public sealed class AffinityConfig
    {
        /// <summary>Larger values flatten the influence of the power/resist gap.</summary>
        public double Divisor { get; set; } = 200;

        /// <summary>Extra effective power when striking an actor's opposed element.</summary>
        public double OpposedBonus { get; set; } = 25;

        /// <summary>Floor, so a heavily resisted hit still lands for something.</summary>
        public double MinFactor { get; set; } = 0.25;

        /// <summary>Ceiling, so stacking power cannot run away.</summary>
        public double MaxFactor { get; set; } = 2.0;

        public static readonly AffinityConfig Default = new AffinityConfig();
    }

    /// <summary>A value carried per element.</summary>
    public struct ElementTable<T>
    {
        private T _terra;
        private T _pyre;
        private T _aeris;
        private T _rime;

        public T this[Element element]
        {
            get => element switch
            {
                Element.Terra => _terra,
                Element.Pyre => _pyre,
                Element.Aeris => _aeris,
                Element.Rime => _rime,
                _ => throw new ArgumentOutOfRangeException(nameof(element)),
            };
            set
            {
                switch (element)
                {
                    case Element.Terra: _terra = value; break;
                    case Element.Pyre: _pyre = value; break;
                    case Element.Aeris: _aeris = value; break;
                    case Element.Rime: _rime = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(element));
                }
            }
        }

        public static ElementTable<T> Filled(T value)
        {
            var table = new ElementTable<T>();
            foreach (var element in Elements.All) table[element] = value;
            return table;
        }

        public ElementTable<T> Copy()
        {
            var table = new ElementTable<T>();
            foreach (var element in Elements.All) table[element] = this[element];
            return table;
        }

        public IEnumerable<KeyValuePair<Element, T>> Entries()
        {
            foreach (var element in Elements.All) yield return new KeyValuePair<Element, T>(element, this[element]);
        }
    }
}
