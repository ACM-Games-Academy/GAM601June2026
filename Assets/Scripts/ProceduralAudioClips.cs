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

    // A short "dirt scuffing" texture — filtered noise with a bumpy,
    // uneven envelope, like a paw dragging over rough carved stone.
    // Filtered/shaped noise bursts are the standard technique for
    // scrape/scuff/footstep sounds, so this holds up much better than
    // a tonal synth would. variantSeed picks a different duration,
    // filter darkness and bump pattern each time, for the 5-ish
    // distinct variants needed so repeated cell taps don't sound
    // identical. Uses its own System.Random (seeded per variant)
    // rather than UnityEngine.Random, so generating clips never
    // disturbs the game's own random state.
    public static AudioClip GenerateScuffSound(int variantSeed)
    {
        System.Random rng = new System.Random(variantSeed);

        float duration = 0.18f + (float)rng.NextDouble() * 0.12f; // 0.18s-0.30s
        int totalSamples = Mathf.CeilToInt(duration * SampleRate);
        float[] samples = new float[totalSamples];

        // Lower = duller/rougher scrape, higher = brighter/scratchier
        float filterAmount = 0.55f + (float)rng.NextDouble() * 0.25f;

        // A few little bumps within the clip simulate a paw catching on
        // carved grooves as it drags, instead of one smooth noise burst
        int bumpCount = 2 + rng.Next(0, 3); // 2-4 bumps
        float[] bumpTimes = new float[bumpCount];
        for (int b = 0; b < bumpCount; b++)
        {
            bumpTimes[b] = (float)rng.NextDouble() * duration;
        }

        float filteredPrev = 0f;

        for (int i = 0; i < totalSamples; i++)
        {
            float t = i / (float)SampleRate;

            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            float filtered = filteredPrev * filterAmount + raw * (1f - filterAmount);
            filteredPrev = filtered;

            // Quick fade in, gradual fade out across the whole clip
            float baseEnvelope = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((duration - t) / (duration * 0.6f));

            // Extra emphasis near each bump time for an uneven,
            // scrubby texture rather than a flat noise swell
            float bumpEnvelope = 0f;
            foreach (float bumpTime in bumpTimes)
            {
                float distance = t - bumpTime;
                bumpEnvelope += Mathf.Exp(-distance * distance * 900f);
            }

            float envelope = baseEnvelope * (0.5f + 0.5f * Mathf.Clamp01(bumpEnvelope));

            samples[i] = filtered * envelope * 0.5f;
        }

        // Normalize
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

        AudioClip clip = AudioClip.Create("CellScuff_Procedural_" + variantSeed, totalSamples, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // A short, harsh "anger" stinger — two low tones a minor second
    // apart (about as dissonant/grating as an interval gets), hard-
    // clipped for a gritty edge, with a fast amplitude tremolo for a
    // snarling, growl-like texture. Deliberately harsher than the calm
    // WrongAnswerChord, which is just a gentle minor third.
    public static AudioClip GenerateAngerStinger()
    {
        float[] frequencies = { 98.00f, 103.83f }; // G2, Ab2 — minor second

        const float clipLength = 0.6f;
        const float attackTime = 0.015f;
        const float decayRate = 3.5f;
        const float growlRate = 18f; // Hz — fast tremolo for a snarling texture

        int totalSamples = Mathf.CeilToInt(clipLength * SampleRate);
        float[] samples = new float[totalSamples];

        foreach (float frequency in frequencies)
        {
            for (int i = 0; i < totalSamples; i++)
            {
                float t = i / (float)SampleRate;

                float fundamental = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float overtone = 0.4f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t);
                float raw = fundamental + overtone;

                // Hard-clip for a grittier, more aggressive edge than a
                // clean sine
                float clipped = Mathf.Clamp(raw * 1.6f, -1f, 1f);

                float attack = Mathf.Clamp01(t / attackTime);
                float decay = Mathf.Exp(-t * decayRate);
                float growl = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * growlRate * t);
                float envelope = attack * decay * growl;

                samples[i] += clipped * envelope * 0.4f;
            }
        }

        // Normalize to full scale (not just clamped down if it happened
        // to exceed 1.0) — the growl tremolo and hard-clip shaping above
        // otherwise leave this well under other effects' peak loudness,
        // even with its own Volume slider maxed out in the Inspector.
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        }
        if (peak > 0f)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] /= peak;
            }
        }

        AudioClip clip = AudioClip.Create("AngerStinger_Procedural", totalSamples, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // A cat purr — a low rumbling tone modulated at roughly 25-30Hz,
    // which is genuinely how real purrs work physically (a cat's
    // laryngeal muscles twitch at that rate), plus a little filtered
    // noise underneath for breathy texture. Short enough to read as a
    // brief purr rather than dragging on — callers needing a longer
    // sustained sound should use SoundEffectManager.PlaySustainedSound
    // so it can be cut short on player input instead of relying on
    // clip length alone.
    public static AudioClip GenerateCatPurr()
    {
        const float clipLength = 1.1f;
        const float rumbleFrequency = 70f;      // low "engine" tone
        const float purrModulationRate = 26f;   // Hz — the buzzing vibration that makes it read as a purr

        // Overall volume "swell": soft-ish at the start, peaking at the
        // midpoint, soft-ish again at the end — minEnvelope keeps it
        // from ever going all the way to silent, so it reads as a
        // natural swell rather than a fade in/out.
        const float minEnvelope = 0.45f;
        const float clickGuardTime = 0.02f; // brief true fade right at the very edges, to avoid a sample-0 pop

        int totalSamples = Mathf.CeilToInt(clipLength * SampleRate);
        float[] samples = new float[totalSamples];

        System.Random rng = new System.Random(7);
        float filteredNoisePrev = 0f;

        for (int i = 0; i < totalSamples; i++)
        {
            float t = i / (float)SampleRate;

            // Low rumbling tone plus a soft overtone for body
            float rumble = Mathf.Sin(2f * Mathf.PI * rumbleFrequency * t);
            rumble += 0.3f * Mathf.Sin(2f * Mathf.PI * rumbleFrequency * 1.5f * t);

            // The fast amplitude modulation is what makes this sound
            // like a purr rather than a plain hum
            float purrModulation = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * purrModulationRate * t);

            // Soft filtered noise underneath for breathy texture
            float rawNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float filteredNoise = filteredNoisePrev * 0.85f + rawNoise * 0.15f;
            filteredNoisePrev = filteredNoise;

            float raw = rumble * 0.7f + filteredNoise * 0.25f;
            float voiced = raw * purrModulation;

            // Smooth arc across the whole clip: 0 at the very start/end,
            // 1 at the midpoint, then lifted so it never dips below
            // minEnvelope instead of fading all the way to silence.
            float swell = Mathf.Sin(Mathf.PI * (t / clipLength));
            float envelope = Mathf.Lerp(minEnvelope, 1f, swell);

            // True fade only in the last few milliseconds at each edge,
            // purely to avoid an audible click at the first/last sample.
            if (t < clickGuardTime)
            {
                envelope *= t / clickGuardTime;
            }
            else if (t > clipLength - clickGuardTime)
            {
                envelope *= Mathf.Clamp01((clipLength - t) / clickGuardTime);
            }

            samples[i] = voiced * envelope * 0.5f;
        }

        // Normalize
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

        AudioClip clip = AudioClip.Create("CatPurr_Procedural", totalSamples, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // A short, bright "happy meow" — pitch rises quickly to a peak then
    // eases back down (the classic up-then-down meow inflection), with
    // a little vibrato for character. Uses phase accumulation (rather
    // than sin(2*pi*f*t) directly) so the pitch slide stays continuous
    // and click-free, same technique as the choir swell's vibrato.
    //
    // variantSeed varies the pitch range, where the peak lands, vibrato
    // and duration, so a batch of these (see SoundEffectManager's
    // "HappyMeow" registration) don't all sound identical — same idea
    // as GenerateScuffSound's variants. Uses its own seeded
    // System.Random rather than UnityEngine.Random, so generating
    // clips never disturbs the game's own random state.
    public static AudioClip GenerateHappyMeow(int variantSeed)
    {
        System.Random rng = new System.Random(variantSeed);

        float clipLength = 0.35f + (float)rng.NextDouble() * 0.2f;    // 0.35s-0.55s — snappier, more exclamatory
        float startFreq = 450f + (float)rng.NextDouble() * 140f;      // 450-590Hz
        float peakFreq = startFreq + 180f + (float)rng.NextDouble() * 160f; // strong, confident upward chirp
        // Ends only partway back down from the peak, staying well above
        // where it started — a bright, elevated finish rather than
        // settling back near the start, which read as a hesitant
        // "up-then-down" question instead of a celebratory exclamation.
        float endFreq = Mathf.Lerp(peakFreq, startFreq, 0.25f + (float)rng.NextDouble() * 0.15f);
        float peakPosition = 0.2f + (float)rng.NextDouble() * 0.15f;  // quick rise to the peak
        // Much subtler vibrato than before — a light shimmer rather
        // than an audible quaver, which was reading as worried/nervous
        // instead of happy.
        float vibratoRate = 9f + (float)rng.NextDouble() * 4f;        // 9-13Hz
        float vibratoDepth = 0.006f + (float)rng.NextDouble() * 0.008f; // 0.006-0.014
        float attackTime = 0.03f + (float)rng.NextDouble() * 0.03f;
        float releaseTime = 0.10f + (float)rng.NextDouble() * 0.08f;

        int totalSamples = Mathf.CeilToInt(clipLength * SampleRate);
        float[] samples = new float[totalSamples];

        float phase = 0f;

        for (int i = 0; i < totalSamples; i++)
        {
            float t = i / (float)SampleRate;
            float t01 = t / clipLength;

            // Pitch contour: quick rise to a peak, then a gentler fall
            float targetFrequency;
            if (t01 < peakPosition)
            {
                targetFrequency = Mathf.Lerp(startFreq, peakFreq, t01 / peakPosition);
            }
            else
            {
                targetFrequency = Mathf.Lerp(peakFreq, endFreq, (t01 - peakPosition) / (1f - peakPosition));
            }

            float vibrato = 1f + vibratoDepth * Mathf.Sin(2f * Mathf.PI * vibratoRate * t);
            phase += targetFrequency * vibrato / SampleRate;

            float fundamental = Mathf.Sin(2f * Mathf.PI * phase);
            float overtone1 = 0.45f * Mathf.Sin(2f * Mathf.PI * phase * 2f);
            float overtone2 = 0.28f * Mathf.Sin(2f * Mathf.PI * phase * 3f); // a bit brighter/chirpier

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

            samples[i] = (fundamental + overtone1 + overtone2) * envelope * 0.35f;
        }

        // Normalize
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

        AudioClip clip = AudioClip.Create("HappyMeow_Procedural_" + variantSeed, totalSamples, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
