# TODO:
- Haptics support in Linux! Look at how it's implemented in Windows and see if there is an equivalent way to do it in Linux. You may need to do some research on Github as I know this is a solved problem on Linux as well.

# GUI
- In Windows there are "contact pills" and "state pills" can you please implement those in Linux?

- BRIGHT_UP, BRIGHT_DOWN doesn't work on Linux. I think we need a Linux specific implementation. 

- Adjust FOrce min / force max slider to max out at 255 ON LINUX ONLY (maybe prod more.. are we not getting force phases from Linux like we are for WIndows? Why is `f:255` the max in the visualizer?)

# Autocorrect
- Turning off Autocorrect should unload it from memory
- Closing the Config should unload it from memory / restart

# TEST / root cause:
- 5-finger Up "D" is triggering Choral shift on it's own side? 