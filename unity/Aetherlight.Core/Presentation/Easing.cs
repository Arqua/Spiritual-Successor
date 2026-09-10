using System;

namespace Aetherlight.Presentation
{
    /// <summary>
    /// Easing curves: pure functions from normalized time (0..1) to normalized
    /// progress. No engine dependency, so these port unchanged.
    /// </summary>
    public static class Easing
    {
        private static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

        public static double Linear(double t) => Clamp01(t);

        public static double InQuad(double t)
        {
            double x = Clamp01(t);
            return x * x;
        }

        public static double OutQuad(double t)
        {
            double x = Clamp01(t);
            return 1 - (1 - x) * (1 - x);
        }

        public static double InOutQuad(double t)
        {
            double x = Clamp01(t);
            return x < 0.5 ? 2 * x * x : 1 - Math.Pow(-2 * x + 2, 2) / 2;
        }

        public static double OutCubic(double t) => 1 - Math.Pow(1 - Clamp01(t), 3);

        /// <summary>Overshoots past 1 then settles. Good for impacts and pop-in.</summary>
        public static double OutBack(double t)
        {
            double x = Clamp01(t);
            const double c1 = 1.70158;
            const double c3 = c1 + 1;
            return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
        }

        /// <summary>Decaying bounce, for damage numerals and landing.</summary>
        public static double OutBounce(double t)
        {
            double x = Clamp01(t);
            const double n1 = 7.5625;
            const double d1 = 2.75;

            if (x < 1 / d1) return n1 * x * x;
            if (x < 2 / d1) { x -= 1.5 / d1; return n1 * x * x + 0.75; }
            if (x < 2.5 / d1) { x -= 2.25 / d1; return n1 * x * x + 0.9375; }
            x -= 2.625 / d1;
            return n1 * x * x + 0.984375;
        }

        /// <summary>A single arc up and back down. Peaks at t = 0.5.</summary>
        public static double Arc(double t) => Math.Sin(Clamp01(t) * Math.PI);

        public static double Lerp(double from, double to, double t) => from + (to - from) * t;
    }
}
