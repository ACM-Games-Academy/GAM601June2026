using UnityEngine;

// ProceduralAudioClips
//
// Generates simple placeholder sound effects entirely in code, with no
// audio asset required — analogous to how this project's visual effects
// (PulseGlowEffect, WrongAnswerWaveEffect) generate their own textures
// at runtime. These are stand-ins: swap in an authored AudioClip on the
// relevant SoundEffectManager entry whenever one's ready and it takes
// priority automatically, no code changes needed.

public static class ProceduralAudioClips
{
    private const int SampleRate = 44100;

    // A short cascade of soft, decaying high bell tones across a
    // pentatonic scale — a simple stand-in for a "magical tinkling"
    // sparkle sound.
    public static AudioClip GenerateMagicalTinkle()
    {
        // Pentatonic scale around C6-C7, for a bright, twinkly character
        // rather than one flat tone.
        float[] noteFrequencies = { 1046.5f, 1318.5f, 1568.0f, 1760.0f, 2093.0f };
        float[] noteStartTimes = { 0f, 0.09f, 0.17f, 0.24f, 0.33f };
        const float noteDuration = 0.6f; // how long each note rings out

        float clipLength = noteStartTimes[noteStartTimes.Length - 1] + noteDuration;
        int totalSamples = Mathf.CeilToInt(clipLength * SampleRate);
        float[] samples = new float[totalSamples];

        for (int n = 0; n < noteFrequencies.Length; n++)
        {
            int startSample = Mathf.RoundToInt(noteStartTimes[n] * SampleRate);
            int noteSamples = Mathf.RoundToInt(noteDuration * SampleRate);

            for (int i = 0; i < noteSamples; i++)
            {
                int sampleIndex = startSample + i;
                if (sampleIndex >= totalSamples) break;

                float t = i / (float)SampleRate;

                // Fundamental plus a couple of soft overtones for a
                // bell-like timbre rather than a plain sine tone.
                float fundamental = Mathf.Sin(2f * Mathf.PI * noteFrequencies[n] * t);
                float overtone1 = 0.5f * Mathf.Sin(2f * Mathf.PI * noteFrequencies[n] * 2.01f * t);
                float overtone2 = 0.25f * Mathf.Sin(2f * Mathf.PI * noteFrequencies[n] * 3.03f * t);

                // Fast attack, exponential decay — a pluck/chime shape
                float attack = Mathf.Clamp01(t / 0.01f);
                float decay = Mathf.Exp(-t * 5f);
                float envelope = attack * decay;

                samples[sampleIndex] += (fundamental + overtone1 + overtone2) * envelope * 0.3f;
            }
        }

        // Normalize so overlapping notes never clip above 1.0
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        }
        if (peak > 1f)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] /= peak;
            }
        }

        AudioClip clip = AudioClip.Create("MagicalTinkle_Procedural", totalSamples, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // A celebratory "angelic chord swell" — several slightly detuned
    // sine voices stacked into a bright major chord, with a slow
    // swell-in, a long tail, and a gentle vibrato. Real choirs have
    // vocal formants and harmonic complexity no sine synth can fake
    // convincingly; stacking detuned voices ("chorus") is the standard
    // cheap trick for a synthetic pad/choir feel, and stands in fine
    // until an authored recording replaces it.
    public static AudioClip GenerateAngelicChordSwell()
    {
        // A bright major chord (G major: root, third, fifth, octave)
        // in a mid-high register, for a celebratory, "heavenly" tone.
        float[] chordFrequencies = { 392.00f, 493.88f, 587.33f, 783.99f };

        const float clipLength = 3f;
        const float attackTime = 0.4f;    // slow swell in
        const float releaseTime = 1.6f;   // long tail out
        const float vibratoRate = 5.5f;   // Hz
        const float vibratoDepth = 0.006f; // fraction of frequency

        int totalSamples = Mathf.CeilToInt(clipLength * SampleRate);
        float[] samples = new float[totalSamples];

        // Three copies of each chord tone, each detuned by a few cents,
        // give the "many voices" chorus character.
        float[] detuneCents = { -6f, 0f, 6f };

        foreach (float baseFrequency in chordFrequencies)
        {
            foreach (float cents in detuneCents)
            {
                float detunedFrequency = baseFrequency * Mathf.Pow(2f, cents / 1200f);

                // Accumulated phase (rather than sin(2*pi*f*t) directly)
                // so the vibrato's frequency modulation stays continuous
                // and click-free from sample to sample.
                float phase = 0f;

                for (int i = 0; i < totalSamples; i++)
                {
                    float t = i / (float)SampleRate;

                    float vibrato = 1f + vibratoDepth * Mathf.Sin(2f * Mathf.PI * vibratoRate * t);
                    phase += detunedFrequency * vibrato / SampleRate;

                    float wave = Mathf.Sin(2f * Mathf.PI * phase);

                    float envelope;
                    if (t < attackTime)
                    {
                        envelope = t / attackTime;
                    }
                    else if (t > clipLength - releaseTime)
                    {
                        envelope = Mathf.Clamp01((clipLength - t) / releaseTime);
                    }
                    else
                    {
                        envelope = 1f;
                    }
                    envelope = envelope * envelope * (3f - 2f * envelope); // smoothstep the swell

                    samples[i] += wave * envelope;
                }
            }
        }

        // Normalize with headroom — this is a sustained chord, not a
        // single transient, so leave it a little quieter than full scale
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        }
        if (peak > 0f)
        {
            const float targetPeak = 0.6f;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = samples[i] / peak * targetPeak;
            }
        }

        AudioClip clip = AudioClip.Create("AngelicChordSwell_Procedural", totalSamples, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // A short, somber two-note minor chord — a simple negative-feedback
    // cue for a wrong puzzle answer. Root + minor third (A3/C4), quick
    // attack, moderate exponential decay.
    public static AudioClip GenerateWrongAnswerChord()
    {
        float[] noteFrequencies = { 220.00f, 261.63f }; // A3, C4 — minor third

        const float clipLength = 0.8f;
        const float attackTime = 0.02f;
        const float decayRate = 4f;

        int totalSamples = Mathf.CeilToInt(clipLength * SampleRate);
        float[] samples = new float[totalSamples];

        foreach (float frequency in noteFrequencies)
        {
            for (int i = 0; i < totalSamples; i++)
            {
                float t = i / (float)SampleRate;

                float fundamental = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float overtone = 0.35f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t);

                float attack = Mathf.Clamp01(t / attackTime);
                float decay = Mathf.Exp(-t * decayRate);
                float envelope = attack * decay;

                samples[i] += (fundamental + overtone) * envelope * 0.45f;
            }
        }

        // Normalize so the two overlapping notes never clip above 1.0
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        }
        if (peak > 1f)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] /= peak;
            }
        }

        AudioClip clip = AudioClip.Create("WrongAnswerChord_Procedural", totalSamples, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
