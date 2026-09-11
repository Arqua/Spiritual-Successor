using UnityEngine;
using Aetherlight.Presentation;

namespace Aetherlight.Unity
{
    /// <summary>
    /// Scene-wide feedback: the art-name banner and the summon flash.
    ///
    /// Built from TextMesh and SpriteRenderer rather than uGUI so the package
    /// carries no dependency beyond the engine itself. Swap it for a Canvas
    /// implementation freely - the director only calls Apply.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        [Header("Banner")]
        [SerializeField] private TextMesh? bannerText = null;
        [SerializeField] private Transform? bannerRoot = null;

        [Header("Screen flash")]
        [Tooltip("A quad parented to the camera, covering the view.")]
        [SerializeField] private SpriteRenderer? flashOverlay = null;
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField] private float flashMaxAlpha = 0.55f;

        [Header("Banner tones")]
        [SerializeField] private Color neutral = new Color(0.81f, 0.78f, 0.91f);
        [SerializeField] private Color good = new Color(0.5f, 0.89f, 0.6f);
        [SerializeField] private Color bad = new Color(0.94f, 0.46f, 0.48f);
        [SerializeField] private Color grand = new Color(1f, 0.83f, 0.28f);

        public void Apply(SceneRenderState scene)
        {
            ApplyBanner(scene);
            ApplyFlash(scene);
        }

        private void ApplyBanner(SceneRenderState scene)
        {
            if (bannerRoot != null && bannerRoot.gameObject.activeSelf != scene.HasBanner)
            {
                bannerRoot.gameObject.SetActive(scene.HasBanner);
            }
            if (!scene.HasBanner || bannerText == null) return;

            bannerText.text = scene.BannerText ?? "";

            // Slide in, hold, fade out - all derived from the step's own
            // progress, so a banner never outlives the beat that raised it.
            float t = Mathf.Clamp01((float)scene.BannerT);
            float alpha = t < 0.15f ? t / 0.15f : t > 0.8f ? (1f - t) / 0.2f : 1f;

            Color tone = ToneColor(scene.BannerTone);
            bannerText.color = new Color(tone.r, tone.g, tone.b, Mathf.Clamp01(alpha));
        }

        private void ApplyFlash(SceneRenderState scene)
        {
            if (flashOverlay == null) return;
            float alpha = Mathf.Clamp01((float)scene.ScreenFlash) * flashMaxAlpha;
            flashOverlay.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
            if (flashOverlay.enabled != alpha > 0f) flashOverlay.enabled = alpha > 0f;
        }

        private Color ToneColor(BannerTone tone) => tone switch
        {
            BannerTone.Good => good,
            BannerTone.Bad => bad,
            BannerTone.Grand => grand,
            _ => neutral,
        };
    }
}
