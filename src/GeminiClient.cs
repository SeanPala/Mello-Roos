using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MelloRoos;

public static class GeminiClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public static Task<string> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        CancellationToken ct = default) =>
        GenerateContentAsync(systemPrompt, userPrompt, model, apiKey, jsonMode: true, imageParts: null, ct);

    public static Task<string> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        CancellationToken ct = default) =>
        GenerateContentAsync(systemPrompt, userPrompt, model, apiKey, jsonMode: false, imageParts: null, ct);

    public static Task<string> GenerateVisionJsonAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(byte[] bytes, string mimeType)> images,
        string model,
        string apiKey,
        CancellationToken ct = default)
    {
        var imageParts = images
            .Select(img => new GeminiPart
            {
                InlineData = new GeminiInlineData
                {
                    MimeType = img.mimeType,
                    Data = Convert.ToBase64String(img.bytes)
                }
            })
            .ToList();

        return GenerateContentAsync(systemPrompt, userPrompt, model, apiKey, jsonMode: true, imageParts, ct);
    }

    private static async Task<string> GenerateContentAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        bool jsonMode,
        List<GeminiPart>? imageParts,
        CancellationToken ct = default)
    {
        using var http = new HttpClient();
        var url = $"{BaseUrl}/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        var userParts = new List<GeminiPart>();
        if (imageParts is not null)
            userParts.AddRange(imageParts);
        userParts.Add(new GeminiPart { Text = userPrompt });

        var request = new GeminiRequest
        {
            SystemInstruction = new GeminiContent
            {
                Parts = [new GeminiPart { Text = systemPrompt }]
            },
            Contents =
            [
                new GeminiContent
                {
                    Parts = userParts
                }
            ],
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0f,
                ResponseMimeType = jsonMode ? "application/json" : null
            }
        };

        var callKind = imageParts is { Count: > 0 } ? $"vision ({imageParts.Count} images)" : "text";
        Console.Error.WriteLine($"Gemini API: {callKind}, model={model}...");

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var response = await http.PostAsJsonAsync(url, request, cancellationToken: ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"Gemini API: {model} OK.");
                    return ParseResponse(body);
                }

                if ((int)response.StatusCode is 429 or 503)
                {
                    if (IsUnavailableOnPlan(body))
                    {
                        throw new InvalidOperationException(
                            $"Gemini model '{model}' is not available on your API plan (free-tier quota is 0). " +
                            $"Use a flash model such as {LlmExtractor.DefaultGeminiVisionModel} or {LlmExtractor.DefaultGeminiModel}, " +
                            "or enable billing for Pro models. " +
                            $"Details: {SummarizeApiError(body)}");
                    }

                    if (attempt < maxAttempts)
                    {
                        var delay = ParseRetryDelay(body) ?? TimeSpan.FromSeconds(5 * attempt);
                        Console.Error.WriteLine(
                            $"Gemini rate limit ({(int)response.StatusCode}, attempt {attempt}/{maxAttempts}); retrying in {delay.TotalSeconds:F0}s...");
                        await Task.Delay(delay, ct);
                        continue;
                    }
                }

                throw new InvalidOperationException($"Gemini API error ({(int)response.StatusCode}): {body}");
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                Console.Error.WriteLine($"Gemini network error (attempt {attempt}/{maxAttempts}), retrying...");
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
        }

        throw new InvalidOperationException("Gemini API failed after retries.");
    }

    private static bool IsUnavailableOnPlan(string body) =>
        body.Contains("limit: 0", StringComparison.Ordinal);

    private static string SummarizeApiError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                var text = message.GetString() ?? body;
                var firstLine = text.Split('\n')[0];
                return firstLine.Length > 240 ? firstLine[..240] + "..." : firstLine;
            }
        }
        catch
        {
            // fall through
        }

        return body.Length > 240 ? body[..240] + "..." : body;
    }

    private static TimeSpan? ParseRetryDelay(string body)
    {
        var retryInfoMatch = Regex.Match(body, @"""retryDelay""\s*:\s*""(?<value>\d+(?:\.\d+)?)s""");
        if (retryInfoMatch.Success
            && double.TryParse(retryInfoMatch.Groups["value"].Value, out var retryInfoSeconds))
        {
            return TimeSpan.FromSeconds(Math.Ceiling(retryInfoSeconds) + 1);
        }

        var messageMatch = Regex.Match(body, @"Please retry in (?<value>\d+(?:\.\d+)?)s", RegexOptions.IgnoreCase);
        if (messageMatch.Success
            && double.TryParse(messageMatch.Groups["value"].Value, out var messageSeconds))
        {
            return TimeSpan.FromSeconds(Math.Ceiling(messageSeconds) + 1);
        }

        return null;
    }

    private static string ParseResponse(string body)
    {
        var parsed = JsonSerializer.Deserialize<GeminiResponse>(body)
            ?? throw new InvalidOperationException("Gemini returned empty response.");

        var text = parsed.Candidates?
            .FirstOrDefault()?.Content?.Parts?
            .FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Gemini returned no text content.");

        return text;
    }

    public static string? ResolveApiKey()
    {
        return Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
    }

    private sealed class GeminiRequest
    {
        [JsonPropertyName("systemInstruction")]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = [];

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = [];
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("inline_data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GeminiInlineData? InlineData { get; set; }
    }

    private sealed class GeminiInlineData
    {
        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = "";

        [JsonPropertyName("data")]
        public string Data { get; set; } = "";
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public float Temperature { get; set; }

        [JsonPropertyName("responseMimeType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ResponseMimeType { get; set; }
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }
}
