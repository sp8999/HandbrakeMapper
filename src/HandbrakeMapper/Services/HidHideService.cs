using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HandbrakeMapper.Services;

public sealed class HidHideService
{
    public string? CliPath { get; }
    public bool IsInstalled => CliPath is not null;

    public HidHideService()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe"),
            Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"),
            Path.Combine(pf, "Nefarius Software Solutions e.U", "HidHideCLI", "HidHideCLI.exe")
        };
        CliPath = candidates.FirstOrDefault(File.Exists);
    }

    public async Task<List<string>> FindDevicePathsAsync(string joystickName, CancellationToken cancellationToken = default)
    {
        if (CliPath is null)
            throw new InvalidOperationException("HidHideCLI.exe was not found.");

        var json = await RunCaptureAsync(CliPath, "--dev-gaming", cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var document = JsonDocument.Parse(json);
        var target = Normalize(joystickName);
        var bestScore = 0;
        var bestCount = 0;
        List<string> best = [];

        foreach (var group in document.RootElement.EnumerateArray())
        {
            var friendly = group.TryGetProperty("friendlyName", out var fn) ? fn.GetString() ?? string.Empty : string.Empty;
            var friendlyNorm = Normalize(friendly);
            var paths = new List<string>();
            var productHit = false;

            if (group.TryGetProperty("devices", out var devices))
            {
                foreach (var dev in devices.EnumerateArray())
                {
                    if (dev.TryGetProperty("deviceInstancePath", out var dip))
                    {
                        var path = dip.GetString();
                        if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
                    }
                    if (dev.TryGetProperty("product", out var product))
                    {
                        var p = Normalize(product.GetString() ?? string.Empty);
                        if (!string.IsNullOrEmpty(p) && (p.Contains(target) || target.Contains(p)))
                            productHit = true;
                    }
                }
            }

            var score = 0;
            if (!string.IsNullOrEmpty(target) && friendlyNorm == target) score = 100;
            else if (!string.IsNullOrEmpty(target) && (friendlyNorm.Contains(target) || target.Contains(friendlyNorm))) score = 80;
            else if (productHit) score = 70;

            if (score > bestScore && paths.Count > 0)
            {
                bestScore = score;
                bestCount = 1;
                best = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            else if (score > 0 && score == bestScore && paths.Count > 0)
            {
                bestCount++;
            }
        }

        return bestScore >= 70 && bestCount == 1 ? best : [];
    }

    public async Task ApplyHiddenAsync(string appPath, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        if (CliPath is null)
            throw new InvalidOperationException("HidHide is not installed.");
        if (paths.Count == 0)
            throw new InvalidOperationException("No matching HidHide device paths were found.");

        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$cli = '{Ps(CliPath)}'");
        script.AppendLine($"& $cli --app-reg '{Ps(appPath)}'");
        foreach (var path in paths)
            script.AppendLine($"& $cli --dev-hide '{Ps(path)}'");
        script.AppendLine("& $cli --cloak-on");
        script.AppendLine("$pnputil = Join-Path $env:SystemRoot 'System32\\pnputil.exe'");
        foreach (var path in paths)
            script.AppendLine($"try {{ & $pnputil /restart-device '{Ps(path)}' | Out-Null }} catch {{ }}");

        await RunElevatedScriptAsync(script.ToString(), cancellationToken);
    }

    public async Task RemoveHiddenAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        if (CliPath is null || paths.Count == 0) return;

        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Continue'");
        script.AppendLine($"$cli = '{Ps(CliPath)}'");
        foreach (var path in paths)
            script.AppendLine($"& $cli --dev-unhide '{Ps(path)}'");
        script.AppendLine("$pnputil = Join-Path $env:SystemRoot 'System32\\pnputil.exe'");
        foreach (var path in paths)
            script.AppendLine($"try {{ & $pnputil /restart-device '{Ps(path)}' | Out-Null }} catch {{ }}");

        await RunElevatedScriptAsync(script.ToString(), cancellationToken);
    }

    private static async Task<string> RunCaptureAsync(string file, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"HidHideCLI failed ({process.ExitCode}): {error}");
        return output;
    }

    private static async Task RunElevatedScriptAsync(string contents, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"HandbrakeMapper-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(path, contents, new UTF8Encoding(false), cancellationToken);
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("Could not start elevated PowerShell.");

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Elevated HidHide configuration failed ({process.ExitCode}).");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Ps(string value) => value.Replace("'", "''");
}
