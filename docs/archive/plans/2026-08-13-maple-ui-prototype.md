# Maple UI Prototype Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a runnable, input-disabled WinForms prototype that validates the approved single-window layout, target-window preview, controls, map-scanning presentation, state transitions, and telemetry before production OpenCV/YOLO/HID integration.

**Architecture:** A standalone .NET Framework WinForms executable owns all prototype UI and window-capture behavior. The prototype automatically discovers the visible `冒险岛怀旧服` window, mirrors its client area without activating it, and uses mock observations for object boxes and map calibration. It contains no input injection APIs and never sends keyboard or mouse events.

**Tech Stack:** C#, WinForms, System.Drawing, Win32 read-only window discovery/client-coordinate APIs, Visual Studio MSBuild/Roslyn, PowerShell contract tests.

---

### Task 1: Establish the prototype contract

**Files:**
- Create: `tests/prototype_contract.tests.ps1`
- Create: `src/MaplePrototype/MaplePrototype.csproj`

- [x] Write a failing contract test that requires the project, executable self-test, fixed movement keys, configurable Alt jump, optional Z pickup, safe prototype mode, and absence of `SendInput`/`keybd_event`/`PostMessage`.
- [x] Run `powershell -NoProfile -ExecutionPolicy Bypass -File tests/prototype_contract.tests.ps1` and confirm it fails because the prototype project does not exist.
- [x] Add a reproducible WinForms project targeting the installed Windows desktop toolchain.

### Task 2: Build the single-window prototype shell

**Files:**
- Create: `src/MaplePrototype/Program.cs`
- Create: `src/MaplePrototype/PrototypeState.cs`
- Create: `src/MaplePrototype/Theme.cs`

- [x] Implement a deterministic prototype state model with `Stopped`, `Observing`, `Paused`, `MapScanning`, `MapCalibrating`, and `EmergencyStop` transitions.
- [x] Implement the 1440x900 responsive shell: top navigation, fixed left controls, central/right preview, and bottom telemetry.
- [x] Add Chinese controls for attack mode, HP/MP thresholds, jump key, pickup toggle/key, observe, pause, emergency stop, and event log.
- [x] Add `--self-test` output so automated verification can prove input is disabled and required states/configuration are present.

### Task 3: Add read-only target discovery and preview

**Files:**
- Create: `src/MaplePrototype/WindowCapture.cs`
- Create: `src/MaplePrototype/PreviewCanvas.cs`

- [x] Discover exactly one visible window whose title contains `冒险岛怀旧服` and record HWND, PID, client rectangle, and DPI.
- [x] Capture only the client area using read-only Win32 APIs and `PrintWindow`; never activate the target.
- [x] Render the latest frame at preserved aspect ratio and draw clearly labeled mock Self/Player/Monster overlays without mutating the game window.
- [x] Pause preview state on missing/minimized/invalid/zero-size/失焦 target and expose the reason in the UI.

### Task 4: Add prototype map-scanning and telemetry views

**Files:**
- Modify: `src/MaplePrototype/Program.cs`
- Modify: `src/MaplePrototype/PreviewCanvas.cs`

- [x] Add tabs for realtime view, map calibration, action configuration, recognition model, and runtime log.
- [x] Add a map-scanning demonstration with coverage, unobserved-area warning, platform/step/ladder annotations, MapWorld coordinates, and calibration error.
- [x] Update capture FPS, recognition FPS, frame latency, queue age, dropped frames, CPU/GPU mode, memory, HID status/heartbeat, state, and pause reason once per second.
- [x] Ensure all action controls only update prototype state/logs and display `演示模式：不会发送按键`.

### Task 5: Build and visually verify

**Files:**
- Create: `tools/build-prototype.ps1`
- Create: `dist/MapleVisualPrototype.exe`

- [x] Build with the installed Visual Studio MSBuild/Roslyn toolchain and copy the executable to `dist/MapleVisualPrototype.exe`.
- [x] Run the contract test and `dist/MapleVisualPrototype.exe --self-test`; require zero failures.
- [x] Launch the EXE, inspect a desktop screenshot at the available viewport, and correct clipping, overlap, contrast, and aspect-ratio issues.
- [x] Confirm the prototype does not activate the target and contains no input injection APIs.
