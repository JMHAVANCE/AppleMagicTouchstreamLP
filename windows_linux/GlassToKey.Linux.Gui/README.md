# GlassToKey.Linux.Gui

Early Linux GUI control shell.

For the canonical Linux status, validated host findings, and remaining-work checklist, use `../LINUX_GOLD.md`.

Current scope:

- load the XDG-backed Linux host settings from the shared `GlassToKey.Linux.Host` library
- enumerate current trackpad candidates
- let the user assign left/right trackpads explicitly
- select the layout preset
- browse, set, or clear a custom keymap path
- run the Linux `doctor` check and inspect its report in-app
- start, stop, and observe the Linux user-service runtime owner without hosting the engine in-process
- expose a first Ubuntu top-bar/tray shell with open, hide, doctor, runtime control, and quit actions
- save back to the same Linux settings file used by the CLI/runtime

Current phase:

- this is still a starting control shell, not the finished Linux GUI
- the live engine session is expected to stay in the Linux user service, not in this config app
- it does not yet provide keymap editing or a finalized tray/product shell
- the tray/indicator shell now exists, but still needs manual runtime validation on the current Ubuntu desktop

Build:

- `dotnet build GlassToKey.Linux.Gui/GlassToKey.Linux.Gui.csproj -c Release`

Run:

- `dotnet run --project GlassToKey.Linux.Gui/GlassToKey.Linux.Gui.csproj -c Release`

Publish:

- framework-dependent:
  - `dotnet publish GlassToKey.Linux.Gui/GlassToKey.Linux.Gui.csproj -c Release -p:PublishProfile=LinuxGuiFrameworkDependent`
- self-contained:
  - `dotnet publish GlassToKey.Linux.Gui/GlassToKey.Linux.Gui.csproj -c Release -p:PublishProfile=LinuxGuiSelfContained`

Current note:

- the GUI no longer depends on the CLI executable project; both now share `GlassToKey.Linux.Host`
- the self-contained GUI publish output now includes the Linux bundled `GLASSTOKEY_DEFAULT_KEYMAP.json`
- the GUI now controls the Linux user service via `systemctl --user` instead of owning the runtime in-process
- the first tray/top-bar shell uses Avalonia `TrayIcon` and a linked placeholder status icon from the repo for now
