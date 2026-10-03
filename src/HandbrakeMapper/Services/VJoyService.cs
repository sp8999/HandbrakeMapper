using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HandbrakeMapper.Services;

public sealed class VJoyService : IDisposable
{
    private const uint HidUsageX = 0x30;

    private IntPtr _library;
    private uint _deviceId;
    private bool _acquired;
    private int _axisMin = 0;
    private int _axisMax = 32768;

    private VJoyEnabledDelegate? _vJoyEnabled;
    private AcquireVjdDelegate? _acquireVjd;
    private RelinquishVjdDelegate? _relinquishVjd;
    private ResetVjdDelegate? _resetVjd;
    private SetAxisDelegate? _setAxis;
    private SetBtnDelegate? _setBtn;
    private GetAxisRangeDelegate? _getAxisMin;
    private GetAxisRangeDelegate? _getAxisMax;
    private GetAxisExistDelegate? _getAxisExist;

    public string LoadedFrom { get; private set; } = string.Empty;
    public bool IsAcquired => _acquired;

    public bool Probe(out string message)
    {
        try
        {
            EnsureLoaded();
            if (_vJoyEnabled?.Invoke() != true)
            {
                message = "vJoy driver is installed but disabled.";
                return false;
            }
            message = $"OK ({LoadedFrom})";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public void Start(uint deviceId)
    {
        EnsureLoaded();
        if (_vJoyEnabled?.Invoke() != true)
            throw new InvalidOperationException("vJoy driver is not enabled.");

        if (_getAxisExist is not null && !_getAxisExist(deviceId, HidUsageX))
            throw new InvalidOperationException($"vJoy Device {deviceId} does not have an X axis. Configure it with vJoyConfig first.");

        if (_acquireVjd?.Invoke(deviceId) != true)
            throw new InvalidOperationException($"Could not acquire vJoy Device {deviceId}. It may be busy or not configured.");

        _deviceId = deviceId;
        _acquired = true;
        _resetVjd?.Invoke(deviceId);

        var min = 0;
        var max = 32768;
        if (_getAxisMin?.Invoke(deviceId, HidUsageX, ref min) == true)
            _axisMin = min;
        if (_getAxisMax?.Invoke(deviceId, HidUsageX, ref max) == true)
            _axisMax = max;

        SetAxisNormalized(0);
        SetButton(false);
    }

    public void SetAxisNormalized(double value)
    {
        if (!_acquired || _setAxis is null) return;
        value = Math.Clamp(value, 0.0, 1.0);
        var raw = _axisMin + (int)Math.Round((_axisMax - _axisMin) * value);
        if (!_setAxis(raw, _deviceId, HidUsageX))
            throw new InvalidOperationException("vJoy SetAxis failed.");
    }

    public void SetButton(bool pressed)
    {
        if (!_acquired || _setBtn is null) return;
        if (!_setBtn(pressed, _deviceId, 1))
            throw new InvalidOperationException("vJoy SetBtn failed.");
    }

    public void Stop()
    {
        if (!_acquired) return;
        try
        {
            _setAxis?.Invoke(_axisMin, _deviceId, HidUsageX);
            _setBtn?.Invoke(false, _deviceId, 1);
            _relinquishVjd?.Invoke(_deviceId);
        }
        finally
        {
            _acquired = false;
        }
    }

    private void EnsureLoaded()
    {
        if (_library != IntPtr.Zero) return;

        foreach (var candidate in CandidatePaths())
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                if (Path.IsPathRooted(candidate) && !File.Exists(candidate)) continue;
                _library = NativeLibrary.Load(candidate);
                LoadedFrom = candidate;
                break;
            }
            catch
            {
                _library = IntPtr.Zero;
            }
        }

        if (_library == IntPtr.Zero)
            throw new DllNotFoundException("vJoyInterface.dll was not found. Install vJoy 2.2.2.0 (BrunnerInnovation signed build) first.");

        _vJoyEnabled = GetDelegate<VJoyEnabledDelegate>("vJoyEnabled");
        _acquireVjd = GetDelegate<AcquireVjdDelegate>("AcquireVJD");
        _relinquishVjd = GetDelegate<RelinquishVjdDelegate>("RelinquishVJD");
        _resetVjd = GetDelegate<ResetVjdDelegate>("ResetVJD");
        _setAxis = GetDelegate<SetAxisDelegate>("SetAxis");
        _setBtn = GetDelegate<SetBtnDelegate>("SetBtn");
        _getAxisMin = GetDelegate<GetAxisRangeDelegate>("GetVJDAxisMin", required: false);
        _getAxisMax = GetDelegate<GetAxisRangeDelegate>("GetVJDAxisMax", required: false);
        _getAxisExist = GetDelegate<GetAxisExistDelegate>("GetVJDAxisExist", required: false);
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return "vJoyInterface.dll";

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "vJoy", "x64", "vJoyInterface.dll");

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8E31F76F-74C3-47F1-9550-E041EEDC5FBB}_is1");
            var dir = key?.GetValue("DllX64Location") as string;
            if (!string.IsNullOrWhiteSpace(dir))
                yield return Path.Combine(dir, "vJoyInterface.dll");
        }
    }

    private T? GetDelegate<T>(string name, bool required = true) where T : Delegate
    {
        if (_library != IntPtr.Zero && NativeLibrary.TryGetExport(_library, name, out var ptr))
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);

        if (required)
            throw new EntryPointNotFoundException($"{name} was not found in vJoyInterface.dll.");
        return null;
    }

    public void Dispose()
    {
        Stop();
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool VJoyEnabledDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool AcquireVjdDelegate(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RelinquishVjdDelegate(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool ResetVjdDelegate(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool SetAxisDelegate(int value, uint id, uint axis);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool SetBtnDelegate([MarshalAs(UnmanagedType.Bool)] bool value, uint id, byte button);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool GetAxisRangeDelegate(uint id, uint axis, ref int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool GetAxisExistDelegate(uint id, uint axis);
}
