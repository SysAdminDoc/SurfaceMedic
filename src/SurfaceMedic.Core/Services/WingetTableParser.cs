using System.Text.RegularExpressions;
using SurfaceMedic.Core.Models;

namespace SurfaceMedic.Core.Services;

public static partial class WingetTableParser
{
    private static readonly string[] KnownColumns = ["Name", "Id", "Version", "Match", "Available", "Source"];

    public static IReadOnlyList<PackageRecord> Parse(string? output) =>
        Parse((output ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.None));

    public static IReadOnlyList<PackageRecord> Parse(IEnumerable<string>? lines)
    {
        if (lines is null)
        {
            return [];
        }

        var cleanLines = lines
            .Select(CleanLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        var headerIndex = Array.FindIndex(cleanLines, IsHeader);
        if (headerIndex < 0)
        {
            return [];
        }

        var columns = FindColumns(cleanLines[headerIndex]);
        if (!columns.Any(column => column.Name == "Name") ||
            !columns.Any(column => column.Name == "Id"))
        {
            return [];
        }

        var results = new List<PackageRecord>();
        for (var index = headerIndex + 1; index < cleanLines.Length; index++)
        {
            var line = cleanLines[index];
            if (ShouldSkip(line))
            {
                continue;
            }

            var values = SliceColumns(line, columns);
            if (!values.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name) ||
                !values.TryGetValue("Id", out var id) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            results.Add(new PackageRecord
            {
                Name = name,
                Id = id,
                Version = GetValue(values, "Version"),
                Available = GetValue(values, "Available"),
                Match = GetValue(values, "Match"),
                Source = GetValue(values, "Source")
            });
        }

        return results;
    }

    private static string CleanLine(string line) =>
        AnsiEscapePattern().Replace(line.Replace("\b", string.Empty).Replace("\0", string.Empty), string.Empty).TrimEnd();

    private static bool IsHeader(string line) =>
        HeaderPattern().IsMatch(line);

    private static IReadOnlyList<Column> FindColumns(string header)
    {
        var columns = new List<Column>();
        foreach (var columnName in KnownColumns)
        {
            var match = Regex.Match(header, $@"(?<!\S){Regex.Escape(columnName)}(?=\s|$)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                columns.Add(new Column(columnName, match.Index));
            }
        }

        return columns.OrderBy(column => column.Start).ToArray();
    }

    private static Dictionary<string, string> SliceColumns(string line, IReadOnlyList<Column> columns)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var end = index + 1 < columns.Count ? columns[index + 1].Start : line.Length;
            if (column.Start >= line.Length)
            {
                values[column.Name] = string.Empty;
                continue;
            }

            var safeEnd = Math.Min(end, line.Length);
            values[column.Name] = line[column.Start..safeEnd].Trim();
        }

        return values;
    }

    private static bool ShouldSkip(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length >= 4 && trimmed.All(character => character == '-'))
        {
            return true;
        }

        return IsHeader(line) ||
               SummaryPattern().IsMatch(trimmed) ||
               trimmed.Contains("upgrades available", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("explicit targeting", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("No package found", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) ? value : string.Empty;

    private sealed record Column(string Name, int Start);

    [GeneratedRegex(@"^\s*Name\s+Id\s+", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\d+\s+(package|upgrade)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryPattern();

    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiEscapePattern();
}
