# Live Vision Auto-Combat Workbench Design

Date: 2026-08-15
Status: Approved direction, pending implementation plan
Authority: This design refines the implementation of `docs/MAPLE_PROJECT_SPEC.md` without changing its product or safety boundaries.

## Goal

Turn the current Windows shell into an operational three-column workbench that displays real client frames, live Self/Player/Monster detections, HP/MP observations, in-frame performance telemetry, combat configuration, input state, and feedback-driven automatic combat state. The first real acceptance scene is the currently open snail map.

## Current Failure

The Host currently places `NativePreviewSurface` and the complete React application in separate halves of a `SplitContainer`. React already owns a three-column layout, so this second split clips its left controls and telemetry. The screenshot therefore shows the native client frame beside only the rightmost React panel.

The runtime composition also stops after target discovery, capture, preview, map-frame storage, and input-session setup. It does not construct or run `VisionPipeline`, publish real `ObservationSnapshot` events, publish overlays to `NativePreviewSurface`, or start `ProductionOrchestrator`. Existing combat controls update React state but are not persisted into the native runtime. The repository contains ONNX adapter code but no production model or label manifest.

## User Experience

### Window Layout

The application keeps the original dark three-column workbench:

- Left, 276 px: session start/pause, HP and MP values and thresholds, attack mode, attack key/profile, potion keys, jump key, pickup key/toggle, and current map profile.
- Center, remaining width: the real native client preview. The native surface is positioned only inside the React-declared preview rectangle instead of splitting the whole window.
- Right, 320 px: Self confidence, Player count, Monster count, selected target, current platform, map state, input broker state, safety gate, and compact event log.
- Top: target identity, session state, input state, settings, and an always-visible native emergency stop.

The preview remains a separate native control for high-frame-rate rendering. React publishes only the preview rectangle and overlay preferences; it never receives raw frames.

### In-Frame Telemetry

Performance information is drawn as a compact native HUD inside the preview:

- Top-left: capture FPS, render FPS, recognition FPS.
- Top-right: end-to-end latency, detector latency, capture backend, CPU/GPU provider.
- Bottom-left: current state and last abstract action.
- Bottom-right: queue age, dropped frames, process memory, and active warning.

Normal values use quiet neutral text with a translucent dark backing. Threshold breaches turn amber; unsafe or stale conditions turn red. The HUD never covers the minimap, HP/MP bar, character, or selected target when another corner is available. A separate full-width telemetry strip is removed from the normal workbench.

### Detection Overlay

Only fresh dynamic detections are drawn:

- Self: green box, `Self 94%`, exactly one active observation and no public track id.
- Other players: cyan box, `Player 91% #12`; never eligible as an attack target.
- Monsters: red box, localized class name, confidence, and target id. The selected target uses a stronger outline and small target marker.
- Loot is internal only and is not drawn.

Every box is associated with its source `frameId`, model version, confidence, and monotonic expiry. Expired or mismatched results disappear immediately. Zero-confidence cards show the concrete repair state such as `模型未加载`, `正在重新标定`, or `Self 丢失`, rather than pretending that recognition is active.

## Runtime Architecture

The operational data flow is:

```text
Windows target
  -> CaptureCoordinator / two latest-frame slots
     -> NativePreviewSurface (unblocked rendering)
     -> VisionWorker
        -> OpenCvHudRecognizer (HP/MP and fixed UI)
        -> OnnxDynamicDetector (Self/Player/Monster candidates)
        -> SelfTracker and ObservationFusion
        -> ObservationSnapshot
           -> Native overlay and telemetry HUD
           -> typed Host events for React status panels
           -> ProductionOrchestrator safety gate
              -> abstract action
              -> BrokerInputAdapter
              -> Maple.InputBroker.exe
              -> next frame feedback
```

The capture loop must not run model inference on the UI thread. Preview consumes every latest frame up to the display limit; vision consumes the newest available frame at its own bounded rate. Slow recognition reduces recognition FPS but cannot freeze preview, EmergencyStop, foreground monitoring, or key release.

## Model Bootstrap

The model at `C:\Users\Levi\Desktop\辅助\Kaelo_ok_sp\Kaelo_ok_sp\weights\best.onnx` may be loaded for local compatibility evaluation because it already exists on this machine. It is not copied into Git or the product distribution because that directory contains no verifiable source license.

Before it can drive actions, a model-inspection step must establish:

- input tensor names, dimensions, normalization, and color order;
- output tensor layout and non-maximum-suppression expectations;
- exact class order and whether Self, Player, and snail are represented;
- inference provider compatibility and a SHA-256-bound local manifest;
- accuracy on captured frames from the current client.

If the external model lacks Self/Player classes, it can only supply monster candidates. Self remains fail-closed until a dedicated model or a validated multi-frame Self tracker produces the unique high-confidence observation required by the master spec. No manual click-to-identify fallback is added.

## Combat Configuration

The left panel writes a versioned native configuration containing:

- attack mode: single, automatic, or group;
- attack action profile and configurable key binding;
- jump key, default Alt;
- pickup enabled and key, default Z;
- HP/MP threshold mode and potion action profiles;
- preferred attack distance and allowed current-platform pursuit range;
- bounded timing variation policy.

Configuration changes pause and release active keys before they are applied. React sends typed configuration intent only. Raw virtual keys, scan codes, flags, HWND values, or action sequences remain rejected at the bridge boundary.

Timing variation is bounded around feedback-derived durations. It cannot override frame freshness, platform boundaries, cooldowns, maximum holds, foreground checks, or ReleaseAll.

## Automatic Combat Behavior

The first operational loop is intentionally narrow but complete:

1. Bind the single eligible foreground client.
2. Capture stable frames and establish fresh HP/MP and unique Self observations.
3. Require a validated current-map profile and healthy input broker.
4. Select the nearest eligible monster on the current platform; exclude players and stale detections.
5. Move in short bounded actions while checking every new observation.
6. Release movement as soon as attack distance is reached or confidence drops.
7. Execute the configured attack profile and verify the next visual state.
8. Optionally pick up observed loot after combat.
9. Prioritize potion or pause behavior when HP/MP crosses its threshold.

Cross-platform navigation, ladder climbing, and map scanning remain connected through the existing map state machine, but the first Windows client acceptance test uses a same-platform snail target before enabling cross-platform actions.

## Safety And Failure States

The application remains fail-closed. It pauses, clears the action queue, and releases all keys when any of these occurs:

- target is not foreground, minimized, restarted, resized, or identity-mismatched;
- frame is black, stale, or inconsistent with the bound client;
- model is missing, its hash/manifest is invalid, or inference times out;
- Self is absent, duplicated, stale, or below the confidence threshold;
- map is unvalidated or current platform cannot be resolved;
- HP/MP observations conflict or expire;
- broker IPC, heartbeat, input mapping, or foreground verification fails;
- unknown popup, system lock/sleep, manual pause, or EmergencyStop occurs.

The UI displays the exact blocking reason and the active automatic repair step. It never displays `自动运行` while the runtime is only observing or missing a model.

## Testing And Acceptance

Implementation follows test-first development and is accepted in layers:

1. Layout tests prove the React workbench remains visible while the native preview occupies only the center rectangle at 1440x900 and 1280x720.
2. Overlay tests prove color semantics, target emphasis, TTL expiry, coordinate scaling, DPI changes, and no loot overlay.
3. Model inspection tests prove tensor/manifest/hash validation and reject unsupported output layouts.
4. Vision integration tests use recorded client frames to verify HP/MP, one Self, players excluded from targeting, and snail detections.
5. Runtime tests prove configuration reaches native state, stale observations block actions, movement releases at attack distance, and all exceptional exits call ReleaseAll.
6. Windows visual acceptance shows the real current client inside Maple with visible green Self and red snail boxes plus non-zero FPS/latency HUD values.
7. A supervised client test proves one short same-platform seek-and-attack cycle, pause, foreground loss, and EmergencyStop without stuck keys.
8. `node tools/verify-portable.mjs` and `tests/windows/production_input_contract.tests.ps1` must pass. Windows/model/client evidence is recorded separately and cannot be inferred from source or replay tests.

## Delivery Boundary

This design does not label the application complete after layout repair or after showing simulated boxes. The first usable delivery requires the real frame, real model result, visible overlay, populated observation panels, live in-frame telemetry, persisted combat settings, and a supervised same-platform closed loop. Broader maps and cross-platform navigation are subsequent evidence milestones, not substitutes for this first vertical path.
