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
    private static readonly TimeSpan SessionRestartDelay = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions SignatureSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LinuxAppRuntime _appRuntime;
    private readonly LinuxInputRuntimeService _runtime;
    private readonly LinuxRuntimeStateStore _stateStore;

    public LinuxRuntimeOwner(
        LinuxAppRuntime? appRuntime = null,
        LinuxInputRuntimeService? runtime = null,
        LinuxRuntimeStateStore? stateStore = null)
    {
        _appRuntime = appRuntime ?? new LinuxAppRuntime();
        _runtime = runtime ?? new LinuxInputRuntimeService();
        _stateStore = stateStore ?? new LinuxRuntimeStateStore();
    }

    public async Task RunAsync(
        ILinuxRuntimeObserver? observer = null,
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        LinuxRuntimeConfiguration configuration = _appRuntime.LoadConfiguration();
        LinuxRuntimeDiagnosticsMonitor diagnostics = new("runtime-owner", logger);
        string settingsSignature = BuildSettingsSignature(configuration.Settings);
        RuntimeSession? session = null;
        bool waitingForBindingsLogged = false;
        TouchProcessorTraceEvent[] diagnosticEvents = new TouchProcessorTraceEvent[2048];

        try
        {
            PersistStoppedState();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (session == null)
                {
                    if (configuration.Bindings.Count == 0)
                    {
                        if (!waitingForBindingsLogged)
                        {
                            diagnostics.EmitLifecycle("Runtime owner is waiting for trackpad bindings.");
                            waitingForBindingsLogged = true;
                        }
                    }
                    else
                    {
                        session = StartSession(configuration, observer, cancellationToken);
                        diagnostics.Reset();
                        waitingForBindingsLogged = false;
                        LogConfiguration(diagnostics, configuration, isReload: false);
                    }
                }

                Task pollTask = Task.Delay(SettingsPollInterval, cancellationToken);
                if (session != null)
                {
                    Task completed = await Task.WhenAny(session.RunTask, pollTask).ConfigureAwait(false);
                    if (completed == session.RunTask)
                    {
                        RuntimeSession completedSession = session;
                        session = null;
                        try
                        {
                            await completedSession.RunTask.ConfigureAwait(false);
                            diagnostics.EmitLifecycle("Runtime session stopped unexpectedly; restarting.");
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            diagnostics.EmitLifecycle($"Runtime session faulted ({ex.GetType().Name}): {ex.Message}. Restarting.");
                        }
                        finally
                        {
                            completedSession.Dispose();
                        }
                        diagnostics.Reset();

                        try
                        {
                            await Task.Delay(SessionRestartDelay, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        continue;
                    }
                }
                else
                {
                    await pollTask.ConfigureAwait(false);
                }

                if (session != null && session.TryGetSnapshot(out TouchProcessorRuntimeSnapshot snapshot))
                {
                    PersistRunningState(snapshot);
                    diagnostics.Observe(in snapshot);
                    while (session.DrainDiagnostics(diagnosticEvents) is int drained && drained > 0)
                    {
                        diagnostics.ObserveEngineDiagnostics(diagnosticEvents.AsSpan(0, drained));
                    }
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
                LogConfiguration(diagnostics, configuration, isReload: true);

                if (session == null)
                {
                    continue;
                }

                await session.StopAsync().ConfigureAwait(false);
                session.Dispose();
                session = null;
                diagnostics.Reset();
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
            diagnostics.Reset();

            PersistStoppedState();
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
        engine.SetDiagnosticsEnabled(true);
        LinuxInputRuntimeOptions options = new()
        {
            Observer = new DiagnosticsRuntimeObserver(observer)
        };
        Task runTask = _runtime.RunAsync([.. configuration.Bindings], engine, options, sessionCts.Token);
        return new RuntimeSession(sessionCts, dispatcher, engine, runTask);
    }

    private static void LogConfiguration(LinuxRuntimeDiagnosticsMonitor diagnostics, LinuxRuntimeConfiguration configuration, bool isReload)
    {
        string action = isReload ? "Reloaded" : "Loaded";
        diagnostics.EmitLifecycle($"{action} runtime config: layout={configuration.LayoutPreset.Name}, keymap={configuration.Settings.KeymapPath ?? "(bundled default)"}, bindings={configuration.Bindings.Count}.");
        diagnostics.EmitLifecycle(
            $"  Profile: typingEnabled={configuration.SharedProfile.TypingEnabled}, keyboardModeEnabled={configuration.SharedProfile.KeyboardModeEnabled}, chordShiftEnabled={configuration.SharedProfile.ChordShiftEnabled}, fourFingerHoldAction={configuration.SharedProfile.FourFingerHoldAction}");
        for (int index = 0; index < configuration.Bindings.Count; index++)
        {
            LinuxTrackpadBinding binding = configuration.Bindings[index];
            diagnostics.EmitLifecycle($"  {binding.Side}: {binding.Device.DisplayName} [{binding.Device.DeviceNode}]");
        }

        for (int index = 0; index < configuration.Warnings.Count; index++)
        {
            diagnostics.EmitLifecycle($"  Warning: {configuration.Warnings[index]}");
        }
    }

    private static string BuildSettingsSignature(LinuxHostSettings settings)
    {
        LinuxHostSettings normalized = new()
        {
            Version = settings.Version,
            LayoutPresetName = settings.LayoutPresetName,
            KeymapPath = settings.KeymapPath,
            KeymapRevision = settings.KeymapRevision,
            LeftTrackpadStableId = settings.LeftTrackpadStableId,
            RightTrackpadStableId = settings.RightTrackpadStableId,
            SharedProfile = settings.SharedProfile?.Clone() ?? UserSettings.LoadBundledDefaultsOrDefault()
        };
        normalized.Normalize();
        return JsonSerializer.Serialize(normalized, SignatureSerializerOptions);
    }

    private void PersistRunningState(in TouchProcessorRuntimeSnapshot snapshot)
    {
        _stateStore.Save(new LinuxRuntimeStateSnapshot(
            IsRunning: true,
            TypingEnabled: snapshot.TypingEnabled,
            KeyboardModeEnabled: snapshot.KeyboardModeEnabled,
            ActiveLayer: snapshot.ActiveLayer,
            UpdatedUtc: DateTimeOffset.UtcNow));
    }

    private void PersistStoppedState()
    {
        _stateStore.Save(new LinuxRuntimeStateSnapshot(
            IsRunning: false,
            TypingEnabled: false,
            KeyboardModeEnabled: false,
            ActiveLayer: 0,
            UpdatedUtc: DateTimeOffset.UtcNow));
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

        public bool TryGetSnapshot(out TouchProcessorRuntimeSnapshot snapshot)
        {
            return _engine.TryGetSnapshot(out snapshot);
        }

        public int DrainDiagnostics(Span<TouchProcessorTraceEvent> destination)
        {
            return _engine.DrainTraceEvents(destination);
        }

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

    private sealed class DiagnosticsRuntimeObserver : ILinuxRuntimeObserver
    {
        private readonly ILinuxRuntimeObserver? _innerObserver;

        public DiagnosticsRuntimeObserver(ILinuxRuntimeObserver? innerObserver)
        {
            _innerObserver = innerObserver;
        }

        public void OnBindingStateChanged(LinuxRuntimeBindingState state)
        {
            LinuxRuntimeDiagnosticsLog.Write(
                "runtime-owner",
                $"[{state.Side}] {state.Status}: {state.StableId} ({state.DeviceNode ?? "no-node"}) - {state.Message}");
            _innerObserver?.OnBindingStateChanged(state);
        }
    }
}
