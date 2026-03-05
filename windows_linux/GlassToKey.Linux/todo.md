# TODO:

1. Can `glasstokey` call `glasstokey-gui`?
2. If `glasstokey` tray is already running, if you run glasstokey show-config it shouldn't open a new instance but should open the config window. (If the tray is not running, IT CAN start it's own instance.)
3. If `glasstokey` tray is already running, `glasstokey start` should say something like "Glasstokey is already running in the tray.
4. If `glasstokey` tray is running, `glasstokey stop` should also try to stop the tray, instead of just saying "The background runtime is not running.


[2] Import button crashes program.

- Test Keymap Edit / set Hold.
- Typing Tuning columns.


# Change

GlassToKey Linux install complete.

Recommended next steps:
# NESSISARY doesnt the install script take care of this?
  1. Reconnect the trackpads or wait a few seconds for refreshed udev permissions.
  2. Add the desktop user to the 'glasstokey' group:
     sudo usermod -aG glasstokey $USER
  3. Log out and back in so the new group membership applies.
# PURPOSE?
  4. Run 'glasstokey doctor'
  6. Run 'glasstokey show-config --print' and verify left/right device bindings

# can this just set up init-config on install? why make them do it after?
  5. Run 'glasstokey init-config' if this is the first install

  7. Run 'glasstokey start' to launch the background runtime
  8. Run 'glasstokey stop' to stop the background runtime
# remove
  9. Run 'glasstokey run-engine 10' for a direct foreground smoke test when needed

Optional user service:
  systemctl --user daemon-reload
  systemctl --user enable --now glasstokey.service

Optional GUI:
  glasstokey-gui            # start tray host in background
  glasstokey-gui --show     # open config window

# TO: 
GlassToKey Linux install complete.

Usage:
  glasstokey-gui            # start tray host in background
  glasstokey-gui --show     # open config window

Recommended next steps:
  1. Reconnect the trackpads or wait a few seconds for refreshed udev permissions.
  2. Add the desktop user to the 'glasstokey' group:
     sudo usermod -aG glasstokey $USER
  3. Log out and back in so the new group membership applies.
  4. Run 'glasstokey start' to launch the background runtime
  5. Run 'glasstokey stop' to stop the background runtime