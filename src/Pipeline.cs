using System.Text.Json;
using MelloRoos.Models;

namespace MelloRoos;

public sealed class PipelineOptions
{
    public required string PdfPath { get; init; }
    public int DebtId { get; init; }
    public DateOnly RunDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public string? JsonPath { get; init; }
    public string? SaveTextPath { get; init; }
    public string? SaveJsonPath { get; init; }
    public string? SqlOutputPath { get; init; }
    public bool Force { get; init; }
    public bool ForceOcr { get; init; }
    public int? FirstPage { get; init; }
    public int? LastPage { get; init; }
    public int Dpi { get; init; } = TextAcquisition.DefaultDpi;
    public string TesseractPsm { get; init; } = TextAcquisition.DefaultPsm;
    public LlmProvider LlmProvider { get; init; } = LlmExtractor.DefaultProvider;
    public string LlmModel { get; init; } = LlmExtractor.DefaultOpenAiModel;
    public LlmProvider VisionProvider { get; init; } = LlmExtractor.DefaultProvider;
    public string VisionModel { get; init; } = LlmExtractor.DefaultOpenAiVisionModel;
    public int LandUseType { get; init; }
    public bool VisionTable { get; init; }
    public string? TablePages { get; init; }
    public int TableDpi { get; init; } = PdfPageImages.TableDpi;
    public bool LlamaParseFallback { get; init; } = true;
    public bool TextractFallback { get; init; }
    public bool AutoLocate { get; init; } = true;
}

public sealed class PipelineResult
{
    public required TextAcquisitionResult? TextResult { get; init; }
    public required ExtractionResult Extraction { get; init; }
    public required List<EscalatedRateClass> Escalated { get; init; }
    public required string Sql { get; init; }
    public required bool ReviewRequired { get; init; }
    public RmaDiscoveryResult? Discovery { get; init; }
}

public static class Pipeline
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public static async Task<PipelineResult> RunExtractAsync(PipelineOptions options, CancellationToken ct = default)
    {
        TextAcquisitionResult? textResult = null;
        ExtractionResult extraction;
        RmaDiscoveryResult? discovery = null;

        if (!string.IsNullOrWhiteSpace(options.JsonPath))
        {
            extraction = LlmExtractor.LoadFromJsonFile(options.JsonPath!);
        }
        else
        {
            var pageCount = TextAcquisition.GetPageCount(options.PdfPath);
            var isLargeDoc = pageCount is int total && total > TextAcquisition.LargePdfPageThreshold;
            var useLargeDocPath = isLargeDoc
                && options.FirstPage is null
                && options.LastPage is null
                && options.AutoLocate;

            if (useLargeDocPath)
            {
                (discovery, textResult, extraction) = await LargeDocumentPipeline.ExtractAsync(options, ct);
            }
            else
            {
                textResult = TextAcquisition.Acquire(options.PdfPath, new TextAcquisitionOptions
                {
                    ForceOcr = options.ForceOcr,
                    FirstPage = options.FirstPage,
                    LastPage = options.LastPage,
                    Dpi = options.Dpi,
                    TesseractPsm = options.TesseractPsm
                });

                if (!string.IsNullOrWhiteSpace(options.SaveTextPath))
                    await File.WriteAllTextAsync(options.SaveTextPath!, textResult.Text, ct);

                var extractor = new LlmExtractor();
                try
                {
                    extraction = await extractor.ExtractAsync(
                        textResult.Text, options.LlmProvider, options.LlmModel, ct);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Text LLM: all providers failed ({LlmProviderFallback.SummarizeError(ex)}); continuing with table extraction...");
                    extraction = LlmExtractor.CreateTextFailureShell(LlmProviderFallback.SummarizeError(ex));
                }

                if (ShouldRunTableExtraction(options, isLargeDoc))
                    await RunVisionTableMergeAsync(options, extraction, textResult, null, ct);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.SaveJsonPath))
        {
            var json = JsonSerializer.Serialize(extraction, JsonWriteOptions);
            await File.WriteAllTextAsync(options.SaveJsonPath!, json, ct);
        }

        var reviewRequired = NeedsReview(extraction);
        if (reviewRequired && !options.Force)
        {
            return new PipelineResult
            {
                TextResult = textResult,
                Extraction = extraction,
                Escalated = [],
                Sql = "",
                ReviewRequired = true,
                Discovery = discovery
            };
        }

        var escalated = EscalationService.Apply(extraction, options.RunDate);
        var sql = SqlGenerator.Generate(options.DebtId, escalated, options.LandUseType);

        if (!string.IsNullOrWhiteSpace(options.SqlOutputPath))
            await File.WriteAllTextAsync(options.SqlOutputPath!, sql, ct);

        return new PipelineResult
        {
            TextResult = textResult,
            Extraction = extraction,
            Escalated = escalated,
            Sql = sql,
            ReviewRequired = false,
            Discovery = discovery
        };
    }

    private static bool ShouldRunTableExtraction(PipelineOptions options, bool isLargeDoc) =>
        options.VisionTable || isLargeDoc;

    private static async Task RunVisionTableMergeAsync(
        PipelineOptions options,
        ExtractionResult extraction,
        TextAcquisitionResult textResult,
        RmaDiscoveryResult? discovery,
        CancellationToken ct)
    {
        var tablePages = TableExtractionService.ResolveTablePages(
            options.TablePages,
            options.FirstPage ?? discovery?.ExtractFirst,
            options.LastPage ?? discovery?.ExtractLast,
            textResult.Text);

        if (tablePages is null)
        {
            extraction.Flags.Add("table_pages_not_specified");
            Console.Error.WriteLine("Warning: vision table enabled but Table 1 page window unknown.");
            return;
        }

        var (tableFirst, tableLast) = tablePages.Value;
        Console.Error.WriteLine(
            $"Table extraction: pages {tableFirst}-{tableLast} " +
            $"(vision={VisionLlmClient.ProviderLabel(options.VisionProvider)}/{options.VisionModel}, " +
            $"llamaparse fallback={(options.LlamaParseFallback ? "on" : "off")})...");
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
    }

    public static bool NeedsReview(ExtractionResult extraction)
    {
        if (extraction.Flags.Count > 0)
            return true;

        return string.Equals(extraction.ExtractionConfidence, "low", StringComparison.OrdinalIgnoreCase);
    }

    public static (int? first, int? last) ParsePageRange(string? pages)
    {
        if (string.IsNullOrWhiteSpace(pages))
            return (null, null);

        var parts = pages.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out var single))
            return (single, single);

        if (parts.Length == 2
            && int.TryParse(parts[0], out var first)
            && int.TryParse(parts[1], out var last))
            return (first, last);

        throw new ArgumentException($"Invalid --pages value: {pages}. Use e.g. 1-18 or 5.");
    }
}
