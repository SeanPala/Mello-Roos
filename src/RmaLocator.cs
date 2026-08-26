using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

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

        var tocResult = TryLocateFromToc(pdfPath, options, totalPages);
        if (tocResult is not null)
            return tocResult;

        Console.Error.WriteLine("TOC locate failed; falling back to chunked keyword scan.");
        return LocateByKeywordScan(pdfPath, options, totalPages);
    }

    private static RmaLocateResult? TryLocateFromToc(string pdfPath, RmaLocateOptions options, int totalPages)
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
        });

        if (!TocParser.LooksLikeTableOfContents(tocText.Text, options.TocLoose))
        {
            Console.Error.WriteLine("No table of contents detected in front matter.");
            if (!options.TocLoose)
                Console.Error.WriteLine("Tip: try --toc-loose to accept index-style TOCs and fuzzy matching.");
            return null;
        }

        var entries = TocParser.Parse(tocText.Text, options.TocLoose, totalPages);
        if (entries.Count == 0)
        {
            Console.Error.WriteLine("Table of contents found but no parseable entries.");
            if (options.TocLoose)
            {
                var rawHit = TocParser.FindRmaInRawText(tocText.Text, minScore: options.TocMinScore, totalPages);
                if (rawHit is not null && IsValidTocHit(rawHit, totalPages))
                    return BuildTocResult(rawHit, entries, options, totalPages, pdfPath, "toc-raw");
            }
            return null;
        }

        var minScore = options.TocLoose ? Math.Min(options.TocMinScore, 35) : options.TocMinScore;
        var rmaEntry = TocParser.FindBestRmaEntry(entries, minScore, totalPages);
        if (rmaEntry is null && options.TocLoose)
            rmaEntry = TocParser.FindRmaInRawText(tocText.Text, minScore: minScore, totalPages);

        if (rmaEntry is null || rmaEntry.Score < minScore || !IsValidTocHit(rmaEntry, totalPages))
        {
            Console.Error.WriteLine($"Table of contents found but no RMA entry matched (min score {minScore}).");
            Console.Error.WriteLine($"Parsed {entries.Count} TOC entries (top matches):");
            foreach (var entry in entries.OrderByDescending(e => TocParser.ScoreTitle(e.Title)).Take(12))
                Console.Error.WriteLine($"  listed p.{entry.PageNumber,3} (score {TocParser.ScoreTitle(entry.Title),3}): {Truncate(entry.Title, 80)}");
            if (!options.TocLoose)
                Console.Error.WriteLine("Tip: try --toc-loose or lower --toc-min-score.");
            return null;
        }

        return BuildTocResult(rmaEntry, entries, options, totalPages, pdfPath, "toc");
    }

    private static RmaLocateResult? BuildTocResult(
        TocEntry rmaEntry,
        IReadOnlyList<TocEntry> entries,
        RmaLocateOptions options,
        int totalPages,
        string pdfPath,
        string method)
    {
        var listedStart = rmaEntry.PageNumber;
        var listedEnd = TocParser.FindSectionEndPage(entries, listedStart, totalPages, options.MaxSpan)
            ?? Math.Min(listedStart + options.MaxSpan - 1, totalPages + 50);

        if (!TocParser.IsValidListedRange(listedStart, listedEnd, totalPages))
        {
            Console.Error.WriteLine($"Rejected TOC result: invalid listed range {listedStart}-{listedEnd} (PDF has {totalPages} pages).");
            return null;
        }

        var offsetResult = PageOffsetDetector.Resolve(pdfPath, options, totalPages, entries);
        Console.Error.WriteLine(offsetResult.Notes);

        var pdfStart = PageOffsetDetector.ToPdfPage(listedStart, offsetResult.Offset);
        var pdfEnd = PageOffsetDetector.ToPdfPage(listedEnd, offsetResult.Offset);

        if (!PageOffsetDetector.IsValidPdfRange(pdfStart, pdfEnd, totalPages))
        {
            Console.Error.WriteLine($"Rejected TOC result: invalid PDF range {pdfStart}-{pdfEnd} (listed {listedStart}-{listedEnd}, offset {offsetResult.Offset}, PDF pages {totalPages}).");
            return null;
        }

        var notes = $"TOC listed pages {listedStart}-{listedEnd}; offset {offsetResult.Offset} → PDF pages {pdfStart}-{pdfEnd}. {Truncate(rmaEntry.Title, 100)} (score {rmaEntry.Score}).";

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

    private static bool IsValidTocHit(TocEntry entry, int totalPages)
    {
        if (!TocParser.IsValidListedPage(entry.PageNumber, totalPages, entry.Title))
        {
            Console.Error.WriteLine($"Rejected TOC hit: invalid listed page {entry.PageNumber}.");
            return false;
        }

        if (!TocParser.LooksLikeRmaTocTitle(entry.Title))
        {
            Console.Error.WriteLine($"Rejected TOC hit: not an RMA section title ({Truncate(entry.Title, 60)}).");
            return false;
        }

        if (entry.Score < 35)
        {
            Console.Error.WriteLine($"Rejected TOC hit: score {entry.Score} below minimum 35.");
            return false;
        }

        if (entry.Title.Length > 160 || (entry.Title.Contains("APPENDIX C", StringComparison.OrdinalIgnoreCase)
            && entry.Title.Contains("APPENDIX D", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Rejected TOC hit: garbled/multi-appendix OCR blob.");
            return false;
        }

        return true;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    private static RmaLocateResult LocateByKeywordScan(string pdfPath, RmaLocateOptions options, int totalPages)
    {
        var bestChunkStart = 0;
        var bestChunkScore = 0;
        string? bestChunkText = null;
        var chunkSize = Math.Max(10, options.ChunkSize);

        for (var chunkStart = 1; chunkStart <= totalPages; chunkStart += chunkSize)
        {
            var chunkEnd = Math.Min(chunkStart + chunkSize - 1, totalPages);
            Console.Error.WriteLine($"Scanning chunk pages {chunkStart}-{chunkEnd}...");

            var chunkText = TextAcquisition.Acquire(pdfPath, new TextAcquisitionOptions
            {
                ForceOcr = options.ForceOcr,
                FirstPage = chunkStart,
                LastPage = chunkEnd,
                Dpi = options.Dpi,
                TesseractPsm = options.TesseractPsm
            });

            var score = ScoreRmaContent(chunkText.Text);
            Console.Error.WriteLine($"  chunk score: {score}, {chunkText.CharCount} chars");

            if (score > bestChunkScore)
            {
                bestChunkScore = score;
                bestChunkStart = chunkStart;
                bestChunkText = chunkText.Text;
            }

            // Only stop early on a strong RMA header hit, not bond preamble "special tax" language
            if (score >= 120 && RmaHeaderPattern.IsMatch(chunkText.Text) && chunkStart > 30)
                break;
        }

        if (bestChunkScore < 40 || bestChunkText is null)
        {
            throw new InvalidOperationException(
                "Could not locate Mello-Roos RMA section. Try widening --toc-pages, adjusting --chunk-size, or locating pages manually.");
        }

        var refineStart = bestChunkStart;
        var refineEnd = Math.Min(bestChunkStart + chunkSize - 1, totalPages);
        var pages = TextAcquisition.SplitMarkedPages(bestChunkText, refineStart);
        Console.Error.WriteLine($"Refining within cached chunk pages {refineStart}-{refineEnd}...");

        var startPage = pages
            .Where(p => ScoreRmaContent(p.Text) >= 50)
            .OrderBy(p => p.PageNumber)
            .Select(p => p.PageNumber)
            .FirstOrDefault();

        if (startPage == 0)
        {
            startPage = pages
                .OrderByDescending(p => ScoreRmaContent(p.Text))
                .Select(p => p.PageNumber)
                .FirstOrDefault(refineStart);
        }

        var endPage = startPage;
        foreach (var page in pages.Where(p => p.PageNumber >= startPage).OrderBy(p => p.PageNumber))
        {
            if (page.PageNumber - startPage >= options.MaxSpan)
                break;

            if (Regex.IsMatch(page.Text, @"(?i)^exhibit\s+[a-z0-9\-]+", RegexOptions.Multiline))
                break;

            endPage = page.PageNumber;

            if (page.PageNumber > startPage + 3 && ScoreRmaContent(page.Text) < 10)
                break;
        }

        endPage = Math.Min(totalPages, Math.Max(endPage, startPage + 4));

        return BuildResult(
            method: "keyword-scan",
            startPage: startPage,
            endPage: endPage,
            totalPages: totalPages,
            padding: options.Padding,
            listedStart: null,
            listedEnd: null,
            pageOffset: 0,
            pageOffsetMethod: "n/a",
            tocEntry: null,
            notes: $"Chunk scan hit pages {refineStart}-{refineEnd} (score {bestChunkScore}); refined to PDF pages {startPage}-{endPage}.");
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
