using System;
using System.Collections.Generic;
using UnityEngine;
using Yarn.Unity;

// SoundEffectManager
//
// Central place to play one-shot sound effects, triggerable either from
// C# (gameplay code — puzzle feedback, portrait effects, etc.) or
// directly from .yarn dialogue via a Yarn command.
//
// YARN COMMANDS:
//
//   <<playsound EffectName>>
//   <<playsustainedsound EffectName>>
//
// playsound fires a normal one-shot (via PlayOneShot, which layers
// freely with other sounds but can't be individually stopped once
// started). playsustainedsound is for effects that might need to be
// cut short — e.g. a purr that should stop the instant the player
// advances dialogue rather than always playing to completion — it
// plays on its own dedicated AudioSource so StopSustainedSound() can
// stop just that one sound without touching anything else. Only one
// sustained sound can play at a time; starting a new one stops
// whatever sustained sound was already playing.
//
// Named effects are configured in the Inspector. An effect with no
// AudioClip assigned yet is silently skipped (no error) — that's an
// expected case (a placeholder waiting on an audio asset), not a bug.
// An effect can optionally list Alternate Clips too — when present, a
// random one (including the main Clip) is chosen each time it plays,
// for variety on repeated triggers like cell-selection scuffs.
// A handful of known effect names ("MagicalTinkle", "CelebratoryChoir",
// "WrongAnswerChord", "CellScuff", "AngerStinger", "CatPurr",
// "HappyMeow") get simple procedurally generated placeholder clip(s)
// auto-filled in if left blank, so the system works immediately with
// zero audio assets;
// assigning real AudioClips in the Inspector always takes priority.
//
// SETUP:
// 1. Attach to a GameObject (an AudioSource is added automatically if
//    missing).
// 2. Assign Dialogue Runner in the Inspector so <<playsound>> works.
// 3. Add entries to Sound Effects, one per named effect, and assign an
//    AudioClip to each (or leave blank until you have one ready).

[RequireComponent(typeof(AudioSource))]
public class SoundEffectManager : MonoBehaviour
{
    [Header("References")]
    public DialogueRunner dialogueRunner;
    public AudioSource audioSource;

    // Dedicated source for "sustained" effects (see playsustainedsound
    // above) — kept separate from the main PlayOneShot source so a
    // sustained sound can be stopped individually without silencing
    // any other one-shot effects currently overlapping it.
    private AudioSource sustainedAudioSource;

    [System.Serializable]
    public class SoundEffectEntry
    {
        public string effectName;
        public AudioClip clip;

        // Optional extra variants. When any are present, PickClip()
        // chooses randomly among clip + these each time this effect
        // plays, instead of always playing the same clip.
        public List<AudioClip> alternateClips = new List<AudioClip>();

        [Range(0f, 1f)] public float volume = 1f;

        // Picks clip if there are no alternates, or a random choice
        // among clip + alternateClips when more than one variant exists.
        public AudioClip PickClip()
        {
            if (alternateClips == null || alternateClips.Count == 0)
            {
                return clip;
            }

            List<AudioClip> options = new List<AudioClip>();
            if (clip != null) options.Add(clip);
            foreach (AudioClip alt in alternateClips)
            {
                if (alt != null) options.Add(alt);
            }

            if (options.Count == 0) return null;
            return options[UnityEngine.Random.Range(0, options.Count)];
        }
    }

    [Header("Sound Effects")]
    public List<SoundEffectEntry> soundEffects = new List<SoundEffectEntry>();

    void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;

        sustainedAudioSource = gameObject.AddComponent<AudioSource>();
        sustainedAudioSource.playOnAwake = false;
        sustainedAudioSource.spatialBlend = 0f;

        if (dialogueRunner != null)
        {
            dialogueRunner.AddCommandHandler<string>("playsound", PlaySoundEffect);
            dialogueRunner.AddCommandHandler<string>("playsustainedsound", PlaySustainedSound);
        }

        EnsureProceduralPlaceholder("MagicalTinkle", ProceduralAudioClips.GenerateMagicalTinkle);
        EnsureProceduralPlaceholder("CelebratoryChoir", ProceduralAudioClips.GenerateAngelicChordSwell);
        EnsureProceduralPlaceholder("WrongAnswerChord", ProceduralAudioClips.GenerateWrongAnswerChord);
        EnsureProceduralPlaceholder("AngerStinger", ProceduralAudioClips.GenerateAngerStinger);
        EnsureProceduralPlaceholder("CatPurr", ProceduralAudioClips.GenerateCatPurr);
        EnsureProceduralPlaceholder("HappyMeow", ProceduralAudioClips.GenerateHappyMeow);

        EnsureProceduralPlaceholderVariants("CellScuff",
            () => ProceduralAudioClips.GenerateScuffSound(1),
            () => ProceduralAudioClips.GenerateScuffSound(2),
            () => ProceduralAudioClips.GenerateScuffSound(3),
            () => ProceduralAudioClips.GenerateScuffSound(4),
            () => ProceduralAudioClips.GenerateScuffSound(5));
    }

    // Plays a named sound effect (a random variant, if the entry has
    // alternates configured). Does nothing if the name isn't configured,
    // or if it's configured but has no clip assigned yet.
    public void PlaySoundEffect(string effectName)
    {
        SoundEffectEntry entry = soundEffects.Find(e => e.effectName == effectName);
        if (entry == null)
        {
            Debug.LogWarning("SoundEffectManager: No sound effect named '" + effectName + "'.");
            return;
        }

        AudioClip clipToPlay = entry.PickClip();
        if (clipToPlay == null) return;

        audioSource.PlayOneShot(clipToPlay, entry.volume);
    }

    // Tracks which frame the current sustained sound started on. A
    // command like <<playsustainedsound CatPurr>> and the line display
    // it's attached to both trigger within the very same frame — so
    // PortraitManager's "stop on new line" hook (see StopSustainedSound)
    // would otherwise kill a sound the instant it starts, before the
    // player ever hears it. Ignoring stop requests on that same frame
    // is what lets it play until a LATER line (i.e. an actual player
    // advance) stops it instead.
    private int sustainedSoundStartFrame = -1;

    // Plays a named sound effect on the dedicated sustained-sound
    // source, replacing whatever sustained sound (if any) was already
    // playing. Use this instead of PlaySoundEffect for anything that
    // might need to be cut short via StopSustainedSound() — e.g. a
    // purr that should stop the instant the player advances dialogue.
    public void PlaySustainedSound(string effectName)
    {
        SoundEffectEntry entry = soundEffects.Find(e => e.effectName == effectName);
        if (entry == null)
        {
            Debug.LogWarning("SoundEffectManager: No sound effect named '" + effectName + "'.");
            return;
        }

        AudioClip clipToPlay = entry.PickClip();
        if (clipToPlay == null) return;

        sustainedAudioSource.Stop();
        sustainedAudioSource.clip = clipToPlay;
        sustainedAudioSource.volume = entry.volume;
        sustainedAudioSource.Play();
        sustainedSoundStartFrame = Time.frameCount;
    }

    // Immediately stops whatever sustained sound is currently playing
    // (does nothing if none is, or if it only just started this same
    // frame — see sustainedSoundStartFrame). Called by PortraitManager
    // whenever a new dialogue line begins, since that's the reliable
    // signal that the player has advanced past whatever line started
    // the sound.
    public void StopSustainedSound()
    {
        if (Time.frameCount == sustainedSoundStartFrame) return;

        sustainedAudioSource.Stop();
    }

    // Makes sure a recognized placeholder-backed effect name exists in
    // the list (adding it if missing) and has a clip (generating the
    // placeholder if missing) — without ever overwriting a real,
    // already-assigned AudioClip.
    private void EnsureProceduralPlaceholder(string effectName, Func<AudioClip> generator)
    {
        SoundEffectEntry entry = soundEffects.Find(e => e.effectName == effectName);
        if (entry == null)
        {
            entry = new SoundEffectEntry { effectName = effectName };
            soundEffects.Add(entry);
        }

        if (entry.clip == null)
        {
            entry.clip = generator();
        }
    }

    // Same idea, but for effects that need several randomized variants
    // (e.g. cell-selection scuffs) rather than a single clip. The first
    // generator fills Clip, the rest fill Alternate Clips — only if
    // Alternate Clips is still empty, so real imported variants are
    // never overwritten.
    private void EnsureProceduralPlaceholderVariants(string effectName, params Func<AudioClip>[] generators)
    {
        if (generators == null || generators.Length == 0) return;

        SoundEffectEntry entry = soundEffects.Find(e => e.effectName == effectName);
        if (entry == null)
        {
            entry = new SoundEffectEntry { effectName = effectName };
            soundEffects.Add(entry);
        }

        if (entry.clip == null)
        {
            entry.clip = generators[0]();
        }

        if (entry.alternateClips == null)
        {
            entry.alternateClips = new List<AudioClip>();
        }

        if (entry.alternateClips.Count == 0)
        {
            for (int i = 1; i < generators.Length; i++)
            {
                entry.alternateClips.Add(generators[i]());
            }
        }
    }
}
