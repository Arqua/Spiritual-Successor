using System.Collections.Generic;
using UnityEngine;
using Aetherlight.Field;
using Aetherlight.Presentation;

namespace Aetherlight.Unity
{
    /// <summary>
    /// One combatant on screen.
    ///
    /// Holds no timing logic of its own. Every frame the director hands it the
    /// folded render state for its track and it applies those values - pose,
    /// flash, shake, opacity, lunge - to a sprite. Anything that looks like
    /// animation timing belongs in the timeline, not here.
    ///
    /// The sprite is a placeholder-friendly SpriteRenderer: swap the sheet and
    /// wire `PoseSprites` and the rest of the pipeline is unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatantView : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Combatant id from the battle state, e.g. party:rell")]
        public string CombatantId = "";

        [Header("Rendering")]
        [SerializeField] private SpriteRenderer? sprite = null;
        [SerializeField] private Transform? spritePivot = null;
        [SerializeField] private Color baseTint = Color.white;

        [Header("Vitals")]
        [SerializeField] private Transform? hpBarFill = null;
        [SerializeField] private Transform? aetherBarFill = null;
        [SerializeField] private TextMesh? nameLabel = null;

        [Header("Numerals")]
        [SerializeField] private FloatingNumeral? numeralPrefab = null;
        [SerializeField] private Transform? numeralAnchor = null;
        [SerializeField] private int numeralPoolSize = 6;

        [Header("Feel")]
        [SerializeField] private float shakeScale = 0.06f;
        [SerializeField] private float lungeScale = 0.35f;
        [SerializeField] private bool idleBob = true;

        private readonly List<FloatingNumeral> _numeralPool = new List<FloatingNumeral>();
        private Vector3 _homePosition;
        private double _bobPhase;
        private bool _initialised;

        /// <summary>Where this view sits when it is not lunging. Set by the director.</summary>
        public Vector3 HomePosition
        {
            get => _homePosition;
            set
            {
                _homePosition = value;
                if (!_initialised) transform.position = value;
            }
        }

        private void Awake()
        {
            EnsureInitialised();
        }

        private void EnsureInitialised()
        {
            if (_initialised) return;
            _initialised = true;

            if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
            _bobPhase = Sprites.PhaseFromId(string.IsNullOrEmpty(CombatantId) ? name : CombatantId);

            if (numeralPrefab != null)
            {
                for (int i = 0; i < numeralPoolSize; i++)
                {
                    var numeral = Object.Instantiate(numeralPrefab);
                    numeral.transform.SetParent(transform, false);
                    numeral.SetVisible(false);
                    _numeralPool.Add(numeral);
                }
            }
        }

        public void SetName(string displayName)
        {
            if (nameLabel != null) nameLabel.text = displayName;
        }

        /// <summary>
        /// Apply one frame.
        ///
        /// `lungeTargetPosition` is the world position of whatever this
        /// combatant is lunging at; the director resolves it, because a view
        /// has no business knowing about other views.
        /// </summary>
        public void Apply(
            EntityRenderState state,
            double timeMs,
            Transform? cameraTransform,
            Vector3? lungeTargetPosition,
            NumeralPalette palette,
            Facing facing)
        {
            EnsureInitialised();

            Vector3 position = _homePosition;

            // Lunge toward the target and back, driven by the folded step.
            if (lungeTargetPosition.HasValue && state.LungeAmount > 0)
            {
                Vector3 toTarget = lungeTargetPosition.Value - _homePosition;
                position = _homePosition + toTarget * (lungeScale * (float)state.LungeAmount);
            }

            // Shake is a positional offset, not a rotation: rotating a
            // billboarded sprite fights the billboard.
            position = position + new Vector3((float)state.ShakeOffset * shakeScale, 0f, 0f);

            if (idleBob && state.Pose != "down")
            {
                position = position + new Vector3(0f, (float)Sprites.IdleBob(timeMs, _bobPhase) * 0.01f, 0f);
            }

            transform.position = position;

            // Y-axis billboard: keep the sprite upright and facing the camera.
            // Full billboarding would tip it and lift the feet off the ground.
            if (cameraTransform != null && spritePivot != null)
            {
                spritePivot.rotation = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);
            }

            if (sprite != null)
            {
                Color tint = baseTint;

                // Impact flash paints toward white over the whole sprite.
                if (state.FlashIntensity > 0)
                {
                    tint = Color.Lerp(tint, Color.white, Mathf.Clamp01((float)state.FlashIntensity));
                }

                sprite.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01((float)state.Opacity));

                // With 8-directional art, pick the row here. With a single
                // direction, flipX is enough to suggest facing.
                var direction = Sprites.SpriteDirection(facing, cameraTransform != null ? cameraTransform.eulerAngles.y : 0f);
                sprite.flipX = direction == Direction.W || direction == Direction.NW || direction == Direction.SW;
            }

            ApplyBars(state);
            ApplyNumerals(state, palette);
        }

        private void ApplyBars(EntityRenderState state)
        {
            foreach (var bar in state.Bars)
            {
                var fill = bar.Stat == "hp" ? hpBarFill : aetherBarFill;
                if (fill == null) continue;
                float fraction = bar.Max > 0 ? Mathf.Clamp01((float)(bar.Value / bar.Max)) : 0f;
                var scale = fill.localScale;
                fill.localScale = new Vector3(fraction, scale.y, scale.z);
            }
        }

        /// <summary>
        /// Match the folded numerals onto pooled views by index.
        ///
        /// Pooled rather than spawned per frame: the folded state is recomputed
        /// every frame, so instantiating from it would create and destroy a
        /// numeral sixty times a second for the life of the step.
        /// </summary>
        private void ApplyNumerals(EntityRenderState state, NumeralPalette palette)
        {
            for (int i = 0; i < _numeralPool.Count; i++)
            {
                var view = _numeralPool[i];
                if (i < state.Numerals.Count)
                {
                    var numeral = state.Numerals[i];
                    Vector3 anchor = numeralAnchor != null ? numeralAnchor.position : transform.position;
                    view.SetVisible(true);
                    view.Apply(numeral.Text, palette.For(numeral.Style), numeral.T, anchor, i);
                }
                else
                {
                    view.SetVisible(false);
                }
            }
        }

        /// <summary>Reset to a clean state, for reuse between battles.</summary>
        public void ResetView()
        {
            EnsureInitialised();
            if (sprite != null) sprite.color = baseTint;
            foreach (var numeral in _numeralPool) numeral.SetVisible(false);
            transform.position = _homePosition;
        }
    }

    /// <summary>
    /// Numeral colours, exposed on the director so a project can restyle
    /// feedback without touching renderer code.
    /// </summary>
    [System.Serializable]
    public sealed class NumeralPalette
    {
        public Color Damage = Color.white;
        public Color Critical = new Color(1f, 0.83f, 0.28f);
        public Color Strong = new Color(1f, 0.62f, 0.29f);
        public Color Weak = new Color(0.62f, 0.58f, 0.72f);
        public Color Heal = new Color(0.44f, 0.88f, 0.55f);
        public Color Miss = new Color(0.73f, 0.69f, 0.82f);
        public Color Status = new Color(0.78f, 0.55f, 0.91f);
        public Color Aether = new Color(0.39f, 0.71f, 0.96f);

        public Color For(NumeralStyle style) => style switch
        {
            NumeralStyle.Critical => Critical,
            NumeralStyle.Strong => Strong,
            NumeralStyle.Weak => Weak,
            NumeralStyle.Heal => Heal,
            NumeralStyle.Miss => Miss,
            NumeralStyle.Status => Status,
            NumeralStyle.Aether => Aether,
            _ => Damage,
        };
    }
}
