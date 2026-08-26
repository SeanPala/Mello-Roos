using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

public static class TextAcquisition
{
    public const int DefaultDpi = 300;
    public const string DefaultPsm = "6";
    public const int MinTextChars = 1000;
    public const int LargePdfPageThreshold = 50;

    public static TextAcquisitionResult Acquire(string pdfPath, TextAcquisitionOptions? options = null)
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

        if (!options.ForceOcr)
        {
            var text = RunPdftotext(pdf, options.FirstPage, options.LastPage);
            if (text.Length >= MinTextChars)
            {
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
        return new TextAcquisitionResult
        {
            Text = ocrText,
            Method = options.ForceOcr ? "ocr-forced" : "ocr-fallback",
            CharCount = ocrText.Length,
            PageCount = pageCount
        };
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
            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                var pageNum = ParsePageNumberFromImage(image, firstPage, i);
                var pageText = RunProcess("tesseract", [image, "stdout", "-l", "eng", "--psm", options.TesseractPsm]);
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
            FileName = command,
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
