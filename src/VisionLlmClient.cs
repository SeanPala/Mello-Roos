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

    public static async Task<string> GenerateJsonAsync(
        LlmProvider provider,
        string systemPrompt,
        string userPrompt,
        string model,
        CancellationToken ct = default)
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
        LlmProvider.Gemini => "GEMINI_API_KEY (or GOOGLE_API_KEY) is required for Gemini vision.",
        LlmProvider.OpenAi => "OPENAI_API_KEY is required for OpenAI vision.",
        LlmProvider.Claude => "ANTHROPIC_API_KEY is required for Claude vision.",
        _ => $"API key required for {provider} vision."
    };
}
