using System.Diagnostics;

namespace MelloRoos;

public static class ExternalToolChecker
{
    private static readonly (string Name, string[] VersionArgs)[] Tools =
    [
        ("pdftotext", ["-v"]),
        ("pdftoppm", ["-v"]),
        ("pdfinfo", ["-v"]),
        ("tesseract", ["--version"])
    ];

    private static readonly List<string> AdditionalSearchPaths = [];
    private static bool _discovered;

    public static void AddSearchPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var normalized = Path.GetFullPath(directory);
        if (!AdditionalSearchPaths.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
            AdditionalSearchPaths.Add(normalized);
    }

    public static void RefreshProcessPath()
    {
        var machine = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
        var user = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var extra = string.Join(Path.PathSeparator, AdditionalSearchPaths);
        var combined = string.Join(Path.PathSeparator.ToString(), new[] { extra, user, machine }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Environment.SetEnvironmentVariable("PATH", combined, EnvironmentVariableTarget.Process);
    }

    public static void ResetDiscovery() => _discovered = false;

    public static void RegisterDiscoveredToolPaths(bool force = false)
    {
        if (_discovered && !force)
            return;

        _discovered = true;

        if (!OperatingSystem.IsWindows())
            return;

        var localTools = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MelloRoos", "tools");

        if (Directory.Exists(localTools))
        {
            foreach (var bin in Directory.EnumerateDirectories(localTools, "bin", SearchOption.AllDirectories))
                AddSearchPath(bin);
        }

        foreach (var root in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
                 })
        {
            if (!Directory.Exists(root))
                continue;

            TryAddParentOfExecutable(root, "pdftotext.exe", maxDepth: 6);
            TryAddParentOfExecutable(root, "tesseract.exe", maxDepth: 4);
        }

        foreach (var wellKnown in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MelloRoos", "tools", "poppler"),
                     @"C:\Program Files\Tesseract-OCR",
                     @"C:\Program Files (x86)\Tesseract-OCR"
                 })
        {
            if (!Directory.Exists(wellKnown))
                continue;

            if (File.Exists(Path.Combine(wellKnown, "tesseract.exe")))
                AddSearchPath(wellKnown);

            var popplerBin = Directory
                .EnumerateDirectories(wellKnown, "Library", SearchOption.AllDirectories)
                .Select(d => Path.Combine(d, "bin"))
                .FirstOrDefault(Directory.Exists);

            if (popplerBin is not null)
                AddSearchPath(popplerBin);
        }

        // WinGet shim directory (poppler aliases)
        var wingetLinks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Links");
        if (Directory.Exists(wingetLinks))
            AddSearchPath(wingetLinks);

        RefreshProcessPath();
    }

    public static IReadOnlyList<(string Name, bool Ok, string Detail)> CheckAll()
    {
        RegisterDiscoveredToolPaths();

        return Tools
            .Select(tool =>
            {
                try
                {
                    var detail = Run(tool.Name, tool.VersionArgs).Split('\n')[0].Trim();
                    if (detail.Length > 120)
                        detail = detail[..120] + "...";
                    return (tool.Name, true, detail);
                }
                catch (Exception ex)
                {
                    return (tool.Name, false, ex.Message.Split('\n')[0]);
                }
            })
            .ToList();
    }

    public static bool AllPresent() => CheckAll().All(r => r.Ok);

    public static string GetExecutablePath(string command)
    {
        RegisterDiscoveredToolPaths();
        return ResolveExecutable(command)
            ?? throw new InvalidOperationException(
                $"{command} not found on PATH. On Windows, tools install automatically on first run.");
    }

    private static void TryAddParentOfExecutable(string root, string exeName, int maxDepth)
    {
        try
        {
            var rootInfo = new DirectoryInfo(root);
            if (rootInfo.FullName.Split(Path.DirectorySeparatorChar).Length > maxDepth + 3)
                return;

            foreach (var file in rootInfo.EnumerateFiles(exeName, SearchOption.AllDirectories))
            {
                if (file.FullName.Split(Path.DirectorySeparatorChar).Length - rootInfo.FullName.Split(Path.DirectorySeparatorChar).Length > maxDepth)
                    continue;

                AddSearchPath(file.DirectoryName!);
                return;
            }
        }
        catch
        {
            // Ignore permission errors during discovery.
        }
    }

    private static string Run(string command, string[] args)
    {
        var executable = ResolveExecutable(command)
            ?? throw new InvalidOperationException($"{command} not found on PATH.");

        var psi = new ProcessStartInfo
        {
            FileName = executable,
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

        var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException($"{command} failed (exit {process.ExitCode}).");

        return output;
    }

    private static string? ResolveExecutable(string command)
    {
        var fileName = OperatingSystem.IsWindows() && !command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? command + ".exe"
            : command;

        foreach (var dir in AdditionalSearchPaths)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
