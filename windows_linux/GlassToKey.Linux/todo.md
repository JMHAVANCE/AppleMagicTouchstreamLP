# TODO:
- I would like you to implement Tray icons that change depending on the status: Circle indicator Green (mixed mode) Red (Mouse Mode), Like in Windows. 

- Keyboard/Mouse mode doesn't capture clicks from trackpads and eat them and send them to the nether realm~

# GUI
- In Windows there are "contact pills" and "state pills" can you please implement those in Linux?

- BRIGHT_UP, BRIGHT_DOWN doesn't work on Linux. I think we need a Linux specific implementation. 

- Adjust FOrce min / force max slider to max out at 255 ON LINUX ONLY (maybe prod more.. are we not getting force phases from Linux like we are for WIndows? Why is `f:255` the max in the visualizer?)

# Autocorrect
- Turning off Autocorrect should unload it from memory
- Closing the Config should unload it from memory / restart

# TEST / root cause:
- 5-finger Up "D" is triggering Choral shift on it's own side? 

# Wayland?
- What happens when you actually run this on arch? do ppl use GUI on arch? lol. 
- What happens when you run this on non-wayland? How flexible is avalonia or whatever?