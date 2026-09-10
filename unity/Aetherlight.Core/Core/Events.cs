using System.Collections.Generic;
using Aetherlight.Domain;

namespace Aetherlight.Core
{
    /// <summary>
    /// The rules layer never draws anything. It emits a flat, ordered list of
    /// events describing what happened, and the presentation layer replays that
    /// list as animation. This is what makes the same core usable from a Unity
    /// renderer, a text client, or a headless balance simulation.
    ///
    /// Modelled as a class hierarchy rather than a tagged struct so callers can
    /// pattern-match, matching the discriminated union on the TypeScript side.
    /// </summary>
    public abstract class GameEvent
    {
    }

    public sealed class RoundStartEvent : GameEvent { public int Round; }
    public sealed class RoundEndEvent : GameEvent { public int Round; }
    public sealed class TurnStartEvent : GameEvent { public string CombatantId = ""; }
    public sealed class TurnSkippedEvent : GameEvent { public string CombatantId = ""; public string Reason = ""; }

    public sealed class ActionDeclaredEvent : GameEvent
    {
        public string CombatantId = "";
        public string Action = "";
        public List<string> TargetIds = new List<string>();
    }

    public sealed class DamageEvent : GameEvent
    {
        public string? SourceId;
        public string TargetId = "";
        public double Amount;
        public Element? Element;
        public bool Critical;
        /// <summary>The affinity multiplier that applied, for UI feedback.</summary>
        public double Effective;
    }

    public sealed class HealEvent : GameEvent { public string? SourceId; public string TargetId = ""; public double Amount; }
    public sealed class AetherSpentEvent : GameEvent { public string CombatantId = ""; public double Amount; }
    public sealed class AetherRestoredEvent : GameEvent { public string CombatantId = ""; public double Amount; }
    public sealed class MissEvent : GameEvent { public string SourceId = ""; public string TargetId = ""; }

    public sealed class StatusAppliedEvent : GameEvent { public string TargetId = ""; public string StatusId = ""; public double Duration; }
    public sealed class StatusResistedEvent : GameEvent { public string TargetId = ""; public string StatusId = ""; }
    public sealed class StatusExpiredEvent : GameEvent { public string TargetId = ""; public string StatusId = ""; }

    public sealed class StatStageChangedEvent : GameEvent { public string TargetId = ""; public string Stat = ""; public double Delta; }

    public sealed class MoteUnleashedEvent : GameEvent { public string CombatantId = ""; public string MoteId = ""; public Element Element; }
    public sealed class MoteRecoveredEvent : GameEvent { public string CombatantId = ""; public string MoteId = ""; }
    public sealed class ClassChangedEvent : GameEvent { public string CombatantId = ""; public string? FromClassId; public string ToClassId = ""; }
    public sealed class SummonCalledEvent : GameEvent { public string CombatantId = ""; public string SummonId = ""; public int MotesSpent; }

    public sealed class DownedEvent : GameEvent { public string CombatantId = ""; }
    public sealed class RevivedEvent : GameEvent { public string CombatantId = ""; public double Hp; }

    public sealed class FleeSucceededEvent : GameEvent { public string Side = ""; }
    public sealed class FleeFailedEvent : GameEvent { public string Side = ""; }

    public enum BattleOutcome { Victory, Defeat, Fled }

    public sealed class BattleEndedEvent : GameEvent { public BattleOutcome Outcome; }
    public sealed class RewardEvent : GameEvent { public long Xp; public long Coin; public List<string> ItemIds = new List<string>(); }
    public sealed class MessageEvent : GameEvent { public string Text = ""; }

    public sealed class FieldEffectEvent : GameEvent
    {
        public string AbilityId = "";
        public int OriginX;
        public int OriginY;
        public List<string> TargetIds = new List<string>();
    }

    /// <summary>Accumulator used by the resolver; also handy in tests.</summary>
    public sealed class EventLog
    {
        private readonly List<GameEvent> _events = new List<GameEvent>();

        public void Push(GameEvent e) => _events.Add(e);

        public IReadOnlyList<GameEvent> Events => _events;

        public int Count => _events.Count;

        public List<GameEvent> Drain()
        {
            var drained = new List<GameEvent>(_events);
            _events.Clear();
            return drained;
        }
    }
}
