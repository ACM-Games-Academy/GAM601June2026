#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// AffinityBalanceWindow
//
// A Unity Editor window that scans all .yarn files in your project and
// shows the maximum affinity points reachable for each God and Human.
// Open it via the menu:  Tools -> Affinity Balance Checker
//
// IMPORTANT: This file MUST live inside a folder named "Editor"
// (any folder named exactly "Editor" anywhere under Assets). That tells
// Unity to compile it only in the editor, not in your shipped game.
// The path tools/Editor/ already satisfies this.
//
// It depends on AffinityScanner.cs and AffinityReport.cs, which live
// OUTSIDE the Editor folder so they can be shared with the standalone
// console script.

public class AffinityBalanceWindow : EditorWindow
{
    // ── EDIT THESE to match your AffinityTracker.cs names ──────────────────
    // When you rename placeholders to real lore names, update them here too.
    private static readonly string[] GodNames =
        { "GodA", "GodB", "GodC", "GodD" };
    private static readonly string[] HumanNames =
        { "HumanA", "HumanB", "HumanC", "HumanD" };

    // Folder to scan, relative to the project root. Adjust if your .yarn
    // files live elsewhere.
    private string scanFolder = "Assets/Conversations";

    private AffinityReport report;
    private Vector2 scrollPos;

    [MenuItem("Tools/Affinity Balance Checker")]
    public static void ShowWindow()
    {
        AffinityBalanceWindow window = GetWindow<AffinityBalanceWindow>("Affinity Balance");
        window.minSize = new Vector2(420, 400);
        window.Refresh();
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void Refresh()
    {
        report = AffinityScanner.ScanDirectory(scanFolder, GodNames, HumanNames);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Scan Folder:", GUILayout.Width(80));
        scanFolder = EditorGUILayout.TextField(scanFolder);
        if (GUILayout.Button("Refresh", GUILayout.Width(80)))
        {
            Refresh();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        if (report == null)
        {
            EditorGUILayout.HelpBox("Click Refresh to scan.", MessageType.Info);
            return;
        }

        if (report.errorMessage != null)
        {
            EditorGUILayout.HelpBox(report.errorMessage, MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField("Files scanned: " + report.filesScanned, EditorStyles.miniLabel);
        EditorGUILayout.Space();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        DrawGroup("GOD AFFINITIES", GodNames);
        EditorGUILayout.Space();

        DrawGroup("HUMAN AFFINITIES", HumanNames);
        EditorGUILayout.Space();

        if (report.unknownNames.Count > 0)
        {
            EditorGUILayout.Space();
            string msg = "Unknown affinity names found (possible typos):\n";
            foreach (var pair in report.unknownNames)
                msg += "  '" + pair.Key + "'  (" + pair.Value + " points)\n";
            msg += "\nThese don't match your valid name lists and will be " +
                   "silently ignored at runtime. Check spelling.";
            EditorGUILayout.HelpBox(msg, MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox("All affinity names valid. No typos detected.", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawGroup(string heading, string[] names)
    {
        EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);

        int maxTotal = 1;
        foreach (string n in names)
            maxTotal = Mathf.Max(maxTotal, report.GetTotal(n));

        foreach (string name in names)
        {
            int total = report.GetTotal(name);
            int occ = report.GetOccurrences(name);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(name, GUILayout.Width(80));

            Rect barRect = GUILayoutUtility.GetRect(100, 16, GUILayout.ExpandWidth(true));
            float fill = (float)total / maxTotal;
            EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f));
            Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height);

            Color barColour = total == 0
                ? new Color(0.8f, 0.3f, 0.3f)
                : new Color(0.3f, 0.7f, 0.5f);
            EditorGUI.DrawRect(fillRect, barColour);

            EditorGUILayout.LabelField(total + " pts (" + occ + ")", GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();
        }

        int spread = report.GetSpread(names);
        string verdict;
        MessageType verdictType;

        if (spread == 0)
        {
            verdict = "Perfectly balanced (spread 0).";
            verdictType = MessageType.Info;
        }
        else if (spread <= 2)
        {
            verdict = "Well balanced (spread " + spread + ").";
            verdictType = MessageType.Info;
        }
        else if (spread <= 5)
        {
            verdict = "Slightly uneven (spread " + spread + ").";
            verdictType = MessageType.None;
        }
        else
        {
            verdict = "Unbalanced (spread " + spread + "). Some endings are much " +
                      "harder to reach than others.";
            verdictType = MessageType.Warning;
        }

        EditorGUILayout.HelpBox(verdict, verdictType);
    }
}
#endif
