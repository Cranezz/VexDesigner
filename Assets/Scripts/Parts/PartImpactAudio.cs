namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Plays an impact sound when a part strikes something.
    ///
    /// Clips are synthesised once per material and shared, rather than shipped
    /// as audio files. A metal impact is a noise transient plus a ringing
    /// partial, which is a few lines of maths, and generating it keeps the
    /// repository free of binary audio assets and their licensing questions.
    ///
    /// The distinction that matters here is aluminium against steel. VEX
    /// structure is aluminium and fasteners are steel, and they ring at
    /// audibly different pitches - a screw that sounds like a C-channel is
    /// wrong in a way people notice without being able to say why.
    /// </summary>
    public sealed class PartImpactAudio : MonoBehaviour
    {
        [Tooltip("Impact speed below which nothing plays, in metres per second. " +
                 "Without a floor, a settling stack chatters continuously as " +
                 "parts micro-collide.")]
        [SerializeField] private float minimumSpeed = 0.25f;

        [Tooltip("Impact speed treated as full volume.")]
        [SerializeField] private float loudSpeed = 2.5f;

        [Tooltip("Seconds before this part may sound again. A single physical " +
                 "impact often generates several contacts.")]
        [SerializeField] private float retriggerDelay = 0.06f;

        private static readonly Dictionary<PartMaterial, AudioClip[]> Cache =
            new Dictionary<PartMaterial, AudioClip[]>();

        private PartMaterial material = PartMaterial.Aluminium;
        private float nextAllowedTime;

        private void Awake()
        {
            var instance = GetComponent<PartInstance>();
            if (instance != null && instance.Definition != null)
            {
                material = instance.Definition.material;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Time.time < nextAllowedTime)
            {
                return;
            }

            float speed = collision.relativeVelocity.magnitude;
            if (speed < minimumSpeed)
            {
                return;
            }

            nextAllowedTime = Time.time + retriggerDelay;

            AudioClip[] clips = GetClips(material);
            AudioClip clip = clips[Random.Range(0, clips.Length)];

            float volume = Mathf.Clamp01(
                Mathf.InverseLerp(minimumSpeed, loudSpeed, speed));

            // Played through a shared pool rather than an AudioSource on every
            // part. A finished robot is hundreds of parts, and hundreds of
            // persistent AudioSources is real overhead for something that is
            // silent almost all of the time.
            Vector3 point = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;

            // Deliberately quiet. These are texture, not events - a workshop
            // where every settling screw announces itself becomes tiring within
            // a minute, and the sound is there to make contact feel real rather
            // than to be noticed.
            ImpactAudioPool.Play(clip, point, volume * 0.16f);
        }

        /// <summary>
        /// A small ring of AudioSources reused for every impact in the scene.
        ///
        /// Sized for the number of impacts that can plausibly overlap - a
        /// dropped handful of screws - not for the number of parts. Older
        /// sounds are cut off when the ring wraps, which is inaudible at these
        /// clip lengths and far cheaper than growing without bound.
        /// </summary>
        private static class ImpactAudioPool
        {
            private const int Size = 12;

            private static AudioSource[] sources;
            private static int next;

            public static void Play(AudioClip clip, Vector3 position, float volume)
            {
                EnsureBuilt();

                AudioSource source = sources[next];
                next = (next + 1) % Size;

                source.transform.position = position;
                source.pitch = Random.Range(0.92f, 1.09f);
                source.PlayOneShot(clip, volume);
            }

            private static void EnsureBuilt()
            {
                // Also rebuilds after a scene load, which destroys the old ring.
                if (sources != null && sources[0] != null)
                {
                    return;
                }

                var root = new GameObject("ImpactAudioPool");
                Object.DontDestroyOnLoad(root);

                sources = new AudioSource[Size];
                for (int i = 0; i < Size; i++)
                {
                    var go = new GameObject($"Impact_{i}");
                    go.transform.SetParent(root.transform, false);

                    AudioSource source = go.AddComponent<AudioSource>();
                    source.playOnAwake = false;

                    // Fully 3D: the sound comes from where the part landed,
                    // which is most of what makes a space feel physical.
                    source.spatialBlend = 1f;
                    source.rolloffMode = AudioRolloffMode.Linear;
                    source.minDistance = 0.4f;
                    source.maxDistance = 14f;

                    sources[i] = source;
                }
            }
        }

        private static AudioClip[] GetClips(PartMaterial material)
        {
            if (Cache.TryGetValue(material, out AudioClip[] cached))
            {
                return cached;
            }

            var clips = new AudioClip[3];
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = Synthesise(material, i);
            }

            Cache[material] = clips;
            return clips;
        }

        /// <summary>
        /// An impact is a broadband transient - the strike - plus a decaying
        /// partial at the object's ringing frequency. Steel rings higher and
        /// longer than aluminium; plastic and rubber barely ring at all.
        /// </summary>
        private static AudioClip Synthesise(PartMaterial material, int variant)
        {
            const int sampleRate = 44100;

            float ringHz;
            float ringDecay;
            float noiseDecay;
            float duration;
            float ringMix;

            switch (material)
            {
                case PartMaterial.Steel:
                    ringHz = 3400f; ringDecay = 16f; noiseDecay = 60f;
                    duration = 0.32f; ringMix = 0.75f;
                    break;

                case PartMaterial.Plastic:
                    ringHz = 1100f; ringDecay = 55f; noiseDecay = 80f;
                    duration = 0.12f; ringMix = 0.35f;
                    break;

                case PartMaterial.Rubber:
                    ringHz = 320f; ringDecay = 90f; noiseDecay = 95f;
                    duration = 0.09f; ringMix = 0.15f;
                    break;

                default: // Aluminium
                    ringHz = 2100f; ringDecay = 26f; noiseDecay = 70f;
                    duration = 0.24f; ringMix = 0.62f;
                    break;
            }

            var random = new System.Random((int)material * 977 + variant * 31);

            // Detune each variant so repeats do not sound mechanical.
            ringHz *= 1f + (((float)random.NextDouble() - 0.5f) * 0.14f);

            int sampleCount = Mathf.RoundToInt(sampleRate * duration);
            var samples = new float[sampleCount];

            // A second partial slightly off the fundamental gives the beating
            // that makes struck metal sound like metal rather than a sine.
            float secondHz = ringHz * 2.71f;
            float filtered = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float phase = 2f * Mathf.PI * t;

                float noise = ((float)random.NextDouble() * 2f) - 1f;
                filtered += (noise - filtered) * 0.45f;

                float transient = filtered * Mathf.Exp(-t * noiseDecay);

                float ring =
                    (Mathf.Sin(phase * ringHz) * Mathf.Exp(-t * ringDecay)) +
                    (Mathf.Sin(phase * secondHz) * Mathf.Exp(-t * ringDecay * 1.7f) * 0.4f);

                samples[i] = Mathf.Clamp(
                    (transient * (1f - ringMix)) + (ring * ringMix * 0.5f), -1f, 1f);
            }

            var clip = AudioClip.Create(
                $"Impact_{material}_{variant}", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
