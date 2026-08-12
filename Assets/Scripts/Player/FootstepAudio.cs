namespace VexDesigner.Player
{
    using UnityEngine;

    /// <summary>
    /// Plays a footstep every time the player has covered a stride.
    ///
    /// Triggered by distance travelled rather than on a timer, so steps stay
    /// in sync when the player speeds up, slows down, or walks into a wall and
    /// stops moving while still holding W.
    ///
    /// The clips are synthesised at startup rather than shipped as audio
    /// files: a footstep is a short burst of filtered noise, four of them cost
    /// well under a megabyte of RAM, and it keeps the repository free of
    /// binary audio assets with their own licensing questions.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FootstepAudio : MonoBehaviour
    {
        [Tooltip("Metres between footfalls. About 0.7 m is a natural walking " +
                 "stride for an adult.")]
        [SerializeField] private float strideLength = 0.7f;

        [SerializeField, Range(0f, 1f)] private float volume = 0.35f;

        [Tooltip("Random pitch spread. Identical repeated steps are one of the " +
                 "most fatiguing sounds in a game.")]
        [SerializeField, Range(0f, 0.3f)] private float pitchVariation = 0.12f;

        [SerializeField] private int variantCount = 4;

        private FirstPersonController player;
        private AudioSource source;
        private AudioClip[] clips;
        private float accumulated;
        private int lastPlayed = -1;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            player = GetComponentInParent<FirstPersonController>();
            clips = new AudioClip[variantCount];
            for (int i = 0; i < variantCount; i++)
            {
                clips[i] = BuildFootstepClip(i);
            }
        }

        private void Update()
        {
            if (player == null || !player.IsGrounded)
            {
                return;
            }

            accumulated += player.DistanceTravelledThisFrame;
            if (accumulated < strideLength)
            {
                return;
            }

            accumulated = 0f;
            Play();
        }

        private void Play()
        {
            if (clips == null || clips.Length == 0)
            {
                return;
            }

            // Never the same clip twice running; a repeat is far more
            // noticeable than any individual clip's character.
            int index = Random.Range(0, clips.Length);
            if (clips.Length > 1 && index == lastPlayed)
            {
                index = (index + 1) % clips.Length;
            }
            lastPlayed = index;

            source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clips[index], volume);
        }

        /// <summary>
        /// A footstep on concrete: a sharp noise transient that decays fast,
        /// low-pass filtered so it reads as a dull scuff rather than a hiss.
        /// </summary>
        private static AudioClip BuildFootstepClip(int seed)
        {
            const int sampleRate = 44100;
            const float duration = 0.16f;
            int sampleCount = Mathf.RoundToInt(sampleRate * duration);

            var random = new System.Random(seed * 7919);
            var samples = new float[sampleCount];

            // One-pole low pass, carried across samples.
            float filtered = 0f;
            float cutoff = 0.16f + ((float)random.NextDouble() * 0.08f);

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleCount;

                float noise = ((float)random.NextDouble() * 2f) - 1f;
                filtered += (noise - filtered) * cutoff;

                // Fast attack, exponential decay. The brief click at the start
                // is what makes it read as an impact rather than a whoosh.
                float attack = Mathf.Clamp01(t / 0.02f);
                float decay = Mathf.Exp(-t * 22f);

                samples[i] = filtered * attack * decay;
            }

            var clip = AudioClip.Create($"Footstep_{seed}", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
