using HandbrakeMapper.Models;
using HandbrakeMapper.Services;

namespace HandbrakeMapper;

public sealed class MainForm : Form
{
    private readonly JoystickService _joystick = new();
    private readonly VJoyService _vjoy = new();
    private readonly HidHideService _hidHide = new();
    private readonly MapperService _mapper;
    private readonly AppSettings _settings;
    private IReadOnlyList<JoystickService.DeviceInfo> _devices = [];

    private readonly ComboBox _device = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly ComboBox _axis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox _button = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ProgressBar _inputBar = new() { Minimum = 0, Maximum = 1000, Width = 320, Height = 22 };
    private readonly ProgressBar _outputBar = new() { Minimum = 0, Maximum = 1000, Width = 320, Height = 22 };
    private readonly Label _inputValue = new() { AutoSize = true, Text = "0.0%" };
    private readonly Label _outputValue = new() { AutoSize = true, Text = "0.0%" };
    private readonly NumericUpDown _deadzone = new() { Minimum = 0, Maximum = 25, DecimalPlaces = 1, Increment = 0.5m, Width = 80 };
    private readonly CheckBox _invert = new() { Text = "軸を反転" };
    private readonly CheckBox _axisEnabled = new() { Text = "軸入力", Checked = true };
    private readonly CheckBox _buttonEnabled = new() { Text = "ボタン入力" };
    private readonly CheckBox _hide = new() { Text = "元の物理デバイスをHidHideで隠す", Checked = true, AutoSize = true };
    private readonly Button _start = new() { Text = "START", Width = 170, Height = 42 };
    private readonly Button _recover = new() { Text = "HidHideの非表示を緊急解除", Width = 230 };
    private readonly Button _inspectHidden = new() { Text = "HidHide登録一覧から個別解除", Width = 230 };
    private bool _busy;
    private readonly Button _refresh = new() { Text = "再検索", Width = 80 };
    private readonly Button _detectDevice = new() { Text = "デバイスを動かして自動設定", Width = 210 };
    private readonly Button _setMin = new() { Text = "現在値を 0%", Width = 110 };
    private readonly Button _setMax = new() { Text = "現在値を 100%", Width = 110 };
    private readonly Label _calibration = new() { AutoSize = true };
    private readonly Label _vjoyStatus = new() { AutoSize = true };
    private readonly Label _hidStatus = new() { AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 33 };

    public MainForm()
    {
        _settings = SettingsStore.Load();
        _mapper = new MapperService(_joystick, _vjoy);

        Text = "Handbrake Mapper 0.1.8";
        Width = 590;
        Height = 750;
        MinimumSize = new Size(590, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        _vjoy.EnsureFriendlyName("HandbrakeMapper(vJoy Device)");

        BuildUi();
        WireEvents();
        ApplySettingsToUi();
        RefreshDevices();
        RefreshDriverStatus();
        _uiTimer.Start();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 22,
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(Title("Handbrake Mapper"));
        root.Controls.Add(Note("USBハンドブレーキの軸入力とボタン入力を選んでvJoyへ出力します。ACRを含むレースシム共通で使えます。"));

        root.Controls.Add(Section("入力デバイス"));
        root.Controls.Add(Row(_detectDevice));
        root.Controls.Add(Row(new Label { Text = "Device", Width = 70, TextAlign = ContentAlignment.MiddleLeft }, _device, _refresh));
        root.Controls.Add(Row(new Label { Text = "Axis", Width = 70, TextAlign = ContentAlignment.MiddleLeft }, _axis));

        root.Controls.Add(Section("入力モニター"));
        root.Controls.Add(Row(new Label { Text = "物理入力", Width = 70, TextAlign = ContentAlignment.MiddleLeft }, _inputBar, _inputValue));
        root.Controls.Add(Row(new Label { Text = "vJoy出力", Width = 70, TextAlign = ContentAlignment.MiddleLeft }, _outputBar, _outputValue));

        root.Controls.Add(Section("キャリブレーション"));
        root.Controls.Add(Row(_setMin, _setMax, _calibration));
        root.Controls.Add(Row(new Label { Text = "Deadzone", Width = 70 }, _deadzone, new Label { Text = "%", AutoSize = true }, _invert));

        root.Controls.Add(Section("入力方式"));
        root.Controls.Add(Row(_axisEnabled, _buttonEnabled));
        root.Controls.Add(Row(new Label { Text = "Button Source", AutoSize = true }, _button, new Label { Text = "→ vJoy Button 1", AutoSize = true }));

        root.Controls.Add(Section("デバイス非表示"));
        root.Controls.Add(_hide);
        root.Controls.Add(Row(_recover, _inspectHidden));
        root.Controls.Add(Note("HidHide Configuration Clientを閉じてから実行してください。同時起動中はアクセス拒否(0x0005)になります。"));
        root.Controls.Add(Note("過去にHandbrakeMapperが非表示にしたデバイスを解除します。START不要。HidHideの他アプリの設定は変更しません。"));
        root.Controls.Add(Note("ONの場合、初回だけUACが表示されます。HidHide設定変更後に自動でデバイス再起動を試みます。ゲームから元デバイスが見える場合はUSBを一度抜き差ししてください。"));

        root.Controls.Add(Section("ドライバ"));
        root.Controls.Add(Row(new Label { Text = "vJoy:", Width = 70 }, _vjoyStatus));
        root.Controls.Add(Row(new Label { Text = "HidHide:", Width = 70 }, _hidStatus));

        root.Controls.Add(Section("実行"));
        root.Controls.Add(Row(_start));
        root.Controls.Add(_status);
    }

    private static Label Title(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 18F, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 4)
    };

    private static Label Section(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(0, 14, 0, 5)
    };

    private static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        MaximumSize = new Size(520, 0),
        Margin = new Padding(0, 2, 0, 5)
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        foreach (var control in controls)
        {
            control.Margin = new Padding(0, 3, 8, 3);
            panel.Controls.Add(control);
        }
        return panel;
    }

    private void WireEvents()
    {
        _refresh.Click += (_, _) => RefreshDevices();
        _detectDevice.Click += async (_, _) => await DetectDeviceAsync();
        _device.SelectedIndexChanged += (_, _) => DeviceChanged();
        _axis.SelectedIndexChanged += (_, _) => SaveUiToSettings();
        _setMin.Click += (_, _) => SetCalibration(min: true);
        _setMax.Click += (_, _) => SetCalibration(min: false);
        _deadzone.ValueChanged += (_, _) => SaveUiToSettings();
        _invert.CheckedChanged += (_, _) => SaveUiToSettings();
        _axisEnabled.CheckedChanged += (_, _) => { UpdateInputModeControls(); SaveUiToSettings(); };
        _buttonEnabled.CheckedChanged += (_, _) => { UpdateInputModeControls(); SaveUiToSettings(); };
        _button.SelectedIndexChanged += (_, _) => SaveUiToSettings();
        _hide.CheckedChanged += (_, _) => SaveUiToSettings();
        _start.Click += async (_, _) => await ToggleStartAsync();
        _recover.Click += async (_, _) => await RecoverHiddenAsync();
        _inspectHidden.Click += async (_, _) => await InspectHiddenAsync();
        _uiTimer.Tick += (_, _) => UpdateMonitor();
        FormClosing += async (_, _) =>
        {
            _uiTimer.Stop();
            SaveUiToSettings();
            await _mapper.StopAsync();
            _vjoy.Dispose();
        };
    }

    private void ApplySettingsToUi()
    {
        _deadzone.Value = Math.Clamp(_settings.DeadzonePercent, _deadzone.Minimum, _deadzone.Maximum);
        _invert.Checked = _settings.Invert;
        _axisEnabled.Checked = _settings.AxisOutputEnabled;
        _buttonEnabled.Checked = _settings.ButtonOutputEnabled;
        UpdateInputModeControls();
        _hide.Checked = _settings.HidePhysicalDevice;
        UpdateCalibrationLabel();
    }

    private void RefreshDevices()
    {
        var oldName = _settings.DeviceName;
        var oldId = _settings.DeviceId;

        _devices = _joystick.Enumerate();
        _device.Items.Clear();

        foreach (var d in _devices)
            _device.Items.Add($"{d.Name}  [Joy {d.Id + 1}]");

        var selected = -1;

        // Prefer the WinMM slot. This keeps an existing selection after upgrading
        // from the old generic "Microsoft PC joystick driver" display name.
        if (oldId >= 0)
            selected = _devices.ToList().FindIndex(d => d.Id == oldId);

        if (selected < 0 && !string.IsNullOrWhiteSpace(oldName))
            selected = _devices.ToList().FindIndex(d => d.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));

        if (selected < 0 && _devices.Count > 0)
            selected = 0;

        if (selected >= 0)
            _device.SelectedIndex = selected;
        else
        {
            _axis.Items.Clear();
            _button.Items.Clear();
            _status.Text = "ゲームコントローラーが見つかりません。";
        }
    }

    private void DeviceChanged()
    {
        if (_device.SelectedIndex < 0 || _device.SelectedIndex >= _devices.Count) return;
        var d = _devices[_device.SelectedIndex];
        _settings.DeviceId = d.Id;
        _settings.DeviceName = d.Name;

        _axis.Items.Clear();
        foreach (var axis in d.Axes) _axis.Items.Add(axis.ToString());
        var axisIndex = d.Axes.ToList().IndexOf(_settings.Axis);
        _axis.SelectedIndex = axisIndex >= 0 ? axisIndex : 0;

        _button.Items.Clear();
        for (var i = 1; i <= Math.Max(1, d.ButtonCount); i++) _button.Items.Add(i.ToString());
        _button.SelectedIndex = Math.Clamp(_settings.SourceButton - 1, 0, _button.Items.Count - 1);

        SaveUiToSettings();
    }

    private void SaveUiToSettings()
    {
        if (_device.SelectedIndex >= 0 && _device.SelectedIndex < _devices.Count)
        {
            var d = _devices[_device.SelectedIndex];
            _settings.DeviceId = d.Id;
            _settings.DeviceName = d.Name;
        }
        if (_axis.SelectedItem is string axis && Enum.TryParse<AxisKind>(axis, out var parsed))
            _settings.Axis = parsed;
        _settings.DeadzonePercent = _deadzone.Value;
        _settings.Invert = _invert.Checked;
        _settings.AxisOutputEnabled = _axisEnabled.Checked;
        _settings.ButtonOutputEnabled = _buttonEnabled.Checked;
        if (_button.SelectedIndex >= 0) _settings.SourceButton = _button.SelectedIndex + 1;
        _settings.HidePhysicalDevice = _hide.Checked;
        SettingsStore.Save(_settings);
    }

    private void RefreshDriverStatus()
    {
        _vjoyStatus.Text = _vjoy.Probe(out var vmsg) ? "OK" : "NG - " + vmsg;
        _vjoyStatus.ForeColor = _vjoyStatus.Text.StartsWith("OK") ? Color.DarkGreen : Color.DarkRed;
        _hidStatus.Text = _hidHide.IsInstalled ? "OK" : "NG - HidHideCLI.exe not found";
        _hidStatus.ForeColor = _hidHide.IsInstalled ? Color.DarkGreen : Color.DarkRed;
    }

    private async Task DetectDeviceAsync()
    {
        if (_devices.Count == 0)
            return;

        _detectDevice.Enabled = false;
        _refresh.Enabled = false;
        _status.Text = "2秒以内にハンドブレーキを大きく引いてください…";

        try
        {
            var baseline = new Dictionary<(int DeviceId, AxisKind Axis), uint>();
            var maxDelta = new Dictionary<(int DeviceId, AxisKind Axis), uint>();

            foreach (var d in _devices)
            {
                if (!_joystick.TryRead(d.Id, out var state))
                    continue;

                foreach (var axis in d.Axes)
                {
                    var key = (d.Id, axis);
                    baseline[key] = state.GetAxis(axis);
                    maxDelta[key] = 0;
                }
            }

            var end = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < end)
            {
                foreach (var d in _devices)
                {
                    if (!_joystick.TryRead(d.Id, out var state))
                        continue;

                    foreach (var axis in d.Axes)
                    {
                        var key = (d.Id, axis);
                        if (!baseline.TryGetValue(key, out var start))
                            continue;

                        var now = state.GetAxis(axis);
                        var delta = now > start ? now - start : start - now;
                        if (delta > maxDelta[key])
                            maxDelta[key] = delta;
                    }
                }

                await Task.Delay(15);
            }

            if (maxDelta.Count == 0)
            {
                _status.Text = "入力デバイスを読み取れませんでした。";
                return;
            }

            var best = maxDelta.OrderByDescending(x => x.Value).First();
            if (best.Value < 1500)
            {
                _status.Text = "十分な入力変化を検出できませんでした。";
                return;
            }

            var deviceIndex = _devices.ToList().FindIndex(d => d.Id == best.Key.DeviceId);
            if (deviceIndex < 0)
                return;

            _device.SelectedIndex = deviceIndex;

            var device = _devices[deviceIndex];
            var axisIndex = device.Axes.ToList().IndexOf(best.Key.Axis);
            if (axisIndex >= 0)
                _axis.SelectedIndex = axisIndex;

            _status.Text = $"検出: {device.Name} / {best.Key.Axis} Axis";
        }
        finally
        {
            _detectDevice.Enabled = true;
            _refresh.Enabled = true;
        }
    }

    private void SetCalibration(bool min)
    {
        if (_device.SelectedIndex < 0 || _axis.SelectedItem is not string axisName || !Enum.TryParse<AxisKind>(axisName, out var axis)) return;
        var d = _devices[_device.SelectedIndex];
        if (!_joystick.TryRead(d.Id, out var state)) return;
        var value = state.GetAxis(axis);
        if (min) _settings.CalibrationMin = value;
        else _settings.CalibrationMax = value;
        UpdateCalibrationLabel();
        SettingsStore.Save(_settings);
    }

    private void UpdateCalibrationLabel()
    {
        _calibration.Text = $"0% {_settings.CalibrationMin} / 100% {_settings.CalibrationMax}";
    }

    private void UpdateMonitor()
    {
        uint raw = _mapper.IsRunning ? _mapper.LastRaw : 0;
        double output = _mapper.IsRunning ? _mapper.LastOutput : 0;

        if (!_mapper.IsRunning && _device.SelectedIndex >= 0 && _axis.SelectedItem is string axisName && Enum.TryParse<AxisKind>(axisName, out var axis))
        {
            var d = _devices[_device.SelectedIndex];
            if (_joystick.TryRead(d.Id, out var state)) raw = state.GetAxis(axis);
            output = PreviewNormalize(raw);
        }

        _inputBar.Value = Math.Clamp((int)Math.Round(raw / 65535.0 * 1000), 0, 1000);
        _outputBar.Value = Math.Clamp((int)Math.Round(output * 1000), 0, 1000);
        _inputValue.Text = $"{raw / 65535.0 * 100:0.0}%";
        _outputValue.Text = $"{output * 100:0.0}%";

        if (_mapper.IsRunning && !string.IsNullOrWhiteSpace(_mapper.LastError))
        {
            _status.Text = "ERROR: " + _mapper.LastError;
            _ = StopMappingAsync();
        }
    }

    private double PreviewNormalize(uint raw)
    {
        if (!_axisEnabled.Checked) return 0;
        if (_settings.CalibrationMax == _settings.CalibrationMin) return 0;
        var value = ((double)raw - _settings.CalibrationMin) / ((double)_settings.CalibrationMax - _settings.CalibrationMin);
        value = Math.Clamp(value, 0, 1);
        if (_invert.Checked) value = 1 - value;
        var dz = (double)_deadzone.Value / 100.0;
        if (value <= dz) return 0;
        return Math.Clamp((value - dz) / (1 - dz), 0, 1);
    }

    private async Task ToggleStartAsync()
    {
        if (_mapper.IsRunning)
            await StopMappingAsync();
        else
            await StartMappingAsync();
    }

    private static List<string> RecoveryPaths(AppSettings settings) =>
        settings.HidHidePaths.Concat(settings.HidHidePendingPaths)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private async Task InspectHiddenAsync()
    {
        if (_busy) return;
        _busy = true;
        _inspectHidden.Enabled = false;
        _recover.Enabled = false;
        _start.Enabled = false;
        try
        {
            var hidden = await _hidHide.GetHiddenDevicesAsync();
            if (hidden.Count == 0)
            {
                MessageBox.Show(this, "HidHideに登録されている非表示デバイスはありません。",
                    "HidHide登録一覧", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new Form
            {
                Text = "HidHideの非表示登録を個別解除",
                StartPosition = FormStartPosition.CenterParent,
                Width = 850,
                Height = 510,
                MinimumSize = new Size(650, 400),
                Font = Font
            };
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            dialog.Controls.Add(root);
            root.Controls.Add(new Label
            {
                Text = "HidHideが実際に非表示登録しているデバイスです。解除するものだけ選択してください。\n他のツールが管理するデバイスも含まれるため、一括選択はしません。",
                AutoSize = true, MaximumSize = new Size(800, 0)
            }, 0, 0);

            var list = new CheckedListBox
            {
                Dock = DockStyle.Fill, CheckOnClick = true,
                HorizontalScrollbar = true, IntegralHeight = false
            };
            foreach (var entry in hidden)
                list.Items.Add(entry.DisplayName + "  [" + entry.InstancePath + "]");
            root.Controls.Add(list, 0, 1);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true
            };
            var cancel = new Button { Text = "閉じる", Width = 100, DialogResult = DialogResult.Cancel };
            var unhide = new Button { Text = "選択した登録を解除", Width = 170, DialogResult = DialogResult.OK };
            actions.Controls.Add(cancel);
            actions.Controls.Add(unhide);
            root.Controls.Add(actions, 0, 2);
            dialog.AcceptButton = unhide;
            dialog.CancelButton = cancel;

            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var paths = list.CheckedIndices.Cast<int>()
                .Select(index => hidden[index].InstancePath).ToList();
            if (paths.Count == 0)
            {
                MessageBox.Show(this, "解除するデバイスを選択してください。");
                return;
            }

            var message = "以下の " + paths.Count + " 件をHidHideの非表示対象から削除します。\n\n" +
                string.Join("\n", list.CheckedIndices.Cast<int>().Select(i => hidden[i].DisplayName)) +
                "\n\n実行しますか？";
            if (MessageBox.Show(this, message, "解除対象の最終確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            await UnhideRecordedAsync(paths);
            _status.Text = "選択したHidHide登録を解除しました。USB再接続後、再検索してください。";
            RefreshDevices();
        }
        catch (Exception ex)
        {
            _status.Text = "HidHide登録一覧の操作に失敗しました: " + ex.Message;
            MessageBox.Show(this, ex.Message, "HidHideエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _inspectHidden.Enabled = true;
            _recover.Enabled = true;
            _start.Enabled = true;
        }
    }

    private async Task RecoverHiddenAsync()
    {
        if (_busy) return;
        var paths = RecoveryPaths(_settings);
        if (paths.Count == 0)
        {
            _status.Text = "このアプリの解除対象の記録はありません。HidHide Configuration Clientも確認してください。";
            return;
        }

        if (MessageBox.Show(this,
            $"HandbrakeMapperが記録した {paths.Count} 件のHIDデバイスを非表示解除します。\n続行しますか？",
            "HidHide緊急解除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _busy = true;
        _recover.Enabled = false;
        _inspectHidden.Enabled = false;
        _start.Enabled = false;
        try
        {
            if (_mapper.IsRunning) await StopMappingAsync();
            await UnhideRecordedAsync(paths);
            _status.Text = "非表示設定を解除しました。必要ならUSBを抜き差しし、再検索してください。";
            RefreshDevices();
        }
        catch (Exception ex)
        {
            _status.Text = "解除失敗。解除対象の記録は保持されています: " + ex.Message;
        }
        finally
        {
            _busy = false;
            _recover.Enabled = true;
            _inspectHidden.Enabled = true;
            _start.Enabled = true;
        }
    }

    private async Task UnhideRecordedAsync(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return;
        // Unhide only paths that really are hidden. A stale recovery journal does
        // not justify a UAC prompt or changing other users' HidHide settings.
        var live = await _hidHide.GetConfigurationAsync();
        var registered = paths.Where(live.HiddenPaths.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (registered.Count > 0)
            await _hidHide.RemoveHiddenAsync(registered);

        _settings.HidHidePaths = _settings.HidHidePaths
            .Except(paths, StringComparer.OrdinalIgnoreCase).ToList();
        _settings.HidHidePendingPaths = _settings.HidHidePendingPaths
            .Except(paths, StringComparer.OrdinalIgnoreCase).ToList();
        SettingsStore.Save(_settings);
    }

    private async Task StartMappingAsync()
    {
        if (_busy) return;
        SaveUiToSettings();
        if (_device.SelectedIndex < 0 || _device.SelectedIndex >= _devices.Count)
        {
            _status.Text = "入力デバイスを選択してください。";
            return;
        }

        var selectedDevice = _devices[_device.SelectedIndex];
        var warning = _settings.HidePhysicalDevice
            ? "HidHideによりゲームや他のアプリからこの物理デバイスが見えなくなります。"
            : "HidHideによる新規の非表示は行いません。";
        if (MessageBox.Show(this,
            $"対象: {selectedDevice.Name} [Joy {selectedDevice.Id + 1}]\n\n{warning}\n\nこのデバイスでSTARTしますか？",
            "START確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _busy = true;
        _start.Enabled = false;
        _recover.Enabled = false;
        _inspectHidden.Enabled = false;
        var hiddenThisAttempt = new List<string>();
        try
        {
            var managedPaths = RecoveryPaths(_settings);
            HidHideService.ConfigurationSnapshot? live = null;
            if (_settings.HidePhysicalDevice || managedPaths.Count > 0)
                live = await _hidHide.GetConfigurationAsync(); // Read-only. No UAC.

            if (_settings.HidePhysicalDevice)
            {
                if (!_hidHide.IsInstalled)
                    throw new InvalidOperationException("HidHide is not installed.");
                if (live!.InverseMode)
                    throw new InvalidOperationException(
                        "HidHideのアプリ許可リストが逆転モードです。安全のためSTARTを中止しました。");

                var currentPaths = await _hidHide.FindDevicePathsAsync(
                    selectedDevice.Name, selectedDevice.OemKey);
                if (currentPaths.Count == 0)
                    throw new InvalidOperationException("選択した物理デバイスのHIDパスを特定できませんでした。");

                var stale = managedPaths.Except(currentPaths,
                    StringComparer.OrdinalIgnoreCase).ToList();
                if (stale.Count > 0)
                    await UnhideRecordedAsync(stale);

                var exe = Environment.ProcessPath ?? Application.ExecutablePath;
                var isAllowed = live.AllowedApplications.Contains(exe);
                var allHidden = currentPaths.All(live.HiddenPaths.Contains);
                // Existing HidHide state is authoritative. Do not launch elevated
                // PowerShell just to re-apply an already-correct configuration.
                if (!allHidden || !isAllowed || !live.CloakEnabled)
                {
                    _settings.HidHidePendingPaths = _settings.HidHidePendingPaths
                        .Concat(currentPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    SettingsStore.Save(_settings); // Write-ahead recovery journal.

                    _status.Text = "HidHideの変更が必要です。UACを許可してください…";
                    await _hidHide.ApplyHiddenAsync(exe, currentPaths, live.HiddenPaths);
                    hiddenThisAttempt = currentPaths.Except(
                        live.HiddenPaths, StringComparer.OrdinalIgnoreCase).ToList();
                }

                _settings.HidHidePaths = currentPaths.ToList();
                _settings.HidHidePendingPaths = _settings.HidHidePendingPaths
                    .Except(currentPaths, StringComparer.OrdinalIgnoreCase).ToList();
                SettingsStore.Save(_settings);
            }
            else
            {
                await UnhideRecordedAsync(managedPaths);
            }

            _mapper.Start(_settings);
            SetRunningUi(true);
            _status.Text = "Mapping active. ゲーム側ではvJoy DeviceをHandbrakeに割り当ててください。";
        }
        catch (Exception ex)
        {
            var error = ex.Message;
            if (hiddenThisAttempt.Count > 0)
            {
                try
                {
                    await UnhideRecordedAsync(hiddenThisAttempt);
                }
                catch (Exception recoveryError)
                {
                    error += " / 自動復旧失敗: " + recoveryError.Message +
                        "。緊急解除ボタンを使ってください。";
                }
            }
            _status.Text = "ERROR: " + error;
            RefreshDriverStatus();
        }
        finally
        {
            _busy = false;
            _start.Enabled = true;
            _recover.Enabled = true;
            _inspectHidden.Enabled = true;
        }
    }

    private async Task StopMappingAsync()
    {
        await _mapper.StopAsync();
        SetRunningUi(false);
        if (string.IsNullOrWhiteSpace(_mapper.LastError)) _status.Text = "Stopped.";
    }

    private void SetRunningUi(bool running)
    {
        _start.Text = running ? "STOP" : "START";
        _device.Enabled = !running;
        _refresh.Enabled = !running;
        _detectDevice.Enabled = !running;
        _axisEnabled.Enabled = !running;
        _buttonEnabled.Enabled = !running;
        _hide.Enabled = !running;
        UpdateInputModeControls();
    }

    private void UpdateInputModeControls()
    {
        var editable = !_mapper.IsRunning;

        _axis.Enabled = editable && _axisEnabled.Checked;
        _setMin.Enabled = editable && _axisEnabled.Checked;
        _setMax.Enabled = editable && _axisEnabled.Checked;
        _deadzone.Enabled = editable && _axisEnabled.Checked;
        _invert.Enabled = editable && _axisEnabled.Checked;

        _button.Enabled = editable && _buttonEnabled.Checked;
    }
}
