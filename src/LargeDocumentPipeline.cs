using System.Text.Json;
using MelloRoos.Models;

namespace MelloRoos;

/// <summary>
/// Large bond-package flow: discover section (TOC + offset → keyword scan) → text LLM → vision/IDP table merge.
/// </summary>
public static class LargeDocumentPipeline
{
    public static async Task<(RmaDiscoveryResult Discovery, TextAcquisitionResult Text, ExtractionResult Extraction)>
        ExtractAsync(PipelineOptions options, CancellationToken ct = default)
    {
        var discovery = RmaDiscoveryService.Discover(new RmaDiscoveryOptions
        {
            PdfPath = options.PdfPath,
            ForceOcr = options.ForceOcr,
            Dpi = options.Dpi,
            TesseractPsm = options.TesseractPsm
        });

        var textResult = discovery.PrefetchedSectionText
            ?? TextAcquisition.Acquire(options.PdfPath, new TextAcquisitionOptions
            {
                ForceOcr = options.ForceOcr,
                FirstPage = discovery.ExtractFirst,
                LastPage = discovery.ExtractLast,
                Dpi = options.Dpi,
                TesseractPsm = options.TesseractPsm
            });

        Console.Error.WriteLine(
            $"Discovery complete. Section text: {textResult.CharCount:N0} chars ({textResult.Method}).");

        if (!string.IsNullOrWhiteSpace(options.SaveTextPath))
            await File.WriteAllTextAsync(options.SaveTextPath!, textResult.Text, ct);

        var extractor = new LlmExtractor();
        var extraction = await extractor.ExtractAsync(
            textResult.Text, options.LlmProvider, options.LlmModel, ct);

        Console.Error.WriteLine(
            $"Text LLM done: {extraction.RateClasses.Count} rate classes, confidence={extraction.ExtractionConfidence}.");

        ApplyDiscoveryFlags(extraction, discovery);

        var tableFirst = discovery.TableFirst ?? discovery.ExtractFirst;
        var tableLast = discovery.TableLast ?? Math.Min(discovery.ExtractLast, tableFirst + 6);

        Console.Error.WriteLine(
            $"Table extraction: {VisionLlmClient.ProviderLabel(options.VisionProvider)}/{options.VisionModel} on pages {tableFirst}-{tableLast}...");
        var tableResult = await TableExtractionService.ExtractAsync(new TableExtractionOptions
        {
            PdfPath = options.PdfPath,
            FirstPage = tableFirst,
            LastPage = tableLast,
            Dpi = options.TableDpi,
            VisionProvider = options.VisionProvider,
            Model = options.VisionModel,
            UseVision = true,
            UseLlamaParse = options.LlamaParseFallback,
            UseTextract = options.TextractFallback,
            SupplementalText = textResult.Text
        }, ct);

        RateClassMerger.Merge(extraction, tableResult);
        Console.Error.WriteLine($"Table merge: method={tableResult.Method}, classes={tableResult.RateClasses.Count}");

        return (discovery, textResult, extraction);
    }

    public static void ApplyDiscoveryFlags(ExtractionResult extraction, RmaDiscoveryResult discovery)
    {
        extraction.Flags.Add("auto_located_pages");
        extraction.Flags.Add($"discovery_method:{discovery.Method}");
        if (discovery.LowConfidence)
            extraction.Flags.Add("page_discovery_low_confidence");
    }
}
