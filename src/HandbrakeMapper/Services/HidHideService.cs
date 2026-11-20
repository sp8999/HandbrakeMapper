using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace HandbrakeMapper.Services;

public sealed class HidHideService
{
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint SPDRP_DEVICEDESC = 0x00000000;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    private const int ERROR_NO_MORE_ITEMS = 259;

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

    public sealed record HiddenDevice(string InstancePath, string DisplayName);

    public sealed record ConfigurationSnapshot(
        HashSet<string> HiddenPaths, HashSet<string> AllowedApplications,
        bool CloakEnabled, bool InverseMode);

    // Read all relevant live settings in ONE CLI session (no UAC).
    // Never assume our local journal matches the driver's current state.
    public async Task<ConfigurationSnapshot> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var output = await ReadCliAsync(
            "--dev-list --app-list --cloak-state --inv-state", cancellationToken);
        var hidden = Regex.Matches(output,
            @"(?im)^\s*--dev-hide\s+""(?<value>[^""]+)""\s*$")
            .Select(m => m.Groups["value"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowed = Regex.Matches(output,
            @"(?im)^\s*--app-reg\s+""(?<value>[^""]+)""\s*$")
            .Select(m => m.Groups["value"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cloakOn = Regex.IsMatch(output, @"(?im)^\s*--cloak-on\s*$");
        var cloakOff = Regex.IsMatch(output, @"(?im)^\s*--cloak-off\s*$");
        var inverseOn = Regex.IsMatch(output, @"(?im)^\s*--inv-on\s*$");
        var inverseOff = Regex.IsMatch(output, @"(?im)^\s*--inv-off\s*$");
        if (cloakOn == cloakOff || inverseOn == inverseOff)
            throw new InvalidOperationException(
                "HidHideの現在状態を読み取れません。安全のためSTARTを中止しました。");
        return new ConfigurationSnapshot(hidden, allowed, cloakOn, inverseOn);
    }


    // HidHide's control device only allows one configuration client at a time.
    // Probe BEFORE showing UAC or modifying the recovery journal.
    public async Task CheckAvailableAsync(CancellationToken cancellationToken = default)
    {
        await ReadCliAsync("--dev-list", cancellationToken);
    }


    public async Task<List<HiddenDevice>> GetHiddenDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (CliPath is null)
            throw new InvalidOperationException("HidHide CLI is not installed.");

        var list = await ReadCliAsync("--dev-list", cancellationToken);
        var paths = Regex.Matches(list, @"(?m)^\s*--dev-hide\s+""(?<path>[^""]+)""\s*$")
            .Select(m => m.Groups["path"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0) return [];

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = await ReadCliAsync("--dev-all", cancellationToken);
            using var doc = JsonDocument.Parse(json);
            foreach (var container in doc.RootElement.EnumerateArray())
            {
                var friendlyName = container.TryGetProperty("friendlyName", out var fn)
                    ? fn.GetString() ?? "" : "";
                if (!container.TryGetProperty("devices", out var devices)) continue;
                foreach (var device in devices.EnumerateArray())
                {
                    if (!device.TryGetProperty("deviceInstancePath", out var id)) continue;
                    var path = id.GetString();
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    var description = device.TryGetProperty("description", out var desc)
                        ? desc.GetString() ?? "" : "";
                    var product = device.TryGetProperty("product", out var prod)
                        ? prod.GetString() ?? "" : "";
                    var label = new[] { friendlyName, product, description }
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
                    if (label.Length > 0) names[path] = label;
                }
            }
        }
        catch (Exception)
        {
            // The raw registered instance IDs remain authoritative even if inventory is unavailable.
        }

        foreach (var d in EnumeratePresentHidDevices())
        {
            if (names.ContainsKey(d.InstanceId)) continue;
            var label = !string.IsNullOrWhiteSpace(d.FriendlyName) ? d.FriendlyName : d.Description;
            if (!string.IsNullOrWhiteSpace(label)) names[d.InstanceId] = label;
        }

        return paths.Select(p => new HiddenDevice(p,
            names.TryGetValue(p, out var name) ? name : "(デバイス名取得不可)")).ToList();
    }

    private async Task<string> ReadCliAsync(string args, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = CliPath!,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start()) throw new InvalidOperationException("HidHide CLI could not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await output;
        var stderr = await error;
        if (process.ExitCode != 0)
        {
            if (process.ExitCode == 5 || stderr.Contains("0x0005", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "HidHideにアクセスできません (0x0005)。HidHide Configuration Clientや他のHidHide管理ツールを完全に終了してください。管理者実行だけでは解決しません。");
            throw new InvalidOperationException("HidHide CLI read failed: " + stderr);
        }
        return stdout.TrimStart('\uFEFF');
    }

    public Task<List<string>> FindDevicePathsAsync(
        string joystickName,
        string oemKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var devices = EnumeratePresentHidDevices();
        var vidPid = ExtractVidPid(oemKey);

        if (!string.IsNullOrWhiteSpace(vidPid))
        {
            var byVidPid = devices
                .Where(d => d.InstanceId.Contains(vidPid, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.InstanceId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (byVidPid.Count > 0)
                return Task.FromResult(byVidPid);
        }

        var target = Normalize(joystickName);
        if (string.IsNullOrWhiteSpace(target))
            return Task.FromResult(new List<string>());

        var scored = devices
            .Select(d => new
            {
                Device = d,
                Score = MatchScore(target, Normalize(d.FriendlyName), Normalize(d.Description))
            })
            .Where(x => x.Score > 0)
            .ToList();

        if (scored.Count == 0)
            return Task.FromResult(new List<string>());

        var bestScore = scored.Max(x => x.Score);
        var best = scored.Where(x => x.Score == bestScore).ToList();

        // Name-only matching is deliberately conservative. With no VID/PID hint,
        // hide only a unique matching HID node to avoid hiding the wrong controller.
        if (best.Count != 1)
            return Task.FromResult(new List<string>());

        return Task.FromResult(new List<string> { best[0].Device.InstanceId });
    }

    public async Task ApplyHiddenAsync(string appPath, IReadOnlyCollection<string> paths,
        IReadOnlyCollection<string>? alreadyHidden = null, CancellationToken cancellationToken = default)
    {
        if (CliPath is null)
            throw new InvalidOperationException("HidHide is not installed.");
        if (paths.Count == 0)
            throw new InvalidOperationException("No matching HidHide device paths were found.");

        // Rollback occurs inside the SAME elevated PowerShell process on failure.
        // Do not remove entries that were already hidden before this attempt.
        var fresh = paths.Except(alreadyHidden ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase).ToList();
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$cli = '{Ps(CliPath)}'");
        script.AppendLine("$added = New-Object 'System.Collections.Generic.List[string]'");
        script.AppendLine("try {");
        script.AppendLine($"  & $cli --app-reg '{Ps(appPath)}'");
        script.AppendLine("  if ($LASTEXITCODE -ne 0) { throw 'HidHide app registration failed' }");
        foreach (var path in paths)
        {
            script.AppendLine($"  & $cli --dev-hide '{Ps(path)}'");
            script.AppendLine("  if ($LASTEXITCODE -ne 0) { throw 'HidHide device hide failed' }");
            if (fresh.Contains(path, StringComparer.OrdinalIgnoreCase))
                script.AppendLine($"  $added.Add('{Ps(path)}')");
        }
        script.AppendLine("  & $cli --cloak-on");
        script.AppendLine("  if ($LASTEXITCODE -ne 0) { throw 'HidHide cloak enable failed' }");
        script.AppendLine("} catch {");
        script.AppendLine("  foreach ($device in $added) { & $cli --dev-unhide $device | Out-Null }");
        script.AppendLine("  exit 1");
        script.AppendLine("}");
        script.AppendLine("$pnputil = Join-Path $env:SystemRoot 'System32\\pnputil.exe'");
        foreach (var path in paths)
            script.AppendLine($"try {{ & $pnputil /restart-device '{Ps(path)}' | Out-Null }} catch {{ }}");

        await RunElevatedScriptAsync(script.ToString(), cancellationToken);
    }

    public async Task RemoveHiddenAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0) return;
        if (CliPath is null) throw new InvalidOperationException("HidHide CLI is not installed; recovery list preserved.");

        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$cli = '{Ps(CliPath)}'");
        foreach (var path in paths)
        {
            script.AppendLine($"& $cli --dev-unhide '{Ps(path)}'");
            script.AppendLine("if ($LASTEXITCODE -ne 0) { throw 'HidHide device unhide failed' }");
        }
        script.AppendLine("$pnputil = Join-Path $env:SystemRoot 'System32\\pnputil.exe'");
        foreach (var path in paths)
            script.AppendLine($"try {{ & $pnputil /restart-device '{Ps(path)}' | Out-Null }} catch {{ }}");

        await RunElevatedScriptAsync(script.ToString(), cancellationToken);
    }

    private static List<HidPnpDevice> EnumeratePresentHidDevices()
    {
        HidD_GetHidGuid(out var hidGuid);

        var set = SetupDiGetClassDevsW(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed.");

        try
        {
            var result = new List<HidPnpDevice>();

            for (uint index = 0; ; index++)
            {
                var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ERROR_NO_MORE_ITEMS)
                        break;
                    throw new Win32Exception(error, "SetupDiEnumDeviceInfo failed.");
                }

                var instanceId = GetInstanceId(set, ref info);
                if (string.IsNullOrWhiteSpace(instanceId))
                    continue;

                var friendly = GetRegistryProperty(set, ref info, SPDRP_FRIENDLYNAME);
                var description = GetRegistryProperty(set, ref info, SPDRP_DEVICEDESC);

                result.Add(new HidPnpDevice(instanceId, friendly, description));
            }

            return result;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var buffer = new StringBuilder(1024);
        if (!SetupDiGetDeviceInstanceIdW(set, ref info, buffer, (uint)buffer.Capacity, out _))
            return string.Empty;
        return buffer.ToString();
    }

    private static string GetRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var buffer = new byte[4096];
        if (!SetupDiGetDeviceRegistryPropertyW(
                set,
                ref info,
                property,
                out _,
                buffer,
                (uint)buffer.Length,
                out var required))
            return string.Empty;

        var length = Math.Min((int)required, buffer.Length);
        if (length <= 2)
            return string.Empty;

        return Encoding.Unicode.GetString(buffer, 0, length).TrimEnd('\0', ' ', '\r', '\n');
    }

    private static string ExtractVidPid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var match = Regex.Match(
            value,
            @"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Value : string.Empty;
    }

    private static int MatchScore(string target, string friendly, string description)
    {
        if (friendly == target || description == target) return 100;
        if ((!string.IsNullOrEmpty(friendly) && (friendly.Contains(target) || target.Contains(friendly))) ||
            (!string.IsNullOrEmpty(description) && (description.Contains(target) || target.Contains(description))))
            return 70;
        return 0;
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

    private sealed record HidPnpDevice(string InstanceId, string FriendlyName, string Description);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        StringBuilder deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
