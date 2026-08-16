# Continuous Combat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the bounded 15-second same-platform trial with continuous combat that stops only through an existing safety or operator stop path.

**Architecture:** Keep the existing React abstract command and `Maple.InputBroker.exe` boundary. Remove only the controller's fixed cancellation timer, then rename the existing control so the UI describes the actual behavior; all existing pause, focus-loss, emergency-stop, and disposal paths continue to cancel the run and release keys.

**Tech Stack:** .NET 8, xUnit, React, TypeScript, Vitest.

---

### Task 1: Continuous controller lifetime

**Files:**
- Modify: `src/Maple.Host/SamePlatformCombatTrialController.cs`
- Test: `src/Maple.Host.Tests/SamePlatformCombatTrialTests.cs`

- [ ] Add a controller test using a blocking observation source and a controllable cancellation token; assert the run remains active until `PauseAsync` is called.
- [ ] Run the focused xUnit test and confirm it fails because the controller installs a 15-second timeout.
- [ ] Remove `TrialDurationMs` and `run.CancelAfter(...)`; retain the linked cancellation source so operator pause, focus loss, emergency stop, and disposal still cancel the run.
- [ ] Run all `Maple.Host.Tests` and confirm they pass.

### Task 2: Continuous-run user control

**Files:**
- Modify: `ui/src/features/workbench/SessionControls.tsx`
- Test: `ui/src/features/workbench/SessionControls.test.tsx`

- [ ] Add a UI test that expects a button named `开始自动运行` to send `{ schemaVersion: 2, type: 'combat.trial.start', payload: {} }` and become unavailable while running.
- [ ] Run the focused Vitest file and confirm it fails on the old 15-second label.
- [ ] Replace the bounded-trial label with `开始自动运行`; keep `暂停并释放按键` as the explicit stop control.
- [ ] Run UI tests, publish Windows Release, and run `node tools/verify-portable.mjs` plus `tests/windows/production_input_contract.tests.ps1`.

