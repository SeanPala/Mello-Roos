using System.Text;
using MelloRoos.Models;

namespace MelloRoos;

/// <summary>Per-page OCR text reused across discovery and extraction to avoid duplicate round-trips.</summary>
public sealed class PageTextCache
{
    private readonly Dictionary<int, string> _pages = new();

    public bool HasPage(int page) => _pages.ContainsKey(page);

    public bool HasCompleteRange(int firstPage, int lastPage)
    {
        for (var page = firstPage; page <= lastPage; page++)
        {
            if (!_pages.ContainsKey(page))
                return false;
        }

        return true;
    }

    public IReadOnlyList<int> MissingPages(int firstPage, int lastPage)
    {
        var missing = new List<int>();
        for (var page = firstPage; page <= lastPage; page++)
        {
            if (!_pages.ContainsKey(page))
                missing.Add(page);
        }

        return missing;
    }

    public void AddPage(int pageNumber, string text) =>
        _pages[pageNumber] = text;

    public void AddPages(IEnumerable<PagedText> pages)
    {
        foreach (var page in pages)
            _pages[page.PageNumber] = page.Text;
    }

    public void AddFromMarkedText(string markedText, int defaultStartPage = 1)
    {
        foreach (var page in TextAcquisition.SplitMarkedPages(markedText, defaultStartPage))
            _pages[page.PageNumber] = page.Text;
    }

    public TextAcquisitionResult ToResult(int firstPage, int lastPage, int? pageCount, string method = "ocr-cached")
    {
        var sb = new StringBuilder();
        for (var page = firstPage; page <= lastPage; page++)
        {
            if (!_pages.TryGetValue(page, out var text))
                throw new InvalidOperationException($"Cached OCR missing page {page}.");

            sb.AppendLine($"<<<PAGE {page}>>>");
            sb.AppendLine(text);
            sb.AppendLine();
        }

        var combined = sb.ToString().TrimEnd();
        return new TextAcquisitionResult
        {
            Text = combined,
            Method = method,
            CharCount = combined.Length,
            PageCount = pageCount
        };
    }
}
