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
// YARN COMMAND:
//
//   <<playsound EffectName>>
//
// Named effects are configured in the Inspector. An effect with no
// AudioClip assigned yet is silently skipped (no error) — that's an
// expected case (a placeholder waiting on an audio asset), not a bug.
// A handful of known effect names ("MagicalTinkle", "CelebratoryChoir",
// "WrongAnswerChord") get a simple procedurally generated placeholder
// clip auto-filled in if left blank, so the system works immediately
// with zero audio assets; assigning a real AudioClip in the Inspector
// always takes priority.
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

    [System.Serializable]
    public class SoundEffectEntry
    {
        public string effectName;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
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

        if (dialogueRunner != null)
        {
            dialogueRunner.AddCommandHandler<string>("playsound", PlaySoundEffect);
        }

        EnsureProceduralPlaceholder("MagicalTinkle", ProceduralAudioClips.GenerateMagicalTinkle);
        EnsureProceduralPlaceholder("CelebratoryChoir", ProceduralAudioClips.GenerateAngelicChordSwell);
        EnsureProceduralPlaceholder("WrongAnswerChord", ProceduralAudioClips.GenerateWrongAnswerChord);
    }

    // Plays a named sound effect. Does nothing if the name isn't
    // configured, or if it's configured but has no clip assigned yet.
    public void PlaySoundEffect(string effectName)
    {
        SoundEffectEntry entry = soundEffects.Find(e => e.effectName == effectName);
        if (entry == null)
        {
            Debug.LogWarning("SoundEffectManager: No sound effect named '" + effectName + "'.");
            return;
        }

        if (entry.clip == null) return;

        audioSource.PlayOneShot(entry.clip, entry.volume);
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
}
