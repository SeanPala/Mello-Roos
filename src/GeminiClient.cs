using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelloRoos;

public static class GeminiClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public static async Task<string> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        CancellationToken ct = default)
    {
        using var http = new HttpClient();
        var url = $"{BaseUrl}/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";

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
                    Parts = [new GeminiPart { Text = userPrompt }]
                }
            ],
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0f,
                ResponseMimeType = "application/json"
            }
        };

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var response = await http.PostAsJsonAsync(url, request, cancellationToken: ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return ParseResponse(body);

            if (attempt < maxAttempts && ((int)response.StatusCode is 429 or 503))
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
                continue;
            }

            throw new InvalidOperationException($"Gemini API error ({(int)response.StatusCode}): {body}");
        }

        throw new InvalidOperationException("Gemini API failed after retries.");
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
        public string Text { get; set; } = "";
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public float Temperature { get; set; }

        [JsonPropertyName("responseMimeType")]
        public string ResponseMimeType { get; set; } = "application/json";
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
