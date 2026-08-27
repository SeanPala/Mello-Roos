using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

public sealed class RmaDiscoveryOptions
{
    public required string PdfPath { get; init; }
    public bool ForceOcr { get; init; }
    public int Dpi { get; init; } = TextAcquisition.DefaultDpi;
    public string TesseractPsm { get; init; } = TextAcquisition.DefaultPsm;
    public int TocFirstPage { get; init; } = RmaLocateOptions.DefaultTocFirstPage;
    public int TocLastPage { get; init; } = RmaLocateOptions.DefaultTocLastPage;
    public int ChunkSize { get; init; } = 30;
    public int MaxSpan { get; init; } = 40;
    public int Padding { get; init; } = 2;
    public bool TocLoose { get; init; } = true;
    public int TocMinScore { get; init; } = 35;
}

public sealed class RmaDiscoveryResult
{
    public required int ExtractFirst { get; init; }
    public required int ExtractLast { get; init; }
    public int? TableFirst { get; init; }
    public int? TableLast { get; init; }
    public required string Method { get; init; }
    public required int ConfidenceScore { get; init; }
    public required bool LowConfidence { get; init; }
    public required string Notes { get; init; }
    public string? TocEntry { get; init; }
    public TextAcquisitionResult? PrefetchedSectionText { get; init; }
}

/// <summary>
/// Locates RMA sections in large bond PDFs: TOC + offset first, then body-text keyword scan when page refs are unclear.
/// </summary>
public static class RmaDiscoveryService
{
    private const int TableWindowPadding = 2;
    private const int StrongScore = 100;

    public static RmaDiscoveryResult Discover(RmaDiscoveryOptions options)
    {
        var totalPages = TextAcquisition.GetPageCount(options.PdfPath)
            ?? throw new InvalidOperationException("Could not determine PDF page count (pdfinfo required).");

        if (totalPages <= TextAcquisition.LargePdfPageThreshold)
        {
            return new RmaDiscoveryResult
            {
                ExtractFirst = 1,
                ExtractLast = totalPages,
                Method = "full-document",
                ConfidenceScore = 100,
                LowConfidence = false,
                Notes = $"Short PDF ({totalPages} pages); using entire document."
            };
        }

        Console.Error.WriteLine($"Large PDF ({totalPages} pages): TOC + offset → keyword scan fallback...");

        var pageCache = new PageTextCache();
        var locate = RmaLocator.Locate(options.PdfPath, new RmaLocateOptions
        {
            PdfPath = options.PdfPath,
            TocFirstPage = options.TocFirstPage,
            TocLastPage = Math.Min(options.TocLastPage, totalPages),
            ChunkSize = options.ChunkSize,
            Padding = options.Padding,
            MaxSpan = options.MaxSpan,
            ForceOcr = options.ForceOcr,
            Dpi = options.Dpi,
            TesseractPsm = options.TesseractPsm,
            TocLoose = options.TocLoose,
            TocMinScore = options.TocMinScore,
            AutoPageOffset = true,
            PageCache = pageCache
        });

        var acquisitionOptions = new TextAcquisitionOptions
        {
            ForceOcr = options.ForceOcr,
            FirstPage = locate.StartPage,
            LastPage = locate.EndPage,
            Dpi = options.Dpi,
            TesseractPsm = options.TesseractPsm
        };

        var sectionTextResult = TextAcquisition.Acquire(
            options.PdfPath, acquisitionOptions, pageCache);

        var sectionText = sectionTextResult.Text;

        var confidenceScore = RmaLocator.ScoreRmaContent(sectionText);
        var tableWindow = DetectTableWindow(sectionText, locate.StartPage, totalPages)
            ?? (locate.StartPage, Math.Min(totalPages, locate.StartPage + 6));

        var lowConfidence = confidenceScore < StrongScore
            || locate.Method.EndsWith("-expanded", StringComparison.Ordinal)
            || !Regex.IsMatch(sectionText, @"(?i)\btable\s+1\b");

        Console.Error.WriteLine(
            $"Discovery: {locate.Method} → PDF pages {locate.StartPage}-{locate.EndPage}, " +
            $"Table 1 window {tableWindow.first}-{tableWindow.last}, score {confidenceScore}" +
            (lowConfidence ? " (low confidence)" : ""));

        return new RmaDiscoveryResult
        {
            ExtractFirst = locate.StartPage,
            ExtractLast = locate.EndPage,
            TableFirst = tableWindow.first,
            TableLast = tableWindow.last,
            Method = locate.Method,
            ConfidenceScore = confidenceScore,
            LowConfidence = lowConfidence,
            Notes = locate.Notes ?? $"Located via {locate.Method}.",
            TocEntry = locate.TocEntry,
            PrefetchedSectionText = sectionTextResult
        };
    }

    private static (int first, int last)? DetectTableWindow(string text, int rangeFirst, int totalPages)
    {
        var tablePage = TableExtractionService.DetectTable1Page(text);
        if (tablePage is null)
            return null;

        return (
            Math.Max(rangeFirst, tablePage.Value - TableWindowPadding),
            Math.Min(totalPages, tablePage.Value + TableWindowPadding));
    }
}
