# Linux Gold

This file tracks only what is still left to do or test for Linux.

Operational guidance, architecture boundaries, and build commands now live in `AGENTS.md`.

## Remaining Work

### 1. Real Arch Validation

- Validate the checked-in Arch package install/runtime story on a real Arch test environment.
- Document the real Arch install flow once it is proven outside the Ubuntu-hosted local package build path.

## Remaining Linux Tests

### Required

- Real Arch install test:
  - install the generated package on a real Arch environment
  - verify `glasstokey doctor`
  - verify `glasstokey init-config`
  - verify desktop tray launch through `glasstokey-gui`
  - verify headless runtime launch through `glasstokey start` / `glasstokey stop`
  - verify packaged evdev, `/dev/uinput`, and actuator permissions

### Ongoing Regression Checks

- When Linux packaging behavior changes:
  - recheck the packaged GUI launcher path
  - recheck the packaged CLI headless path
  - recheck `doctor`
- When Linux shared dispatch/keymap behavior changes:
  - re-run Linux self-test
  - re-run representative `.atpcap` fixture checks

## Update Rule

If a Linux item here is finished:

- remove it from this file
- update `AGENTS.md` if the change affects workflows, commands, or boundaries
