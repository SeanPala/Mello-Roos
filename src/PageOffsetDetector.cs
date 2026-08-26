using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

public sealed class PageOffsetResult
{
    public required int Offset { get; init; }
    public required string Method { get; init; }
    public string? Notes { get; init; }
}

public static class PageOffsetDetector
{
    // pdfPage = listedPage + Offset
    public static PageOffsetResult Resolve(
        string pdfPath,
        RmaLocateOptions options,
        int totalPages,
        IReadOnlyList<TocEntry>? tocEntries = null)
    {
        if (options.PageOffset is int manual)
        {
            return new PageOffsetResult
            {
                Offset = manual,
                Method = "manual",
                Notes = $"Using manual offset {manual} (pdf = listed + {manual})."
            };
        }

        if (!options.AutoPageOffset)
        {
            return new PageOffsetResult
            {
                Offset = 0,
                Method = "none",
                Notes = "Auto offset disabled; listed page numbers assumed equal to PDF pages."
            };
        }

        var fromPrinted = DetectFromPrintedNumbers(pdfPath, options, totalPages);
        if (fromPrinted is not null)
            return fromPrinted;

        if (tocEntries is { Count: > 0 })
        {
            var fromToc = DetectFromTocRoman(tocEntries, totalPages);
            if (fromToc is not null)
                return fromToc;
        }

        return new PageOffsetResult
        {
            Offset = 0,
            Method = "none",
            Notes = "Could not detect page offset; assuming listed pages match PDF pages."
        };
    }

    public static int ToPdfPage(int listedPage, int offset) => listedPage + offset;

    public static bool IsValidPdfRange(int pdfStart, int pdfEnd, int totalPages) =>
        pdfStart >= 1 && pdfEnd >= pdfStart && pdfStart <= totalPages;

    private static PageOffsetResult? DetectFromPrintedNumbers(string pdfPath, RmaLocateOptions options, int totalPages)
    {
        var scanLast = Math.Min(totalPages, Math.Max(options.TocLastPage + 15, 50));
        Console.Error.WriteLine($"Detecting page offset from printed numbers (PDF pages 1-{scanLast}, batch OCR)...");

        var batch = TextAcquisition.Acquire(pdfPath, new TextAcquisitionOptions
        {
            ForceOcr = options.ForceOcr,
            FirstPage = 1,
            LastPage = scanLast,
            Dpi = options.Dpi,
            TesseractPsm = options.TesseractPsm
        });

        var pages = TextAcquisition.SplitMarkedPages(batch.Text, 1);
        var samples = new List<(int pdfPage, int printedPage)>();

        foreach (var page in pages)
        {
            var printed = TryExtractPrintedArabicPage(page.Text);
            if (printed is int n && TocParser.IsValidListedPage(n, totalPages))
                samples.Add((page.PageNumber, n));
        }

        if (samples.Count == 0)
            return null;

        var offsets = samples
            .Select(s => s.pdfPage - s.printedPage)
            .Where(o => o >= 0 && o < totalPages)
            .GroupBy(o => o)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .FirstOrDefault();

        if (offsets is null)
            return null;

        var offset = offsets.Key;
        var anchor = samples.First(s => s.pdfPage - s.printedPage == offset);

        return new PageOffsetResult
        {
            Offset = offset,
            Method = "auto-printed",
            Notes = $"Detected offset {offset}: PDF page {anchor.pdfPage} shows printed page {anchor.printedPage} ({samples.Count} samples, {offsets.Count()} agree)."
        };
    }

    private static PageOffsetResult? DetectFromTocRoman(IReadOnlyList<TocEntry> entries, int totalPages)
    {
        var romanEntries = entries.Where(e => Regex.IsMatch(e.Title, @"(?i)\b(i{1,3}|iv|vi{0,3}|ix|x{1,3})\b")).ToList();
        if (romanEntries.Count == 0)
            return null;

        var offset = romanEntries.Count;
        if (offset >= totalPages)
            return null;

        return new PageOffsetResult
        {
            Offset = offset,
            Method = "auto-toc-roman",
            Notes = $"Estimated offset {offset} from {romanEntries.Count} roman-numeral TOC entries (approximate)."
        };
    }

    internal static int? TryExtractPrintedArabicPage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var candidates = lines.Take(6).Concat(lines.TakeLast(8)).Distinct();

        foreach (var line in candidates)
        {
            if (Regex.IsMatch(line, @"^(page|pg\.?)\s*(\d{1,4})$", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(line, @"(\d{1,4})$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is >= 1 and <= 999)
                    return n;
            }

            if (Regex.IsMatch(line, @"^\d{1,3}$") && int.TryParse(line, out var solo) && solo >= 1)
                return solo;

            var dash = Regex.Match(line, @"^[\-–—]\s*(\d{1,4})\s*[\-–—]$");
            if (dash.Success && int.TryParse(dash.Groups[1].Value, out var dashed) && dashed is >= 1 and <= 999)
                return dashed;
        }

        return null;
    }
}
