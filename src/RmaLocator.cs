using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

public sealed class TocScanContext
{
    public required string Text { get; init; }
    public required List<TocEntry> Entries { get; init; }
    public required PageOffsetResult Offset { get; init; }
    public TocEntry? RmaEntry { get; init; }
    public string? AppendixTitle { get; init; }
    public bool UsesAppendixPageRef { get; init; }
}

public static class RmaLocator
{
    private static readonly Regex RmaHeaderPattern = new(
        @"(?i)rate\s+and\s+method\s+of\s+apportionment|section\s+c[^\n]{0,40}special\s+tax|maximum\s+special\s+tax",
        RegexOptions.Compiled);

    public static RmaLocateResult Locate(string pdfPath, RmaLocateOptions? options = null)
    {
        options ??= new RmaLocateOptions { PdfPath = pdfPath };
        var totalPages = TextAcquisition.GetPageCount(pdfPath)
            ?? throw new InvalidOperationException("Could not determine PDF page count (pdfinfo required).");

        var toc = TryScanToc(pdfPath, options, totalPages);
        if (toc is not null)
        {
            if (toc.RmaEntry is not null && !toc.UsesAppendixPageRef)
            {
                var tocResult = BuildTocResult(toc.RmaEntry, toc.Entries, options, totalPages, toc.Offset, "toc");
                if (tocResult is not null && ValidateDiscoveredRange(pdfPath, options, tocResult.StartPage, tocResult.EndPage))
                    return tocResult;

                Console.Error.WriteLine(
                    tocResult is null
                        ? "TOC listed page invalid; searching by body-text scan..."
                        : $"TOC+offset pages {tocResult.StartPage}-{tocResult.EndPage} failed validation; searching by body-text scan...");

                var refineLo = tocResult?.StartPage is int s ? Math.Max(1, s - 30) : 1;
                var refineHi = tocResult?.EndPage is int e ? Math.Min(totalPages, e + 50) : totalPages;
                return BuildFromSectionSearch(
                    pdfPath, options, totalPages, refineLo, refineHi,
                    TocParser.ExtractAppendixLetter(toc.RmaEntry.Title),
                    toc.RmaEntry.Title, toc.Offset,
                    method: "toc-refine");
            }

            if (toc.AppendixTitle is not null)
            {
                var letter = TocParser.ExtractAppendixLetter(toc.AppendixTitle)
                    ?? throw new InvalidOperationException("TOC appendix title has no letter.");

                var (lo, hi) = RmaSectionSearch.BoundsFromTocBracket(
                    toc.Entries, toc.AppendixTitle, toc.Offset.Offset, totalPages);

                Console.Error.WriteLine(
                    $"TOC lists RMA appendix ({Truncate(toc.AppendixTitle, 70)}) with appendix page ref (e.g. {letter}-1); locating by body-text scan...");

                return BuildFromSectionSearch(
                    pdfPath, options, totalPages, lo, hi, letter,
                    toc.AppendixTitle, toc.Offset,
                    method: "appendix-content");
            }
        }

        return BuildKeywordScanFallback(pdfPath, options, totalPages);
    }

    private static RmaLocateResult BuildKeywordScanFallback(
        string pdfPath,
        RmaLocateOptions options,
        int totalPages)
    {
        Console.Error.WriteLine("No usable TOC; scanning full document for RMA keywords...");
        return BuildFromSectionSearch(
            pdfPath, options, totalPages,
            1, totalPages,
            appendixLetter: null,
            tocEntry: null, offset: PageOffsetDetector.Resolve(pdfPath, options, totalPages),
            method: "keyword-scan");
    }

    private static TocScanContext? TryScanToc(string pdfPath, RmaLocateOptions options, int totalPages)
    {
        var tocLast = Math.Min(options.TocLastPage, totalPages);
        if (tocLast < options.TocFirstPage)
            return null;

        Console.Error.WriteLine($"Scanning pages {options.TocFirstPage}-{tocLast} for table of contents...");

        var tocText = TextAcquisition.Acquire(pdfPath, new TextAcquisitionOptions
        {
            ForceOcr = options.ForceOcr,
            FirstPage = options.TocFirstPage,
            LastPage = tocLast,
            Dpi = options.Dpi,
            TesseractPsm = options.TesseractPsm
        }, options.PageCache);

        if (!TocParser.LooksLikeTableOfContents(tocText.Text, options.TocLoose))
        {
            Console.Error.WriteLine("No table of contents detected in front matter.");
            return null;
        }

        var entries = TocParser.Parse(tocText.Text, options.TocLoose, totalPages);
        var offset = PageOffsetDetector.Resolve(pdfPath, options, totalPages, entries, tocText.Text);
        Console.Error.WriteLine(offset.Notes);

        var minScore = options.TocLoose ? Math.Min(options.TocMinScore, 15) : options.TocMinScore;
        var rmaEntry = TocParser.FindBestRmaEntry(entries, minScore, totalPages);
        if (rmaEntry is null && options.TocLoose)
            rmaEntry = TocParser.FindRmaInRawText(tocText.Text, minScore: minScore, totalPages);

        var appendixTitle = TocParser.FindRmaAppendixTitle(tocText.Text);
        var usesAppendixRef = appendixTitle is not null && RmaEntryUsesAppendixPageRef(tocText.Text, appendixTitle);

        if (rmaEntry is not null && IsValidTocHit(rmaEntry, totalPages, minScore))
        {
            return new TocScanContext
            {
                Text = tocText.Text,
                Entries = entries,
                Offset = offset,
                RmaEntry = rmaEntry,
                AppendixTitle = appendixTitle,
                UsesAppendixPageRef = usesAppendixRef
            };
        }

        if (appendixTitle is not null)
        {
            return new TocScanContext
            {
                Text = tocText.Text,
                Entries = entries,
                Offset = offset,
                RmaEntry = null,
                AppendixTitle = appendixTitle,
                UsesAppendixPageRef = true
            };
        }

        if (entries.Count > 0)
        {
            Console.Error.WriteLine($"Table of contents found but no RMA entry matched (min score {minScore}).");
            foreach (var entry in entries.OrderByDescending(e => TocParser.ScoreTitle(e.Title)).Take(8))
                Console.Error.WriteLine($"  listed p.{entry.PageNumber,3}: {Truncate(entry.Title, 80)}");
        }

        return null;
    }

    private static bool RmaEntryUsesAppendixPageRef(string tocText, string appendixTitle)
    {
        var idx = tocText.IndexOf(appendixTitle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return TocParser.ContainsRmaAppendixTitle(tocText);

        var tail = tocText[(idx + appendixTitle.Length)..];
        var lineEnd = tail.IndexOf('\n');
        if (lineEnd > 0)
            tail = tail[..lineEnd];

        return TocParser.HasAppendixPageRef(tail);
    }

    private static RmaLocateResult BuildFromSectionSearch(
        string pdfPath,
        RmaLocateOptions options,
        int totalPages,
        int searchLow,
        int searchHigh,
        string? appendixLetter,
        string? tocEntry,
        PageOffsetResult offset,
        string method)
    {
        try
        {
            var (start, end) = RmaSectionSearch.FindSectionBounds(
                pdfPath, options, totalPages, searchLow, searchHigh, appendixLetter, tocEntry);

            var listedStart = offset.Offset > 0 ? start - offset.Offset : (int?)null;
            var listedEnd = offset.Offset > 0 ? end - offset.Offset : (int?)null;

            return BuildResult(
                method: method,
                startPage: start,
                endPage: end,
                totalPages: totalPages,
                padding: options.Padding,
                listedStart: listedStart,
                listedEnd: listedEnd,
                pageOffset: offset.Offset,
                pageOffsetMethod: offset.Method,
                tocEntry: tocEntry,
                notes: appendixLetter is not null
                    ? $"Appendix {appendixLetter} located at PDF pages {start}-{end} (searched {searchLow}-{searchHigh})."
                    : $"RMA section located at PDF pages {start}-{end} (searched {searchLow}-{searchHigh}).");
        }
        catch (InvalidOperationException)
        {
            var backThird = RmaSectionSearch.DefaultAppendixSearchLow(totalPages);

            if (method.Contains("-full", StringComparison.Ordinal) && method != "keyword-scan")
                throw;

            // keyword-scan already searches the full document
            if (method == "keyword-scan" || method.StartsWith("keyword-scan-", StringComparison.Ordinal))
                throw;

            if (!method.Contains("-expanded", StringComparison.Ordinal)
                && (searchLow > backThird || searchHigh < totalPages))
            {
                Console.Error.WriteLine(
                    $"Section search failed in PDF {searchLow}-{searchHigh}; expanding to back third ({backThird}-{totalPages})...");
                return BuildFromSectionSearch(
                    pdfPath, options, totalPages, backThird, totalPages,
                    appendixLetter, tocEntry, offset, method: method + "-expanded");
            }

            if (searchLow > 1)
            {
                Console.Error.WriteLine(
                    $"Section search failed in back third; scanning full document (1-{totalPages})...");
                return BuildFromSectionSearch(
                    pdfPath, options, totalPages, 1, totalPages,
                    appendixLetter, tocEntry, offset, method: method + "-full");
            }

            throw;
        }
    }

    private static RmaLocateResult? BuildTocResult(
        TocEntry rmaEntry,
        IReadOnlyList<TocEntry> entries,
        RmaLocateOptions options,
        int totalPages,
        PageOffsetResult offsetResult,
        string method)
    {
        var listedStart = rmaEntry.PageNumber;
        var listedEnd = TocParser.FindSectionEndPage(entries, listedStart, totalPages, options.MaxSpan)
            ?? Math.Min(listedStart + options.MaxSpan - 1, totalPages + 50);

        if (!TocParser.IsValidListedRange(listedStart, listedEnd, totalPages))
        {
            Console.Error.WriteLine($"Rejected TOC result: invalid listed range {listedStart}-{listedEnd}.");
            return null;
        }

        var pdfStart = PageOffsetDetector.ToPdfPage(listedStart, offsetResult.Offset);
        var pdfEnd = PageOffsetDetector.ToPdfPage(listedEnd, offsetResult.Offset);

        if (!PageOffsetDetector.IsValidPdfRange(pdfStart, pdfEnd, totalPages))
        {
            Console.Error.WriteLine(
                $"Rejected TOC result: invalid PDF range {pdfStart}-{pdfEnd} (listed {listedStart}-{listedEnd}, offset {offsetResult.Offset}).");
            return null;
        }

        var notes =
            $"TOC listed pages {listedStart}-{listedEnd}; offset {offsetResult.Offset} → PDF pages {pdfStart}-{pdfEnd}. {Truncate(rmaEntry.Title, 100)}.";

        return BuildResult(
            method: method,
            startPage: pdfStart,
            endPage: pdfEnd,
            totalPages: totalPages,
            padding: options.Padding,
            listedStart: listedStart,
            listedEnd: listedEnd,
            pageOffset: offsetResult.Offset,
            pageOffsetMethod: offsetResult.Method,
            tocEntry: rmaEntry.Title,
            notes: notes);
    }

    private static bool IsValidTocHit(TocEntry entry, int totalPages, int minScore = 35)
    {
        if (!TocParser.IsValidListedPage(entry.PageNumber, totalPages, entry.Title))
            return false;
        if (!TocParser.LooksLikeRmaTocTitle(entry.Title))
            return false;
        if (entry.Score < minScore)
            return false;
        if (entry.Title.Length > 160)
            return false;

        return true;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    private const int MinValidationScore = 60;

    private static bool ValidateDiscoveredRange(
        string pdfPath,
        RmaLocateOptions options,
        int startPage,
        int endPage)
    {
        var sampleEnd = Math.Min(endPage, startPage + 5);
        Console.Error.WriteLine($"Validating TOC+offset range: OCR pages {startPage}-{sampleEnd}...");

        var sample = TextAcquisition.Acquire(pdfPath, new TextAcquisitionOptions
        {
            ForceOcr = options.ForceOcr,
            FirstPage = startPage,
            LastPage = sampleEnd,
            Dpi = options.Dpi,
            TesseractPsm = options.TesseractPsm
        }, options.PageCache);

        var score = ScoreRmaContent(sample.Text);
        var hasHeader = RmaHeaderPattern.IsMatch(sample.Text);
        var hasTable = Regex.IsMatch(sample.Text, @"(?i)\btable\s+1\b");

        Console.Error.WriteLine($"  validation score: {score}, rma_header: {hasHeader}, table_1: {hasTable}");

        return score >= MinValidationScore && (hasHeader || hasTable);
    }

    internal static int ScoreRmaContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var score = 0;

        if (RmaHeaderPattern.IsMatch(text))
            score += 80;

        if (Regex.IsMatch(text, @"(?i)community\s+facilities\s+district"))
            score += 10;

        if (Regex.IsMatch(text, @"(?i)maximum\s+special\s+tax|max(?:imum)?\s+special\s+tax"))
            score += 25;

        if (Regex.IsMatch(text, @"(?i)table\s+1|land\s+use\s+class|zone\s+\d"))
            score += 20;

        if (Regex.IsMatch(text, @"(?i)mello[\-\s]?roos"))
            score += 15;

        if (Regex.IsMatch(text, @"(?i)indenture|underwriter|book[\-\s]?entry|official\s+statement"))
            score -= 30;

        return score;
    }

    private static RmaLocateResult BuildResult(
        string method,
        int startPage,
        int endPage,
        int totalPages,
        int padding,
        int? listedStart,
        int? listedEnd,
        int pageOffset,
        string? pageOffsetMethod,
        string? tocEntry,
        string? notes)
    {
        var paddedStart = Math.Max(1, startPage - padding);
        var paddedEnd = Math.Min(totalPages, endPage + padding);

        return new RmaLocateResult
        {
            Method = method,
            StartPage = paddedStart,
            EndPage = paddedEnd,
            TotalPages = totalPages,
            ListedStartPage = listedStart,
            ListedEndPage = listedEnd,
            PageOffset = pageOffset,
            PageOffsetMethod = pageOffsetMethod,
            TocEntry = tocEntry,
            Notes = notes,
            PagesArg = $"{paddedStart}-{paddedEnd}"
        };
    }
}
