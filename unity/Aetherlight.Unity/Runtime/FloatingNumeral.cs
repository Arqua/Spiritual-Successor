using UnityEngine;
using Aetherlight.Presentation;

namespace Aetherlight.Unity
{
    /// <summary>
    /// A damage or healing numeral.
    ///
    /// Driven entirely by the timeline's progress value rather than a timer of
    /// its own: the clock lives in Playback, and a numeral that ran on its own
    /// timer would keep rising through a hitstop while everything else froze.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public sealed class FloatingNumeral : MonoBehaviour
    {
        [SerializeField] private float riseDistance = 0.9f;
        [SerializeField] private float fadeStart = 0.75f;

        private TextMesh _text = null!;
        private Vector3 _anchor;

        private void Awake()
        {
            _text = GetComponent<TextMesh>();
        }

        public void Bind(Vector3 anchor)
        {
            _anchor = anchor;
        }

        /// <summary>Apply one frame of the numeral's folded state.</summary>
        public void Apply(string text, Color color, double t, Vector3 anchor, int index)
        {
            if (_text == null) _text = GetComponent<TextMesh>();
            _anchor = anchor;

            float progress = Mathf.Clamp01((float)t);
            _text.text = text;

            // Stagger simultaneous numerals sideways so a multi-hit art does not
            // stack them into an unreadable pile.
            transform.position = _anchor + new Vector3(index * 0.25f, progress * riseDistance, 0f);

            float alpha = progress > fadeStart ? 1f - (progress - fadeStart) / (1f - fadeStart) : 1f;
            _text.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
