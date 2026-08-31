namespace MelloRoos;

/// <summary>Routes Table 1 vision and structure calls to Gemini, OpenAI, or Claude.</summary>
public static class VisionLlmClient
{
    public static async Task<string> GenerateVisionJsonAsync(
        LlmProvider provider,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(byte[] bytes, string mimeType)> images,
        string model,
        CancellationToken ct = default)
    {
        Exception? lastError = null;
        foreach (var (attemptProvider, attemptModel) in LlmProviderFallback.Attempts(
                     provider, model, LlmExtractor.DefaultVisionModelFor))
        {
            try
            {
                return await GenerateVisionJsonOnceAsync(
                    attemptProvider, systemPrompt, userPrompt, images, attemptModel, ct);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (HasMoreAttempts(provider, model, attemptProvider))
                {
                    Console.Error.WriteLine(
                        $"Vision LLM: {ProviderLabel(attemptProvider)} failed ({LlmProviderFallback.SummarizeError(ex)}); trying fallback...");
                }
            }
        }

        throw lastError ?? new InvalidOperationException("Vision LLM failed with no configured providers.");
    }

    public static async Task<string> GenerateJsonAsync(
        LlmProvider provider,
        string systemPrompt,
        string userPrompt,
        string model,
        CancellationToken ct = default)
    {
        Exception? lastError = null;
        foreach (var (attemptProvider, attemptModel) in LlmProviderFallback.Attempts(
                     provider, model, LlmExtractor.DefaultVisionModelFor))
        {
            try
            {
                return await GenerateJsonOnceAsync(
                    attemptProvider, systemPrompt, userPrompt, attemptModel, ct);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (HasMoreAttempts(provider, model, attemptProvider))
                {
                    Console.Error.WriteLine(
                        $"Structure LLM: {ProviderLabel(attemptProvider)} failed ({LlmProviderFallback.SummarizeError(ex)}); trying fallback...");
                }
            }
        }

        throw lastError ?? new InvalidOperationException("Structure LLM failed with no configured providers.");
    }

    private static async Task<string> GenerateVisionJsonOnceAsync(
        LlmProvider provider,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(byte[] bytes, string mimeType)> images,
        string model,
        CancellationToken ct)
    {
        var apiKey = ResolveApiKey(provider)
            ?? throw new InvalidOperationException(ApiKeyError(provider));

        return provider switch
        {
            LlmProvider.Gemini => await GeminiClient.GenerateVisionJsonAsync(
                systemPrompt, userPrompt, images, model, apiKey, ct),
            LlmProvider.OpenAi => await OpenAiClient.GenerateVisionJsonAsync(
                systemPrompt, userPrompt, images, model, apiKey, ct),
            LlmProvider.Claude => await ClaudeClient.GenerateVisionJsonAsync(
                systemPrompt, userPrompt, images, model, apiKey, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private static async Task<string> GenerateJsonOnceAsync(
        LlmProvider provider,
        string systemPrompt,
        string userPrompt,
        string model,
        CancellationToken ct)
    {
        var apiKey = ResolveApiKey(provider)
            ?? throw new InvalidOperationException(ApiKeyError(provider));

        return provider switch
        {
            LlmProvider.Gemini => await GeminiClient.GenerateJsonAsync(
                systemPrompt, userPrompt, model, apiKey, ct),
            LlmProvider.OpenAi => await OpenAiClient.GenerateJsonAsync(
                systemPrompt, userPrompt, model, apiKey, ct),
            LlmProvider.Claude => await ClaudeClient.GenerateJsonAsync(
                systemPrompt, userPrompt, model, apiKey, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private static bool HasMoreAttempts(LlmProvider primary, string? model, LlmProvider current)
    {
        var attempts = LlmProviderFallback.Attempts(primary, model, LlmExtractor.DefaultVisionModelFor).ToList();
        return attempts.FindIndex(a => a.Provider == current) < attempts.Count - 1;
    }

    public static string? ResolveApiKey(LlmProvider provider) => provider switch
    {
        LlmProvider.Gemini => GeminiClient.ResolveApiKey(),
        LlmProvider.OpenAi => OpenAiClient.ResolveApiKey(),
        LlmProvider.Claude => ClaudeClient.ResolveApiKey(),
        _ => null
    };

    public static string ProviderLabel(LlmProvider provider) => provider switch
    {
        LlmProvider.Gemini => "gemini",
        LlmProvider.OpenAi => "openai",
        LlmProvider.Claude => "claude",
        _ => provider.ToString().ToLowerInvariant()
    };

    private static string ApiKeyError(LlmProvider provider) => provider switch
    {
        LlmProvider.Gemini => "GEMINI_API_KEY (or GOOGLE_API_KEY) is required for Gemini.",
        LlmProvider.OpenAi => "OPENAI_API_KEY is required for OpenAI.",
        LlmProvider.Claude => "ANTHROPIC_API_KEY is required for Claude.",
        _ => $"API key required for {provider}."
    };
}
