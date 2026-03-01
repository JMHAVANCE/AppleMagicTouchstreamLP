using System.ComponentModel;
using System.Diagnostics;

namespace GlassToKey.Linux.Runtime;

public sealed class LinuxRuntimeServiceController
{
    private const string DefaultServiceName = "glasstokey-linux.service";
    private static readonly string[] KnownServiceNames =
    [
        DefaultServiceName,
        "glasstokey-linux-validation.service"
    ];

    private readonly string? _explicitServiceName;

    public LinuxRuntimeServiceController(string? serviceName = null)
    {
        _explicitServiceName = NormalizeServiceName(
            string.IsNullOrWhiteSpace(serviceName)
                ? Environment.GetEnvironmentVariable("GLASSTOKEY_LINUX_SERVICE_NAME")
                : serviceName);
    }

    public async Task<LinuxRuntimeServiceSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return await QueryPreferredServiceAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LinuxRuntimeServiceSnapshot> StartAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteControlActionAsync("start", "start", cancellationToken).ConfigureAwait(false);
    }

    public async Task<LinuxRuntimeServiceSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteControlActionAsync("stop", "stop", cancellationToken).ConfigureAwait(false);
    }

    public async Task<LinuxRuntimeServiceSnapshot> RestartAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteControlActionAsync("restart", "restart", cancellationToken).ConfigureAwait(false);
    }

    private async Task<LinuxRuntimeServiceSnapshot> ExecuteControlActionAsync(
        string verb,
        string actionLabel,
        CancellationToken cancellationToken)
    {
        LinuxRuntimeServiceSnapshot snapshot = await QueryPreferredServiceAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.IsInstalled)
        {
            return snapshot;
        }

        SystemctlResult result = await RunSystemctlAsync(
            ["--user", verb, snapshot.ServiceName],
            cancellationToken).ConfigureAwait(false);

        if (result.LaunchError != null)
        {
            return new LinuxRuntimeServiceSnapshot(
                LinuxRuntimeServiceStatus.Unavailable,
                snapshot.ServiceName,
                "The Linux user service manager is unavailable in this session.",
                result.LaunchError,
                snapshot.UnitFileState,
                snapshot.FragmentPath);
        }

        LinuxRuntimeServiceSnapshot refreshed = await QueryServiceAsync(snapshot.ServiceName, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return refreshed;
        }

        return refreshed with
        {
            Failure = FirstNonEmpty(result.StandardError, result.StandardOutput, refreshed.Failure),
            Message = $"The runtime owner could not {actionLabel} cleanly."
        };
    }

    private async Task<LinuxRuntimeServiceSnapshot> QueryPreferredServiceAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_explicitServiceName))
        {
            return await QueryServiceAsync(_explicitServiceName, cancellationToken).ConfigureAwait(false);
        }

        LinuxRuntimeServiceSnapshot? missingSnapshot = null;
        for (int index = 0; index < KnownServiceNames.Length; index++)
        {
            LinuxRuntimeServiceSnapshot snapshot = await QueryServiceAsync(KnownServiceNames[index], cancellationToken).ConfigureAwait(false);
            if (snapshot.Status == LinuxRuntimeServiceStatus.NotInstalled)
            {
                missingSnapshot ??= snapshot;
                continue;
            }

            return snapshot;
        }

        return missingSnapshot ?? new LinuxRuntimeServiceSnapshot(
            LinuxRuntimeServiceStatus.NotInstalled,
            DefaultServiceName,
            "No GlassToKey Linux user service is installed yet.",
            null,
            null,
            null);
    }

    private static async Task<LinuxRuntimeServiceSnapshot> QueryServiceAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        SystemctlResult result = await RunSystemctlAsync(
            [
                "--user",
                "show",
                serviceName,
                "--property=LoadState",
                "--property=ActiveState",
                "--property=SubState",
                "--property=UnitFileState",
                "--property=FragmentPath"
            ],
            cancellationToken).ConfigureAwait(false);

        if (result.LaunchError != null)
        {
            return new LinuxRuntimeServiceSnapshot(
                LinuxRuntimeServiceStatus.Unavailable,
                serviceName,
                "The Linux user service manager is unavailable in this session.",
                result.LaunchError,
                null,
                null);
        }

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return new LinuxRuntimeServiceSnapshot(
                LinuxRuntimeServiceStatus.Unavailable,
                serviceName,
                "Could not query the Linux user service manager.",
                FirstNonEmpty(result.StandardError, result.StandardOutput, "systemctl --user show failed."),
                null,
                null);
        }

        Dictionary<string, string> properties = ParseProperties(result.StandardOutput);
        string loadState = GetProperty(properties, "LoadState");
        string activeState = GetProperty(properties, "ActiveState");
        string subState = GetProperty(properties, "SubState");
        string unitFileState = GetProperty(properties, "UnitFileState");
        string fragmentPath = GetProperty(properties, "FragmentPath");

        if (string.Equals(loadState, "not-found", StringComparison.OrdinalIgnoreCase))
        {
            return new LinuxRuntimeServiceSnapshot(
                LinuxRuntimeServiceStatus.NotInstalled,
                serviceName,
                $"The runtime owner service '{serviceName}' is not installed yet.",
                null,
                null,
                null);
        }

        LinuxRuntimeServiceStatus status = MapStatus(activeState, subState);
        string message = BuildMessage(serviceName, status, activeState, subState, unitFileState);
        string? failure = status == LinuxRuntimeServiceStatus.Failed
            ? FirstNonEmpty(result.StandardError, "The service reported a failed state.")
            : null;

        return new LinuxRuntimeServiceSnapshot(
            status,
            serviceName,
            message,
            failure,
            NullIfEmpty(unitFileState),
            NullIfEmpty(fragmentPath));
    }

    private static LinuxRuntimeServiceStatus MapStatus(string activeState, string subState)
    {
        if (string.Equals(activeState, "active", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(subState, "running", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxRuntimeServiceStatus.Running;
        }

        if (string.Equals(activeState, "activating", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxRuntimeServiceStatus.Starting;
        }

        if (string.Equals(activeState, "deactivating", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxRuntimeServiceStatus.Stopping;
        }

        if (string.Equals(activeState, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxRuntimeServiceStatus.Failed;
        }

        return LinuxRuntimeServiceStatus.Stopped;
    }

    private static string BuildMessage(
        string serviceName,
        LinuxRuntimeServiceStatus status,
        string activeState,
        string subState,
        string unitFileState)
    {
        return status switch
        {
            LinuxRuntimeServiceStatus.Running =>
                $"The runtime owner service '{serviceName}' is active and running.",
            LinuxRuntimeServiceStatus.Starting =>
                $"The runtime owner service '{serviceName}' is starting.",
            LinuxRuntimeServiceStatus.Stopping =>
                $"The runtime owner service '{serviceName}' is stopping.",
            LinuxRuntimeServiceStatus.Failed =>
                $"The runtime owner service '{serviceName}' is failed.",
            _ => string.IsNullOrWhiteSpace(unitFileState)
                ? $"The runtime owner service '{serviceName}' is inactive."
                : $"The runtime owner service '{serviceName}' is inactive ({FirstNonEmpty(subState, activeState, unitFileState)})."
        };
    }

    private static Dictionary<string, string> ParseProperties(string output)
    {
        Dictionary<string, string> properties = new(StringComparer.OrdinalIgnoreCase);
        string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();
            properties[key] = value;
        }

        return properties;
    }

    private static string GetProperty(IReadOnlyDictionary<string, string> properties, string key)
    {
        return properties.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    private static async Task<SystemctlResult> RunSystemctlAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "systemctl",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            for (int index = 0; index < arguments.Count; index++)
            {
                process.StartInfo.ArgumentList.Add(arguments[index]);
            }

            process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new SystemctlResult(
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false),
                null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new SystemctlResult(-1, string.Empty, string.Empty, ex.Message);
        }
    }

    private static string? NormalizeServiceName(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return null;
        }

        string trimmed = serviceName.Trim();
        return trimmed.EndsWith(".service", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}.service";
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(values[index]))
            {
                return values[index]!.Trim();
            }
        }

        return string.Empty;
    }

    private sealed record SystemctlResult(int ExitCode, string StandardOutput, string StandardError, string? LaunchError);
}
