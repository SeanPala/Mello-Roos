using System.Text.Json;
using System.Text.RegularExpressions;

namespace MelloRoos;

/// <summary>
/// Uses Gemini vision to classify page images (reliable on scanned bond appendices).
/// </summary>
public static class VisionSectionLocator
{
    public const int LocateDpi = 150;
    public const string DefaultModel = LlmExtractor.DefaultOpenAiVisionModel;

    public static int BinarySearchAppendixStart(
        string pdfPath,
        int searchLow,
        int searchHigh,
        string appendixLetter,
        string? appendixTitle,
        string model,
        string apiKey)
    {
        var result = -1;
        var low = searchLow;
        var high = searchHigh;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            Console.Error.WriteLine($"  vision probe page {mid}...");

            if (PageMatchesAppendixStart(pdfPath, mid, appendixLetter, appendixTitle, model, apiKey))
            {
                result = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return result;
    }

    private static bool PageMatchesAppendixStart(
        string pdfPath,
        int page,
        string appendixLetter,
        string? appendixTitle,
        string model,
        string apiKey)
    {
        var pages = PdfPageImages.Render(pdfPath, page, page, LocateDpi);
        var image = pages[0];
        var titleHint = appendixTitle
            ?? $"Appendix {appendixLetter} Rate and Method of Apportionment of Special Tax";

        var json = GeminiClient.GenerateVisionJsonAsync(
            """
            You classify scanned bond PDF page images.
            Respond with JSON only: {"match":true} or {"match":false}.
            """,
            $"""
            Does this page contain the START of the following section (large heading / opening page, not a TOC listing)?

            "{titleHint}"

            match=true only if Appendix {appendixLetter} RMA content clearly begins on this page.
            """,
            [(image.Bytes, "image/png")],
            model,
            apiKey).GetAwaiter().GetResult();

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("match", out var match)
                && match.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return Regex.IsMatch(json, @"(?i)""true""|match\s*:\s*true");
        }
    }
}
