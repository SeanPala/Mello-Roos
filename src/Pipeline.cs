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
    public LlmProvider LlmProvider { get; init; } = LlmProvider.Gemini;
    public string LlmModel { get; init; } = LlmExtractor.DefaultGeminiModel;
    public int LandUseType { get; init; }
}

public sealed class PipelineResult
{
    public required TextAcquisitionResult? TextResult { get; init; }
    public required ExtractionResult Extraction { get; init; }
    public required List<EscalatedRateClass> Escalated { get; init; }
    public required string Sql { get; init; }
    public required bool ReviewRequired { get; init; }
}

public static class Pipeline
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public static async Task<PipelineResult> RunExtractAsync(PipelineOptions options, CancellationToken ct = default)
    {
        TextAcquisitionResult? textResult = null;
        ExtractionResult extraction;

        var extractor = new LlmExtractor();

        if (!string.IsNullOrWhiteSpace(options.JsonPath))
        {
            extraction = LlmExtractor.LoadFromJsonFile(options.JsonPath!);
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

            extraction = await extractor.ExtractAsync(textResult.Text, options.LlmProvider, options.LlmModel, ct);
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
                ReviewRequired = true
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
            ReviewRequired = false
        };
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
