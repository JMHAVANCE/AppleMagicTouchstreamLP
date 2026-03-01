# GlassToKey.Linux.Gui

Early Linux GUI shell.

Current scope:

- load the XDG-backed Linux host settings
- enumerate current trackpad candidates
- let the user assign left/right trackpads explicitly
- select the layout preset
- set or clear a custom keymap path
- save back to the same Linux settings file used by the CLI/runtime

Current phase:

- this is a starting device-picker/settings shell, not the finished Linux GUI
- it does not yet host the live engine, diagnostics, or keymap editing surface

Build:

- `dotnet build GlassToKey.Linux.Gui/GlassToKey.Linux.Gui.csproj -c Release`

Publish:

- `dotnet publish GlassToKey.Linux.Gui/GlassToKey.Linux.Gui.csproj -c Release -p:PublishProfile=LinuxGuiFrameworkDependent`

Current note:

- the first GUI publish profile is framework-dependent
- the clean long-term fix is to extract the reusable Linux host/config surface into a dedicated library so the GUI does not publish through an exe-to-exe reference
