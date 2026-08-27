using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

public static class TocParser
{
    private static readonly Regex DottedLeaderLine = new(
        @"^(?<title>.+?)\s*[\.·…\-–—_\s]{2,}(?<page>\d{1,4})\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex AppendixRmaBlock = new(
        @"(?is)appendix\s+[a-z0-9]+\s+rate\s+and\s+method\s+of\s+apportionment(?:\s+of\s+special\s+tax)?",
        RegexOptions.Compiled);

    private static readonly Regex AppendixSplit = new(
        @"(?i)(?=appendix\s+[a-z0-9]+\b)",
        RegexOptions.Compiled);

    public static bool LooksLikeTableOfContents(string text, bool loose = false)
    {
        if (Regex.IsMatch(text, @"(?i)table\s+of\s+contents|\bcontents\b|^\s*index\s*$", RegexOptions.Multiline))
            return true;

        if (!loose)
            return false;

        var pageTailLines = Regex.Matches(text, @"(?<![0-9])([1-9]\d{0,2})\s*$", RegexOptions.Multiline).Count;
        return pageTailLines >= 8;
    }

    public static List<TocEntry> Parse(string text, bool loose = false, int totalPdfPages = 9999)
    {
        text = NormalizeOcr(text);
        var entries = new List<TocEntry>();

        AddAppendixEntries(entries, text, totalPdfPages);
        AddMatches(entries, DottedLeaderLine.Matches(text), totalPdfPages);

        if (loose)
            AddLineBasedEntries(entries, text, totalPdfPages);

        return entries
            .Where(e => IsValidListedPage(e.PageNumber, totalPdfPages, e.Title))
            .Where(e => !IsGarbledTitle(e.Title))
            .GroupBy(e => (NormalizeTitle(e.Title), e.PageNumber))
            .Select(g => g.OrderByDescending(e => e.Score).First())
            .OrderBy(e => e.PageNumber)
            .ToList();
    }

    public static TocEntry? FindBestRmaEntry(IReadOnlyList<TocEntry> entries, int minScore = 35, int totalPdfPages = 9999)
    {
        return entries
            .Select(e => new TocEntry
            {
                Title = e.Title,
                PageNumber = e.PageNumber,
                Score = ScoreRmaTitle(e.Title)
            })
            .Where(e => IsValidListedPage(e.PageNumber, totalPdfPages, e.Title))
            .Where(e => !IsGarbledTitle(e.Title))
            .Where(e => !IsBondBoilerplateTitle(e.Title))
            .Where(e => LooksLikeRmaTocTitle(e.Title))
            .Where(e => e.Score >= minScore)
            .OrderByDescending(e => e.Score)
            .ThenByDescending(e => TitleQuality(e.Title))
            .ThenByDescending(e => e.PageNumber > 5 && e.PageNumber <= totalPdfPages + 50 ? 1 : 0)
            .FirstOrDefault();
    }

    public static TocEntry? FindRmaInRawText(string text, int minScore = 15, int totalPdfPages = 9999)
    {
        text = NormalizeOcr(text);
        var entries = new List<TocEntry>();
        AddAppendixEntries(entries, text, totalPdfPages);
        return FindBestRmaEntry(entries, minScore, totalPdfPages);
    }

    /// <summary>True when TOC tail uses appendix section numbering (e.g. ...... D-1), not a listed PDF page.</summary>
    public static bool ContainsRmaAppendixTitle(string text) =>
        FindRmaAppendixTitle(text) is not null;

    public static string? FindRmaAppendixTitle(string text)
    {
        text = NormalizeOcr(text);
        foreach (var chunk in AppendixSplit.Split(text))
        {
            if (!AppendixRmaBlock.IsMatch(chunk))
                continue;

            var title = ExtractAppendixTitle(chunk);
            if (title is not null)
                return title;
        }

        return null;
    }

    public static int? FindSectionEndPage(IReadOnlyList<TocEntry> entries, int rmaStartPage, int totalPdfPages, int maxSpan)
    {
        var nextMajor = entries
            .Where(e => e.PageNumber > rmaStartPage && IsValidListedPage(e.PageNumber, totalPdfPages, e.Title))
            .Where(e => IsMajorSectionBoundary(e.Title))
            .OrderBy(e => e.PageNumber)
            .FirstOrDefault();

        if (nextMajor is not null)
            return Math.Max(rmaStartPage, nextMajor.PageNumber - 1);

        return Math.Min(rmaStartPage + maxSpan - 1, totalPdfPages + 50);
    }

    public static bool IsValidListedRange(int listedStart, int listedEnd, int totalPdfPages)
    {
        if (!IsValidListedPage(listedStart, totalPdfPages))
            return false;
        if (!IsValidListedPage(listedEnd, totalPdfPages))
            return false;
        if (listedEnd < listedStart)
            return false;
        return true;
    }

    public static int ScoreTitle(string title) => ScoreRmaTitle(title);

    /// <summary>True when a TOC line looks like an RMA section header, not bond boilerplate.</summary>
    public static bool LooksLikeRmaTocTitle(string title)
    {
        if (IsBondBoilerplateTitle(title))
            return false;

        var t = NormalizeOcr(title).ToLowerInvariant();

        if (Regex.IsMatch(t, @"(?i)appendix\s+[a-z0-9]+\s+rate\s+and\s+method"))
            return true;

        if (Regex.IsMatch(t, @"(?i)rate\s+and\s+method\s+of\s+apportionment"))
            return true;

        if (Regex.IsMatch(t, @"(?i)\brma\b"))
            return true;

        if (Regex.IsMatch(t, @"(?i)section\s+c\b") && Regex.IsMatch(t, @"(?i)special\s+tax|apportionment|rate"))
            return true;

        return false;
    }

    /// <summary>Bond indenture TOC lines that mention taxes but are not the RMA appendix.</summary>
    public static bool IsBondBoilerplateTitle(string title)
    {
        var t = NormalizeOcr(title).ToLowerInvariant();

        if (Regex.IsMatch(t, @"(?i)special\s+tax\s+fund|administrative\s+expense|bond\s+trust\s+fund|debt\s+service|requisition\s+for\s+payment"))
            return true;

        if (Regex.IsMatch(t, @"(?i)indenture|trustee|underwriter|official\s+statement|paying\s+agent|registrar|form\s+of\s+approving"))
            return true;

        // "Apportionment of Special Tax" alone appears in bond body/TOC, not the RMA section title.
        if (Regex.IsMatch(t, @"(?i)(?:method\s+of\s+)?apportionment\s+of\s+special\s+tax")
            && !Regex.IsMatch(t, @"(?i)appendix\s+[a-z0-9]+\s+rate\s+and\s+method|rate\s+and\s+method\s+of\s+apportionment|\brma\b|section\s+c\b"))
            return true;

        return false;
    }

    private static void AddAppendixEntries(List<TocEntry> entries, string text, int totalPdfPages)
    {
        foreach (var chunk in AppendixSplit.Split(text))
        {
            if (!AppendixRmaBlock.IsMatch(chunk))
                continue;

            var title = ExtractAppendixTitle(chunk);
            if (title is null)
                continue;

            // Page number must come from text AFTER the title (avoid "Series 2002" in filename/header)
            var titleIdx = chunk.IndexOf(title, StringComparison.OrdinalIgnoreCase);
            var tail = titleIdx >= 0 ? chunk[(titleIdx + title.Length)..] : chunk;
            var page = ExtractTrailingPageNumber(tail, requireLeader: true);
            if (page is null)
            {
                if (HasAppendixPageRef(tail))
                    continue; // appendix section ref (e.g. D-1) — not a listed page; use binary search
                continue;
            }
            if (!IsValidListedPage(page.Value, totalPdfPages, title))
                continue;

            entries.Add(new TocEntry
            {
                Title = title,
                PageNumber = page.Value,
                Score = ScoreRmaTitle(title) + 30
            });
        }
    }

    private static void AddLineBasedEntries(List<TocEntry> entries, string text, int totalPdfPages)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = CleanTitle(line);
            if (trimmed.Length < 8 || IsGarbledTitle(trimmed))
                continue;

            if (!Regex.IsMatch(trimmed, @"(?i)appendix\s+[a-z0-9]+\s+rate\s+and\s+method|rate\s+and\s+method\s+of\s+apportionment|\brma\b|section\s+c\b.*(?:special\s+tax|apportionment|rate)", RegexOptions.None))
                continue;

            if (IsBondBoilerplateTitle(trimmed))
                continue;

            var page = ExtractTrailingPageNumber(trimmed, requireLeader: true);
            if (page is null || !IsValidListedPage(page.Value, totalPdfPages, trimmed))
                continue;

            var title = Regex.Replace(trimmed, @"[\.·…_\s]{2,}\d{1,4}\s*$", "").Trim();
            if (title.Length < 5)
                continue;

            entries.Add(new TocEntry
            {
                Title = title,
                PageNumber = page.Value,
                Score = ScoreRmaTitle(title)
            });
        }
    }

    private static void AddMatches(List<TocEntry> entries, MatchCollection matches, int totalPdfPages)
    {
        foreach (Match match in matches)
        {
            var title = CleanTitle(match.Groups["title"].Value);
            if (!int.TryParse(match.Groups["page"].Value, out var page))
                continue;

            if (!IsValidListedPage(page, totalPdfPages, title) || title.Length < 3 || IsNoiseTitle(title) || IsGarbledTitle(title))
                continue;

            entries.Add(new TocEntry
            {
                Title = title,
                PageNumber = page,
                Score = ScoreRmaTitle(title)
            });
        }
    }

    private static string? ExtractAppendixTitle(string chunk)
    {
        var match = Regex.Match(chunk,
            @"(?is)(appendix\s+[a-z0-9]+\s+rate\s+and\s+method\s+of\s+apportionment(?:\s+of\s+special\s+tax)?)",
            RegexOptions.IgnoreCase);

        return match.Success ? CleanTitle(match.Groups[1].Value) : null;
    }

    private static int? ExtractTrailingPageNumber(string text, bool requireLeader = false)
    {
        var leader = Regex.Match(text, @"[\.·…_\s]{3,}(\d{1,4})\s*$");
        if (leader.Success && int.TryParse(leader.Groups[1].Value, out var fromLeader))
            return fromLeader;

        var lineLeader = Regex.Match(text, @"[\.·…_\s]{2,}(\d{1,4})\s*$");
        if (lineLeader.Success && int.TryParse(lineLeader.Groups[1].Value, out var fromLine))
            return fromLine;

        if (requireLeader)
            return null;

        var numbers = Regex.Matches(text, @"(?<![0-9])(\d{1,4})(?![0-9])");
        for (var i = numbers.Count - 1; i >= 0; i--)
        {
            if (!int.TryParse(numbers[i].Groups[1].Value, out var n))
                continue;
            if (n >= 1 && !IsLikelyYear(n))
                return n;
        }

        return null;
    }

    public static string? ExtractAppendixLetter(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var match = Regex.Match(title, @"(?i)appendix\s+([A-G])\b");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>Detect appendix-style page refs (D-1, E-1) — section labels, not TOC listed pages.</summary>
    internal static bool HasAppendixPageRef(string text) => ParseAppendixPageRef(text) is not null;

    public static AppendixPageRef? ParseAppendixPageRef(string text, string? expectedLetter = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = NormalizeOcr(text).Trim();

        if (expectedLetter is not null)
        {
            var fromExpected = ParseExpectedAppendixRef(text, expectedLetter);
            if (fromExpected is not null)
                return fromExpected;
        }

        foreach (var pattern in new[]
                 {
                     @"(?<![A-Z])([A-G])[\s\-–—]+(\d{1,3})\s*$",
                     @"(?i)\b([A-G])[\s\-–—]+(\d{1,3})\b",
                     @"^[\-–—]\s*([A-G])[\s\-–—]+(\d{1,3})\s*[\-–—]$",
                     @"(?i)\b([A-G])(\d{1,3})\b"
                 })
        {
            var match = Regex.Match(text, pattern);
            if (match.Success
                && int.TryParse(match.Groups[2].Value, out var subPage)
                && subPage >= 1)
            {
                return new AppendixPageRef(match.Groups[1].Value.ToUpperInvariant(), subPage);
            }
        }

        var garbled = Regex.Match(text, @"(?i)[\.\s0]+(?:e)?([A-G])\s*[>|1lI]\s*(\d?)");
        if (garbled.Success)
        {
            var sub = garbled.Groups[2].Success && int.TryParse(garbled.Groups[2].Value, out var n) && n >= 1
                ? n
                : 1;
            return new AppendixPageRef(garbled.Groups[1].Value.ToUpperInvariant(), sub);
        }

        var garbledShort = Regex.Match(text, @"(?i)[\.\s]{2,}([A-G])\s*l\b");
        if (garbledShort.Success)
            return new AppendixPageRef(garbledShort.Groups[1].Value.ToUpperInvariant(), 1);

        return null;
    }

    private static AppendixPageRef? ParseExpectedAppendixRef(string text, string expectedLetter)
    {
        var letter = Regex.Escape(expectedLetter.ToUpperInvariant());
        var patterns = new[]
        {
            $@"(?i)(?:{letter}|0)[\s\-–—I1l>|\.]{{0,5}}(\d{{1,3}})",
            $@"(?i)\b{letter}[\s\-–—]*(\d{{1,3}})\b",
            $@"(?i)[\-–—]\s*{letter}[\s\-–—]+(\d{{1,3}})\s*[\-–—]"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern);
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out var subPage)
                && subPage >= 1)
            {
                return new AppendixPageRef(expectedLetter.ToUpperInvariant(), subPage);
            }
        }

        if (Regex.IsMatch(text, $@"(?i){letter}\s*[>|1lI]\b"))
            return new AppendixPageRef(expectedLetter.ToUpperInvariant(), 1);

        return null;
    }

    public static bool IsValidListedPage(int page, int totalPdfPages = 9999, string? context = null)
    {
        if (page < 1)
            return false;

        if (IsLikelyYear(page))
            return false;

        // Listed TOC pages shouldn't exceed doc length by much
        if (page > totalPdfPages + 50)
            return false;

        if (context is not null && IsLikelyYearPageFromContext(page, context))
            return false;

        return true;
    }

    private static bool IsLikelyYear(int n) => n is >= 1900 and <= 2100;

    private static bool IsLikelyYearPageFromContext(int page, string context)
    {
        if (!IsLikelyYear(page))
            return false;

        if (Regex.IsMatch(context, @"(?i)series\s+\d{4}|cfd\s+\d|no\.\s*\d{4}|fiscal\s+year|\b20\d{2}\b"))
            return true;

        // Reject page numbers within ±3 of a series year in the same title/line
        foreach (Match m in Regex.Matches(context, @"(?i)series\s+(\d{4})"))
        {
            if (int.TryParse(m.Groups[1].Value, out var year) && Math.Abs(page - year) <= 3)
                return true;
        }

        return false;
    }

    private static bool IsGarbledTitle(string title)
    {
        if (title.Length > 160)
            return true;

        if (Regex.IsMatch(title, @"(.)\1{5,}"))
            return true;

        var appendixCount = Regex.Matches(title, @"(?i)\bappendix\s+[a-z0-9]+\b").Count;
        if (appendixCount > 1)
            return true;

        var alpha = title.Count(char.IsLetter);
        return alpha > 0 && (double)alpha / title.Length < 0.35;
    }

    private static int TitleQuality(string title)
    {
        var score = 100 - Math.Min(title.Length, 100);
        if (Regex.IsMatch(title, @"(?i)^appendix\s+[a-z0-9]+\s+rate\s+and\s+method"))
            score += 50;
        if (title.Length <= 80)
            score += 20;
        return score;
    }

    private static int ScoreRmaTitle(string title)
    {
        var t = NormalizeOcr(title).ToLowerInvariant();
        var score = 0;

        if (Regex.IsMatch(t, @"appendix\s+[a-z0-9]+\s+rate\s+and\s+method\s+of\s+apportionment"))
            score += 120;
        else if (Regex.IsMatch(t, @"rate\s+and\s+method\s+of\s+apportionment"))
            score += 100;
        else if (Regex.IsMatch(t, @"rate\s+and\s+method"))
            score += 80;

        if (Regex.IsMatch(t, @"method\s+of\s+apportionment|apportionment\s+of\s+special"))
            score += 40;

        if (Regex.IsMatch(t, @"\bapportionment\b"))
            score += 20;

        if (Regex.IsMatch(t, @"\brma\b"))
            score += 70;

        if (Regex.IsMatch(t, @"section\s+c\b") && Regex.IsMatch(t, @"special\s+tax|apportionment|rate"))
            score += 60;

        if (Regex.IsMatch(t, @"special\s+tax"))
            score += 15;

        if (IsBondBoilerplateTitle(title))
            score -= 120;

        if (Regex.IsMatch(t, @"legal\s+opinion|form\s+of\s+approving"))
            score -= 80;

        if (Regex.IsMatch(t, @"indenture|trustee|underwriter|official\s+statement"))
            score -= 50;

        return score;
    }

    private static bool IsMajorSectionBoundary(string title)
    {
        var t = title.ToLowerInvariant();
        if (Regex.IsMatch(t, @"rate\s+and\s+method|apportionment\s+of\s+special"))
            return false;

        return Regex.IsMatch(t, @"^exhibit\b")
            || Regex.IsMatch(t, @"^appendix\s+[a-z0-9]+\b(?!.*(?:rate|method|apportionment|special\s+tax))")
            || Regex.IsMatch(t, @"indenture|official\s+statement|trustee|form\s+of\b");
    }

    private static bool IsNoiseTitle(string title)
    {
        var t = title.ToLowerInvariant();
        return Regex.IsMatch(t, @"^\d+$")
            || t.Length < 4
            || Regex.IsMatch(t, @"^page\s+\d");
    }

    private static string NormalizeOcr(string text)
    {
        text = text.Replace('\u2019', '\'').Replace('\u2018', '\'');
        text = Regex.Replace(text, @"[ \t]+", " ");
        return text;
    }

    private static string CleanTitle(string title) =>
        Regex.Replace(title.Trim(), @"\s+", " ");

    private static string NormalizeTitle(string title) =>
        Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
}
