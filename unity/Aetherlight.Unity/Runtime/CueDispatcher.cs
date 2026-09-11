using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherlight.Unity
{
    /// <summary>
    /// Maps the timeline's named sound cues onto clips.
    ///
    /// The rules layer emits opaque strings - "hit", "summon" - and never knows
    /// what they sound like. That indirection is why the same events drive a
    /// silent headless simulation and a full mix.
    /// </summary>
    public sealed class CueDispatcher : MonoBehaviour
    {
        [Serializable]
        public sealed class CueBinding
        {
            public string Cue = "";
            public AudioClip? Clip = null;
        }

        [SerializeField] private AudioSource? source = null;
        [SerializeField] private List<CueBinding> bindings = new List<CueBinding>();

        private readonly Dictionary<string, AudioClip> _byCue = new Dictionary<string, AudioClip>();
        private bool _built;

        private void Awake() => Build();

        private void Build()
        {
            if (_built) return;
            _built = true;
            foreach (var binding in bindings)
            {
                if (!string.IsNullOrEmpty(binding.Cue) && binding.Clip != null) _byCue[binding.Cue] = binding.Clip;
            }
            if (source == null) source = GetComponent<AudioSource>();
        }

        /// <summary>
        /// Play a cue. An unbound cue is silently ignored rather than warning:
        /// a project that has not wired audio yet should not flood the console
        /// on every hit.
        /// </summary>
        public void Play(string cue)
        {
            Build();
            if (source == null) return;
            if (_byCue.TryGetValue(cue, out var clip)) source.PlayOneShot(clip);
        }
    }
}
