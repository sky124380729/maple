# Humanized Stationary Attack Rhythm Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a bounded random stationary-attack cycle that continuously holds the attack key for a weighted 1–30 seconds, performs two randomized opposite-direction movements, optionally rests, and publishes an authoritative UI countdown.

**Architecture:** Keep random generation and phase control in Core/Runtime, with injected randomness for deterministic tests. Extend action and bridge contracts so only `Attack` can last 30 seconds, then drive the existing input executor through a feedback-aware rhythm loop. Publish read-only rhythm snapshots through Host and render them from the React session store.

**Tech Stack:** .NET 8, C#, xUnit, JSON Schema draft 2020-12, React 19, TypeScript, Zod, Zustand, Vitest/Testing Library.

---

### Task 1: Long-attack and rhythm-event contracts

**Files:**
- Modify: `src/Maple.Runtime.Tests/Contracts/ContractV2Tests.cs`
- Modify: `src/Maple.Contracts/DomainContracts.cs`
- Modify: `schemas/action.schema.json`
- Modify: `schemas/bridge.schema.json`
- Modify: `tests/portable-contracts.mjs`
- Modify: `ui/src/contracts/bridge.test.ts`
- Modify: `ui/src/contracts/bridge.ts`

- [ ] **Step 1: Write failing tests** proving a 30,000ms `Attack` is valid, a movement above 5,000ms is invalid, and `combat.rhythm.updated` validates the exact phase/countdown payload.
- [ ] **Step 2: Run RED:** `dotnet test src/Maple.Runtime.Tests/Maple.Runtime.Tests.csproj --filter ContractV2Tests`, `npm --prefix ui test -- ui/src/contracts/bridge.test.ts`, and `node tests/portable-contracts.mjs`; expect duration/event failures.
- [ ] **Step 3: Implement** `MaxAttackDurationMs = 30000`, conditional C# and JSON Schema limits, `CombatRhythmPhase`, `CombatRhythmSnapshot`, Host event enum value, and matching strict Zod schema. `earlyReleaseReason` is nullable and at most 200 characters.
- [ ] **Step 4: Run the Step 2 commands; expect PASS.**
- [ ] **Step 5: Commit:** `git commit -m "feat: extend contracts for stationary attack rhythm"` with only contract/schema tests and implementation.

The payload shape is:

```json
{
  "cycleId": 7,
  "phase": "attackHolding",
  "sampledDurationMs": 26430,
  "remainingMs": 18620,
  "updatedAtMonoMs": 120000,
  "earlyReleaseReason": null
}
```

### Task 2: Deterministic bounded random sampler

**Files:**
- Create: `src/Maple.Core/StationaryAttackRhythm.cs`
- Create: `src/Maple.Runtime.Tests/Runtime/StationaryAttackRhythmTests.cs`

- [ ] **Step 1: Write failing tests** for weighted selector boundaries `0..87`, `88..96`, `97..99`; attack band boundaries; both first directions; independent movement holds; 50–350ms gap; 25% rest decision; and 2–5 second rest duration.
- [ ] **Step 2: Run RED:** `dotnet test src/Maple.Runtime.Tests/Maple.Runtime.Tests.csproj --filter StationaryAttackRhythmTests`; expect missing rhythm types.
- [ ] **Step 3: Implement** `IRandomSource`, `SystemRandomSource`, validated `StationaryAttackRhythmOptions`, `HorizontalDirection`, and `StationaryAttackRhythmSampler` methods `SampleAttackHoldMs`, `SampleFirstDirection`, `SampleMovementHoldMs`, `SampleMovementGapMs`, `ShouldRest`, and `SampleRestMs`.
- [ ] **Step 4: Run the Step 2 command; expect PASS.**
- [ ] **Step 5: Commit:** `git commit -m "feat: add bounded stationary rhythm sampler"`.

The production sampler delegates to `Random.Shared.Next(minInclusive, maxExclusive)`. Tests use a queue-backed implementation so every branch and boundary is deterministic; no flaky statistical assertion is a correctness gate.

### Task 3: Feedback-aware stationary rhythm execution

**Files:**
- Create: `src/Maple.Runtime/ICombatRhythmSink.cs`
- Modify: `src/Maple.Runtime/OrchestratorOptions.cs`
- Modify: `src/Maple.Runtime/IRuntimeJournal.cs`
- Modify: `src/Maple.Runtime/ProductionOrchestrator.cs`
- Modify: `src/Maple.Runtime.Tests/Runtime/ProductionOrchestratorTests.cs`

- [ ] **Step 1: Write failing tests** for one attack down/up across the full sampled hold, left→right and right→left movement, independent movement durations, optional rest with no active key, target loss before deadline, cancellation, and final `ReleaseAll`.
- [ ] **Step 2: Run RED:** `dotnet test src/Maple.Runtime.Tests/Maple.Runtime.Tests.csproj --filter ProductionOrchestratorTests`; expect missing rhythm behavior.
- [ ] **Step 3: Add** `ICombatRhythmSink.PublishAsync(CombatRhythmSnapshot, CancellationToken)` and `NullCombatRhythmSink`. Inject the sampler and sink with safe defaults.
- [ ] **Step 4: Implement the phase loop:** sample long attack on entry; hold one key-down until monotonic observation time reaches its deadline; publish at phase start and at most every 250ms; on normal completion perform two opposite randomized moves with a no-key gap; then independently choose optional rest. An early attack interruption returns to the normal policy loop without repositioning.
- [ ] **Step 5: Preserve safety:** each observation re-runs the safety gate; target loss, potion priority, replan, cancellation, exception, focus loss, stale frame, or device failure releases the current key immediately. The existing outer `finally` still calls `ReleaseAll`.
- [ ] **Step 6: Extend journal entries** with nullable cycle, phase, planned duration, actual duration, remaining duration, sampled direction, and early-release reason.
- [ ] **Step 7: Run the Step 2 command; expect PASS**, including legacy tests with rhythm disabled explicitly in their helper.
- [ ] **Step 8: Commit:** `git commit -m "feat: execute randomized stationary attack cycles"`.

Expected left-first action trace:

```text
Attack:SingleAttack.down
Attack:SingleAttack.up
MoveLeft.down
MoveLeft.up
MoveRight.down
MoveRight.up
releaseAll
```

### Task 4: Host publication and React countdown card

**Files:**
- Modify: `src/Maple.Host/WebViewHostForm.cs`
- Modify: `src/Maple.Host.Tests/HostSafetyCoordinatorTests.cs`
- Modify: `ui/src/store/sessionStore.ts`
- Modify: `ui/src/mock/mockSession.ts`
- Modify: `ui/src/bridge/HostBridge.test.ts`
- Create: `ui/src/features/workbench/RhythmCountdown.tsx`
- Create: `ui/src/features/workbench/RhythmCountdown.test.tsx`
- Modify: `ui/src/features/workbench/SessionControls.tsx`
- Modify: `ui/src/features/workbench/WorkbenchPage.tsx`
- Modify: `ui/src/app/app.css`

- [ ] **Step 1: Write failing tests** for Host serialization, store updates/clearing, and labels for attack, left/right movement, gap, rest, stopped, paused, emergency, and early release.
- [ ] **Step 2: Run RED:** `dotnet test src/Maple.Host.Tests/Maple.Host.Tests.csproj` and `npm --prefix ui test -- ui/src/features/workbench/RhythmCountdown.test.tsx ui/src/bridge/HostBridge.test.ts`; expect missing sender/store/component failures.
- [ ] **Step 3: Implement Host publication** by making `WebViewHostForm` an `ICombatRhythmSink`; serialize only the versioned snapshot and marshal to the UI thread when required.
- [ ] **Step 4: Implement store/mock handling.** Store the latest snapshot, clear it for stopped/paused/emergency session states, and emit deterministic mock rhythm events without producing any input command.
- [ ] **Step 5: Implement the read-only card** below the control hero. Use tabular numerals and two decimal seconds. The component receives data only and exposes no callback for attack or movement.
- [ ] **Step 6: Run GREEN:** repeat Step 2, then `npm --prefix ui run typecheck` and `npm --prefix ui run lint`; expect PASS.
- [ ] **Step 7: Commit:** `git commit -m "feat: show stationary rhythm countdown"`.

Required attack text:

```text
攻击键按住中
本轮 26.43 秒
剩余 18.62 秒
```

### Task 5: Full verification and evidence

**Files:**
- Modify: `docs/maple-runtime/VERIFICATION_2026-08-14.md`

- [ ] **Step 1: Run** `node tools/verify-portable.mjs`; expect all portable schema, .NET, UI, and closed-loop checks to pass.
- [ ] **Step 2: Run** `git diff --check`, `git status --short`, and a scoped diff review; expect no whitespace errors or unrelated files.
- [ ] **Step 3: Append** exact commands/results to the verification document. State explicitly that macOS portable success is not Windows PASS.
- [ ] **Step 4: Record** the remaining Windows-only command: `tests/windows/hid_contract.tests.ps1 -RequireEvidence`.
- [ ] **Step 5: Commit:** `git commit -m "docs: record stationary rhythm verification"`.
