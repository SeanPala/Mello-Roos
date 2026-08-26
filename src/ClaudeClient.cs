using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelloRoos;

public static class ClaudeClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    public static async Task<string> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var request = new ClaudeRequest
        {
            Model = model,
            MaxTokens = 8192,
            Temperature = 0f,
            System = systemPrompt,
            Messages =
            [
                new ClaudeMessage
                {
                    Role = "user",
                    Content = userPrompt
                }
            ]
        };

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var response = await http.PostAsJsonAsync(MessagesUrl, request, cancellationToken: ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return ParseResponse(body);

            if (attempt < maxAttempts && ((int)response.StatusCode is 429 or 503 or 529))
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
                continue;
            }

            throw new InvalidOperationException($"Claude API error ({(int)response.StatusCode}): {body}");
        }

        throw new InvalidOperationException("Claude API failed after retries.");
    }

    public static string? ResolveApiKey() =>
        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    private static string ParseResponse(string body)
    {
        var parsed = JsonSerializer.Deserialize<ClaudeResponse>(body)
            ?? throw new InvalidOperationException("Claude returned empty response.");

        var text = parsed.Content?
            .FirstOrDefault(b => b.Type == "text")?.Text;

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Claude returned no text content.");

        return text;
    }

    private sealed class ClaudeRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; }

        [JsonPropertyName("system")]
        public string System { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<ClaudeMessage> Messages { get; set; } = [];
    }

    private sealed class ClaudeMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private sealed class ClaudeResponse
    {
        [JsonPropertyName("content")]
        public List<ClaudeContentBlock>? Content { get; set; }
    }

    private sealed class ClaudeContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
