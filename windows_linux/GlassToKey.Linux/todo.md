# TODO:
- We recently implemented these features in windows and they are lacking and incomplete for Linux. Here are my notes. CAREFULLY move what is needed into core and make Host specific adjustments to Linux for Windows Parity
{
    0. Super in Shortcut Builder gets added to keymap as Win but should remain Super.
    1. Add "mx spacing" and "choc spacing" buttons to Keymap Tuning with the corresponding logic from windows to adjust the spacing % to scale up to MX. Please do your research in our codebase, it's flawless in Windows.
    2. I would like to adjut Shortcut Builder to mirror how Windows works more closely, where you can click a button to toggle it on, or hold a button to get dropdowns with left/right options, including AltGr (Whic h may be Super+ in Linux?) Please analyze our codebase, it's flawless in Windows and I want a clean Linux implementation with a shared core.
}
-------
- 50 / 50 buttons should be 100% width split 50% / 50% { Auto Splay, e v e n s p a c e, mx spacing, choc spacing}
---
- Test AltGr on Linux
1. You said Super+N then N should send the international letter? I can't get it to work now, (because it's set to Win?)
- Meta keys? or is that same as Super? distinct enough to add to shortcut builder and action dropdowns?
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