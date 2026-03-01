namespace GlassToKey.Linux.Runtime;

public sealed record LinuxRuntimeServiceSnapshot(
    LinuxRuntimeServiceStatus Status,
    string ServiceName,
    string Message,
    string? Failure,
    string? UnitFileState,
    string? FragmentPath)
{
    public bool IsInstalled => Status != LinuxRuntimeServiceStatus.Unavailable &&
                               Status != LinuxRuntimeServiceStatus.NotInstalled;

    public bool CanStart => Status is LinuxRuntimeServiceStatus.Stopped or LinuxRuntimeServiceStatus.Failed;

    public bool CanStop => Status is LinuxRuntimeServiceStatus.Starting or LinuxRuntimeServiceStatus.Running;
}
