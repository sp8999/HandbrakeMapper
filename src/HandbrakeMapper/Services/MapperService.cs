using HandbrakeMapper.Models;

namespace HandbrakeMapper.Services;

public sealed class MapperService : IAsyncDisposable
{
    private readonly JoystickService _joystick;
    private readonly VJoyService _vjoy;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private int _deviceId;
    private AxisKind _axis;
    private uint _min;
    private uint _max;
    private double _deadzone;
    private bool _invert;
    private bool _buttonEnabled;
    private int _button;

    public uint LastRaw;
    public double LastOutput;
    public bool LastButton;
    public string? LastError { get; private set; }
    public bool IsRunning => _cts is not null;

    public MapperService(JoystickService joystick, VJoyService vjoy)
    {
        _joystick = joystick;
        _vjoy = vjoy;
    }

    public void Start(AppSettings settings)
    {
        if (IsRunning) return;
        if (settings.DeviceId < 0) throw new InvalidOperationException("Select an input device first.");
        if (settings.CalibrationMax == settings.CalibrationMin)
            throw new InvalidOperationException("0% and 100% calibration points must be different.");

        _deviceId = settings.DeviceId;
        _axis = settings.Axis;
        _min = settings.CalibrationMin;
        _max = settings.CalibrationMax;
        _deadzone = Math.Clamp((double)settings.DeadzonePercent / 100.0, 0.0, 0.95);
        _invert = settings.Invert;
        _buttonEnabled = settings.ButtonOutputEnabled;
        _button = settings.SourceButton;
        LastError = null;

        _vjoy.Start((uint)settings.VJoyDeviceId);
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts is not null)
        {
            cts.Cancel();
            try { if (loop is not null) await loop; } catch (OperationCanceledException) { }
            cts.Dispose();
        }
        _vjoy.Stop();
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
            while (await timer.WaitForNextTickAsync(token))
            {
                if (!_joystick.TryRead(_deviceId, out var state))
                {
                    LastError = "Input device disconnected.";
                    break;
                }

                var raw = state.GetAxis(_axis);
                var normalized = Normalize(raw);
                var button = _buttonEnabled && state.GetButton(_button);

                LastRaw = raw;
                LastOutput = normalized;
                LastButton = button;

                _vjoy.SetAxisNormalized(normalized);
                _vjoy.SetButton(button);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private double Normalize(uint raw)
    {
        var value = ((double)raw - _min) / ((double)_max - _min);
        value = Math.Clamp(value, 0.0, 1.0);
        if (_invert) value = 1.0 - value;

        if (value <= _deadzone) return 0.0;
        value = (value - _deadzone) / (1.0 - _deadzone);
        return Math.Clamp(value, 0.0, 1.0);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
