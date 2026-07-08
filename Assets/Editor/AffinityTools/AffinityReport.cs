using System.Collections.Generic;
using System.Text;

// AffinityReport
//
// Holds the tallied results of an affinity scan. Shared by both the
// Editor window and the standalone console script so their output is
// always identical.
//
// Lives OUTSIDE any Editor folder so runtime/console code can use it too.

public class AffinityReport
{
    // affinityName -> total points available across the whole game
    public Dictionary<string, int> totals = new Dictionary<string, int>();

    // affinityName -> number of separate commands found for it
    public Dictionary<string, int> occurrences = new Dictionary<string, int>();

    // affinityName -> per-file breakdown (fileName -> points in that file)
    public Dictionary<string, Dictionary<string, int>> perFile =
        new Dictionary<string, Dictionary<string, int>>();

    // Names found in .yarn that are NOT in the valid lists — likely typos
    public Dictionary<string, int> unknownNames = new Dictionary<string, int>();

    public int filesScanned = 0;
    public string errorMessage = null;

    private readonly HashSet<string> validGodNames;
    private readonly HashSet<string> validHumanNames;

    public AffinityReport(IEnumerable<string> godNames, IEnumerable<string> humanNames)
    {
        validGodNames = new HashSet<string>(godNames);
        validHumanNames = new HashSet<string>(humanNames);

        // Pre-seed all valid names at 0 so affinities that appear
        // nowhere still show up in the report (a 0 is meaningful — it
        // means that ending is currently unreachable).
        foreach (string g in validGodNames) { totals[g] = 0; occurrences[g] = 0; }
        foreach (string h in validHumanNames) { totals[h] = 0; occurrences[h] = 0; }
    }

    public void Record(string name, int amount, bool isGod, string sourceFile)
    {
        bool isValid = isGod ? validGodNames.Contains(name)
                             : validHumanNames.Contains(name);

        if (!isValid)
        {
            if (!unknownNames.ContainsKey(name)) unknownNames[name] = 0;
            unknownNames[name] += amount;
            return;
        }

        if (!totals.ContainsKey(name)) totals[name] = 0;
        if (!occurrences.ContainsKey(name)) occurrences[name] = 0;

        totals[name] += amount;
        occurrences[name] += 1;

        if (!perFile.ContainsKey(name))
            perFile[name] = new Dictionary<string, int>();
        if (!perFile[name].ContainsKey(sourceFile))
            perFile[name][sourceFile] = 0;
        perFile[name][sourceFile] += amount;
    }

    public List<string> GodNames { get { return new List<string>(validGodNames); } }
    public List<string> HumanNames { get { return new List<string>(validHumanNames); } }

    public int GetTotal(string name)
    {
        return totals.ContainsKey(name) ? totals[name] : 0;
    }

    public int GetOccurrences(string name)
    {
        return occurrences.ContainsKey(name) ? occurrences[name] : 0;
    }

    // Balance spread for a set of names: difference between highest and
    // lowest total. A smaller spread means a more balanced game.
    public int GetSpread(IEnumerable<string> names)
    {
        int min = int.MaxValue, max = int.MinValue;
        foreach (string n in names)
        {
            int t = GetTotal(n);
            if (t < min) min = t;
            if (t > max) max = t;
        }
        if (min == int.MaxValue) return 0;
        return max - min;
    }

    // Plain-text version of the report, used by the console script and
    // also handy for logging.
    public string ToText()
    {
        StringBuilder sb = new StringBuilder();

        if (errorMessage != null)
        {
            sb.AppendLine("ERROR: " + errorMessage);
            return sb.ToString();
        }

        sb.AppendLine("==================================================");
        sb.AppendLine(" AFFINITY BALANCE REPORT");
        sb.AppendLine(" Files scanned: " + filesScanned);
        sb.AppendLine("==================================================");
        sb.AppendLine();

        sb.AppendLine("GOD AFFINITIES (max points reachable each):");
        AppendGroup(sb, GodNames);
        sb.AppendLine("  Spread (max - min): " + GetSpread(GodNames));
        sb.AppendLine();

        sb.AppendLine("HUMAN AFFINITIES (max points reachable each):");
        AppendGroup(sb, HumanNames);
        sb.AppendLine("  Spread (max - min): " + GetSpread(HumanNames));
        sb.AppendLine();

        if (unknownNames.Count > 0)
        {
            sb.AppendLine("WARNING - UNKNOWN AFFINITY NAMES (possible typos):");
            foreach (var pair in unknownNames)
                sb.AppendLine("  '" + pair.Key + "' (" + pair.Value + " points) - not in your valid name lists");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No unknown affinity names found. All names valid.");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void AppendGroup(StringBuilder sb, List<string> names)
    {
        foreach (string name in names)
        {
            int total = GetTotal(name);
            int occ = GetOccurrences(name);
            sb.AppendLine("  " + name.PadRight(10) +
                          " total: " + total.ToString().PadLeft(4) +
                          "   appears in " + occ + " puzzle(s)");
        }
    }
}
