# TODO:
- Can we add "Autocorrect Status" from windows to the bottom of the GUI, when Autocorrect is enabled, with the features from the windows side?

- Gestures section needs to be fully fleshed out, look at how it's implemented on the Windows side and CAREFULLY move everything needed into the shared/core.

- BRIGHT_UP, BRIGHT_DOWN doesn't work on Linux. I think we need a Linux specific implementation. 

- Turning off Autocorrect should unload it from memory
- Closing the Config should unload it from memory / restart

# TEST / root cause:
- 5-finger Up "D" is triggering Choral shift on it's own side? 