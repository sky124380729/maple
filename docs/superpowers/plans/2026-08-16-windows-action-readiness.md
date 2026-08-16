# Windows Action Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the remaining fail-closed blockers without weakening safety: match the confirmed key bindings, identify maps without relying on stylized OCR, establish Self identity from controlled visual feedback, and expose a supervised production Broker action matrix.

**Architecture:** Keep React restricted to typed user intents and keep raw key encoding inside `Maple.InputBroker.exe`. The fixed-UI vision provider will combine OCR with a stable minimap perceptual fingerprint; the Self tracker will only become action-capable after a stable track is confirmed by a bounded calibration movement. A native Host controller will run one supervised abstract action at a time after countdown, foreground, target identity, TTL, and Broker health checks.

**Tech Stack:** .NET 8, OpenCvSharp, xUnit, React 19, TypeScript, Vitest, WebView2, existing production Broker IPC.

---

### Task 1: Apply Confirmed Key Bindings

**Files:**
- Modify: `src/Maple.Input/BrokerKeyProfile.cs`
- Modify: `src/Maple.Host/CombatConfiguration.cs`
- Modify: `ui/src/features/workbench/combatConfiguration.ts`
- Modify: `ui/src/features/workbench/KeyBindingEditor.tsx`
- Test: `src/Maple.Input.Tests/BrokerKeyProfileTests.cs`
- Test: `src/Maple.Host.Tests/CombatConfigurationStoreTests.cs`
- Test: `ui/src/features/workbench/SessionControls.test.tsx`

- [ ] Add failing tests for `Delete`/`End` extended scan codes and the confirmed defaults: Ctrl attack, Alt jump, Z pickup, Delete HP, End MP.
- [ ] Run focused Input, Host, and UI tests and verify the expected failures.
- [ ] Add navigation-key logical encodings and update native/React defaults and labels without exposing scan codes to React.
- [ ] Run focused tests and verify they pass.

### Task 2: Add Stable Visual Map Identity

**Files:**
- Create: `src/Maple.Vision/VisualMapFingerprint.cs`
- Modify: `src/Maple.Vision/OpenCvHudRecognizer.cs`
- Modify: `src/Maple.Vision/AdaptiveFixedUiVisionProvider.cs`
- Create: `src/Maple.Runtime.Tests/Vision/VisualMapFingerprintTests.cs`
- Modify: `src/Maple.Runtime.Tests/Vision/AdaptiveFixedUiVisionProviderTests.cs`

- [ ] Add failing tests proving small dynamic minimap changes retain one identity, a different topology changes identity, and identity remains unknown until stable.
- [ ] Run the focused Runtime tests and verify the expected failures.
- [ ] Implement a minimap structural perceptual hash, Hamming-distance stability tracker, and OCR-first/fingerprint-fallback map observation.
- [ ] Run focused tests and verify they pass.

### Task 3: Confirm Self Through Controlled Motion

**Files:**
- Modify: `src/Maple.Vision/SelfIdentityTracker.cs`
- Modify: `src/Maple.Vision/OnnxDynamicDetector.cs`
- Create: `src/Maple.Runtime.Tests/Vision/SelfMotionConfirmationTests.cs`
- Modify: `src/Maple.Runtime.Tests/Vision/SelfIdentityTrackerTests.cs`

- [ ] Add failing tests showing temporal low-confidence detections remain display-only, a unique track whose displacement matches a calibration action becomes Self, and ambiguous/opposite movement stays blocked.
- [ ] Run focused Runtime tests and verify the expected failures.
- [ ] Add a bounded motion-confirmation API and retain fail-closed behavior until confirmation.
- [ ] Run focused tests and verify they pass.

### Task 4: Add Supervised Broker Action Matrix

**Files:**
- Modify: `schemas/ui-command.schema.json`
- Modify: `schemas/host-event.schema.json`
- Modify: `ui/src/contracts/bridge.ts`
- Modify: `src/Maple.Contracts/DomainContracts.cs`
- Create: `src/Maple.Host/InputAcceptanceController.cs`
- Modify: `src/Maple.Host/HostCommandDispatcher.cs`
- Modify: `src/Maple.Host/HostCompositionRoot.cs`
- Modify: `ui/src/features/workbench/SessionControls.tsx`
- Create: `src/Maple.Host.Tests/InputAcceptanceControllerTests.cs`
- Modify: `ui/src/features/workbench/SessionControls.test.tsx`

- [ ] Add failing contract/controller/UI tests for a bounded abstract action, countdown, foreground wait, result event, timeout, cancellation, and guaranteed `ReleaseAll`.
- [ ] Run focused tests and verify the expected failures.
- [ ] Implement the supervised matrix for left/right/up/down/jump/attack/pickup/HP/MP using abstract actions only.
- [ ] Run focused tests and verify they pass.

### Task 5: Build And Verify Before Client Testing

**Files:**
- Modify: `docs/maple-runtime/VERIFICATION_2026-08-14.md`
- Modify only production files required by verification failures.

- [ ] Run focused .NET and UI tests.
- [ ] Run `node tools/verify-portable.mjs`.
- [ ] Run `tests/windows/production_input_contract.tests.ps1`.
- [ ] Publish `dist/windows-x64/Maple.exe` and `Maple.InputBroker.exe`.
- [ ] Record exact remaining Windows evidence; do not claim PASS before the supervised client action matrix and ReleaseAll checks succeed.

---

## Plan Self-Review

- The plan preserves the Host/Broker privilege boundary and never restores VHF, SendInput, or raw-key React commands.
- Visual map identity is a lookup key, not automatic map validation; candidate maps still cannot drive navigation.
- Low-confidence temporal detections are not promoted to action-capable Self without controlled-motion evidence.
- The supervised action matrix is explicitly user-armed and always releases all keys.
