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
            page_ranges = new { target_pages = FormatTargetPages(firstPage, lastPage) }
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

            using var statusResponse = await http.GetAsync(
                $"{BaseUrl}/{jobId}?expand=markdown,markdown_full,items", ct);
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

            var markdown = ExtractMarkdown(root);
            Console.Error.WriteLine($"  LlamaParse markdown: {markdown.Length:N0} chars");
            if (markdown.Length < 200)
                Console.Error.WriteLine($"  LlamaParse preview: {Truncate(markdown, 400)}");

            return markdown;
        }

        throw new TimeoutException($"LlamaParse job {jobId} did not complete within {MaxPollAttempts * PollInterval.TotalSeconds}s.");
    }

    public static string? ResolveApiKey() =>
        Environment.GetEnvironmentVariable("LLAMA_CLOUD_API_KEY")
        ?? Environment.GetEnvironmentVariable("LLAMAPARSE_API_KEY");

    public static bool IsConfigured() => !string.IsNullOrWhiteSpace(ResolveApiKey());

    private static string FormatTargetPages(int firstPage, int lastPage) =>
        firstPage == lastPage ? firstPage.ToString() : $"{firstPage}-{lastPage}";

    private static string ExtractMarkdown(JsonElement root)
    {
        if (TryReadNonEmptyString(root, "markdown_full", out var full))
            return full;

        if (root.TryGetProperty("markdown", out var markdown))
        {
            if (markdown.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(markdown.GetString()))
            {
                return markdown.GetString()!;
            }

            if (markdown.TryGetProperty("pages", out var pages)
                && pages.ValueKind == JsonValueKind.Array)
            {
                var fromPages = CombineMarkdownPages(pages);
                if (!string.IsNullOrWhiteSpace(fromPages))
                    return fromPages;
            }
        }

        if (root.TryGetProperty("items", out var items)
            && items.TryGetProperty("pages", out var itemPages)
            && itemPages.ValueKind == JsonValueKind.Array)
        {
            var fromItems = ExtractTablesFromItems(itemPages);
            if (!string.IsNullOrWhiteSpace(fromItems))
                return fromItems;
        }

        throw new InvalidOperationException("LlamaParse completed but returned no usable markdown or table items.");
    }

    private static string CombineMarkdownPages(JsonElement pages)
    {
        var sb = new StringBuilder();
        foreach (var page in pages.EnumerateArray())
        {
            var pageNum = ReadPageNumber(page);
            if (pageNum > 0)
                sb.AppendLine($"<<<PAGE {pageNum}>>>");

            if (page.TryGetProperty("markdown", out var md) && md.ValueKind == JsonValueKind.String)
                sb.AppendLine(md.GetString());

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string ExtractTablesFromItems(JsonElement itemPages)
    {
        var sb = new StringBuilder();
        foreach (var page in itemPages.EnumerateArray())
        {
            var pageNum = ReadPageNumber(page);
            if (pageNum > 0)
                sb.AppendLine($"<<<PAGE {pageNum}>>>");

            if (!page.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeEl)
                    && typeEl.GetString() is string type
                    && type.Equals("table", StringComparison.OrdinalIgnoreCase))
                {
                    if (item.TryGetProperty("md", out var md) && md.ValueKind == JsonValueKind.String)
                        sb.AppendLine(md.GetString());
                    else if (item.TryGetProperty("rows", out var rows))
                        sb.AppendLine(rows.GetRawText());
                }
                else if (item.TryGetProperty("md", out var md) && md.ValueKind == JsonValueKind.String)
                {
                    sb.AppendLine(md.GetString());
                }
            }

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static int ReadPageNumber(JsonElement page)
    {
        if (page.TryGetProperty("page_number", out var pageNumber))
            return pageNumber.GetInt32();

        if (page.TryGetProperty("page", out var pageAlt))
            return pageAlt.GetInt32();

        return 0;
    }

    private static bool TryReadNonEmptyString(JsonElement root, string property, out string value)
    {
        value = "";
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}
