using System.Collections.Generic;

namespace Aetherlight.Field
{
    public enum InteractableKind
    {
        /// <summary>Can be picked up, carried, and set down.</summary>
        Liftable,
        /// <summary>Can be pushed one tile at a time.</summary>
        Pushable,
        /// <summary>Toggles between two states.</summary>
        Switch,
        /// <summary>Can be frozen into a solid platform.</summary>
        Freezable,
        /// <summary>Can be grown into a climbable surface.</summary>
        Growable,
        /// <summary>Can be burned away.</summary>
        Burnable,
        /// <summary>Reveals something otherwise invisible.</summary>
        Hidden,
        /// <summary>Reads as scenery until an ability wakes it.</summary>
        Inert,
    }

    public class FieldEntity
    {
        public string Id = "";
        public Vec2 Position;
        /// <summary>Height above the tile, for entities on ledges or in the air.</summary>
        public double Elevation;
        /// <summary>Renderer hint; the rules layer never reads it.</summary>
        public string? SpriteId;
        /// <summary>Blocks movement while present.</summary>
        public bool Blocking;
    }

    /// <summary>
    /// An interactable declares which field abilities it responds to, rather
    /// than an ability knowing what it can affect.
    ///
    /// That inversion is what keeps the puzzle toolkit open: adding a new
    /// liftable rock is content, not code, and a new ability automatically
    /// works on everything that already listens for it.
    /// </summary>
    public sealed class Interactable : FieldEntity
    {
        public InteractableKind Kind;

        /// <summary>Field ability ids this object reacts to.</summary>
        public List<string> RespondsTo = new List<string>();

        /// <summary>Current state, toggled by abilities. Free-form so content owns meaning.</summary>
        public string? State;

        /// <summary>Set once consumed, so one-shot puzzles do not repeat.</summary>
        public bool Spent;

        /// <summary>Elevation the tile becomes when this object is activated.</summary>
        public double? BecomesElevation;

        /// <summary>Terrain the tile becomes when activated.</summary>
        public string? BecomesTerrain;

        /// <summary>Whether activation removes the blocking flag.</summary>
        public bool ClearsBlocking;

        public bool RespondsToAbility(string abilityId) => !Spent && RespondsTo.Contains(abilityId);
    }
}
