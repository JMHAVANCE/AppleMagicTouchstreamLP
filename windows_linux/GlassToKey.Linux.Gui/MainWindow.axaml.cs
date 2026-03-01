using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GlassToKey.Linux.Config;
using GlassToKey.Linux.Runtime;
using GlassToKey.Platform.Linux.Models;

namespace GlassToKey.Linux.Gui;

public partial class MainWindow : Window
{
    private readonly LinuxAppRuntime _runtime = new();
    private readonly LinuxRuntimeServiceController _runtimeController = new();
    private readonly DispatcherTimer _runtimeStatusTimer;
    private readonly ComboBox _leftDeviceCombo;
    private readonly ComboBox _rightDeviceCombo;
    private readonly ComboBox _layoutPresetCombo;
    private readonly TextBox _keymapPathBox;
    private readonly TextBlock _keymapStatusText;
    private readonly TextBlock _runtimeSummaryText;
    private readonly TextBlock _runtimeBindingsText;
    private readonly TextBlock _settingsPathText;
    private readonly TextBlock _resolvedBindingsText;
    private readonly TextBlock _warningsText;
    private readonly TextBlock _statusText;
    private readonly TextBox _doctorReportBox;
    private bool _allowExit;
    private bool _runtimeRefreshPending;
    private LinuxRuntimeServiceSnapshot _runtimeSnapshot = new(
        LinuxRuntimeServiceStatus.Unavailable,
        "glasstokey-linux.service",
        "Checking runtime owner state.",
        null,
        null,
        null);

    public MainWindow()
    {
        InitializeComponent();
        _leftDeviceCombo = RequireControl<ComboBox>("LeftDeviceCombo");
        _rightDeviceCombo = RequireControl<ComboBox>("RightDeviceCombo");
        _layoutPresetCombo = RequireControl<ComboBox>("LayoutPresetCombo");
        _keymapPathBox = RequireControl<TextBox>("KeymapPathBox");
        _keymapStatusText = RequireControl<TextBlock>("KeymapStatusText");
        _runtimeSummaryText = RequireControl<TextBlock>("RuntimeSummaryText");
        _runtimeBindingsText = RequireControl<TextBlock>("RuntimeBindingsText");
        _settingsPathText = RequireControl<TextBlock>("SettingsPathText");
        _resolvedBindingsText = RequireControl<TextBlock>("ResolvedBindingsText");
        _warningsText = RequireControl<TextBlock>("WarningsText");
        _statusText = RequireControl<TextBlock>("StatusText");
        _doctorReportBox = RequireControl<TextBox>("DoctorReportBox");
        _runtimeStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _runtimeStatusTimer.Tick += OnRuntimeStatusTimerTick;
        Closing += OnWindowClosing;
        WireEvents();
        LoadScreen();
        ApplyRuntimeSnapshot(_runtimeSnapshot);
        _runtimeStatusTimer.Start();
        _ = RefreshRuntimeSnapshotAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireEvents()
    {
        RequireControl<Button>("RefreshDevicesButton").Click += OnRefreshDevicesClick;
        RequireControl<Button>("SwapSidesButton").Click += OnSwapSidesClick;
        RequireControl<Button>("StartRuntimeButton").Click += OnStartRuntimeClick;
        RequireControl<Button>("StopRuntimeButton").Click += OnStopRuntimeClick;
        RequireControl<Button>("SaveSettingsButton").Click += OnSaveSettingsClick;
        RequireControl<Button>("InitializeDefaultsButton").Click += OnInitializeDefaultsClick;
        RequireControl<Button>("BrowseKeymapButton").Click += OnBrowseKeymapClick;
        RequireControl<Button>("ClearKeymapButton").Click += OnClearKeymapClick;
        RequireControl<Button>("RunDoctorButton").Click += OnRunDoctorClick;
    }

    private void LoadScreen(string? statusOverride = null)
    {
        LinuxRuntimeConfiguration configuration = _runtime.LoadConfiguration();
        LinuxHostSettings settings = configuration.Settings;

        List<DeviceChoice> deviceChoices = BuildDeviceChoices(configuration.Devices);
        DeviceChoice autoChoice = deviceChoices[0];
        _leftDeviceCombo.ItemsSource = deviceChoices;
        _rightDeviceCombo.ItemsSource = deviceChoices;
        _leftDeviceCombo.SelectedItem = SelectDeviceChoice(deviceChoices, settings.LeftTrackpadStableId) ?? autoChoice;
        _rightDeviceCombo.SelectedItem = SelectDeviceChoice(deviceChoices, settings.RightTrackpadStableId) ?? autoChoice;

        List<PresetChoice> presetChoices = BuildPresetChoices();
        _layoutPresetCombo.ItemsSource = presetChoices;
        _layoutPresetCombo.SelectedItem = SelectPresetChoice(presetChoices, settings.LayoutPresetName) ?? presetChoices[0];
        _keymapPathBox.Text = settings.KeymapPath ?? string.Empty;
        _keymapStatusText.Text = BuildKeymapStatusText(settings.KeymapPath);

        _settingsPathText.Text = $"Settings: {configuration.SettingsPath}";
        _resolvedBindingsText.Text = BuildResolvedBindingsText(configuration);
        _warningsText.Text = configuration.Warnings.Count == 0
            ? "No current warnings."
            : string.Join(Environment.NewLine, configuration.Warnings);
        _statusText.Text = statusOverride ?? $"Detected {configuration.Devices.Count} candidate trackpad(s). Save writes directly to the XDG-backed Linux settings file.";
        if (_runtimeSnapshot.Status is LinuxRuntimeServiceStatus.Running or LinuxRuntimeServiceStatus.Starting)
        {
            _statusText.Text += " Restart the runtime service to apply setting changes.";
        }

        if (string.IsNullOrWhiteSpace(_doctorReportBox.Text))
        {
            _doctorReportBox.Text = "Run Doctor to validate evdev, uinput, bundled keymap, and current device bindings.";
        }
    }

    private void OnRefreshDevicesClick(object? sender, RoutedEventArgs e)
    {
        LoadScreen("Device list refreshed from the current Linux evdev state.");
    }

    private void OnSwapSidesClick(object? sender, RoutedEventArgs e)
    {
        string path = _runtime.SwapTrackpadBindings();
        LoadScreen($"Swapped left/right bindings in {path}.");
    }

    private void OnSaveSettingsClick(object? sender, RoutedEventArgs e)
    {
        LinuxHostSettings settings = _runtime.LoadSettings();
        settings.LeftTrackpadStableId = (_leftDeviceCombo.SelectedItem as DeviceChoice)?.StableId;
        settings.RightTrackpadStableId = (_rightDeviceCombo.SelectedItem as DeviceChoice)?.StableId;
        settings.LayoutPresetName = (_layoutPresetCombo.SelectedItem as PresetChoice)?.Name ?? TrackpadLayoutPreset.SixByThree.Name;
        settings.KeymapPath = string.IsNullOrWhiteSpace(_keymapPathBox.Text) ? null : _keymapPathBox.Text.Trim();
        string path = _runtime.SaveSettings(settings);
        LoadScreen($"Saved Linux host settings to {path}.");
    }

    private void OnInitializeDefaultsClick(object? sender, RoutedEventArgs e)
    {
        string path = _runtime.InitializeSettings();
        LoadScreen($"Initialized Linux host settings at {path}.");
    }

    private async void OnBrowseKeymapClick(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanOpen)
        {
            _statusText.Text = "This Linux GUI session cannot open a file picker on the current platform backend.";
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a GlassToKey keymap JSON file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON") { Patterns = ["*.json"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count == 0)
        {
            _statusText.Text = "Keymap selection canceled.";
            return;
        }

        string? localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            _statusText.Text = "The selected keymap could not be resolved to a local file path.";
            return;
        }

        _keymapPathBox.Text = localPath;
        _keymapStatusText.Text = BuildKeymapStatusText(localPath);
        _statusText.Text = "Selected a custom keymap. Save Settings to apply it to the Linux runtime.";
    }

    private void OnClearKeymapClick(object? sender, RoutedEventArgs e)
    {
        _keymapPathBox.Text = string.Empty;
        _keymapStatusText.Text = BuildKeymapStatusText(null);
        _statusText.Text = "Cleared the custom keymap path. Save Settings to return to the bundled Linux default.";
    }

    private void OnRunDoctorClick(object? sender, RoutedEventArgs e)
    {
        RunDoctorFromStatusArea();
    }

    private async void OnStartRuntimeClick(object? sender, RoutedEventArgs e)
    {
        await StartRuntimeAsync().ConfigureAwait(false);
    }

    private async void OnStopRuntimeClick(object? sender, RoutedEventArgs e)
    {
        await StopRuntimeAsync().ConfigureAwait(false);
    }

    public void RunDoctorFromStatusArea()
    {
        LinuxDoctorResult result = LinuxDoctorRunner.Run();
        _doctorReportBox.Text = result.Report;
        _statusText.Text = result.Success
            ? "Doctor completed successfully."
            : "Doctor found issues. Review the report below before treating the runtime as ready.";
    }

    public void HideToStatusArea()
    {
        _statusText.Text = "Window hidden. The runtime owner keeps running outside the config UI. Use the GlassToKey top-bar item to reopen the control surface.";
        Hide();
    }

    public void StartRuntimeFromStatusArea()
    {
        _ = StartRuntimeAsync();
    }

    public void StopRuntimeFromStatusArea()
    {
        _ = StopRuntimeAsync();
    }

    public void RequestExit()
    {
        _allowExit = true;
        Close();
    }

    private static List<DeviceChoice> BuildDeviceChoices(IReadOnlyList<LinuxInputDeviceDescriptor> devices)
    {
        List<DeviceChoice> choices =
        [
            new DeviceChoice("(Auto / first available)", null)
        ];

        for (int index = 0; index < devices.Count; index++)
        {
            LinuxInputDeviceDescriptor device = devices[index];
            choices.Add(new DeviceChoice(
                $"{device.DisplayName}  [{device.DeviceNode}]  {device.StableId}",
                device.StableId));
        }

        return choices;
    }

    private static List<PresetChoice> BuildPresetChoices()
    {
        List<PresetChoice> choices = new(TrackpadLayoutPreset.All.Length);
        for (int index = 0; index < TrackpadLayoutPreset.All.Length; index++)
        {
            TrackpadLayoutPreset preset = TrackpadLayoutPreset.All[index];
            choices.Add(new PresetChoice(preset.DisplayName, preset.Name));
        }

        return choices;
    }

    private static DeviceChoice? SelectDeviceChoice(IEnumerable<DeviceChoice> choices, string? stableId)
    {
        foreach (DeviceChoice choice in choices)
        {
            if (string.Equals(choice.StableId, stableId, StringComparison.OrdinalIgnoreCase))
            {
                return choice;
            }
        }

        return null;
    }

    private static PresetChoice? SelectPresetChoice(IEnumerable<PresetChoice> choices, string? name)
    {
        foreach (PresetChoice choice in choices)
        {
            if (string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return choice;
            }
        }

        return null;
    }

    private static string BuildResolvedBindingsText(LinuxRuntimeConfiguration configuration)
    {
        if (configuration.Bindings.Count == 0)
        {
            return "No trackpad bindings are currently resolved.";
        }

        List<string> lines = new(configuration.Bindings.Count + 1)
        {
            $"Layout preset: {configuration.LayoutPreset.DisplayName}"
        };
        for (int index = 0; index < configuration.Bindings.Count; index++)
        {
            LinuxTrackpadBinding binding = configuration.Bindings[index];
            lines.Add($"{binding.Side}: {binding.Device.DisplayName} [{binding.Device.DeviceNode}]");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildKeymapStatusText(string? keymapPath)
    {
        if (string.IsNullOrWhiteSpace(keymapPath))
        {
            return "Keymap source: bundled Linux default.";
        }

        return File.Exists(keymapPath)
            ? $"Keymap source: custom file '{keymapPath}'."
            : $"Keymap source: missing custom file '{keymapPath}'.";
    }

    private T RequireControl<T>(string name) where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"Required control '{name}' was not found in the Linux GUI.");
    }

    private async Task StartRuntimeAsync()
    {
        _statusText.Text = "Starting the Linux runtime owner service.";
        ApplyRuntimeSnapshot(await _runtimeController.StartAsync().ConfigureAwait(false));
    }

    private async Task StopRuntimeAsync()
    {
        _statusText.Text = "Stopping the Linux runtime owner service.";
        ApplyRuntimeSnapshot(await _runtimeController.StopAsync().ConfigureAwait(false));
    }

    private async void OnRuntimeStatusTimerTick(object? sender, EventArgs e)
    {
        await RefreshRuntimeSnapshotAsync().ConfigureAwait(false);
    }

    private async Task RefreshRuntimeSnapshotAsync()
    {
        if (_runtimeRefreshPending)
        {
            return;
        }

        _runtimeRefreshPending = true;
        try
        {
            ApplyRuntimeSnapshot(await _runtimeController.RefreshAsync().ConfigureAwait(false));
        }
        finally
        {
            _runtimeRefreshPending = false;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowExit)
        {
            _runtimeStatusTimer.Stop();
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            e.Cancel = true;
            HideToStatusArea();
        }
    }

    private void ApplyRuntimeSnapshot(LinuxRuntimeServiceSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyRuntimeSnapshot(snapshot));
            return;
        }

        _runtimeSnapshot = snapshot;
        _runtimeSummaryText.Text = snapshot.Failure is null
            ? $"Runtime owner: {snapshot.Status}. {snapshot.Message}"
            : $"Runtime owner: {snapshot.Status}. {snapshot.Message} Failure: {snapshot.Failure}";
        _runtimeBindingsText.Text = BuildRuntimeServiceText(snapshot);

        RequireControl<Button>("StartRuntimeButton").IsEnabled = snapshot.CanStart;
        RequireControl<Button>("StopRuntimeButton").IsEnabled = snapshot.CanStop;
    }

    private static string BuildRuntimeServiceText(LinuxRuntimeServiceSnapshot snapshot)
    {
        List<string> lines =
        [
            $"Service unit: {snapshot.ServiceName}"
        ];

        if (!string.IsNullOrWhiteSpace(snapshot.UnitFileState))
        {
            lines.Add($"Unit file state: {snapshot.UnitFileState}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.FragmentPath))
        {
            lines.Add($"Unit path: {snapshot.FragmentPath}");
        }

        if (snapshot.Status == LinuxRuntimeServiceStatus.NotInstalled)
        {
            lines.Add("Install a user service to keep the runtime on the hotpath outside this config UI.");
        }
        else if (snapshot.Status == LinuxRuntimeServiceStatus.Unavailable)
        {
            lines.Add("The current session cannot reach systemd --user. Start the runtime from a normal desktop login.");
        }
        else
        {
            lines.Add("This window controls the runtime owner service but does not host the engine itself.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record DeviceChoice(string Label, string? StableId)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private sealed record PresetChoice(string Label, string Name)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
