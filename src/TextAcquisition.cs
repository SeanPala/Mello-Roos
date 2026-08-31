using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

public static class TextAcquisition
{
    public const int DefaultDpi = 300;
    public const int PrintedPageProbeDpi = 150;
    public const int AppendixPageProbeDpi = 200;
    public const string DefaultPsm = "6";
    public const int MinTextChars = 1000;
    public const int LargePdfPageThreshold = 50;

    public static TextAcquisitionResult Acquire(
        string pdfPath,
        TextAcquisitionOptions? options = null,
        PageTextCache? pageCache = null)
    {
        options ??= new TextAcquisitionOptions();
        var pdf = Path.GetFullPath(pdfPath);
        if (!File.Exists(pdf))
            throw new FileNotFoundException($"PDF not found: {pdf}");

        var pageCount = GetPageCount(pdf);
        if (pageCount > LargePdfPageThreshold && options.FirstPage is null && options.LastPage is null)
        {
            Console.Error.WriteLine(
                $"Warning: PDF has {pageCount} pages. Consider --pages to limit OCR scope.");
        }

        if (pageCache is not null
            && options.FirstPage is int cacheFirst
            && options.LastPage is int cacheLast)
        {
            if (pageCache.HasCompleteRange(cacheFirst, cacheLast))
            {
                Console.Error.WriteLine(
                    $"Reusing cached OCR for pages {cacheFirst}-{cacheLast} ({cacheLast - cacheFirst + 1} pages).");
                return pageCache.ToResult(cacheFirst, cacheLast, pageCount);
            }

            return AcquireMissingPages(pdf, options, pageCache, pageCount);
        }

        if (!options.ForceOcr)
        {
            var text = RunPdftotext(pdf, options.FirstPage, options.LastPage);
            if (text.Length >= MinTextChars)
            {
                pageCache?.AddFromMarkedText(text, options.FirstPage ?? 1);
                return new TextAcquisitionResult
                {
                    Text = text,
                    Method = "pdftotext",
                    CharCount = text.Length,
                    PageCount = pageCount
                };
            }
        }

        var ocrText = RunOcr(pdf, options);
        pageCache?.AddFromMarkedText(ocrText, options.FirstPage ?? 1);
        return new TextAcquisitionResult
        {
            Text = ocrText,
            Method = options.ForceOcr ? "ocr-forced" : "ocr-fallback",
            CharCount = ocrText.Length,
            PageCount = pageCount
        };
    }

    private static TextAcquisitionResult AcquireMissingPages(
        string pdfPath,
        TextAcquisitionOptions options,
        PageTextCache pageCache,
        int? pageCount)
    {
        var first = options.FirstPage!.Value;
        var last = options.LastPage!.Value;
        var missing = pageCache.MissingPages(first, last);
        var reused = last - first + 1 - missing.Count;

        if (missing.Count > 0)
        {
            Console.Error.WriteLine(
                $"Reusing {reused} cached page(s); OCR {missing.Count} additional page(s)...");

            foreach (var (rangeFirst, rangeLast) in ContiguousRanges(missing))
            {
                var rangeOptions = new TextAcquisitionOptions
                {
                    ForceOcr = options.ForceOcr,
                    FirstPage = rangeFirst,
                    LastPage = rangeLast,
                    Dpi = options.Dpi,
                    TesseractPsm = options.TesseractPsm
                };

                var ocrText = RunOcr(pdfPath, rangeOptions);
                pageCache.AddFromMarkedText(ocrText, rangeFirst);
            }
        }
        else
        {
            Console.Error.WriteLine($"Reusing cached OCR for pages {first}-{last} ({last - first + 1} pages).");
        }

        return pageCache.ToResult(first, last, pageCount, method: reused > 0 ? "ocr-cached" : options.ForceOcr ? "ocr-forced" : "ocr-fallback");
    }

    private static IEnumerable<(int First, int Last)> ContiguousRanges(IReadOnlyList<int> pages)
    {
        if (pages.Count == 0)
            yield break;

        var start = pages[0];
        var prev = pages[0];

        for (var i = 1; i < pages.Count; i++)
        {
            if (pages[i] == prev + 1)
            {
                prev = pages[i];
                continue;
            }

            yield return (start, prev);
            start = pages[i];
            prev = pages[i];
        }

        yield return (start, prev);
    }

    public static int? GetPageCount(string pdfPath)
    {
        try
        {
            var output = RunProcess("pdfinfo", pdfPath);
            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("Pages:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var count))
                        return count;
                }
            }
        }
        catch
        {
            // pdfinfo optional
        }

        return null;
    }

    public static IReadOnlyList<PagedText> AcquirePages(
        string pdfPath,
        int firstPage,
        int lastPage,
        TextAcquisitionOptions? options = null)
    {
        options ??= new TextAcquisitionOptions();
        var pdf = Path.GetFullPath(pdfPath);
        if (!File.Exists(pdf))
            throw new FileNotFoundException($"PDF not found: {pdf}");

        if (firstPage > lastPage)
            throw new ArgumentException($"Invalid page range: {firstPage}-{lastPage}");

        var pages = new List<PagedText>();
        for (var page = firstPage; page <= lastPage; page++)
        {
            var pageOptions = new TextAcquisitionOptions
            {
                ForceOcr = options.ForceOcr,
                FirstPage = page,
                LastPage = page,
                Dpi = options.Dpi,
                TesseractPsm = options.TesseractPsm
            };

            string text;
            if (!options.ForceOcr)
            {
                text = RunPdftotext(pdf, page, page);
                if (text.Length < MinTextChars)
                    text = RunOcr(pdf, pageOptions);
            }
            else
            {
                text = RunOcr(pdf, pageOptions);
            }

            pages.Add(new PagedText { PageNumber = page, Text = text });
            Console.Error.WriteLine($"  page {page}: {text.Length} chars");
        }

        return pages;
    }

    /// <summary>
    /// Reads a printed/listed page number from headers or footers without full-page OCR.
    /// Tries embedded text first, then OCR on cropped margin strips (~12% of page height).
    /// </summary>
    public static int? TryReadPrintedPageNumber(string pdfPath, int page, TextAcquisitionOptions? options = null)
    {
        options ??= new TextAcquisitionOptions();

        var embedded = RunPdftotext(Path.GetFullPath(pdfPath), page, page);
        if (PageOffsetDetector.TryExtractPrintedArabicPage(embedded) is int fromText)
        {
            Console.Error.WriteLine($"  page {page}: printed {fromText} (embedded text)");
            return fromText;
        }

        foreach (var marginText in TryReadMarginOcrTexts(pdfPath, page))
        {
            if (PageOffsetDetector.TryExtractPrintedArabicPage(marginText) is int fromStrip)
            {
                Console.Error.WriteLine($"  page {page}: printed {fromStrip} (margin strip, {PrintedPageProbeDpi} DPI)");
                return fromStrip;
            }
        }

        Console.Error.WriteLine($"  page {page}: no printed page number found");
        return null;
    }

    /// <summary>Reads appendix margin page refs (e.g. D-1) from header/footer strips only.</summary>
    public static AppendixPageRef? TryReadAppendixPageRef(
        string pdfPath,
        int page,
        TextAcquisitionOptions? options = null,
        string? expectedLetter = null)
    {
        options ??= new TextAcquisitionOptions();
        var pdf = Path.GetFullPath(pdfPath);

        var embedded = RunPdftotext(pdf, page, page);
        if (TocParser.ParseAppendixPageRef(embedded, expectedLetter) is { } fromText)
        {
            Console.Error.WriteLine($"  page {page}: appendix {fromText.Letter}-{fromText.SubPage} (embedded text)");
            return fromText;
        }

        foreach (var marginText in TryReadAppendixMarginOcrTexts(pdfPath, page))
        {
            if (TocParser.ParseAppendixPageRef(marginText, expectedLetter) is { } fromStrip)
            {
                Console.Error.WriteLine(
                    $"  page {page}: appendix {fromStrip.Letter}-{fromStrip.SubPage} (margin OCR: \"{Truncate(marginText, 40)}\")");
                return fromStrip;
            }
        }

        return null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    internal static int? TryReadPrintedPageNumberFromEmbedded(string pdfPath, int page) =>
        PageOffsetDetector.TryExtractPrintedArabicPage(RunPdftotext(Path.GetFullPath(pdfPath), page, page));

    private static IEnumerable<string> TryReadAppendixMarginOcrTexts(string pdfPath, int page)
    {
        var pdf = Path.GetFullPath(pdfPath);
        var tempDir = Path.Combine(Path.GetTempPath(), $"mello-roos-page-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var prefix = Path.Combine(tempDir, "page");
            RunProcess("pdftoppm", [
                "-f", page.ToString(),
                "-l", page.ToString(),
                "-png", "-r", AppendixPageProbeDpi.ToString(),
                pdf, prefix
            ]);

            var image = Directory.GetFiles(tempDir, "page-*.png").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (image is null)
                yield break;

            foreach (var margin in new[] { PageMargin.Footer, PageMargin.Header })
            {
                var stripPath = Path.Combine(tempDir, margin == PageMargin.Footer ? "footer.png" : "header.png");
                if (!ImageMarginCrop.TryCropStrip(image, stripPath, margin))
                    continue;

                foreach (var ocr in OcrAppendixCandidates(stripPath))
                {
                    if (!string.IsNullOrWhiteSpace(ocr))
                        yield return ocr;
                }
            }

            var fullPageOcr = OcrAppendixCandidates(image).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (string.IsNullOrWhiteSpace(fullPageOcr))
                yield break;

            var lines = fullPageOcr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
                yield break;

            yield return lines[^1];
            if (lines.Length > 1)
                yield return lines[0];
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static IEnumerable<string> TryReadMarginOcrTexts(string pdfPath, int page)
    {
        var pdf = Path.GetFullPath(pdfPath);
        var tempDir = Path.Combine(Path.GetTempPath(), $"mello-roos-page-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var prefix = Path.Combine(tempDir, "page");
            RunProcess("pdftoppm", [
                "-f", page.ToString(),
                "-l", page.ToString(),
                "-png", "-r", PrintedPageProbeDpi.ToString(),
                pdf, prefix
            ]);

            var image = Directory.GetFiles(tempDir, "page-*.png").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (image is null)
                yield break;

            foreach (var margin in new[] { PageMargin.Footer, PageMargin.Header })
            {
                var stripPath = Path.Combine(tempDir, margin == PageMargin.Footer ? "footer.png" : "header.png");
                if (!ImageMarginCrop.TryCropStrip(image, stripPath, margin))
                    continue;

                var ocr = OcrMarginStrip(stripPath);
                if (!string.IsNullOrWhiteSpace(ocr))
                    yield return ocr;
            }

            var fullPageOcr = OcrMarginStrip(image);
            if (string.IsNullOrWhiteSpace(fullPageOcr))
                yield break;

            var lines = fullPageOcr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
                yield break;

            yield return string.Join('\n', lines.Take(Math.Min(4, lines.Length)));
            if (lines.Length > 4)
                yield return string.Join('\n', lines.TakeLast(Math.Min(6, lines.Length)));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static IEnumerable<string> OcrAppendixCandidates(string imagePath)
    {
        yield return RunProcess("tesseract", [imagePath, "stdout", "-l", "eng", "--psm", "7"]).Trim();
        yield return RunProcess("tesseract", [
            imagePath, "stdout", "-l", "eng", "--psm", "8",
            "-c", "tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-–—>l|"
        ]).Trim();
        yield return OcrMarginStrip(imagePath);
    }

    private static string OcrMarginStrip(string imagePath) =>
        RunProcess("tesseract", [
            imagePath, "stdout",
            "-l", "eng",
            "--psm", "7",
            "-c", "tessedit_char_whitelist=ABCDEFG0123456789-–—>l."
        ]).Trim();

    private static string RunPdftotext(string pdfPath, int? firstPage, int? lastPage)
    {
        var args = new List<string>();
        if (firstPage is not null)
            args.AddRange(["-f", firstPage.Value.ToString()]);
        if (lastPage is not null)
            args.AddRange(["-l", lastPage.Value.ToString()]);
        args.AddRange(["-layout", pdfPath, "-"]);

        return RunProcess("pdftotext", [.. args]);
    }

    private static string RunOcr(string pdfPath, TextAcquisitionOptions options)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mello-roos-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var prefix = Path.Combine(tempDir, "page");
            var pdftoppmArgs = new List<string>();
            if (options.FirstPage is not null)
                pdftoppmArgs.AddRange(["-f", options.FirstPage.Value.ToString()]);
            if (options.LastPage is not null)
                pdftoppmArgs.AddRange(["-l", options.LastPage.Value.ToString()]);
            pdftoppmArgs.AddRange(["-png", "-r", options.Dpi.ToString(), pdfPath, prefix]);

            RunProcess("pdftoppm", [.. pdftoppmArgs]);

            var images = Directory.GetFiles(tempDir, "page-*.png")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            if (images.Count == 0)
                throw new InvalidOperationException("pdftoppm produced no images for OCR.");

            var sb = new StringBuilder();
            var firstPage = options.FirstPage ?? 1;
            var lastPage = options.LastPage ?? (firstPage + images.Count - 1);
            Console.Error.WriteLine($"OCR pages {firstPage}-{lastPage} ({images.Count} images at {options.Dpi} DPI)...");

            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                var pageNum = ParsePageNumberFromImage(image, firstPage, i);
                var pageText = RunProcess("tesseract", [image, "stdout", "-l", "eng", "--psm", options.TesseractPsm]);
                Console.Error.WriteLine($"  page {pageNum}: {pageText.Length} chars");
                sb.AppendLine($"<<<PAGE {pageNum}>>>");
                sb.AppendLine(pageText);
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    public static IReadOnlyList<PagedText> SplitMarkedPages(string text, int defaultStartPage = 1)
    {
        var pages = new List<PagedText>();
        var matches = Regex.Matches(text, @"<<<PAGE (\d+)>>>");
        if (matches.Count == 0)
        {
            pages.Add(new PagedText { PageNumber = defaultStartPage, Text = text });
            return pages;
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var pageNum = int.Parse(matches[i].Groups[1].Value);
            var contentStart = matches[i].Index + matches[i].Length;
            var contentEnd = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var pageText = text[contentStart..contentEnd].Trim();
            pages.Add(new PagedText { PageNumber = pageNum, Text = pageText });
        }

        return pages;
    }

    private static int ParsePageNumberFromImage(string imagePath, int firstPage, int index)
    {
        var name = Path.GetFileNameWithoutExtension(imagePath);
        var match = Regex.Match(name, @"(\d+)$");
        return match.Success ? int.Parse(match.Groups[1].Value) : firstPage + index;
    }

    private static string RunProcess(string command, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExternalToolChecker.GetExecutablePath(command),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {command}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{command} failed (exit {process.ExitCode}): {stderr.Trim()}");

        return stdout;
    }
}
