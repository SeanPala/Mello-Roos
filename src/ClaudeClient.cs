using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MelloRoos;

public static class ClaudeClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    public static Task<string> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        CancellationToken ct = default) =>
        GenerateTextAsync(systemPrompt, userPrompt, model, apiKey, ct);

    public static Task<string> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        CancellationToken ct = default) =>
        PostMessagesAsync(systemPrompt, userPrompt, model, apiKey, images: null, ct);

    public static Task<string> GenerateVisionJsonAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(byte[] bytes, string mimeType)> images,
        string model,
        string apiKey,
        CancellationToken ct = default) =>
        PostMessagesAsync(systemPrompt, userPrompt, model, apiKey, images, ct);

    private static async Task<string> PostMessagesAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        IReadOnlyList<(byte[] bytes, string mimeType)>? images,
        CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        JsonNode userContent = images is { Count: > 0 }
            ? BuildVisionContent(userPrompt, images)
            : JsonValue.Create(userPrompt)!;

        var request = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 8192,
            ["temperature"] = 0,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userContent
                }
            }
        };

        var callKind = images is { Count: > 0 } ? $"vision ({images.Count} images)" : "text";
        Console.Error.WriteLine($"Claude API: {callKind}, model={model}...");

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var response = await http.PostAsJsonAsync(MessagesUrl, request, cancellationToken: ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Claude API: {model} OK.");
                return ParseResponse(body);
            }

            if (attempt < maxAttempts && ((int)response.StatusCode is 429 or 503 or 529))
            {
                Console.Error.WriteLine(
                    $"Claude rate limit ({(int)response.StatusCode}, attempt {attempt}/{maxAttempts}); retrying...");
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
                continue;
            }

            throw new InvalidOperationException($"Claude API error ({(int)response.StatusCode}): {body}");
        }

        throw new InvalidOperationException("Claude API failed after retries.");
    }

    private static JsonArray BuildVisionContent(
        string userPrompt,
        IReadOnlyList<(byte[] bytes, string mimeType)> images)
    {
        var blocks = new JsonArray();
        foreach (var (bytes, mimeType) in images)
        {
            blocks.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = mimeType,
                    ["data"] = Convert.ToBase64String(bytes)
                }
            });
        }

        blocks.Add(new JsonObject
        {
            ["type"] = "text",
            ["text"] = userPrompt
        });

        return blocks;
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
