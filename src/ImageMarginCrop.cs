using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MelloRoos;

internal enum PageMargin { Header, Footer }

/// <summary>Crops top/bottom margin strips from page images for lightweight page-number OCR.</summary>
internal static class ImageMarginCrop
{
    private const double StripHeightFraction = 0.12;

    public static bool TryCropStrip(string sourcePath, string destPath, PageMargin margin)
    {
        if (OperatingSystem.IsMacOS() && TrySipsCrop(sourcePath, destPath, margin))
            return true;

        if (TryMagickCrop(sourcePath, destPath, margin))
            return true;

        return false;
    }

    private static bool TryMagickCrop(string sourcePath, string destPath, PageMargin margin)
    {
        foreach (var command in new[] { "magick", "convert" })
        {
            var gravity = margin == PageMargin.Footer ? "South" : "North";
            var percent = (int)(StripHeightFraction * 100);
            if (TryRunProcess(command, [
                    sourcePath,
                    "-gravity", gravity,
                    "-crop", $"100x{percent}%+0+0",
                    "+repage",
                    destPath
                ]) && File.Exists(destPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySipsCrop(string sourcePath, string destPath, PageMargin margin)
    {
        if (!TryGetImageSize(sourcePath, out var width, out var height))
            return false;

        var cropHeight = Math.Max(24, (int)(height * StripHeightFraction));
        var topOffset = margin == PageMargin.Footer ? height - cropHeight : 0;

        File.Copy(sourcePath, destPath, overwrite: true);
        return TryRunProcess("sips", [
            "-c", cropHeight.ToString(), width.ToString(),
            "--cropOffset", topOffset.ToString(), "0",
            destPath
        ]);
    }

    private static bool TryGetImageSize(string imagePath, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!TryRunProcess("sips", ["-g", "pixelWidth", "-g", "pixelHeight", imagePath], out var output))
            return false;

        var widthMatch = Regex.Match(output, @"pixelWidth:\s*(\d+)");
        var heightMatch = Regex.Match(output, @"pixelHeight:\s*(\d+)");

        return widthMatch.Success
            && heightMatch.Success
            && int.TryParse(widthMatch.Groups[1].Value, out width)
            && int.TryParse(heightMatch.Groups[1].Value, out height);
    }

    private static bool TryRunProcess(string command, string[] args) =>
        TryRunProcess(command, args, out _);

    private static bool TryRunProcess(string command, string[] args, out string stdout)
    {
        stdout = string.Empty;

        try
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

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
