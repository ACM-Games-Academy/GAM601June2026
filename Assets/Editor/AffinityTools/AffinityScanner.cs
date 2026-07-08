using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

// AffinityScanner
//
// Shared parsing logic used by BOTH the Unity Editor window
// (AffinityBalanceWindow) and the standalone console script.
//
// It reads raw .yarn text and tallies every <<addgodaffinity Name X>>
// and <<addhumanaffinity Name X>> command, producing an AffinityReport.
//
// It does NOT depend on Yarn Spinner's API — it only reads text — so it
// is robust against Yarn version changes. This file lives OUTSIDE any
// Editor folder so both the Editor tool and runtime/console can use it.

public static class AffinityScanner
{
    // Matches: <<addgodaffinity GodA 2>>  or  <<addhumanaffinity HumanB 3>>
    // Group 1: command type (god or human)
    // Group 2: affinity name
    // Group 3: point value (may be negative)
    private static readonly Regex AffinityRegex = new Regex(
        @"<<\s*add(god|human)affinity\s+(\w+)\s+(-?\d+)\s*>>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Scan all .yarn files in a directory (recursively) and build a report.
    public static AffinityReport ScanDirectory(
        string directoryPath,
        IEnumerable<string> validGodNames,
        IEnumerable<string> validHumanNames)
    {
        AffinityReport report = new AffinityReport(validGodNames, validHumanNames);

        if (!Directory.Exists(directoryPath))
        {
            report.errorMessage = "Directory not found: " + directoryPath;
            return report;
        }

        string[] yarnFiles = Directory.GetFiles(directoryPath, "*.yarn", SearchOption.AllDirectories);
        report.filesScanned = yarnFiles.Length;

        foreach (string file in yarnFiles)
        {
            string fileName = Path.GetFileName(file);
            string content;

            try
            {
                content = File.ReadAllText(file);
            }
            catch
            {
                continue; // skip unreadable files
            }

            ScanText(content, fileName, report);
        }

        return report;
    }

    // Scan a single block of .yarn text. Exposed separately so it can be
    // unit-tested or used on in-memory strings.
    public static void ScanText(string content, string sourceName, AffinityReport report)
    {
        MatchCollection matches = AffinityRegex.Matches(content);

        foreach (Match match in matches)
        {
            string type = match.Groups[1].Value.ToLowerInvariant();
            string name = match.Groups[2].Value;
            int amount = int.Parse(match.Groups[3].Value);

            bool isGod = (type == "god");

            report.Record(name, amount, isGod, sourceName);
        }
    }
}
