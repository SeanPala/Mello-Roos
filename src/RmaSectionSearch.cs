using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

/// <summary>
/// Locates appendix/RMA sections by body-text chunk scan and optional vision classification.
/// </summary>
public static class RmaSectionSearch
{
    private const int EndMissThreshold = 3;

    public static (int StartPage, int EndPage) FindSectionBounds(
        string pdfPath,
        RmaLocateOptions options,
        int totalPages,
        int searchLow,
        int searchHigh,
        string? appendixLetter = null,
        string? appendixTitle = null)
    {
        searchLow = Math.Max(1, searchLow);
        searchHigh = Math.Min(totalPages, searchHigh);
        if (searchLow > searchHigh)
            (searchLow, searchHigh) = (DefaultAppendixSearchLow(totalPages), totalPages);

        if (appendixLetter is not null)
        {
            return FindAppendixSectionBounds(
                pdfPath, options, totalPages, searchLow, searchHigh, appendixLetter, appendixTitle);
        }

        return FindRmaSectionByKeywordScan(pdfPath, options, totalPages, searchLow, searchHigh);
    }

    public static (int Low, int High) BoundsFromTocBracket(
        IReadOnlyList<TocEntry> entries,
        string appendixTitle,
        int offset,
        int totalPages)
    {
        var letter = TocParser.ExtractAppendixLetter(appendixTitle);
        if (letter is null || entries.Count == 0)
            return (DefaultAppendixSearchLow(totalPages), totalPages);

        var sorted = entries.OrderBy(e => e.PageNumber).ToList();
        var nextAppendix = sorted.FirstOrDefault(e =>
            TocParser.ExtractAppendixLetter(e.Title) is string l
            && string.CompareOrdinal(l, letter) > 0);

        var listedHigh = nextAppendix?.PageNumber ?? (totalPages - offset);
        var listedLow = sorted.LastOrDefault(e => e.PageNumber < listedHigh)?.PageNumber ?? 1;

        var pdfLow = Math.Max(DefaultAppendixSearchLow(totalPages), PageOffsetDetector.ToPdfPage(listedLow, offset) - 10);
        var pdfHigh = Math.Min(totalPages, PageOffsetDetector.ToPdfPage(listedHigh, offset) + 5);

        if (pdfLow >= pdfHigh)
            return (DefaultAppendixSearchLow(totalPages), totalPages);

        Console.Error.WriteLine(
            $"TOC bracket: listed {listedLow}-{listedHigh}, offset {offset} → search PDF {pdfLow}-{pdfHigh}.");

        return (pdfLow, pdfHigh);
    }

    public static int DefaultAppendixSearchLow(int totalPages) =>
        Math.Max(1, (totalPages * 2) / 3);

    private static (int StartPage, int EndPage) FindAppendixSectionBounds(
        string pdfPath,
        RmaLocateOptions options,
        int totalPages,
        int searchLow,
        int searchHigh,
        string appendixLetter,
        string? appendixTitle)
    {
        Console.Error.WriteLine(
            $"Locating Appendix {appendixLetter} by body-text chunk scan (PDF {searchLow}-{searchHigh})...");

        var start = FindAppendixStartByChunkScan(
            pdfPath, options, searchLow, searchHigh, appendixLetter, appendixTitle);

        if (start < 0 && options.UseVisionLocate && GeminiClient.ResolveApiKey() is { } apiKey)
        {
            Console.Error.WriteLine(
                $"Chunk scan did not find Appendix {appendixLetter}; trying vision page classification...");
            start = VisionSectionLocator.BinarySearchAppendixStart(
                pdfPath, searchLow, searchHigh, appendixLetter, appendixTitle,
                options.VisionLocateModel, apiKey);
        }

        if (start < 0)
        {
            throw new InvalidOperationException(
                $"Could not locate Appendix {appendixLetter} in PDF pages {searchLow}-{searchHigh}. " +
                "Try --pages N-M if you know the range, or ensure GEMINI_API_KEY is set for vision locate.");
        }

        var end = CollectSectionEndByKeywordScan(
            pdfPath, options, start, totalPages, options.MaxSpan, appendixLetter);
        Console.Error.WriteLine($"Appendix {appendixLetter}: PDF pages {start}-{end}.");

        return (start, end);
    }

    private static int FindAppendixStartByChunkScan(
        string pdfPath,
        RmaLocateOptions options,
        int searchLow,
        int searchHigh,
        string appendixLetter,
        string? appendixTitle)
    {
        var chunkSize = Math.Max(4, options.ContentChunkSize);

        for (var chunkStart = searchLow; chunkStart <= searchHigh; chunkStart += chunkSize)
        {
            var chunkEnd = Math.Min(searchHigh, chunkStart + chunkSize - 1);
            Console.Error.WriteLine($"  OCR chunk {chunkStart}-{chunkEnd} for Appendix {appendixLetter} heading...");

            var acquired = AcquireChunk(pdfPath, options, chunkStart, chunkEnd);

            foreach (var page in TextAcquisition.SplitMarkedPages(acquired.Text, chunkStart))
            {
                if (PageLooksLikeAppendixStart(page.Text, appendixLetter, appendixTitle))
                {
                    Console.Error.WriteLine($"  → Appendix {appendixLetter} opens on PDF page {page.PageNumber}");
                    return page.PageNumber;
                }
            }
        }

        return -1;
    }

    private static (int StartPage, int EndPage) FindRmaSectionByKeywordScan(
        string pdfPath,
        RmaLocateOptions options,
        int totalPages,
        int searchLow,
        int searchHigh)
    {
        Console.Error.WriteLine(
            $"Locating RMA by keyword scan (PDF {searchLow}-{searchHigh})...");

        var start = FindKeywordScanStart(pdfPath, options, searchLow, searchHigh);

        if (start < 0 && options.UseVisionLocate && GeminiClient.ResolveApiKey() is { } apiKey)
        {
            Console.Error.WriteLine("Keyword scan missed; trying vision page classification...");
            start = VisionSectionLocator.BinarySearchAppendixStart(
                pdfPath, searchLow, searchHigh, appendixLetter: "D",
                "Rate and Method of Apportionment of Special Tax",
                options.VisionLocateModel, apiKey);
        }

        if (start < 0)
        {
            throw new InvalidOperationException(
                $"Could not find RMA section by keyword scan in PDF pages {searchLow}-{searchHigh}.");
        }

        var end = CollectSectionEndByKeywordScan(
            pdfPath, options, start, totalPages, options.MaxSpan, appendixLetter: null);

        Console.Error.WriteLine($"RMA keyword scan: PDF pages {start}-{end}.");
        return (start, end);
    }

    private static int FindKeywordScanStart(
        string pdfPath,
        RmaLocateOptions options,
        int searchLow,
        int searchHigh)
    {
        var chunkSize = Math.Max(4, options.ContentChunkSize);

        for (var chunkStart = searchLow; chunkStart <= searchHigh; chunkStart += chunkSize)
        {
            var chunkEnd = Math.Min(searchHigh, chunkStart + chunkSize - 1);
            Console.Error.WriteLine($"  OCR chunk {chunkStart}-{chunkEnd} for RMA keywords...");

            var acquired = AcquireChunk(pdfPath, options, chunkStart, chunkEnd);

            foreach (var page in TextAcquisition.SplitMarkedPages(acquired.Text, chunkStart))
            {
                if (RmaKeywordMatcher.IsSectionStart(page.Text))
                {
                    Console.Error.WriteLine(
                        $"  → RMA keywords detected on PDF page {page.PageNumber} (score {RmaLocator.ScoreRmaContent(page.Text)})");
                    return page.PageNumber;
                }
            }
        }

        return -1;
    }

    private static int CollectSectionEndByKeywordScan(
        string pdfPath,
        RmaLocateOptions options,
        int startPage,
        int totalPages,
        int maxSpan,
        string? appendixLetter)
    {
        var maxEnd = Math.Min(totalPages, startPage + maxSpan - 1);
        var endPage = startPage;
        var misses = 0;
        var chunkSize = Math.Max(4, options.ContentChunkSize);

        for (var chunkStart = startPage; chunkStart <= maxEnd; chunkStart += chunkSize)
        {
            var chunkEnd = Math.Min(maxEnd, chunkStart + chunkSize - 1);

            var acquired = AcquireChunk(pdfPath, options, chunkStart, chunkEnd);

            foreach (var page in TextAcquisition.SplitMarkedPages(acquired.Text, chunkStart))
            {
                if (appendixLetter is not null
                    && page.PageNumber > startPage + 3
                    && PageLooksLikeAppendixStart(page.Text, appendixLetter, appendixTitle: null))
                {
                    endPage = page.PageNumber;
                    misses = 0;
                    continue;
                }

                if (page.PageNumber > startPage + 2 && RmaKeywordMatcher.IsHardSectionEnd(page.Text))
                {
                    Console.Error.WriteLine($"  → section ends before PDF page {page.PageNumber} (hard boundary)");
                    return Math.Max(startPage, page.PageNumber - 1);
                }

                if (RmaKeywordMatcher.IsSectionContinued(page.Text)
                    || (appendixLetter is not null && PageLooksLikeAppendixStart(page.Text, appendixLetter, null)))
                {
                    endPage = page.PageNumber;
                    misses = 0;
                    continue;
                }

                misses++;
                if (misses >= EndMissThreshold && page.PageNumber > startPage + 2)
                {
                    Console.Error.WriteLine(
                        $"  → section ends at PDF page {endPage} ({EndMissThreshold} irrelevant pages)");
                    return endPage;
                }
            }
        }

        return Math.Max(endPage, Math.Min(totalPages, startPage + 4));
    }

    private static TextAcquisitionResult AcquireChunk(
        string pdfPath,
        RmaLocateOptions options,
        int chunkStart,
        int chunkEnd) =>
        TextAcquisition.Acquire(pdfPath, new TextAcquisitionOptions
        {
            ForceOcr = options.ForceOcr,
            FirstPage = chunkStart,
            LastPage = chunkEnd,
            Dpi = options.Dpi,
            TesseractPsm = options.TesseractPsm
        }, options.PageCache);

    internal static bool PageLooksLikeAppendixStart(
        string text,
        string appendixLetter,
        string? appendixTitle)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (appendixTitle is not null
            && text.Contains(appendixTitle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hasAppendix = Regex.IsMatch(text, $@"(?i)appendix\s+{Regex.Escape(appendixLetter)}\b");
        var hasRma = Regex.IsMatch(
            text,
            @"(?i)rate\s+and\s+method|apportionment\s+of\s+special|maximum\s+special\s+tax|\btable\s+1\b");

        if (hasAppendix && hasRma)
            return true;

        return PageLooksLikeRmaStart(text, appendixLetter);
    }

    internal static bool PageLooksLikeRmaStart(string text, string? appendixLetter)
    {
        if (RmaKeywordMatcher.IsSectionStart(text))
            return true;

        if (RmaLocator.ScoreRmaContent(text) < 50)
            return false;

        if (Regex.IsMatch(text, @"(?i)rate\s+and\s+method\s+of\s+apportionment|maximum\s+special\s+tax"))
            return true;

        if (appendixLetter is null)
            return Regex.IsMatch(text, @"(?i)\btable\s+1\b");

        return Regex.IsMatch(text, $@"(?i)appendix\s+{Regex.Escape(appendixLetter)}\b")
            && Regex.IsMatch(text, @"(?i)rate\s+and\s+method|maximum\s+special\s+tax|\btable\s+1\b");
    }
}
