using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MelloRoos;

/// <summary>Installs Poppler and Tesseract on Windows when missing (first run).</summary>
public static class WindowsDependencyInstaller
{
    private const string PopplerWingetId = "oschwartz10612.Poppler";
    private const string TesseractWingetId = "tesseract-ocr.tesseract";
    private const string TesseractLegacyWingetId = "UB-Mannheim.TesseractOCR";

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "MelloRoos-setup" } }
    };

    public static async Task EnsureInstalledAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return;

        ExternalToolChecker.RefreshProcessPath();
        ExternalToolChecker.RegisterDiscoveredToolPaths(force: true);

        if (ExternalToolChecker.AllPresent())
            return;

        Console.Error.WriteLine("Mello-Roos: installing PDF tools (first run on Windows)...");
        Console.Error.WriteLine("This may take a few minutes. Admin approval may be requested.");
        Console.Error.WriteLine();

        if (!await IsWingetAvailableAsync(ct))
        {
            Console.Error.WriteLine("winget not found — using direct download for Poppler.");
            await InstallPopplerFromGitHubAsync(ct);
        }
        else
        {
            if (!IsToolPresent("pdftotext") || !IsToolPresent("pdftoppm") || !IsToolPresent("pdfinfo"))
                await InstallWingetPackageAsync(PopplerWingetId, "Poppler", ct);

            if (!IsToolPresent("tesseract"))
            {
                if (!await InstallWingetPackageAsync(TesseractWingetId, "Tesseract OCR", ct))
                    await InstallWingetPackageAsync(TesseractLegacyWingetId, "Tesseract OCR (legacy package)", ct);
            }
        }

        ExternalToolChecker.RefreshProcessPath();
        ExternalToolChecker.RegisterDiscoveredToolPaths(force: true);

        if (!IsToolPresent("pdftotext") || !IsToolPresent("pdftoppm") || !IsToolPresent("pdfinfo"))
            await InstallPopplerFromGitHubAsync(ct);

        ExternalToolChecker.RefreshProcessPath();
        ExternalToolChecker.RegisterDiscoveredToolPaths(force: true);

        if (ExternalToolChecker.AllPresent())
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("PDF tools installed successfully.");
            Console.Error.WriteLine();
            return;
        }

        var missing = ExternalToolChecker.CheckAll().Where(r => !r.Ok).Select(r => r.Name);
        throw new InvalidOperationException(
            "Could not install required PDF tools: " + string.Join(", ", missing) + ". " +
            "Install manually — see INSTRUCTIONS.md § Windows manual install — or docs/windows-setup.md.");
    }

    private static bool IsToolPresent(string name) =>
        ExternalToolChecker.CheckAll().Any(r => r.Name == name && r.Ok);

    private static async Task<bool> IsWingetAvailableAsync(CancellationToken ct)
    {
        try
        {
            var code = await RunProcessAsync("winget", ["--version"], ct);
            return code == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> InstallWingetPackageAsync(string packageId, string label, CancellationToken ct)
    {
        Console.Error.WriteLine($"Installing {label} via winget ({packageId})...");

        var code = await RunProcessAsync("winget", [
            "install", "--id", packageId, "-e",
            "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity"
        ], ct, echoOutput: true);

        if (code == 0)
            return true;

        Console.Error.WriteLine($"winget install {packageId} exited with code {code}.");
        return false;
    }

    private static async Task InstallPopplerFromGitHubAsync(CancellationToken ct)
    {
        if (IsToolPresent("pdftotext") && IsToolPresent("pdftoppm") && IsToolPresent("pdfinfo"))
            return;

        Console.Error.WriteLine("Downloading Poppler from GitHub (oschwartz10612/poppler-windows)...");

        var release = await Http.GetFromJsonAsync<GitHubRelease>(
            "https://api.github.com/repos/oschwartz10612/poppler-windows/releases/latest",
            ct);

        var asset = release?.Assets?
            .FirstOrDefault(a => a.Name.StartsWith("Release-", StringComparison.OrdinalIgnoreCase)
                                 && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (asset?.BrowserDownloadUrl is null)
            throw new InvalidOperationException("Could not find Poppler release zip on GitHub.");

        var toolsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MelloRoos", "tools");
        Directory.CreateDirectory(toolsRoot);

        var zipPath = Path.Combine(toolsRoot, asset.Name);
        var extractRoot = Path.Combine(toolsRoot, "poppler");

        if (!Directory.Exists(extractRoot))
        {
            await using (var stream = await Http.GetStreamAsync(asset.BrowserDownloadUrl, ct))
            await using (var file = File.Create(zipPath))
                await stream.CopyToAsync(file, ct);

            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

            try { File.Delete(zipPath); }
            catch { /* best effort */ }

            // Zip contains a versioned top folder (Release-XX/Library/bin).
            var binDir = Directory
                .EnumerateDirectories(extractRoot, "Library", SearchOption.AllDirectories)
                .Select(d => Path.Combine(d, "bin"))
                .FirstOrDefault(Directory.Exists);

            if (binDir is null)
                throw new InvalidOperationException("Poppler zip extracted but Library/bin not found.");

            ExternalToolChecker.AddSearchPath(binDir);
            AppendToUserPath(binDir);
            Console.Error.WriteLine($"Poppler installed to {binDir}");
        }
        else
        {
            var binDir = Directory
                .EnumerateDirectories(extractRoot, "Library", SearchOption.AllDirectories)
                .Select(d => Path.Combine(d, "bin"))
                .FirstOrDefault(Directory.Exists);

            if (binDir is not null)
            {
                ExternalToolChecker.AddSearchPath(binDir);
                AppendToUserPath(binDir);
            }
        }
    }

    private static void AppendToUserPath(string directory)
    {
        var normalized = Path.GetFullPath(directory).TrimEnd('\\');
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";

        if (userPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(p => string.Equals(p.TrimEnd('\\'), normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        var updated = string.IsNullOrWhiteSpace(userPath) ? normalized : userPath + ";" + normalized;
        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.User);
        Console.Error.WriteLine($"Added to user PATH: {normalized}");
    }

    private static async Task<int> RunProcessAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken ct,
        bool echoOutput = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = echoOutput,
            RedirectStandardError = echoOutput,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {command}");

        if (echoOutput)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stdout))
                Console.Error.WriteLine(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr))
                Console.Error.WriteLine(stderr.TrimEnd());
            return process.ExitCode;
        }

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
