using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

public sealed class TableExtractionOptions
{
    public required string PdfPath { get; init; }
    public required int FirstPage { get; init; }
    public required int LastPage { get; init; }
    public int Dpi { get; init; } = PdfPageImages.TableDpi;
    public LlmProvider VisionProvider { get; init; } = LlmProvider.Gemini;
    public string Model { get; init; } = LlmExtractor.DefaultGeminiVisionModel;
    public bool UseVision { get; init; } = true;
    public bool UseLlamaParse { get; init; }
    public bool UseTextract { get; init; }
    public string? SupplementalText { get; init; }
}

public sealed record TableExtractionResult
{
    public required List<RateClass> RateClasses { get; init; }
    public required string Method { get; init; }
    public required string ExtractionConfidence { get; init; }
    public required List<string> Flags { get; init; }
}

public static class RateClassMerger
{
    public static void Merge(ExtractionResult extraction, TableExtractionResult tableResult)
    {
        foreach (var incoming in tableResult.RateClasses.OrderBy(r => r.DisplayOrder))
        {
            var existing = extraction.RateClasses.FirstOrDefault(r => r.ClassId == incoming.ClassId);
            if (existing is null)
            {
                extraction.RateClasses.Add(incoming);
                continue;
            }

            MergeField(existing.MaxTaxRate, incoming.MaxTaxRate, v => existing.MaxTaxRate = v);
            MergeField(existing.MaxTaxUnit, incoming.MaxTaxUnit, v => existing.MaxTaxUnit = v);
            MergeField(existing.ClassName, incoming.ClassName, v => existing.ClassName = v);
            MergeField(existing.ClassDescription, incoming.ClassDescription, v => existing.ClassDescription = v);
            MergeField(existing.LandUse, incoming.LandUse, v => existing.LandUse = v);
            MergeField(existing.BackupTaxRate, incoming.BackupTaxRate, v => existing.BackupTaxRate = v);
            MergeField(existing.BackupTaxText, incoming.BackupTaxText, v => existing.BackupTaxText = v);

            if (incoming.BackupTaxFlag)
                existing.BackupTaxFlag = true;
        }

        extraction.RateClasses.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

        extraction.Flags.RemoveAll(f =>
            f.StartsWith("table_", StringComparison.OrdinalIgnoreCase)
            || f.Contains("ocr_garbled", StringComparison.OrdinalIgnoreCase)
            || f.Contains("missing_classes", StringComparison.OrdinalIgnoreCase));

        foreach (var flag in tableResult.Flags)
        {
            if (!extraction.Flags.Contains(flag))
                extraction.Flags.Add(flag);
        }

        if (tableResult.Flags.Count == 0 && tableResult.ExtractionConfidence is "high" or "medium")
            extraction.ExtractionConfidence = tableResult.ExtractionConfidence;
    }

    private static void MergeField<T>(T? existing, T? incoming, Action<T> assign)
    {
        if (incoming is null)
            return;

        if (existing is null || (existing is string s && string.IsNullOrWhiteSpace(s)))
            assign(incoming);
    }
}

public static class TableExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<TableExtractionResult> ExtractAsync(
        TableExtractionOptions options,
        CancellationToken ct = default)
    {
        TableExtractionResult? result = null;

        if (options.UseVision)
        {
            var providerLabel = VisionLlmClient.ProviderLabel(options.VisionProvider);
            Console.Error.WriteLine(
                $"Table extract: {providerLabel} vision on pages {options.FirstPage}-{options.LastPage} at {options.Dpi} DPI...");
            result = await ExtractWithVisionAsync(options, ct);
            if (IsComplete(result))
            {
                Console.Error.WriteLine($"Table extract: vision OK ({result.RateClasses.Count} classes, method={result.Method}).");
                return result;
            }

            Console.Error.WriteLine($"Table extract: vision incomplete ({MissingRateSummary(result)}).");
        }

        if (options.UseLlamaParse)
        {
            Console.Error.WriteLine("Table extract: trying LlamaParse...");
            var llamaparse = await ExtractWithLlamaParseAsync(options, ct);
            result = MergeAttempts(result, llamaparse);
            if (IsComplete(result))
            {
                Console.Error.WriteLine($"Table extract: LlamaParse OK ({result.RateClasses.Count} classes).");
                return result;
            }

            Console.Error.WriteLine($"Table extract: LlamaParse incomplete ({MissingRateSummary(result)}).");
        }

        if (options.UseTextract)
        {
            Console.Error.WriteLine("Table extract: trying AWS Textract...");
            var textract = await ExtractWithTextractAsync(options, ct);
            result = MergeAttempts(result, textract);
            if (IsComplete(result))
                Console.Error.WriteLine($"Table extract: Textract OK ({result.RateClasses.Count} classes).");
            else
                Console.Error.WriteLine($"Table extract: still incomplete ({MissingRateSummary(result)}).");
        }

        return result ?? new TableExtractionResult
        {
            RateClasses = [],
            Method = "none",
            ExtractionConfidence = "low",
            Flags = ["table_extraction_failed"]
        };
    }

    public static (int first, int last)? ResolveTablePages(
        string? tablePages,
        int? extractFirst,
        int? extractLast,
        string? ocrText)
    {
        if (!string.IsNullOrWhiteSpace(tablePages))
        {
            var (first, last) = Pipeline.ParsePageRange(tablePages);
            if (first is not null && last is not null)
                return (first.Value, last.Value);
        }

        if (extractFirst is not null && extractLast is not null)
            return (extractFirst.Value, extractLast.Value);

        var detected = DetectTable1Page(ocrText);
        if (detected is not null)
            return (Math.Max(1, detected.Value - 1), detected.Value + 1);

        return null;
    }

    public static int? DetectTable1Page(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return null;

        var pages = TextAcquisition.SplitMarkedPages(ocrText);
        foreach (var page in pages)
        {
            if (Regex.IsMatch(page.Text, @"\bTABLE\s+1\b", RegexOptions.IgnoreCase)
                && Regex.IsMatch(page.Text, @"(?i)(assigned\s+special\s+tax|land\s+use\s+class|maximum\s+special\s+tax)"))
                return page.PageNumber;
        }

        return null;
    }

    private static async Task<TableExtractionResult> ExtractWithVisionAsync(
        TableExtractionOptions options,
        CancellationToken ct)
    {
        var pages = PdfPageImages.Render(options.PdfPath, options.FirstPage, options.LastPage, options.Dpi);
        var providerLabel = VisionLlmClient.ProviderLabel(options.VisionProvider);
        Console.Error.WriteLine(
            $"Table extract: rendered {pages.Count} page image(s) at {options.Dpi} DPI; calling {providerLabel}/{options.Model}...");
        var images = pages.Select(p => (p.Bytes, "image/png")).ToList();

        var json = await VisionLlmClient.GenerateVisionJsonAsync(
            options.VisionProvider,
            TableRatePrompt.SystemPrompt,
            TableRatePrompt.VisionUserPrompt(pages),
            images,
            options.Model,
            ct);

        var parsed = ParseTableJson(json);
        parsed = parsed with { Method = $"vision-{providerLabel}" };
        return parsed;
    }

    private static async Task<TableExtractionResult> ExtractWithLlamaParseAsync(
        TableExtractionOptions options,
        CancellationToken ct)
    {
        var markdown = await LlamaParseClient.ParsePagesAsync(
            options.PdfPath,
            options.FirstPage,
            options.LastPage,
            ct);

        var providerLabel = VisionLlmClient.ProviderLabel(options.VisionProvider);
        var json = await VisionLlmClient.GenerateJsonAsync(
            options.VisionProvider,
            TableRatePrompt.SystemPrompt,
            TableRatePrompt.TextUserPrompt(markdown),
            options.Model,
            ct);

        var parsed = ParseTableJson(json);
        return parsed with { Method = $"llamaparse+{providerLabel}" };
    }

    private static async Task<TableExtractionResult> ExtractWithTextractAsync(
        TableExtractionOptions options,
        CancellationToken ct)
    {
        var pages = PdfPageImages.Render(options.PdfPath, options.FirstPage, options.LastPage, options.Dpi);
        var tableText = await TextractClient.ExtractTablesAsync(pages, ct);

        var supplemental = string.IsNullOrWhiteSpace(options.SupplementalText)
            ? ""
            : $"\n\n=== OCR CONTEXT ===\n{options.SupplementalText}";

        var providerLabel = VisionLlmClient.ProviderLabel(options.VisionProvider);
        var json = await VisionLlmClient.GenerateJsonAsync(
            options.VisionProvider,
            TableRatePrompt.SystemPrompt,
            TableRatePrompt.TextUserPrompt(tableText + supplemental),
            options.Model,
            ct);

        var parsed = ParseTableJson(json);
        return parsed with { Method = $"textract+{providerLabel}" };
    }

    private static TableExtractionResult ParseTableJson(string json)
    {
        json = StripMarkdownFences(json.Trim());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var rateClasses = root.TryGetProperty("rate_classes", out var rc)
            ? JsonSerializer.Deserialize<List<RateClass>>(rc.GetRawText(), JsonOptions) ?? []
            : [];

        var confidence = root.TryGetProperty("extraction_confidence", out var conf)
            ? conf.GetString() ?? "medium"
            : "medium";

        var flags = root.TryGetProperty("flags", out var fl)
            ? JsonSerializer.Deserialize<List<string>>(fl.GetRawText(), JsonOptions) ?? []
            : [];

        if (rateClasses.Count == 0)
            flags.Add("table_no_rate_classes");

        return new TableExtractionResult
        {
            RateClasses = rateClasses,
            Method = "unknown",
            ExtractionConfidence = confidence,
            Flags = flags
        };
    }

    private static TableExtractionResult MergeAttempts(TableExtractionResult? baseline, TableExtractionResult incoming)
    {
        if (baseline is null)
            return incoming;

        var merged = baseline.RateClasses.ToDictionary(r => r.ClassId);
        foreach (var row in incoming.RateClasses)
        {
            if (!merged.TryGetValue(row.ClassId, out var existing))
            {
                merged[row.ClassId] = row;
                continue;
            }

            if (existing.MaxTaxRate is null && row.MaxTaxRate is not null)
                existing.MaxTaxRate = row.MaxTaxRate;
            if (string.IsNullOrWhiteSpace(existing.MaxTaxUnit) && !string.IsNullOrWhiteSpace(row.MaxTaxUnit))
                existing.MaxTaxUnit = row.MaxTaxUnit;
            if (string.IsNullOrWhiteSpace(existing.ClassDescription) && !string.IsNullOrWhiteSpace(row.ClassDescription))
                existing.ClassDescription = row.ClassDescription;
            if (existing.BackupTaxRate is null && row.BackupTaxRate is not null)
                existing.BackupTaxRate = row.BackupTaxRate;
        }

        var flags = baseline.Flags.Concat(incoming.Flags).Distinct().ToList();
        return new TableExtractionResult
        {
            RateClasses = merged.Values.OrderBy(r => r.DisplayOrder).ToList(),
            Method = $"{baseline.Method}+{incoming.Method}",
            ExtractionConfidence = incoming.ExtractionConfidence,
            Flags = flags
        };
    }

    public static bool IsComplete(TableExtractionResult result)
    {
        if (result.RateClasses.Count == 0)
            return false;

        var missingRates = result.RateClasses.Count(r => r.MaxTaxRate is null);
        return missingRates == 0;
    }

    private static string MissingRateSummary(TableExtractionResult result) =>
        $"{result.RateClasses.Count(r => r.MaxTaxRate is null)} classes missing max_tax_rate";

    private static string StripMarkdownFences(string content)
    {
        if (!content.StartsWith("```", StringComparison.Ordinal))
            return content;

        var lines = content.Split('\n');
        return string.Join('\n', lines.Skip(1).TakeWhile(l => !l.TrimStart().StartsWith("```")));
    }
}
