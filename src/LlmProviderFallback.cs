namespace MelloRoos;

public static class LlmProviderFallback
{
    public static IEnumerable<(LlmProvider Provider, string Model)> Attempts(
        LlmProvider primary,
        string? model,
        Func<LlmProvider, string> defaultModel)
    {
        if (!(primary == LlmProvider.OpenAi && OpenAiAvailability.IsDisabled))
            yield return (primary, model ?? defaultModel(primary));

        foreach (var candidate in Enum.GetValues<LlmProvider>())
        {
            if (candidate == primary)
                continue;

            if (candidate == LlmProvider.OpenAi && OpenAiAvailability.IsDisabled)
                continue;

            if (VisionLlmClient.ResolveApiKey(candidate) is null)
                continue;

            yield return (candidate, defaultModel(candidate));
        }
    }

    public static string SummarizeError(Exception ex)
    {
        var message = ex.Message.Split('\n')[0];
        return message.Length > 160 ? message[..160] + "..." : message;
    }
}
