using System.Globalization;
using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

/// <summary>Fills missing Table 1 max_tax_rate values from LlamaParse markdown or OCR text.</summary>
public static class TableRateHeuristic
{
    private static readonly Regex MarkdownRow = new(
        @"^\|\s*(?<id>\d{1,2})\s*\|(?<cells>[^|]*(?:\|[^|]*)+)\|\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClassAmount = new(
        @"(?i)(?:class|land\s+use\s+class)\s*[#:]?\s*(?<id>\d{1,2})\b.{0,240}?(?<amt>\$?\s*\d{1,3}(?:,\d{3})+(?:\.\d{2})|\$?\s*\d+(?:\.\d{2})?)",
        RegexOptions.Compiled);

    private static readonly Regex DollarAmount = new(
        @"\$?\s*(?<amt>\d{1,3}(?:,\d{3})+(?:\.\d{2})|\d+(?:\.\d{2})?)",
        RegexOptions.Compiled);

    public static TableExtractionResult Apply(
        TableExtractionResult result,
        string? markdown = null,
        string? supplementalText = null)
    {
        if (result.RateClasses.Count == 0)
            return result;

        var hints = ExtractRateHints(markdown, supplementalText);
        if (hints.Count == 0)
            return result;

        var filled = 0;
        foreach (var row in result.RateClasses)
        {
            if (row.MaxTaxRate is not null)
                continue;

            if (!hints.TryGetValue(row.ClassId, out var hint))
                continue;

            row.MaxTaxRate = hint.Rate;
            if (string.IsNullOrWhiteSpace(row.MaxTaxUnit) && !string.IsNullOrWhiteSpace(hint.Unit))
                row.MaxTaxUnit = hint.Unit;
            filled++;
        }

        if (filled == 0)
            return result;

        var flags = result.Flags.ToList();
        flags.RemoveAll(f => f.Equals("table_no_rate_classes", StringComparison.OrdinalIgnoreCase));
        if (result.RateClasses.Any(r => r.MaxTaxRate is null))
            flags.Add($"heuristic_filled_{filled}_rates");
        else
            flags.RemoveAll(f => f.Contains("missing max_tax_rate", StringComparison.OrdinalIgnoreCase));

        return result with
        {
            RateClasses = result.RateClasses,
            Flags = flags,
            ExtractionConfidence = result.RateClasses.All(r => r.MaxTaxRate is not null)
                ? BumpConfidence(result.ExtractionConfidence)
                : result.ExtractionConfidence
        };
    }

    private static string BumpConfidence(string current) => current switch
    {
        "low" => "medium",
        _ => current
    };

    private static Dictionary<int, RateHint> ExtractRateHints(string? markdown, string? supplementalText)
    {
        var hints = new Dictionary<int, RateHint>();
        if (!string.IsNullOrWhiteSpace(markdown))
            MergeHints(hints, ParseMarkdown(markdown));
        if (!string.IsNullOrWhiteSpace(supplementalText))
            MergeHints(hints, ParseOcrText(supplementalText));
        return hints;
    }

    private static void MergeHints(Dictionary<int, RateHint> target, IEnumerable<(int Id, RateHint Hint)> incoming)
    {
        foreach (var (id, hint) in incoming)
        {
            if (!target.ContainsKey(id))
                target[id] = hint;
        }
    }

    private static IEnumerable<(int Id, RateHint Hint)> ParseMarkdown(string markdown)
    {
        foreach (Match match in MarkdownRow.Matches(markdown))
        {
            if (!int.TryParse(match.Groups["id"].Value, out var classId))
                continue;

            var cells = match.Groups["cells"].Value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rate = FindLargestPlausibleRate(cells);
            if (rate is null)
                continue;

            yield return (classId, new RateHint(rate.Value, InferUnit(cells)));
        }
    }

    private static IEnumerable<(int Id, RateHint Hint)> ParseOcrText(string text)
    {
        foreach (Match match in ClassAmount.Matches(text))
        {
            if (!int.TryParse(match.Groups["id"].Value, out var classId))
                continue;

            var rate = ParseAmount(match.Groups["amt"].Value);
            if (rate is null)
                continue;

            yield return (classId, new RateHint(rate.Value, null));
        }

        foreach (var page in TextAcquisition.SplitMarkedPages(text))
        {
            if (!Regex.IsMatch(page.Text, @"\bTABLE\s+1\b", RegexOptions.IgnoreCase))
                continue;

            foreach (var line in page.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var idMatch = Regex.Match(line, @"^\s*(?<id>\d{1,2})\b");
                if (!idMatch.Success)
                    continue;

                if (!int.TryParse(idMatch.Groups["id"].Value, out var classId))
                    continue;

                var rate = FindLargestPlausibleRate(line.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries));
                if (rate is null)
                    continue;

                yield return (classId, new RateHint(rate.Value, null));
            }
        }
    }

    private static double? FindLargestPlausibleRate(IEnumerable<string> cells)
    {
        double? best = null;
        foreach (var cell in cells)
        {
            var rate = ParseAmount(cell);
            if (rate is null || rate < 50 || rate > 500_000)
                continue;

            if (best is null || rate > best)
                best = rate;
        }

        return best;
    }

    private static string? InferUnit(string[] cells)
    {
        foreach (var cell in cells)
        {
            if (Regex.IsMatch(cell, @"(?i)per\s+(unit|acre|parcel|lot|sq\.?\s*ft|square\s+foot)"))
                return Regex.Match(cell, @"(?i)per\s+\S+(?:\s+\S+)?").Value.Trim();
        }

        return null;
    }

    private static double? ParseAmount(string raw)
    {
        var cleaned = raw.Replace("$", "").Replace(",", "").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private sealed record RateHint(double Rate, string? Unit);
}
