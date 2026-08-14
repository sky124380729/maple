import { CONTRACT_SCHEMA_VERSION, type HostEvent } from '../contracts/bridge'

const baseTimestampMs = Date.parse('2026-08-14T12:00:00Z')
const target = {
  schemaVersion: CONTRACT_SCHEMA_VERSION,
  hwnd: '0x4d41504c',
  pid: 1008,
  clientWidth: 1280,
  clientHeight: 720,
  dpi: 96,
} as const

export function createMockTelemetryEvent(tick = 0): Extract<HostEvent, { type: 'telemetry.updated' }> {
  return {
    schemaVersion: CONTRACT_SCHEMA_VERSION,
    type: 'telemetry.updated',
    timestamp: new Date(baseTimestampMs + tick * 1_000).toISOString(),
    payload: {
      schemaVersion: CONTRACT_SCHEMA_VERSION,
      timestamp: new Date(baseTimestampMs + tick * 1_000).toISOString(),
      captureFps: 60 - (tick % 3),
      renderFps: 60,
      recognitionFps: 30,
      frameLatencyMs: 16 + (tick % 4),
      droppedFrames: 0,
      queueAgeMs: 2,
      state: 'Observing',
      pauseReason: 'None',
    },
  }
}

export function createMockSessionEvents(tick = 0): HostEvent[] {
  const capturedAtMonoMs = 100_000 + tick * 1_000
  const freshUntilMonoMs = capturedAtMonoMs + 250
  const timestamp = new Date(baseTimestampMs + tick * 1_000).toISOString()

  return [
    { schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'target.updated', timestamp, payload: target },
    { schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'session.stateChanged', timestamp, payload: { state: 'Observing', pauseReason: 'None' } },
    createMockTelemetryEvent(tick),
    {
      schemaVersion: CONTRACT_SCHEMA_VERSION,
      type: 'cloud.status.updated',
      timestamp,
      payload: {
        provider: 'bailian',
        enabled: false,
        credentialConfigured: false,
        modelId: 'qwen3-vl-plus',
        connectionStatus: 'notConfigured',
        requestInFlight: false,
        lastErrorCode: null,
      },
    },
    { schemaVersion: CONTRACT_SCHEMA_VERSION, type: 'preview.availabilityChanged', timestamp, payload: { available: true, backend: 'browser-mock' } },
    {
      schemaVersion: CONTRACT_SCHEMA_VERSION,
      type: 'observation.updated',
      timestamp,
      payload: {
        schemaVersion: CONTRACT_SCHEMA_VERSION,
        frameId: tick,
        capturedAtMonoMs,
        target,
        self: { box: [0.42, 0.51, 0.08, 0.18], confidence: 0.94, freshUntilMonoMs },
        players: [{ box: [0.2, 0.5, 0.08, 0.18], confidence: 0.81, freshUntilMonoMs, trackId: 'player-7' }],
        monsters: [{ class: 'snail', box: [0.66, 0.54, 0.07, 0.13], confidence: 0.88, freshUntilMonoMs, targetId: 'monster-1' }],
        loot: { visible: false, confidence: 0, freshUntilMonoMs },
        hp: { mode: 'percent', value: 0.99, confidence: 0.98, freshUntilMonoMs },
        mp: { mode: 'percent', value: 0.35, confidence: 0.96, freshUntilMonoMs },
        map: { mapId: 'forest-east', state: 'validated', confidence: 0.91, freshUntilMonoMs },
        state: 'Observing',
      },
    },
    {
      schemaVersion: CONTRACT_SCHEMA_VERSION,
      type: 'log.appended',
      timestamp,
      payload: { level: 'info', code: 'INPUT_INJECTION_DISABLED', message: 'INPUT_INJECTION=DISABLED' },
    },
  ]
}
