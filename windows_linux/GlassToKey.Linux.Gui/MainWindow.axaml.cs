using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GlassToKey.Linux.Config;
using GlassToKey.Linux.Runtime;
using GlassToKey.Platform.Linux.Models;

namespace GlassToKey.Linux.Gui;

public partial class MainWindow : Window
{
    private readonly LinuxAppRuntime _runtime = new();

    public MainWindow()
    {
        InitializeComponent();
        WireEvents();
        LoadScreen();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireEvents()
    {
        this.FindControl<Button>("RefreshDevicesButton")!.Click += OnRefreshDevicesClick;
        this.FindControl<Button>("SwapSidesButton")!.Click += OnSwapSidesClick;
        this.FindControl<Button>("SaveSettingsButton")!.Click += OnSaveSettingsClick;
        this.FindControl<Button>("InitializeDefaultsButton")!.Click += OnInitializeDefaultsClick;
    }

    private void LoadScreen(string? statusOverride = null)
    {
        LinuxRuntimeConfiguration configuration = _runtime.LoadConfiguration();
        LinuxHostSettings settings = configuration.Settings;

        List<DeviceChoice> deviceChoices = BuildDeviceChoices(configuration.Devices);
        DeviceChoice autoChoice = deviceChoices[0];
        LeftDeviceCombo.ItemsSource = deviceChoices;
        RightDeviceCombo.ItemsSource = deviceChoices;
        LeftDeviceCombo.SelectedItem = SelectDeviceChoice(deviceChoices, settings.LeftTrackpadStableId) ?? autoChoice;
        RightDeviceCombo.SelectedItem = SelectDeviceChoice(deviceChoices, settings.RightTrackpadStableId) ?? autoChoice;

        List<PresetChoice> presetChoices = BuildPresetChoices();
        LayoutPresetCombo.ItemsSource = presetChoices;
        LayoutPresetCombo.SelectedItem = SelectPresetChoice(presetChoices, settings.LayoutPresetName) ?? presetChoices[0];
        KeymapPathBox.Text = settings.KeymapPath ?? string.Empty;

        SettingsPathText.Text = $"Settings: {configuration.SettingsPath}";
        ResolvedBindingsText.Text = BuildResolvedBindingsText(configuration);
        WarningsText.Text = configuration.Warnings.Count == 0
            ? "No current warnings."
            : string.Join(Environment.NewLine, configuration.Warnings);
        StatusText.Text = statusOverride ?? $"Detected {configuration.Devices.Count} candidate trackpad(s). Save writes directly to the XDG-backed Linux settings file.";
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
        settings.LeftTrackpadStableId = (LeftDeviceCombo.SelectedItem as DeviceChoice)?.StableId;
        settings.RightTrackpadStableId = (RightDeviceCombo.SelectedItem as DeviceChoice)?.StableId;
        settings.LayoutPresetName = (LayoutPresetCombo.SelectedItem as PresetChoice)?.Name ?? TrackpadLayoutPreset.SixByThree.Name;
        settings.KeymapPath = string.IsNullOrWhiteSpace(KeymapPathBox.Text) ? null : KeymapPathBox.Text.Trim();
        string path = _runtime.SaveSettings(settings);
        LoadScreen($"Saved Linux host settings to {path}.");
    }

    private void OnInitializeDefaultsClick(object? sender, RoutedEventArgs e)
    {
        string path = _runtime.InitializeSettings();
        LoadScreen($"Initialized Linux host settings at {path}.");
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
