using MelloRoos.Models;

namespace MelloRoos;

public sealed class AnalyzeOptions
{
    public required string PdfPath { get; init; }
    public required int DebtId { get; init; }
    public DateOnly RunDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public int LandUseType { get; init; }
    public bool ForceOcr { get; init; }
    public int? FirstPage { get; init; }
    public int? LastPage { get; init; }
    public int Dpi { get; init; } = TextAcquisition.DefaultDpi;
    public string TesseractPsm { get; init; } = TextAcquisition.DefaultPsm;
    public string VisionModel { get; init; } = LlmExtractor.DefaultOpenAiVisionModel;
    public LlmProvider VisionProvider { get; init; } = LlmExtractor.DefaultProvider;
    public bool LlamaParseFallback { get; init; } = true;
    public bool TextractFallback { get; init; }
    public string? SaveTextPath { get; init; }
    public string? SaveJsonPath { get; init; }
    public string? SqlOutputPath { get; init; }
    public LlmProvider LlmProvider { get; init; } = LlmExtractor.DefaultProvider;
    public string LlmModel { get; init; } = LlmExtractor.DefaultOpenAiModel;
}

public sealed class AnalyzeResult
{
    public required TextAcquisitionResult TextResult { get; init; }
    public required ExtractionResult Extraction { get; init; }
    public required string Sql { get; init; }
    public RmaDiscoveryResult? Discovery { get; init; }
}

/// <summary>
/// Long-document entry point — uses the same discovery + vision/IDP pipeline as extract, emits SQL directly.
/// </summary>
public static class DocumentAnalyzer
{
    public static async Task<AnalyzeResult> RunAsync(AnalyzeOptions options, CancellationToken ct = default)
    {
        var pipelineOptions = new PipelineOptions
        {
            PdfPath = options.PdfPath,
            DebtId = options.DebtId,
            RunDate = options.RunDate,
            LandUseType = options.LandUseType,
            ForceOcr = options.ForceOcr,
            FirstPage = options.FirstPage,
            LastPage = options.LastPage,
            Dpi = options.Dpi,
            TesseractPsm = options.TesseractPsm,
            VisionProvider = options.VisionProvider,
            VisionModel = options.VisionModel,
            LlamaParseFallback = options.LlamaParseFallback,
            TextractFallback = options.TextractFallback,
            LlmProvider = options.LlmProvider,
            LlmModel = options.LlmModel,
            SaveTextPath = options.SaveTextPath,
            SaveJsonPath = options.SaveJsonPath,
            SqlOutputPath = options.SqlOutputPath,
            Force = true,
            AutoLocate = options.FirstPage is null && options.LastPage is null
        };

        var result = await Pipeline.RunExtractAsync(pipelineOptions, ct);

        if (result.TextResult is null)
            throw new InvalidOperationException("Analyze produced no text result.");

        return new AnalyzeResult
        {
            TextResult = result.TextResult,
            Extraction = result.Extraction,
            Sql = result.Sql,
            Discovery = result.Discovery
        };
    }
}
