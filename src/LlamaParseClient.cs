using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelloRoos;

public static class LlamaParseClient
{
    private const string BaseUrl = "https://api.cloud.llamaindex.ai/api/v2/parse";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private const int MaxPollAttempts = 120;

    public static async Task<string> ParsePagesAsync(
        string pdfPath,
        int firstPage,
        int lastPage,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey()
            ?? throw new InvalidOperationException("LLAMA_CLOUD_API_KEY (or LLAMAPARSE_API_KEY) required for --llamaparse.");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var configuration = JsonSerializer.Serialize(new
        {
            tier = "agentic",
            version = "latest",
            target_pages = $"{firstPage}-{lastPage}"
        });

        using var form = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(pdfPath);
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", Path.GetFileName(pdfPath));
        form.Add(new StringContent(configuration, Encoding.UTF8, "application/json"), "configuration");

        using var uploadResponse = await http.PostAsync($"{BaseUrl}/upload", form, ct);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(ct);
        if (!uploadResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"LlamaParse upload failed ({(int)uploadResponse.StatusCode}): {uploadBody}");

        var jobId = JsonDocument.Parse(uploadBody).RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("LlamaParse returned no job id.");

        Console.Error.WriteLine($"  LlamaParse job {jobId} started (pages {firstPage}-{lastPage})...");

        for (var attempt = 1; attempt <= MaxPollAttempts; attempt++)
        {
            await Task.Delay(PollInterval, ct);

            using var statusResponse = await http.GetAsync($"{BaseUrl}/{jobId}?expand=markdown", ct);
            var statusBody = await statusResponse.Content.ReadAsStringAsync(ct);
            if (!statusResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"LlamaParse poll failed ({(int)statusResponse.StatusCode}): {statusBody}");

            var statusDoc = JsonDocument.Parse(statusBody);
            var root = statusDoc.RootElement;
            var status = root.TryGetProperty("job", out var jobEl)
                ? jobEl.GetProperty("status").GetString()
                : root.GetProperty("status").GetString();

            Console.Error.WriteLine($"  LlamaParse poll {attempt}: {status}");

            if (string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"LlamaParse job failed: {statusBody}");

            if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                continue;

            return ExtractMarkdown(root);
        }

        throw new TimeoutException($"LlamaParse job {jobId} did not complete within {MaxPollAttempts * PollInterval.TotalSeconds}s.");
    }

    public static string? ResolveApiKey() =>
        Environment.GetEnvironmentVariable("LLAMA_CLOUD_API_KEY")
        ?? Environment.GetEnvironmentVariable("LLAMAPARSE_API_KEY");

    private static string ExtractMarkdown(JsonElement root)
    {
        if (root.TryGetProperty("markdown_full", out var full) && full.ValueKind == JsonValueKind.String)
            return full.GetString() ?? "";

        if (!root.TryGetProperty("markdown", out var markdown))
            throw new InvalidOperationException("LlamaParse completed but returned no markdown.");

        if (markdown.ValueKind == JsonValueKind.String)
            return markdown.GetString() ?? "";

        if (markdown.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var page in pages.EnumerateArray())
            {
                if (page.TryGetProperty("page_number", out var pageNum))
                    sb.AppendLine($"<<<PAGE {pageNum.GetInt32()}>>>");

                if (page.TryGetProperty("markdown", out var md) && md.ValueKind == JsonValueKind.String)
                    sb.AppendLine(md.GetString());
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        throw new InvalidOperationException("LlamaParse markdown response shape not recognized.");
    }
}
