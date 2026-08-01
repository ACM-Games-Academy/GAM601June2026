using UnityEngine;
using Yarn.Unity;

// CritterJudgmentSequence
//
// One-off end-of-chapter beat: reveals the SAME mouse/snake tally
// readout used throughout the game (see CritterCountDisplay), enlarged
// and centered, and sets a Yarn variable ($critterJudgmentTier) so the
// .yarn script can have Bes react appropriately to how the player
// actually did at the catching minigame — the same "C# computes it,
// Yarn branches on it" pattern WordsearchDialogueBridge already uses
// for $selectedPath.
//
// Registered as a single Yarn command:
//
//   <<showcritterjudgment CritterJudgment>>
//
// The second word must match the exact name of the GameObject this
// script is attached to in the Hierarchy, same convention as
// BackgroundManager's <<fadetonight BackgroundManager>>.
//
// $critterJudgmentTier is set to one of three strings, based on the
// ratio of caught : spawned:
//   "none"      — poorCatchRatio (default 25%) or less
//   "few"       — above poorCatchRatio, up to and including excellentCatchRatio
//   "excellent" — strictly above excellentCatchRatio (default 90%)
//
// SETUP IN UNITY:
// 1. Attach this script to an empty GameObject named "CritterJudgment".
// 2. In the Inspector assign:
//      - Dialogue Runner       → the scene's DialogueRunner
//      - Critter Catch Effect  → the scene's CritterCatchEffect
//      - Count Display         → the scene's CritterCountDisplay
// 3. Declare the variable once near the top of your .yarn file:
//
//        <<declare $critterJudgmentTier = "">>
//
// 4. In your .yarn script, right before announcing the end of the
//    chapter:
//
//        <<showportrait Left Bes>>
//        <<showcritterjudgment CritterJudgment>>
//        <<if $critterJudgmentTier == "none">>
//            Bes: You've been a right awful cat! ...
//        <<elseif $critterJudgmentTier == "excellent">>
//            Bes: You're an utterly extraordinary huntress, my dear! ...
//        <<else>>
//            Bes: You've been an acceptable exterminator. ...
//        <<endif>>

public class CritterJudgmentSequence : MonoBehaviour
{
    [Header("References")]
    public DialogueRunner dialogueRunner;
    public CritterCatchEffect critterCatchEffect;
    public CritterCountDisplay countDisplay;

    // A catch ratio AT OR BELOW this counts as "none" (the worst tier) —
    // matches "between 0 and 25%".
    [Range(0f, 1f)] public float poorCatchRatio = 0.25f;
    // A catch ratio strictly ABOVE this counts as "excellent" (the best
    // tier) — matches "over 90%". Anything above poorCatchRatio but at
    // or below this is the middling "few" tier.
    [Range(0f, 1f)] public float excellentCatchRatio = 0.9f;

    [YarnCommand("showcritterjudgment")]
    public void ShowCritterJudgment()
    {
        if (dialogueRunner == null || critterCatchEffect == null)
        {
            Debug.LogWarning("CritterJudgmentSequence: Dialogue Runner or Critter Catch Effect not assigned.");
            return;
        }

        int caught = critterCatchEffect.TotalCaughtCount;
        int spawned = critterCatchEffect.TotalSpawnCount;
        float ratio = spawned > 0 ? (float)caught / spawned : 0f;

        string tier;
        if (ratio <= poorCatchRatio)
        {
            tier = "none";
        }
        else if (ratio > excellentCatchRatio)
        {
            tier = "excellent";
        }
        else
        {
            tier = "few";
        }

        dialogueRunner.VariableStorage.SetValue("$critterJudgmentTier", tier);

        if (countDisplay != null)
        {
            countDisplay.ShowFinalTally(critterCatchEffect.MouseCaughtCount, critterCatchEffect.SnakeCaughtCount);
        }
    }
}
