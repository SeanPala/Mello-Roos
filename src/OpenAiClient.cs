using OpenAI.Chat;

namespace MelloRoos;

public static class OpenAiClient
{
    public static async Task<string> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        string apiKey,
        CancellationToken ct = default)
    {
        var client = new ChatClient(model, apiKey);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0f,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        Console.Error.WriteLine($"OpenAI API: text, model={model}...");
        var completion = await client.CompleteChatAsync(messages, options, ct);
        var content = completion.Value.Content[0].Text
            ?? throw new InvalidOperationException("OpenAI returned empty content.");

        Console.Error.WriteLine($"OpenAI API: {model} OK.");
        return content;
    }

    public static async Task<string> GenerateVisionJsonAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(byte[] bytes, string mimeType)> images,
        string model,
        string apiKey,
        CancellationToken ct = default)
    {
        var parts = new List<ChatMessageContentPart>();
        foreach (var (bytes, mimeType) in images)
            parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(bytes), mimeType));
        parts.Add(ChatMessageContentPart.CreateTextPart(userPrompt));

        var client = new ChatClient(model, apiKey);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(parts)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0f,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        Console.Error.WriteLine($"OpenAI API: vision ({images.Count} images), model={model}...");
        var completion = await client.CompleteChatAsync(messages, options, ct);
        var content = completion.Value.Content[0].Text
            ?? throw new InvalidOperationException("OpenAI returned empty content.");

        Console.Error.WriteLine($"OpenAI API: {model} OK.");
        return content;
    }

    public static string? ResolveApiKey() =>
        Environment.GetEnvironmentVariable("OPENAI_API_KEY");
}
