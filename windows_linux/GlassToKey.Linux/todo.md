# TODO:
- Ctrl, Shift, Alt, Super do not match the description in Shortcut Builder: when you click they DO toggle BUT nothing happens when I HOLD (it should present sided options: Left Shift, Right Shift, Alt, Left Right, AltGr) please see windows. I want to do AltGr+n > Accented N in Linux with a shortcut builder
# Test AltGr+n on Linux
- The GUI colors are so bad when you hover over any button please make it more high contrast
- Add Linux Equivalents for: `System & Media`: EMOJI, VOICE, BRIGHT_UP, BRIGHT_DOWN
-------
# Test Linux Restart:
- Linux does not respect `open in tray` and always opens in fullscreen
- Test `open after restart` in linux by restarting
- Test typing password after restart in Linux type shit.

# BUG:
- If I Switch between keyboard (purple) and mouse (red) during keyboard/mouse mode.. It actually wont give mouse control back to the mouse until I click - Is there another way to accomplish this? I really don't like EVIOCGRAB style suppression. =x 

- Test `Run on Linux Startup`


# ARCH SUPPORT
- try installing Arch tonight, lol. 

# GUI
- BRIGHT_UP, BRIGHT_DOWN doesn't work on Linux. I think we need a Linux specific implementation. 

- In Windows there are "contact pills" and "state pills" can you please implement those in Linux?

# Autocorrect
- Turning off Autocorrect should unload it from memory
- Closing the Config should unload it from memory / restart

# TEST / root cause:
- 5-finger Up "D" is triggering Choral shift on it's own side? 

# NUKE
pkill -f 'GlassToKey.Linux/Gui/GlassToKey.Linux.Gui.csproj'