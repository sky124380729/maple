# React Modular Maple Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the WinForms workbench with a React/Mantine UI while extracting portable domain contracts, feedback-driven action policy, replayable vision boundaries, and Windows-only capture/WebView2/HID adapters into independently testable modules.

**Architecture:** React runs inside a WebView2 host for the Windows desktop build, but it is developed as a standalone Vite app with a mock bridge so the UI and portable logic can be built on macOS. C# owns window identity, capture, vision, state machine, safety gates, action policy, and input adapters. The high-frequency preview stays native and is fed by a two-slot latest-frame buffer; React receives state, overlays, telemetry, and commands rather than base64 frames.

**Tech Stack:** React 18+, TypeScript, Vite, Mantine, `lucide-react`, Zustand, Zod, Vitest, Playwright, C#/.NET Framework 4.8-compatible core contracts, WinForms/WebView2 host, Windows Graphics Capture/BitBlt adapters, ONNX Runtime/OpenCV adapters, JSONL session logs.

---

## 1. Execution Boundaries

### 1.1 macOS-compatible work

- React workbench and design system;
- TypeScript/C# message contracts and schema validation;
- Mock WebView2 bridge and replay bridge;
- Portable state machine, safety gates, action policy, timing calculations and map topology;
- Offline frame/observation fixtures and replay tests;
- Vision provider interfaces, fake detector, confidence/TTL fusion and overlay mapping;
- Cloud provider interface, Bailian request/response schema, redacted mock client;
- Null/replay input adapter and active-key bookkeeping tests;
- Documentation, contract tests, UI screenshots and static builds.

### 1.2 Windows-only work

- WebView2 host lifecycle and native child preview surface;
- Windows Graphics Capture, BitBlt fallback and `PrintWindow` diagnostic fallback;
- Direct2D/Direct3D preview path and 30-60 FPS measurements;
- Real OpenCV/ONNX native providers if the selected runtime requires Windows binaries;
- Virtual HID adapter, driver/device installation and device/OS/client three-layer acceptance;
- Windows packaging, DPI matrix, WebView2 runtime, soak and signed installer verification.

### 1.3 Completion labels

- `DONE`: implementation exists and the relevant tests pass in the current environment;
- `CONTRACT_COMPLETE`: portable API, schema, mock adapter and tests are complete, but the native adapter is not verified;
- `WINDOWS_PENDING`: blocked by Windows APIs, device access or client-side acceptance;
- `NOT_STARTED`: no implementation or test evidence yet.

The main specification must only mark a module `DONE` after evidence exists. HID is allowed to be marked `CONTRACT_COMPLETE / WINDOWS_PENDING`; it must not be marked as production-complete from macOS.

## 2. Module Ownership

```text
maple-ui (React)
  Workbench / Map / Replay / Settings / Diagnostics / UI store
          |
Maple.Bridge (versioned JSON messages + Zod schemas)
          |
Maple.Core (portable domain models, FSM, safety gates, action policy)
  |          |            |             |
Maple.Capture  Maple.Vision  Maple.Map  Maple.Input
  |          |            |             |
Windows WGC  OpenCV/ONNX  MapWorld    Null/Replay/HID
```

| Module | Responsibility | macOS status target | Windows handoff |
| --- | --- | --- | --- |
| `maple-ui` | Modern desktop workbench | `DONE` | Loaded by WebView2 |
| `Maple.Bridge` | Typed host events and UI commands | `DONE` | Bind to WebView2 |
| `Maple.Core` | Domain state, safety gates, action policy | `DONE` | Reuse unchanged |
| `Maple.Capture` | Capture backend abstraction and frame slots | `CONTRACT_COMPLETE` | WGC/BitBlt implementation |
| `Maple.Preview` | Native high-FPS rendering contract | `CONTRACT_COMPLETE` | Native control + measurements |
| `Maple.Vision` | Detector/OCR interfaces and fusion | `CONTRACT_COMPLETE` | Real providers |
| `Maple.Map` | MapWorld, scan registration, topology | `DONE` for pure logic | Integrate live frames |
| `Maple.Cloud` | Bailian schema/client boundary | `CONTRACT_COMPLETE` | Credential/runtime verification |
| `Maple.Input` | Abstract actions and key lifecycle | `CONTRACT_COMPLETE` | Virtual HID adapter |
| `Maple.Host` | WebView2/native host | `WINDOWS_PENDING` | Windows-only |
| `Maple.Replay` | Offline frame/event playback | `DONE` | Reuse in acceptance |

## 3. Task 1: Freeze Shared Contracts

**Files:**
- Create: `schemas/bridge.schema.json`
- Create: `schemas/observation.schema.json`
- Create: `schemas/action.schema.json`
- Create: `src/Maple.Contracts/DomainContracts.cs`
- Create: `ui/src/contracts/bridge.ts`
- Test: `ui/src/contracts/bridge.test.ts`
- Test: `tests/contracts/contract-fixtures.tests.ps1`

- [ ] **Step 1: Define the source-of-truth types**

Define `TargetBinding`, `CaptureFrameMetadata`, `OverlaySnapshot`, `ObservationSnapshot`, `TelemetrySnapshot`, `SessionState`, `PauseReason`, `AbstractAction`, `ActionPlan`, `InputResult`, `HostEvent`, and `UiCommand`. `self` has no public `trackId`; `players` retain `trackId`; `monsters` retain `targetId`.

- [ ] **Step 2: Add schema validation tests**

Run `npm run test -- bridge` and validate fixtures for valid/invalid `schemaVersion`, stale observations, malformed boxes, unknown commands, missing emergency-stop payloads, and invalid action durations.

- [ ] **Step 3: Add C# and TypeScript parity fixtures**

Use the same JSON fixtures from `tests/fixtures/` in both runtimes. A fixture is accepted only when both validators accept/reject it identically.

- [ ] **Step 4: Record contract completion**

Update the plan status to `DONE` and leave the product spec behavior unchanged; contracts are implementation artifacts, not a replacement for the master spec.

## 4. Task 2: Scaffold the React Workbench

**Files:**
- Create: `ui/package.json`
- Create: `ui/tsconfig.json`
- Create: `ui/vite.config.ts`
- Create: `ui/index.html`
- Create: `ui/src/main.tsx`
- Create: `ui/src/app/App.tsx`
- Create: `ui/src/app/theme.ts`
- Create: `ui/src/app/app.css`
- Create: `ui/src/test/setup.ts`

- [ ] **Step 1: Create the Vite TypeScript app**

Use React, Mantine, `lucide-react`, Zustand, Zod, Vitest and Playwright. Configure strict TypeScript and an `npm run build` that emits `ui/dist/` without a Windows runtime.

- [ ] **Step 2: Establish the visual tokens**

Define the dark operations-workbench palette, spacing scale, typography, borders, focus states, semantic colors for Self/Player/Monster, warning, paused, emergency and unavailable states. Keep layout dense and responsive; do not use gradients or decorative blobs.

- [ ] **Step 3: Add the app shell**

Create the top status bar, left controls, central preview region, right diagnostics rail and bottom telemetry strip using stable grid tracks. The shell must render at 1280x720 and 1440x900 without overlap.

- [ ] **Step 4: Verify locally**

Run:

```bash
cd ui
npm install
npm run build
npm run test
```

Expected: successful production build and no test failures on macOS.

## 5. Task 3: Implement the Typed Bridge and Mock Host

**Files:**
- Create: `ui/src/bridge/HostBridge.ts`
- Create: `ui/src/bridge/MockHostBridge.ts`
- Create: `ui/src/store/sessionStore.ts`
- Create: `ui/src/store/telemetryStore.ts`
- Create: `ui/src/mock/mockSession.ts`
- Test: `ui/src/bridge/HostBridge.test.ts`

- [ ] **Step 1: Implement the bridge interface**

Expose `send(command: UiCommand)`, `subscribe(listener)`, `requestSnapshot()`, and `dispose()`. The browser implementation uses `window.chrome.webview.postMessage` only when available; otherwise it returns a typed unavailable result.

- [ ] **Step 2: Implement the mock session**

Emit deterministic `target.updated`, `session.stateChanged`, `telemetry.updated`, `preview.availabilityChanged`, `observation.updated`, and `log.appended` events. The mock never emits real input results and always reports `INPUT_INJECTION=DISABLED`.

- [ ] **Step 3: Test command safety**

Verify the UI can request `session.pause` and `session.emergencyStop`, but cannot send raw keys, HWND messages, HID reports, or arbitrary URLs. Verify bridge disposal stops all timers.

## 6. Task 4: Build the Real-Time Workbench UI

**Files:**
- Create: `ui/src/features/workbench/WorkbenchPage.tsx`
- Create: `ui/src/features/workbench/TargetStatus.tsx`
- Create: `ui/src/features/workbench/SessionControls.tsx`
- Create: `ui/src/features/workbench/HealthPanel.tsx`
- Create: `ui/src/features/workbench/TelemetryStrip.tsx`
- Create: `ui/src/features/preview/PreviewRegion.tsx`
- Create: `ui/src/features/map/MapCalibrationPage.tsx`
- Create: `ui/src/features/replay/ReplayPage.tsx`
- Create: `ui/src/features/settings/SettingsPage.tsx`
- Create: `ui/src/features/diagnostics/DiagnosticsPage.tsx`

- [ ] **Step 1: Implement safe session controls**

Show `Stopped`, `Arming`, `Observing`, `Paused`, `MapScanning`, `MapCalibrating` and `EmergencyStop`. Keep Emergency Stop visible and enabled independently of page data loading.

- [ ] **Step 2: Implement the control panel**

Use inputs for attack mode, HP/MP threshold mode, skill keys, jump key, pickup key and pickup enabled. Load defaults automatically. Do not add a Self click-confirm control or any public Self tracking-number field.

- [ ] **Step 3: Implement diagnostics and logs**

Render target identity, focus, capture backend, Self/Player/Monster confidence, current platform, monster count, map version, input contract status, pause reason and JSONL event summaries.

- [ ] **Step 4: Add UI tests and screenshot checks**

Use Vitest for disabled/enabled/error states and Playwright for 1280x720, 1440x900 and narrow viewport screenshots. Verify no text overlap, clipped labels or hidden emergency control.

## 7. Task 5: Add Preview and Overlay Contracts

**Files:**
- Create: `src/Maple.Contracts/PreviewContracts.cs`
- Create: `src/Maple.Preview/FrameSlot.cs`
- Create: `src/Maple.Preview/OverlaySnapshot.cs`
- Create: `ui/src/features/preview/OverlayLegend.tsx`
- Create: `ui/src/features/preview/MockPreviewCanvas.tsx`
- Test: `src/Maple.Preview.Tests/FrameSlotTests.cs`
- Test: `ui/src/features/preview/MockPreviewCanvas.test.tsx`

- [ ] **Step 1: Implement the two-slot latest-frame buffer**

Publish only fully-written frames, expose frame age and dropped-frame counters, and never block the render consumer on vision results.

- [ ] **Step 2: Implement overlay semantics**

Render Self green with confidence only, Player cyan with confidence and track ID, and Monster red with class/confidence/target ID. Hide stale overlays; never render loot, HP/MP or static HUD boxes.

- [ ] **Step 3: Implement a browser mock preview**

Use a deterministic fixture or generated test frame to render at 60 FPS in a canvas for UI development. This measures browser rendering only; it is not evidence for Windows capture performance.

## 8. Task 6: Extract Portable Core and Feedback-Driven Action Policy

**Files:**
- Create: `src/Maple.Core/SessionStateMachine.cs`
- Create: `src/Maple.Core/SafetyGate.cs`
- Create: `src/Maple.Core/ActionPolicy.cs`
- Create: `src/Maple.Core/MovementDurationEstimator.cs`
- Create: `src/Maple.Core/ActionJournal.cs`
- Modify: `src/MaplePrototype/PrototypeState.cs` to consume shared contracts
- Test: `src/Maple.Core.Tests/SessionStateMachineTests.cs`
- Test: `src/Maple.Core.Tests/ActionPolicyTests.cs`
- Test: `src/Maple.Core.Tests/MovementDurationEstimatorTests.cs`

- [ ] **Step 1: Implement the safety gate first**

Require target binding, foreground, fresh frame, Self confidence, map validation, HP/MP health and input adapter health before returning `CanAct=true`. Low confidence returns `CalibrationRequired`, never a user-click request.

- [ ] **Step 2: Implement the observation-driven action loop**

Given Self/Monster boxes, platform, facing, attack range and map topology, return either `MoveLeft/MoveRight/Jump/Climb*`, `Attack`, `Pause`, or `Replan`. The estimator uses distance, observed displacement speed, edge distance, camera stability and min/max safety bounds.

- [ ] **Step 3: Enforce action lifecycle**

For every action require `Precondition -> KeyDown -> Observe -> EarlyReleaseOrTimeout -> KeyUp -> Postcondition`. Enter attack only after the post-movement observation confirms attack range and target validity. Record computed duration and all observations.

- [ ] **Step 4: Test deterministic scenarios**

Cover target left/right, target already in range, target disappears, edge proximity, camera movement, stale frame, lost Self, attack no-feedback, potion priority, pickup disabled, focus lost and EmergencyStop. Use fixed fixtures and no real input.

## 9. Task 7: Implement Map and Replay Modules

**Files:**
- Create: `src/Maple.Map/MapWorld.cs`
- Create: `src/Maple.Map/MapScanRegistrar.cs`
- Create: `src/Maple.Map/TopologyValidator.cs`
- Create: `src/Maple.Replay/SessionReplayReader.cs`
- Create: `src/Maple.Replay/ReplayClock.cs`
- Test: `src/Maple.Map.Tests/TopologyValidatorTests.cs`
- Test: `src/Maple.Replay.Tests/ReplayClockTests.cs`
- Fixture: `tests/fixtures/forest-east/*.json`

- [ ] **Step 1: Model candidate/validated/archived maps**

Persist source frames, camera transforms, coverage, platforms, ladders, boundaries, edges, calibration error and validation report. A candidate cannot produce an action plan.

- [ ] **Step 2: Implement topology checks**

Validate platform continuity, ladder endpoints, walk/jump/climb/drop edges, safe distances, unresolved structures and version compatibility.

- [ ] **Step 3: Implement replay**

Replay frame metadata, observations, state transitions and action decisions without any real input adapter. The replay clock must support pause, step, speed and deterministic timestamps.

## 10. Task 8: Implement Vision and Cloud Boundaries

**Files:**
- Create: `src/Maple.Vision/IVisionProvider.cs`
- Create: `src/Maple.Vision/ObservationFusion.cs`
- Create: `src/Maple.Vision/ConfidencePolicy.cs`
- Create: `src/Maple.Vision/MockVisionProvider.cs`
- Create: `src/Maple.Cloud/IBailianMapClient.cs`
- Create: `src/Maple.Cloud/BailianSchemas.cs`
- Create: `src/Maple.Cloud/MockBailianMapClient.cs`
- Test: `src/Maple.Vision.Tests/ConfidencePolicyTests.cs`
- Test: `src/Maple.Cloud.Tests/BailianSchemaTests.cs`

- [ ] **Step 1: Define detector boundaries**

Separate fixed UI/OCR, dynamic object detection, tracking, loot observation and map annotation. Providers return timestamped observations only; they never return keys or actions.

- [ ] **Step 2: Implement confidence and TTL fusion**

Hide stale boxes, reject conflicting HP/MP channels, require continuous Self confidence, and transition to `CalibrationRequired` on low confidence. Do not expose user click-to-correct behavior.

- [ ] **Step 3: Implement cloud schema and mock**

Validate `InitialMapAnnotation` source frames, coordinate system, platforms, ladders, boundaries, connections, confidence, coverage and calibration error. The mock supports timeout, malformed response and offline fallback tests.

- [ ] **Step 4: Leave native model providers portable**

Record ONNX/OpenCV provider interfaces and manifest fields on macOS. Do not claim real detector accuracy until a Windows/Linux-compatible fixture benchmark exists.

## 11. Task 9: Implement Input Contracts and macOS-safe Adapters

**Files:**
- Create: `src/Maple.Input/IInputAdapter.cs`
- Create: `src/Maple.Input/NullInputAdapter.cs`
- Create: `src/Maple.Input/ReplayInputAdapter.cs`
- Create: `src/Maple.Input/ActiveKeyRegistry.cs`
- Create: `src/Maple.Input/WindowsVirtualHidAdapter.cs` (interface-only stub; no guessed device writes)
- Test: `src/Maple.Input.Tests/ActiveKeyRegistryTests.cs`
- Test: `src/Maple.Input.Tests/NullInputAdapterTests.cs`

- [ ] **Step 1: Implement the portable contract**

Expose `KeyDown`, `KeyUp`, `Press`, `ReleaseAll`, `Heartbeat`, `GetStatus` and active-key inspection. Accept only abstract actions from `Maple.Core`.

- [ ] **Step 2: Implement Null and Replay adapters**

Null adapter always reports `INPUT_INJECTION=DISABLED`; replay adapter records requested actions without touching the OS. Verify every key-down has a matching key-up and `ReleaseAll` is idempotent.

- [ ] **Step 3: Mark Windows HID as pending**

Do not implement or guess VID/PID, report descriptors, private device paths or driver protocols on macOS. Mark the module `CONTRACT_COMPLETE / WINDOWS_PENDING` in the implementation tracker and hand off the exact device contract requirement.

## 12. Task 10: Windows Host and Native Preview Handoff

**Files (Windows implementation):**
- Create: `src/Maple.Host/Maple.Host.csproj`
- Create: `src/Maple.Host/WebViewHostForm.cs`
- Create: `src/Maple.Host/BridgeMessageRouter.cs`
- Create: `src/Maple.Preview/NativePreviewSurface.cs`
- Create: `src/Maple.Capture/WindowsGraphicsCaptureBackend.cs`
- Create: `src/Maple.Capture/BitBltCaptureBackend.cs`
- Modify: `src/MaplePrototype/WindowCapture.cs` only through the new `ICaptureBackend`
- Test: `tests/windows/native_preview_contract.tests.ps1`

- [ ] **Step 1: Load the React bundle in WebView2**

Serve local static assets, enable only the versioned JSON bridge, reject unknown commands, and keep the C# safety core alive if the page reloads or crashes.

- [ ] **Step 2: Add the native preview surface**

Host the preview as a sibling native child control, not a per-frame React image. Render the latest complete frame and TTL-valid overlays while keeping the original aspect ratio.

- [ ] **Step 3: Add capture backends**

Use Windows Graphics Capture first, BitBlt as measured fallback, and PrintWindow only as diagnostic fallback. Record backend, frame age, dropped frames and failure reason.

- [ ] **Step 4: Measure performance**

Record P50/P95/P99 capture-to-render latency, capture FPS, render FPS, recognition FPS, queue age, memory and backend fallback at 1280x720 and 1440x900. Require 30 FPS stable and 60 FPS target before marking native preview `DONE`.

## 13. Task 11: Windows HID Handoff and Verification

**Files (Windows implementation):**
- Modify: `src/Maple.Input/WindowsVirtualHidAdapter.cs`
- Create: `src/Maple.Input/VirtualHidDiagnostics.cs`
- Test: `tests/windows/hid_contract.tests.ps1`
- Evidence: `dist/hid-device-report.json`, `dist/hid-os-report.json`, `dist/hid-client-response.json`

- [ ] **Step 1: Fill the device contract from Windows evidence**

Record exact device interface path, VID/PID, report descriptor, transport, `KeyDown/KeyUp/ReleaseAll/Heartbeat`, installation/signing status and neutral-report behavior. Unknown devices remain disabled.

- [ ] **Step 2: Test three layers separately**

Verify device installation, OS Raw Input receipt and authorized-client visual response. Preserve screenshots/logs and key-down/key-up pairing evidence.

- [ ] **Step 3: Run safety interruption tests**

Test focus loss, window recreation, process exit, adapter disconnect, heartbeat timeout, WebView2 crash and EmergencyStop. Require zero stuck keys and no automatic resume.

- [ ] **Step 4: Update the spec status**

Only after the three-layer evidence and signed installation matrix pass may the master spec change HID from `WINDOWS_PENDING` to `DONE`.

## 14. Task 12: Verification and Release Gates

**Files:**
- Create: `tests/ui/playwright/workbench.spec.ts`
- Create: `tests/replay/replay_contract.tests.ps1`
- Create: `tools/build-react-ui.ps1`
- Modify: `README.md`
- Modify: `docs/MAPLE_PROJECT_SPEC.md` status table

- [ ] **Step 1: Run macOS gates**

```bash
cd ui
npm ci
npm run lint
npm run test
npm run build
npm run e2e
```

Expected: UI build, contract tests, replay tests and screenshots pass without Windows APIs or real input.

- [ ] **Step 2: Run Windows gates**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-react-ui.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\prototype_contract.tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\native_preview_contract.tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\windows\hid_contract.tests.ps1
git diff --check
```

- [ ] **Step 3: Run release matrix**

Verify Windows 10/11, DPI 100/125/150%, 1280x720 and 1440x900, WebView2 runtime availability, capture backend fallback, 30-minute UI/preview memory stability, then 4/8-hour candidate soak.

- [ ] **Step 4: Update status and handoff**

Mark only evidenced modules as `DONE`; record Windows-pending work, exact commands, evidence paths and remaining blockers in `docs/SESSION_HANDOFF_2026-08-14.md` without claiming HID success from macOS.

