using OpenAI.Chat;

namespace MelloRoos;

public static class OpenAiClient
{
    private static readonly string[] ModelFallbacks =
    [
        "gpt-5",
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-4.1-mini",
        "gpt-4.1",
        "chatgpt-4o-latest"
    ];

    public static async Task<string> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        CancellationToken ct = default) =>
        await CompleteJsonAsync(systemPrompt, userPrompt, model, apiKey, images: null, ct);

    public static async Task<string> GenerateVisionJsonAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(byte[] bytes, string mimeType)> images,
        string model,
        string apiKey,
        CancellationToken ct = default) =>
        await CompleteJsonAsync(systemPrompt, userPrompt, model, apiKey, images, ct);

    public static string ResolveModel(string? requested = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        return string.IsNullOrWhiteSpace(requested)
            ? LlmExtractor.DefaultOpenAiModel
            : requested;
    }

    public static string? ResolveApiKey() =>
        Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public static async Task<IReadOnlyList<string>> ListModelIdsAsync(string apiKey, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI models list failed ({(int)response.StatusCode}): {SummarizeHttpError(body)}");

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
            .Select(m => m.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static string SummarizeHttpError(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? body;
            }
        }
        catch
        {
            // fall through
        }

        return body.Length > 200 ? body[..200] + "..." : body;
    }

    public static IReadOnlyList<string> PreferredModels(IReadOnlyList<string> available) =>
        ModelFallbacks.Where(available.Contains).ToList();

    public static async Task<bool> ProbeQuotaAsync(string apiKey, CancellationToken ct = default)
    {
        if (OpenAiAvailability.IsDisabled)
            return false;

        try
        {
            var models = await ListModelIdsAsync(apiKey, ct);
            var model = PreferredModels(models).FirstOrDefault() ?? models.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(model))
                return false;

            var client = new ChatClient(model, apiKey);
            var messages = new List<ChatMessage> { new UserChatMessage("Reply with OK.") };
            var options = new ChatCompletionOptions { MaxOutputTokenCount = 5, Temperature = 0f };
            await client.CompleteChatAsync(messages, options, ct);
            return true;
        }
        catch (Exception ex) when (OpenAiAvailability.IsQuotaError(ex))
        {
            OpenAiAvailability.MarkQuotaExhausted();
            return false;
        }
    }

    private static async Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        IReadOnlyList<(byte[] bytes, string mimeType)>? images,
        CancellationToken ct)
    {
        model = ResolveModel(model);
        Exception? lastError = null;
        var callKind = images is { Count: > 0 } ? $"vision ({images.Count} images)" : "text";

        foreach (var attemptModel in ModelAttempts(model))
        {
            if (OpenAiAvailability.IsDisabled)
                throw new InvalidOperationException(
                    "OpenAI API quota exhausted (insufficient_quota). Add credits at platform.openai.com " +
                    "or use --provider gemini.");

            try
            {
                Console.Error.WriteLine($"OpenAI API: {callKind}, model={attemptModel}...");
                var content = await CompleteOnceAsync(
                    systemPrompt, userPrompt, attemptModel, apiKey, images, ct);
                Console.Error.WriteLine($"OpenAI API: {attemptModel} OK.");
                return content;
            }
            catch (Exception ex) when (IsModelAccessError(ex) && HasMoreModels(model, attemptModel))
            {
                lastError = ex;
                Console.Error.WriteLine(
                    $"OpenAI model '{attemptModel}' unavailable ({Summarize(ex)}); trying next model...");
            }
            catch (Exception ex) when (OpenAiAvailability.IsQuotaError(ex))
            {
                OpenAiAvailability.MarkQuotaExhausted();
                throw new InvalidOperationException(
                    "OpenAI API quota exhausted (insufficient_quota). ChatGPT Plus does not include API credits — " +
                    "add billing at platform.openai.com, or run with --provider gemini.", ex);
            }
        }

        throw lastError ?? new InvalidOperationException(
            "OpenAI request failed. Set OPENAI_MODEL to a model your project can access " +
            "(check platform.openai.com → project → Limits → allowed models).");
    }

    private static async Task<string> CompleteOnceAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        IReadOnlyList<(byte[] bytes, string mimeType)>? images,
        CancellationToken ct)
    {
        var client = new ChatClient(model, apiKey);
        ChatMessage userMessage = images is { Count: > 0 }
            ? BuildVisionMessage(userPrompt, images)
            : new UserChatMessage(userPrompt);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            userMessage
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0f,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        var completion = await client.CompleteChatAsync(messages, options, ct);
        return completion.Value.Content[0].Text
            ?? throw new InvalidOperationException("OpenAI returned empty content.");
    }

    private static UserChatMessage BuildVisionMessage(
        string userPrompt,
        IReadOnlyList<(byte[] bytes, string mimeType)> images)
    {
        var parts = new List<ChatMessageContentPart>();
        foreach (var (bytes, mimeType) in images)
            parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(bytes), mimeType));
        parts.Add(ChatMessageContentPart.CreateTextPart(userPrompt));
        return new UserChatMessage(parts);
    }

    private static IEnumerable<string> ModelAttempts(string model)
    {
        yield return model;
        foreach (var fallback in ModelFallbacks)
        {
            if (!fallback.Equals(model, StringComparison.OrdinalIgnoreCase))
                yield return fallback;
        }
    }

    private static bool HasMoreModels(string requested, string current)
    {
        var attempts = ModelAttempts(requested).ToList();
        return attempts.FindIndex(m => m.Equals(current, StringComparison.OrdinalIgnoreCase)) < attempts.Count - 1;
    }

    private static bool IsModelAccessError(Exception ex)
    {
        var msg = ex.ToString();
        return msg.Contains("model_not_found", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("does not have access to model", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static string Summarize(Exception ex)
    {
        var message = ex.Message.Split('\n')[0];
        return message.Length > 120 ? message[..120] + "..." : message;
    }
}
