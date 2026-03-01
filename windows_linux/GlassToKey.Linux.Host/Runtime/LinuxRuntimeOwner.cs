using System.Text.Json;
using GlassToKey.Linux.Config;
using GlassToKey.Platform.Linux;
using GlassToKey.Platform.Linux.Contracts;
using GlassToKey.Platform.Linux.Models;
using GlassToKey.Platform.Linux.Uinput;

namespace GlassToKey.Linux.Runtime;

public sealed class LinuxRuntimeOwner
{
    private static readonly TimeSpan SettingsPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions SignatureSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LinuxAppRuntime _appRuntime;
    private readonly LinuxInputRuntimeService _runtime;

    public LinuxRuntimeOwner(
        LinuxAppRuntime? appRuntime = null,
        LinuxInputRuntimeService? runtime = null)
    {
        _appRuntime = appRuntime ?? new LinuxAppRuntime();
        _runtime = runtime ?? new LinuxInputRuntimeService();
    }

    public async Task RunAsync(
        ILinuxRuntimeObserver? observer = null,
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        LinuxRuntimeConfiguration configuration = _appRuntime.LoadConfiguration();
        string settingsSignature = BuildSettingsSignature(configuration.Settings);
        RuntimeSession? session = null;
        bool waitingForBindingsLogged = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (session == null)
                {
                    if (configuration.Bindings.Count == 0)
                    {
                        if (!waitingForBindingsLogged)
                        {
                            logger?.Invoke("Runtime owner is waiting for trackpad bindings.");
                            waitingForBindingsLogged = true;
                        }
                    }
                    else
                    {
                        session = StartSession(configuration, observer, cancellationToken);
                        waitingForBindingsLogged = false;
                        LogConfiguration(logger, configuration, isReload: false);
                    }
                }

                Task pollTask = Task.Delay(SettingsPollInterval, cancellationToken);
                if (session != null)
                {
                    Task completed = await Task.WhenAny(session.RunTask, pollTask).ConfigureAwait(false);
                    if (completed == session.RunTask)
                    {
                        await session.RunTask.ConfigureAwait(false);
                        break;
                    }
                }
                else
                {
                    await pollTask.ConfigureAwait(false);
                }

                LinuxRuntimeConfiguration updated = _appRuntime.LoadConfiguration();
                string updatedSignature = BuildSettingsSignature(updated.Settings);
                if (updatedSignature == settingsSignature)
                {
                    configuration = updated;
                    continue;
                }

                settingsSignature = updatedSignature;
                configuration = updated;
                LogConfiguration(logger, configuration, isReload: true);

                if (session == null)
                {
                    continue;
                }

                await session.StopAsync().ConfigureAwait(false);
                session.Dispose();
                session = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown path.
        }
        finally
        {
            if (session != null)
            {
                await session.StopAsync().ConfigureAwait(false);
                session.Dispose();
            }
        }
    }

    private RuntimeSession StartSession(
        LinuxRuntimeConfiguration configuration,
        ILinuxRuntimeObserver? observer,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        LinuxUinputDispatcher dispatcher = new();
        TouchProcessorRuntimeHost engine = new(dispatcher, configuration.Keymap, configuration.LayoutPreset, configuration.SharedProfile);
        LinuxInputRuntimeOptions options = new()
        {
            Observer = observer
        };
        Task runTask = _runtime.RunAsync([.. configuration.Bindings], engine, options, sessionCts.Token);
        return new RuntimeSession(sessionCts, dispatcher, engine, runTask);
    }

    private static void LogConfiguration(Action<string>? logger, LinuxRuntimeConfiguration configuration, bool isReload)
    {
        if (logger == null)
        {
            return;
        }

        string action = isReload ? "Reloaded" : "Loaded";
        logger($"{action} runtime config: layout={configuration.LayoutPreset.Name}, keymap={configuration.Settings.KeymapPath ?? "(bundled default)"}, bindings={configuration.Bindings.Count}.");
        for (int index = 0; index < configuration.Bindings.Count; index++)
        {
            LinuxTrackpadBinding binding = configuration.Bindings[index];
            logger($"  {binding.Side}: {binding.Device.DisplayName} [{binding.Device.DeviceNode}]");
        }

        for (int index = 0; index < configuration.Warnings.Count; index++)
        {
            logger($"  Warning: {configuration.Warnings[index]}");
        }
    }

    private static string BuildSettingsSignature(LinuxHostSettings settings)
    {
        LinuxHostSettings normalized = new()
        {
            Version = settings.Version,
            LayoutPresetName = settings.LayoutPresetName,
            KeymapPath = settings.KeymapPath,
            LeftTrackpadStableId = settings.LeftTrackpadStableId,
            RightTrackpadStableId = settings.RightTrackpadStableId,
            SharedProfile = settings.SharedProfile?.Clone() ?? UserSettings.LoadBundledDefaultsOrDefault()
        };
        normalized.Normalize();
        return JsonSerializer.Serialize(normalized, SignatureSerializerOptions);
    }

    private sealed class RuntimeSession : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly LinuxUinputDispatcher _dispatcher;
        private readonly TouchProcessorRuntimeHost _engine;
        private bool _disposed;

        public RuntimeSession(
            CancellationTokenSource cts,
            LinuxUinputDispatcher dispatcher,
            TouchProcessorRuntimeHost engine,
            Task runTask)
        {
            _cts = cts;
            _dispatcher = dispatcher;
            _engine = engine;
            RunTask = runTask;
        }

        public Task RunTask { get; }

        public async Task StopAsync()
        {
            if (_disposed || _cts.IsCancellationRequested)
            {
                return;
            }

            _cts.Cancel();
            try
            {
                await RunTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path for a canceled runtime session.
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _engine.Dispose();
            _dispatcher.Dispose();
            _cts.Dispose();
        }
    }
}
