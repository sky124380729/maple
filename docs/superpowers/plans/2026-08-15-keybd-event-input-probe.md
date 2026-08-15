# Keybd Event Input Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Build a small, independently publishable Windows diagnostic executable that reuses the open-source PyAutoGUI keybd_event behavior to test whether the authorized MapleStory client accepts foreground keyboard input. The probe must produce auditable evidence, release every key on every exit path, and remain completely separate from Maple's production virtual-HID input boundary.

**Architecture:** Keep portable input semantics in Maple.Input, inject Windows user32.keybd_event behind a testable interface, and place window discovery, integrity checks, foreground confirmation, screenshots, and the diagnostic UI in a new Maple.InputProbe Windows-only executable. The probe sends at most one left and one right test per run, never registers global hotkeys, never writes game memory, and refuses to send unless the target window is unique, foreground, visible, non-minimized, and at the same or lower integrity level. Maple.Host remains virtual-HID-only until its independent L4/L5 evidence is complete.

**Tech Stack:** C#/.NET 8, SDK-style projects, net8.0-windows, WinForms for the diagnostic-only UI, Win32 keybd_event/window inspection P/Invoke, System.Drawing client-area evidence capture, xUnit tests with fake senders and gates, self-contained win-x64 publish.

---

## 1. Freeze the boundary and add the probe contract

**Files:**

- Modify docs/MAPLE_PROJECT_SPEC.md section 10 to state that keybd_event is an experimental diagnostic adapter only; production automation remains virtual-HID-only.
- Modify docs/WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md with a Windows diagnostic milestone that does not change the L4/L5 status.
- Create src/Maple.InputProbe/Maple.InputProbe.csproj as net8.0-windows WinExe targeting win-x64, referencing Maple.Input, Maple.Contracts, and Maple.Core.
- Create src/Maple.InputProbe/app.manifest with requestedExecutionLevel requireAdministrator; explain elevation in the UI and do not auto-relaunch in a loop.
- Add the new project to Maple.sln without referencing it from Maple.Host.
- Create tests/windows/input_probe_contract.tests.ps1 for static/package assertions.

**Steps:**

- [ ] Add a short diagnostic-only keybd_event paragraph to the main spec. Explicitly prohibit this adapter in the production closed loop and preserve the existing virtual-HID-only rule.
- [ ] Add the probe to the handoff Windows sequence after basic host startup and before any claim of real input evidence.
- [ ] Create the SDK-style project with UseWindowsForms=true, Nullable=disable, PlatformTarget=x64, and AssemblyName=MapleInputProbe.
- [ ] Add the manifest and verify it is embedded in the published executable.
- [ ] Write contract checks that fail when probe source contains SendInput, PostMessage, mouse_event, or memory-write APIs; check for keybd_event, requireAdministrator, the JSONL evidence field names, and a published EXE.

**Verification:**

~~~powershell
dotnet sln Maple.sln list | Select-String Maple.InputProbe
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\input_probe_contract.tests.ps1 -SourceOnly
~~~

Expected output includes Maple.InputProbe and INPUT_PROBE_CONTRACT=PASS for source-only checks.

## 2. Implement portable keybd_event semantics with TDD

**Files:**

- Create src/Maple.Input/IKeyboardEventSender.cs.
- Create src/Maple.Input/IInputSafetyGate.cs.
- Create src/Maple.Input/KeybdEventInputAdapter.cs.
- Create src/Maple.Input/VirtualKeyMap.cs.
- Create src/Maple.Input.Tests/KeybdEventInputAdapterTests.cs.

**Required API and behavior:**

~~~csharp
public interface IKeyboardEventSender
{
    void Send(ushort virtualKey, uint scanCode, uint flags);
}

public interface IInputSafetyGate
{
    bool CanSend(string reason);
}

public sealed class KeybdEventInputAdapter : IInputAdapter
{
    public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs);
    public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs);
    public InputResult Press(AbstractAction action, string key, long nowMonoMs);
    public InputResult ReleaseAll(long nowMonoMs);
    public bool Heartbeat(long nowMonoMs);
    public InputAdapterStatus GetStatus();
}
~~~

- KeyDown sends one flags=0 event and records the logical key only after the sender call succeeds.
- KeyUp sends one KEYEVENTF_KEYUP event and removes the logical key.
- Press is an explicit down/up pair, with no sleep in the portable adapter.
- The default map covers left, right, up, down, alt, ctrl, space, z, x, c, a, d, j, and k, case-insensitively; unknown keys fail without sending.
- left/right are mutually exclusive, as are up/down; switching direction releases the opposite key first.
- ReleaseAll snapshots the registry, sends key-up for every active key, clears the registry even if one release fails, and returns failure if any release failed.
- Every send checks IInputSafetyGate.CanSend; a rejected check calls ReleaseAll and returns failure without injecting a new key.
- GetStatus reports AdapterName=KeybdEventInputAdapter, InjectionEnabled=true only when the gate most recently allowed a send, and the exact active-key list.

**Steps:**

- [ ] Write failing tests for VK mapping, down/up flags, unknown-key rejection, directional mutual exclusion, gate rejection, ReleaseAll, and status reporting.
- [ ] Implement the registry and adapter until the focused test file passes.
- [ ] Add a test that a failed release still clears the registry and prevents a stale key from being reported active.
- [ ] Keep all Win32 P/Invoke out of this portable project; the adapter must be fully testable with a fake sender.

**Verification:**

~~~powershell
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj --filter FullyQualifiedName~KeybdEventInputAdapterTests
~~~

Expected output: all KeybdEventInputAdapterTests pass and no real keyboard event is sent.

## 3. Add the Windows sender and foreground/integrity gate

**Files:**

- Create src/Maple.InputProbe/WindowsKeybdEventSender.cs.
- Create src/Maple.InputProbe/TargetWindowInspector.cs.
- Create src/Maple.InputProbe/ProbeSafetyGate.cs.
- Create src/Maple.InputProbe/ProbeEvidence.cs.
- Create src/Maple.InputProbe/WindowScreenshot.cs.
- Create src/Maple.InputProbe/ProbeLogger.cs.

**Win32 rules:**

- WindowsKeybdEventSender is the only class allowed to P/Invoke user32.dll!keybd_event; it calls keybd_event(vk, 0, flags, UIntPtr.Zero) with no SendInput fallback.
- TargetWindowInspector enumerates visible top-level windows and matches title substring 冒险岛怀旧服 plus class UnityWndClass. It returns zero, one, or multiple matches rather than silently picking the first.
- For the selected HWND, record title, class, HWND, PID, process path, process start time, client width/height, DPI, IsIconic, visible state, current foreground HWND/PID, and both process integrity levels.
- ProbeSafetyGate returns false unless the target is valid, non-minimized, foreground-confirmed, and not higher integrity than the probe. On false it calls ReleaseAll and records a machine-readable reason.
- Do not use AttachThreadInput, synthetic Alt presses, topmost tricks, or repeated focus stealing. A user click on the probe Start button is the only trigger; the target must already be usable and the probe may make one documented foreground request followed by polling.
- WindowScreenshot captures the client rectangle only, never title bar or borders. A failed capture is UNKNOWN, not a movement failure.

**Evidence schema:** each JSONL record includes sessionId, actionId, targetHwnd, targetPid, targetClass, targetTitle, clientWidth, clientHeight, dpi, targetIntegrity, probeIntegrity, foregroundBefore, foregroundAfter, foregroundConfirmed, isMinimized, holdMs, vk, scanCode, flagsDown, flagsUp, inputAttempted, screenshotBefore, screenshotAfter, classification, and reason.

**Steps:**

- [ ] Write inspector tests around a pure window-match helper using synthetic window records; test zero, one, and multiple matches.
- [ ] Implement the real Win32 inspector and integrity query with clear error codes for missing process, access denied, minimized target, and focus mismatch.
- [ ] Implement the native sender and probe safety gate.
- [ ] Implement JSONL logging under %LOCALAPPDATA%\Maple\input-probe\<sessionId>\ and PNG evidence under the same session directory.
- [ ] Ensure try/finally in the runner always calls ReleaseAll, including exceptions, form close, cancellation, and focus loss.

**Verification:**

~~~powershell
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj --filter FullyQualifiedName~TargetWindow
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\input_probe_contract.tests.ps1 -SourceOnly
~~~

Expected output: helper tests pass; source checks confirm a single keybd_event P/Invoke and no prohibited injection APIs.

## 4. Build the minimal probe workflow and diagnostic UI

**Files:**

- Create src/Maple.InputProbe/Program.cs.
- Create src/Maple.InputProbe/ProbeForm.cs.
- Create src/Maple.InputProbe/ProbeRunner.cs.
- Create src/Maple.InputProbe/ProbeRunOptions.cs.
- Create tools/build-input-probe.ps1.

**UI and workflow:**

- The form has target status, a read-only target identity panel, a prominent Start button, a Stop and Release Keys button, a left/right result list, and a scrollable evidence path/log area.
- Default mode is observation-only and sends nothing until the user explicitly clicks Start and confirms the visible authorized-test-client/foreground-input checkbox.
- On start: discover target, show identity and integrity, take a baseline screenshot, show a three-second countdown, confirm foreground, run one left test and one right test with 500 ms hold each, capture post-action screenshots, then stop automatically.
- Wait at least three seconds between left and right tests. Do not repeat automatically. If any safety gate fails, stop with UNKNOWN_* and release all keys.
- Display “system call completed” separately from “observed character movement”; keybd_event completion alone is never a movement pass.
- Classify visual movement conservatively as MOVED_LEFT, MOVED_RIGHT, NO_OBSERVED_TRANSLATION, or UNKNOWN. The probe may require the user to compare saved before/after images; it must not claim a pass from whole-screen pixel differences.
- Never run automatically at startup, never launch the game, never change game settings, and never keep a background loop after the result dialog closes.

**Steps:**

- [ ] Add the form and wire explicit buttons to a cancellable ProbeRunner.
- [ ] Implement countdown, target discovery, one foreground confirmation, left/right action sequencing, and automatic cleanup.
- [ ] Add visible “all keys released” status that becomes true only after ReleaseAll returns.
- [ ] Add --self-test that exercises discovery and schema formatting without sending input or requiring the game.
- [ ] Add --output <directory> for deterministic evidence locations in CI/manual testing.

**Verification:**

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-input-probe.ps1 -Configuration Release
Test-Path .\artifacts\input-probe\MapleInputProbe.exe
.\artifacts\input-probe\MapleInputProbe.exe --self-test --output .\artifacts\input-probe\self-test
Get-Content .\artifacts\input-probe\self-test\*.jsonl
~~~

Expected output: a self-contained executable exists, self-test exits 0, and JSONL reports inputAttempted=false with no native sender invocation.

## 5. Add package and safety contract verification

**Files:**

- Complete tests/windows/input_probe_contract.tests.ps1 with published-binary checks.
- Add artifacts/input-probe/.gitignore so local screenshots/logs are never committed.
- Update README.md with diagnostic-only launch command and the limitation that production still requires virtual-HID evidence.

**Contract checks:**

- MapleInputProbe.exe exists after publish and is x64/self-contained.
- The manifest requests administrator execution.
- Source and binary do not contain SendInput, PostMessage, mouse_event, WriteProcessMemory, or VirtualProtect symbols.
- Source contains exactly one keybd_event P/Invoke declaration and expected explicit key-up flag.
- Self-test emits inputAttempted=false, allKeysReleased=true, and a valid sessionId.
- Maple.Host has no project reference to Maple.InputProbe.

**Steps:**

- [ ] Make the contract script fail with actionable file/line output for every violation.
- [ ] Add the build script publish output and self-test invocation to the contract script.
- [ ] Run the contract script against a clean artifacts/input-probe directory.

**Verification:**

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\input_probe_contract.tests.ps1 -RequirePublished
~~~

Expected output: INPUT_PROBE_CONTRACT=PASS.

## 6. Run repository-wide verification and record evidence

**Steps:**

- [ ] Run focused adapter tests, then the full input test project.
- [ ] Run node .\tools\verify-portable.mjs as required by AGENTS.md.
- [ ] Run git diff --check and inspect the final diff for accidental production-input changes.
- [ ] On the real Windows machine, run the probe with the authorized client already open and frontmost. Do not use it while another application is focused.
- [ ] Perform at most one manual run per direction, preserving JSONL and before/after PNGs; classify any non-movement result conservatively.
- [ ] Update docs/maple-runtime/VERIFICATION_2026-08-14.md only with observed evidence and keep the main spec status WINDOWS_PENDING unless all required L4/L5 gates are satisfied.

**Commands:**

~~~powershell
dotnet test .\src\Maple.Input.Tests\Maple.Input.Tests.csproj
node .\tools\verify-portable.mjs
git diff --check
~~~

Expected output: existing portable tests remain green, verify-portable passes, and no production Host input boundary changes. A successful probe only proves a diagnostic observation for that exact client/window/session; it does not prove a production automation pass.

## Self-review and explicit follow-up boundary

- This plan does not implement OpenCV, YOLO, WGC, map scanning, movement tracking, or the automatic combat loop. Those require a separate plan after the input probe establishes whether the client accepts the selected user-mode path.
- This plan does not remove or weaken WindowsVirtualHidAdapter; it keeps the production virtual-HID contract intact and adds only an isolated diagnostic executable.
- The probe is foreground-only and fail-closed. It cannot control a background game while the user types in WeChat or plays another game.
- The plan borrows only public keybd_event behavior and key naming semantics from MIT/BSD-licensed upstream projects; it does not copy closed-source binaries, private drivers, memory modification, anti-detection logic, or game assets.
- A movement result is valid only when evidence contains focus confirmation, paired key events, stable client screenshots, and a conservative visual classification. OS event dispatch alone is not enough.

