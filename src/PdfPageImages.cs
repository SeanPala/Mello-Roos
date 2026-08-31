using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MelloRoos;

public sealed class PageImage
{
    public required int PageNumber { get; init; }
    public required string FilePath { get; init; }
    public required byte[] Bytes { get; init; }
}

public static class PdfPageImages
{
    public const int TableDpi = 400;

    public static IReadOnlyList<PageImage> Render(string pdfPath, int firstPage, int lastPage, int dpi = TableDpi)
    {
        var pdf = Path.GetFullPath(pdfPath);
        if (!File.Exists(pdf))
            throw new FileNotFoundException($"PDF not found: {pdf}");

        if (firstPage > lastPage)
            throw new ArgumentException($"Invalid page range: {firstPage}-{lastPage}");

        var tempDir = Path.Combine(Path.GetTempPath(), $"mello-roos-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var prefix = Path.Combine(tempDir, "page");
            RunProcess("pdftoppm", [
                "-f", firstPage.ToString(),
                "-l", lastPage.ToString(),
                "-png", "-r", dpi.ToString(),
                pdf, prefix
            ]);

            var images = Directory.GetFiles(tempDir, "page-*.png")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            if (images.Count == 0)
                throw new InvalidOperationException("pdftoppm produced no images.");

            var pages = new List<PageImage>();
            for (var i = 0; i < images.Count; i++)
            {
                var path = images[i];
                var pageNum = ParsePageNumber(path, firstPage, i);
                pages.Add(new PageImage
                {
                    PageNumber = pageNum,
                    FilePath = path,
                    Bytes = File.ReadAllBytes(path)
                });
            }

            return pages;
        }
        catch
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }

            throw;
        }
    }

    private static int ParsePageNumber(string imagePath, int firstPage, int index)
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
