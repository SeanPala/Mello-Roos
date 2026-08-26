using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MelloRoos.Models;
using OpenAI.Chat;

namespace MelloRoos;

public enum LlmProvider
{
    Gemini,
    OpenAi,
    Claude
}

public sealed class LlmExtractor
{
    public const string DefaultGeminiModel = "gemini-3.6-flash";
    public const string DefaultOpenAiModel = "gpt-4o-mini";
    public const string DefaultClaudeModel = "claude-sonnet-4-20250514";

    private const int MaxPromptChars = 120_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly string SystemPrompt = """
        You extract Mello-Roos RMA rate tables into structured JSON for SQL loading.

        Return ONLY valid JSON matching this schema (no markdown fences):
        {
          "source": {
            "cfd_name": string,
            "agency": string,
            "base_fiscal_year": string (e.g. "2008-09"),
            "variant": "A"|"B"|"C"|"D"|"E"|"unknown",
            "escalation": {
              "type": "percent_annual"|"multiplier_annual"|"none",
              "rate": number|null (e.g. 0.02 for 2% annual),
              "multiplier": number|null (e.g. 1.02),
              "start": "YYYY-MM-DD"|null (first July 1 escalation date)
            }
          },
          "rate_classes": [
            {
              "class_id": number,
              "class_name": string,
              "class_description": string|null,
              "class_other": string|null,
              "land_use": string|null,
              "max_tax_rate": number|null,
              "max_tax_unit": string|null,
              "max_tax_qty": number|null,
              "max_tax_qty_source": string|null,
              "backup_tax_flag": boolean,
              "backup_tax_rate": number|null,
              "backup_tax_text": string|null,
              "display_order": number,
              "rate_type": number|null
            }
          ],
          "one_time_taxes": [],
          "extraction_confidence": "high"|"medium"|"low",
          "flags": string[]
        }

        Rules:
        - Extract every discrete rate-class row from the RMA rate table(s).
        - Put recurring annual max tax rows in rate_classes; one-time annexation levies in one_time_taxes.
        - Do NOT compute escalated/current rates — only base-year amounts from the document.
        - escalation.type=percent_annual when doc says X% annual increase; multiplier_annual when doc says 102% of prior year.
        - Add flags for ambiguous OCR, missing units, or unclear table cells.
        - Use numeric amounts without currency symbols or commas.
        """;

    public static LlmProvider ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("gemini", StringComparison.OrdinalIgnoreCase))
            return LlmProvider.Gemini;

        if (value.Equals("openai", StringComparison.OrdinalIgnoreCase))
            return LlmProvider.OpenAi;

        if (value.Equals("claude", StringComparison.OrdinalIgnoreCase)
            || value.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
            return LlmProvider.Claude;

        throw new ArgumentException($"Unknown --provider: {value}. Use gemini, openai, or claude.");
    }

    public static string DefaultModel(LlmProvider provider) => provider switch
    {
        LlmProvider.Gemini => DefaultGeminiModel,
        LlmProvider.OpenAi => DefaultOpenAiModel,
        LlmProvider.Claude => DefaultClaudeModel,
        _ => DefaultGeminiModel
    };

    public static ExtractionResult LoadFromJsonFile(string path)
    {
        var json = File.ReadAllText(path);
        return Deserialize(json);
    }

    public static ExtractionResult Deserialize(string json)
    {
        var result = JsonSerializer.Deserialize<ExtractionResult>(json, JsonOptions)
            ?? throw new InvalidOperationException("JSON deserialized to null.");

        Validate(result);
        return result;
    }

    public async Task<ExtractionResult> ExtractAsync(
        string documentText,
        LlmProvider provider = LlmProvider.Gemini,
        string? model = null,
        CancellationToken ct = default)
    {
        model ??= DefaultModel(provider);
        var promptText = PrepareText(documentText);
        var userPrompt = $"Extract rate table data from this RMA document text:\n\n{promptText}";

        var json = provider switch
        {
            LlmProvider.Gemini => await ExtractWithGeminiAsync(userPrompt, model, ct),
            LlmProvider.OpenAi => await ExtractWithOpenAiAsync(userPrompt, model, ct),
            LlmProvider.Claude => await ExtractWithClaudeAsync(userPrompt, model, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

        return Deserialize(json);
    }

    private static async Task<string> ExtractWithGeminiAsync(string userPrompt, string model, CancellationToken ct)
    {
        var apiKey = GeminiClient.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("GEMINI_API_KEY (or GOOGLE_API_KEY) is required for Gemini extraction.");

        var content = await GeminiClient.GenerateJsonAsync(SystemPrompt, userPrompt, model, apiKey, ct);
        return StripMarkdownFences(content);
    }

    private static async Task<string> ExtractWithOpenAiAsync(string userPrompt, string model, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY is required for OpenAI extraction.");

        var client = new ChatClient(model, apiKey);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0f,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        var completion = await client.CompleteChatAsync(messages, options, ct);
        var content = completion.Value.Content[0].Text
            ?? throw new InvalidOperationException("LLM returned empty content.");

        return StripMarkdownFences(content);
    }

    private static async Task<string> ExtractWithClaudeAsync(string userPrompt, string model, CancellationToken ct)
    {
        var apiKey = ClaudeClient.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ANTHROPIC_API_KEY is required for Claude extraction.");

        var content = await ClaudeClient.GenerateJsonAsync(SystemPrompt, userPrompt, model, apiKey, ct);
        return StripMarkdownFences(content);
    }

    private static string PrepareText(string text)
    {
        if (text.Length <= MaxPromptChars)
            return text;

        var sb = new StringBuilder();
        sb.AppendLine("=== DOCUMENT START (truncated) ===");
        sb.AppendLine(text[..30_000]);

        var sectionMatch = Regex.Match(
            text,
            @"(Section\s+C[^\n]{0,80}|MAXIMUM\s+SPECIAL\s+TAX|Rate\s+Table|Special\s+Tax)",
            RegexOptions.IgnoreCase);

        if (sectionMatch.Success)
        {
            var start = Math.Max(0, sectionMatch.Index - 5_000);
            var length = Math.Min(40_000, text.Length - start);
            sb.AppendLine();
            sb.AppendLine("=== RATE TABLE REGION ===");
            sb.AppendLine(text.Substring(start, length));
        }

        sb.AppendLine();
        sb.AppendLine("=== DOCUMENT END ===");
        sb.AppendLine(text[^20_000..]);
        return sb.ToString();
    }

    private static string StripMarkdownFences(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split('\n');
            return string.Join('\n', lines.Skip(1).TakeWhile(l => !l.TrimStart().StartsWith("```")));
        }

        return trimmed;
    }

    private static void Validate(ExtractionResult result)
    {
        if (result.RateClasses.Count == 0 && result.OneTimeTaxes.Count == 0)
            throw new InvalidOperationException("Extraction has no rate_classes or one_time_taxes rows.");

        if (string.IsNullOrWhiteSpace(result.Source.BaseFiscalYear))
            throw new InvalidOperationException("Extraction missing source.base_fiscal_year.");
    }
}
