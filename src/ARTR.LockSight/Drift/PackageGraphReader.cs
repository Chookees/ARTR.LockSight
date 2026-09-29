using System.Xml.Linq;

namespace ARTR.LockSight.Drift;

/// <summary>
/// A property that may be missing, a literal bool, or an expression this tool does not evaluate.
/// </summary>
internal readonly struct OptionalBool
{
    public bool IsPresent { get; }
    public bool? Value { get; }

    public OptionalBool(bool isPresent, bool? value)
    {
        IsPresent = isPresent;
        Value = value;
    }

    public static OptionalBool Absent => new(false, null);
}

/// <summary>
/// Last literal text of one MSBuild property. Empty when the property is absent.
/// </summary>
internal readonly struct SourcedText
{
    public bool IsPresent { get; }
    public string Value { get; }

    public SourcedText(bool isPresent, string value)
    {
        IsPresent = isPresent;
        Value = value;
    }

    public static SourcedText Absent => new(false, string.Empty);
}

/// <summary>
/// Reads the project facts lockfile drift needs from XML, without invoking MSBuild.
/// </summary>
internal static class PackageGraphReader
{
    private const int MaxItems = 2_000;
    private const int MaxPropertyHits = 1_000;

    public static IReadOnlyList<string> ReadTargetFrameworks(XDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        string? single = null;
        string? multi = null;
        int seen = 0;
        foreach (XElement element in doc.Descendants())
        {
            bool isSingle = element.Name.LocalName == "TargetFramework";
            bool isMulti = element.Name.LocalName == "TargetFrameworks";
            if (!isSingle && !isMulti)
            {
                continue;
            }

            if (seen >= MaxPropertyHits)
            {
                break;
            }

            seen++;
            if (isSingle && !string.IsNullOrWhiteSpace(element.Value))
            {
                single = element.Value.Trim();
                multi = null;
            }
            else if (isMulti && !string.IsNullOrWhiteSpace(element.Value))
            {
                multi = element.Value.Trim();
                single = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(multi))
        {
            return SplitFrameworks(multi);
        }

        if (!string.IsNullOrWhiteSpace(single))
        {
            return [single];
        }

        return [];
    }

    public static OptionalBool ReadBoolProperty(XDocument doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        bool found = false;
        bool? value = null;
        int seen = 0;
        foreach (XElement element in doc.Descendants(name))
        {
            if (seen >= MaxPropertyHits)
            {
                break;
            }

            seen++;
            found = true;
            string text = element.Value.Trim();
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
            }
            else if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
            }
            else
            {
                // $(Property) and other expressions are not evaluated here.
                value = null;
            }
        }

        return found ? new OptionalBool(true, value) : OptionalBool.Absent;
    }

    public static SourcedText ReadTextProperty(XDocument doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        bool found = false;
        string value = string.Empty;
        int seen = 0;
        foreach (XElement element in doc.Descendants(name))
        {
            if (seen >= MaxPropertyHits)
            {
                break;
            }

            seen++;
            if (string.IsNullOrWhiteSpace(element.Value))
            {
                continue;
            }

            found = true;
            value = element.Value.Trim();
        }

        return found ? new SourcedText(true, value) : SourcedText.Absent;
    }

    public static void MergePackageReferences(
        XDocument doc,
        string sourcePath,
        IReadOnlyDictionary<string, PackagePin> cpmPins,
        Dictionary<string, PackagePin> into)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(cpmPins);
        ArgumentNullException.ThrowIfNull(into);

        int count = 0;
        foreach (XElement item in doc.Descendants("PackageReference"))
        {
            if (count >= MaxItems)
            {
                return;
            }

            string? id = (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string condition = ((string?)item.Attribute("Condition") ?? string.Empty).Trim();
            string? version = (string?)item.Attribute("VersionOverride")
                ?? (string?)item.Attribute("Version")
                ?? (string?)item.Element("Version");
            string source = sourcePath;
            if (string.IsNullOrWhiteSpace(version) && cpmPins.TryGetValue(id, out PackagePin? cpmPin))
            {
                version = cpmPin.Version;
                source = cpmPin.Source;
            }

            // Last declaration wins, matching MSBuild item override order for this static read.
            into[id] = new PackagePin(id, version?.Trim() ?? string.Empty, source, condition);
            count++;
        }
    }

    public static IReadOnlyList<string> ReadProjectReferences(XDocument doc, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        string? projectDir = Path.GetDirectoryName(projectPath);
        var list = new List<string>(capacity: 16);
        int count = 0;
        foreach (XElement item in doc.Descendants("ProjectReference"))
        {
            if (count >= MaxItems)
            {
                break;
            }

            string? include = (string?)item.Attribute("Include");
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            string normalized = include.Replace('\\', Path.DirectorySeparatorChar);
            string combined = projectDir is null
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(projectDir, normalized));
            list.Add(combined);
            count++;
        }

        return list;
    }

    private static IReadOnlyList<string> SplitFrameworks(string multi)
    {
        string[] parts = multi.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<string>(capacity: Math.Min(parts.Length, 32));
        int limit = Math.Min(parts.Length, 32);
        for (int i = 0; i < limit; i++)
        {
            if (!string.IsNullOrWhiteSpace(parts[i]))
            {
                list.Add(parts[i]);
            }
        }

        return list;
    }
}
