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
    private const int MinAgreeingSamples = 2;
    private const int MaxSinglePageProbes = 10;

    // pdfPage = listedPage + Offset
    public static PageOffsetResult Resolve(
        string pdfPath,
        RmaLocateOptions options,
        int totalPages,
        IReadOnlyList<TocEntry>? tocEntries = null,
        string? frontMatterOcrText = null)
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

        var samples = CollectSamplesFromText(frontMatterOcrText, totalPages);
        if (TryResolveOffset(samples, totalPages, out var fromReuse))
        {
            return new PageOffsetResult
            {
                Offset = fromReuse.Offset,
                Method = "auto-printed",
                Notes = FormatNotes(fromReuse.Offset, samples, fromReuse: true)
            };
        }

        if (tocEntries is { Count: > 0 })
        {
            var fromToc = DetectFromTocRoman(tocEntries, totalPages);
            if (fromToc is not null)
                return fromToc;
        }

        var probeStart = frontMatterOcrText is not null
            ? Math.Min(totalPages, options.TocLastPage + 1)
            : Math.Min(totalPages, 10);

        var fromPrinted = DetectFromPrintedNumbers(pdfPath, options, totalPages, samples, probeStart);
        if (fromPrinted is not null)
            return fromPrinted;

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

    private static PageOffsetResult? DetectFromPrintedNumbers(
        string pdfPath,
        RmaLocateOptions options,
        int totalPages,
        List<(int PdfPage, int PrintedPage)> samples,
        int probeStart)
    {
        var probePages = BuildProbeSchedule(probeStart, totalPages, MaxSinglePageProbes)
            .Where(p => samples.All(s => s.PdfPage != p))
            .ToList();

        if (probePages.Count == 0)
            return null;

        Console.Error.WriteLine(
            $"Detecting page offset from printed numbers ({probePages.Count} margin-strip probes, starting PDF p.{probePages[0]})...");

        foreach (var page in probePages)
        {
            if (TextAcquisition.TryReadPrintedPageNumber(pdfPath, page, new TextAcquisitionOptions
                {
                    ForceOcr = options.ForceOcr,
                    Dpi = options.Dpi,
                    TesseractPsm = options.TesseractPsm
                }) is int printed
                && TocParser.IsValidListedPage(printed, totalPages))
            {
                samples.Add((page, printed));
            }

            if (TryResolveOffset(samples, totalPages, out var resolved))
            {
                return new PageOffsetResult
                {
                    Offset = resolved.Offset,
                    Method = "auto-printed",
                    Notes = FormatNotes(resolved.Offset, samples, fromReuse: false)
                };
            }
        }

        return null;
    }

    private static List<(int PdfPage, int PrintedPage)> CollectSamplesFromText(string? text, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return TextAcquisition.SplitMarkedPages(text, 1)
            .Select(page =>
            {
                var printed = TryExtractPrintedArabicPage(page.Text);
                return printed is int n && TocParser.IsValidListedPage(n, totalPages)
                    ? ((int PdfPage, int PrintedPage)?)(page.PageNumber, n)
                    : null;
            })
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .ToList();
    }

    private static List<int> BuildProbeSchedule(int start, int totalPages, int maxProbes)
    {
        var pages = new List<int>();
        var page = Math.Clamp(start, 1, totalPages);
        var step = 2;

        while (pages.Count < maxProbes && page <= totalPages)
        {
            pages.Add(page);
            page += step;
            if (pages.Count == 4)
                step = 3;
        }

        return pages;
    }

    private static bool TryResolveOffset(
        IReadOnlyList<(int PdfPage, int PrintedPage)> samples,
        int totalPages,
        out PageOffsetResult result)
    {
        result = null!;

        if (samples.Count < MinAgreeingSamples)
            return false;

        var offsets = samples
            .Select(s => s.PdfPage - s.PrintedPage)
            .Where(o => o >= 0 && o < totalPages)
            .GroupBy(o => o)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .FirstOrDefault();

        if (offsets is null || offsets.Count() < MinAgreeingSamples)
            return false;

        result = new PageOffsetResult { Offset = offsets.Key, Method = "auto-printed" };
        return true;
    }

    private static string FormatNotes(int offset, IReadOnlyList<(int PdfPage, int PrintedPage)> samples, bool fromReuse)
    {
        var agreeing = samples.Where(s => s.PdfPage - s.PrintedPage == offset).ToList();
        var anchor = agreeing[0];
        var source = fromReuse && agreeing.Count >= MinAgreeingSamples
            ? "reused front-matter OCR"
            : $"{samples.Count} probe sample(s)";
        return
            $"Detected offset {offset}: PDF page {anchor.PdfPage} shows printed page {anchor.PrintedPage} ({source}, {agreeing.Count} agree).";
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
