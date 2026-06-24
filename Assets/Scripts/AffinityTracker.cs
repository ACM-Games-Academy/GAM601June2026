using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Yarn.Unity;

// AffinityTracker
//
// Tracks two independent affinity scores — God and Human — each with
// four named sub-scores. At the end of the game, the highest God score
// and highest Human score combine to select one of 16 ending nodes.
//
// YARN COMMANDS:
//
//   <<addgodaffinity GodA 2>>
//   Adds 2 to the GodA sub-score. Use any of: GodA GodB GodC GodD.
//
//   <<addhumanaffinity HumanA 1>>
//   Adds 1 to the HumanA sub-score. Use any of: HumanA HumanB HumanC HumanD.
//
//   <<checkending>>
//   Evaluates both tracks, constructs the ending node name, and jumps
//   to it. Call this as the last command in your final game node.
//   Ending node names follow the pattern: Ending_GodA_HumanB
//
// SETUP:
// 1. Attach this script to an empty GameObject called "AffinityTracker".
// 2. Drag your Dialogue Runner into the dialogueRunner slot.
// 3. Rename GodA/GodB/GodC/GodD and HumanA/HumanB/HumanC/HumanD to
//    your actual lore names when you're ready — update both this script
//    and your .yarn files at the same time.

public class AffinityTracker : MonoBehaviour
{
    [Header("References")]
    public DialogueRunner dialogueRunner;

    // ── Affinity score tables ─────────────────────────────────────────────
    // Each dictionary maps a sub-score name to its current value.
    // The names here must match exactly what you write in <<addgodaffinity>>
    // and <<addhumanaffinity>> commands in your .yarn files.

    private Dictionary<string, int> godAffinities = new Dictionary<string, int>()
    {
        { "GodA",  0 },
        { "GodB",  0 },
        { "GodC",  0 },
        { "GodD",  0 }
    };

    private Dictionary<string, int> humanAffinities = new Dictionary<string, int>()
    {
        { "HumanA", 0 },
        { "HumanB", 0 },
        { "HumanC", 0 },
        { "HumanD", 0 }
    };

    void Awake()
    {
        dialogueRunner.AddCommandHandler<string, int>("addgodaffinity", AddGodAffinity);
        dialogueRunner.AddCommandHandler<string, int>("addhumanaffinity", AddHumanAffinity);
        dialogueRunner.AddCommandHandler("checkending", CheckEnding);
    }

    // ── Command handlers ─────────────────────────────────────────────────

    private void AddGodAffinity(string name, int amount)
    {
        if (godAffinities.ContainsKey(name))
        {
            godAffinities[name] += amount;
            Debug.Log("AffinityTracker: " + name + " god affinity → " + godAffinities[name]);
        }
        else
        {
            Debug.LogWarning("AffinityTracker: Unknown god affinity name '" + name +
                             "'. Check spelling against the names defined in AffinityTracker.cs.");
        }
    }

    private void AddHumanAffinity(string name, int amount)
    {
        if (humanAffinities.ContainsKey(name))
        {
            humanAffinities[name] += amount;
            Debug.Log("AffinityTracker: " + name + " human affinity → " + humanAffinities[name]);
        }
        else
        {
            Debug.LogWarning("AffinityTracker: Unknown human affinity name '" + name +
                             "'. Check spelling against the names defined in AffinityTracker.cs.");
        }
    }

    private void CheckEnding()
    {
        // Wait one frame before starting the ending node so the current
        // dialogue has fully finished cleaning up first
        StartCoroutine(StartEndingNextFrame());
    }

    // ── Ending evaluation ────────────────────────────────────────────────

    private IEnumerator StartEndingNextFrame()
    {
        yield return null;

        string godResult = GetHighest(godAffinities);
        string humanResult = GetHighest(humanAffinities);

        // Construct the ending node name from both results
        // e.g. "Ending_GodA_HumanB"
        string endingNode = "Ending_" + godResult + "_" + humanResult;

        Debug.Log("AffinityTracker: Final scores —");
        foreach (var pair in godAffinities)
            Debug.Log("  God   " + pair.Key + ": " + pair.Value);
        foreach (var pair in humanAffinities)
            Debug.Log("  Human " + pair.Key + ": " + pair.Value);
        Debug.Log("AffinityTracker: Ending node → " + endingNode);

        // Jump to the combined ending node
        dialogueRunner.StartDialogue(endingNode);
    }

    // Returns the name of the sub-score with the highest value.
    // In the event of a tie, the sub-score that was defined first in
    // the dictionary wins — you can change this tiebreaker logic later.
    private string GetHighest(Dictionary<string, int> scores)
    {
        string highest = "";
        int highestValue = int.MinValue;

        foreach (var pair in scores)
        {
            if (pair.Value > highestValue)
            {
                highestValue = pair.Value;
                highest = pair.Key;
            }
        }

        return highest;
    }
}
