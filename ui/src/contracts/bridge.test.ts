import { describe, expect, test } from 'vitest'
import Ajv2020 from 'ajv/dist/2020'
import addFormats from 'ajv-formats'
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  CONTRACT_SCHEMA_VERSION,
  abstractActionSchema,
  actionPlanSchema,
  emergencyStopCommandSchema,
  hostEventSchema,
  observationSnapshotSchema,
  telemetrySnapshotSchema,
  uiCommandSchema,
  validateObservationSnapshot,
} from './bridge'

const fixturesRoot = resolve(process.cwd(), '../tests/fixtures/contracts')
const readFixture = (name: string) => JSON.parse(readFileSync(resolve(fixturesRoot, name), 'utf8')) as unknown

const validObservation = {
  schemaVersion: 2,
  frameId: 1842,
  capturedAtMonoMs: 441238,
  target: {
    schemaVersion: 2,
    hwnd: '0x1234',
    pid: 1008,
    clientWidth: 1280,
    clientHeight: 720,
    dpi: 96,
  },
  self: {
    box: [0.42, 0.51, 0.08, 0.18],
    confidence: 0.94,
    freshUntilMonoMs: 441338,
  },
  players: [
    {
      box: [0.2, 0.5, 0.08, 0.18],
      confidence: 0.81,
      freshUntilMonoMs: 441338,
      trackId: 'player-7',
    },
  ],
  monsters: [
    {
      class: 'snail',
      box: [0.66, 0.54, 0.07, 0.13],
      confidence: 0.88,
      freshUntilMonoMs: 441338,
      targetId: 'monster-12',
    },
  ],
  loot: { visible: false, confidence: 0, freshUntilMonoMs: 441338 },
  hp: { mode: 'percent', value: 0.99, confidence: 0.98, freshUntilMonoMs: 441338 },
  mp: { mode: 'percent', value: 0.35, confidence: 0.96, freshUntilMonoMs: 441338 },
  map: { mapId: 'forest-east', state: 'validated', confidence: 0.91, freshUntilMonoMs: 441338 },
  state: 'Observing',
}

describe('shared bridge contracts', () => {
  test('accepts stationary attack toggle without accepting raw keys', () => {
    expect(uiCommandSchema.safeParse({ schemaVersion: 2, type: 'stationary.attack.set', payload: { enabled: true } }).success).toBe(true)
    expect(uiCommandSchema.safeParse({ schemaVersion: 2, type: 'stationary.attack.set', payload: { enabled: true, key: 'Ctrl' } }).success).toBe(false)
  })

  test('accepts map calibration confirmation and runtime status', () => {
    expect(uiCommandSchema.safeParse({ schemaVersion: 2, type: 'map.calibration.confirm', payload: { mapId: 'forest-east' } }).success).toBe(true)
    expect(hostEventSchema.safeParse({
      schemaVersion: 2,
      type: 'map.status.updated',
      payload: { mapId: 'forest-east', state: 'candidate', coverage: 0.92, calibrationErrorPx: 2, platformCount: 8, ladderCount: 3, errors: [], canProduceActions: false },
    }).success).toBe(true)
  })

  test('accepts only finite bounded native preview layout intent', () => {
    const command = {
      schemaVersion: 2,
      type: 'preview.boundsChanged',
      payload: { left: 24, top: 80, width: 1280, height: 720, devicePixelRatio: 1.25 },
    }

    expect(uiCommandSchema.safeParse(command).success).toBe(true)
    expect(uiCommandSchema.safeParse({ ...command, payload: { ...command.payload, left: -1 } }).success).toBe(false)
    expect(uiCommandSchema.safeParse({ ...command, payload: { ...command.payload, width: 319 } }).success).toBe(false)
    expect(uiCommandSchema.safeParse({ ...command, payload: { ...command.payload, height: 10_001 } }).success).toBe(false)
    expect(uiCommandSchema.safeParse({ ...command, payload: { ...command.payload, devicePixelRatio: Number.POSITIVE_INFINITY } }).success).toBe(false)
    expect(uiCommandSchema.safeParse({ ...command, payload: { ...command.payload, scanCode: 75 } }).success).toBe(false)
  })

  test('accepts bounded abstract input tests without raw key fields', () => {
    const command = { schemaVersion: 2, type: 'input.test', payload: { kind: 'jump', holdMs: 90 } }

    expect(uiCommandSchema.safeParse(command).success).toBe(true)
    expect(uiCommandSchema.safeParse({ ...command, payload: { ...command.payload, holdMs: 2000 } }).success).toBe(false)
    expect(uiCommandSchema.safeParse({ ...command, payload: { ...command.payload, scanCode: 56 } }).success).toBe(false)
  })

  test('accepts only the explicit closed vision model status', () => {
    const event = {
      schemaVersion: 2,
      type: 'vision.status.updated',
      payload: { status: 'ready', modelId: 'maple-yolo-v2', provider: 'directml', diagnostic: null },
    }

    expect(hostEventSchema.safeParse(event).success).toBe(true)
    expect(hostEventSchema.safeParse({ ...event, payload: { ...event.payload, provider: 'webgpu' } }).success).toBe(false)
    expect(hostEventSchema.safeParse({ ...event, payload: { ...event.payload, modelId: 'x'.repeat(129) } }).success).toBe(false)
    expect(hostEventSchema.safeParse({ ...event, payload: { ...event.payload, action: 'Attack' } }).success).toBe(false)
  })

  test('requires richer bounded telemetry fields', () => {
    const telemetry = {
      schemaVersion: 2,
      timestamp: '2026-08-15T12:00:00Z',
      captureFps: 58,
      renderFps: 60,
      recognitionFps: 18,
      frameLatencyMs: 42,
      detectorLatencyMs: 31,
      droppedFrames: 3,
      queueAgeMs: 12,
      processMemoryMb: 384.5,
      inferenceProvider: 'directml',
      captureBackend: 'WGC',
      lastAction: null,
      warningCode: 'QUEUE_AGE_HIGH',
      state: 'Observing',
      pauseReason: 'None',
    }

    expect(telemetrySnapshotSchema.safeParse(telemetry).success).toBe(true)
    expect(telemetrySnapshotSchema.safeParse({ ...telemetry, detectorLatencyMs: -1 }).success).toBe(false)
    expect(telemetrySnapshotSchema.safeParse({ ...telemetry, processMemoryMb: -1 }).success).toBe(false)
    expect(telemetrySnapshotSchema.safeParse({ ...telemetry, lastAction: 'x'.repeat(129) }).success).toBe(false)
  })

  test('publishes strict split schemas for preview, vision, and telemetry', () => {
    const commandPath = resolve(process.cwd(), '../schemas/ui-command.schema.json')
    const eventPath = resolve(process.cwd(), '../schemas/host-event.schema.json')
    expect(existsSync(commandPath)).toBe(true)
    expect(existsSync(eventPath)).toBe(true)
    if (!existsSync(commandPath) || !existsSync(eventPath)) return

    const ajv = new Ajv2020({ allErrors: true, strict: false })
    addFormats(ajv)
    const validateCommand = ajv.compile(JSON.parse(readFileSync(commandPath, 'utf8')))
    const validateEvent = ajv.compile(JSON.parse(readFileSync(eventPath, 'utf8')))
    const preview = {
      schemaVersion: 2,
      type: 'preview.boundsChanged',
      payload: { left: 0, top: 0, width: 320, height: 180, devicePixelRatio: 0.5 },
    }
    const vision = {
      schemaVersion: 2,
      type: 'vision.status.updated',
      payload: { status: 'notConfigured', modelId: null, provider: 'none', diagnostic: null },
    }

    expect(validateCommand(preview)).toBe(true)
    expect(validateCommand({ ...preview, payload: { ...preview.payload, flags: 1 } })).toBe(false)
    expect(validateCommand({ ...preview, payload: { ...preview.payload, left: 10_001 } })).toBe(false)
    expect(validateCommand({
      schemaVersion: 2,
      type: 'config.update',
      payload: { hpThresholdMode: 'percent', hpThreshold: 101 },
    })).toBe(false)
    expect(validateCommand({
      schemaVersion: 2,
      type: 'config.update',
      payload: { hpThresholdMode: 'absolute', hpThreshold: 101 },
    })).toBe(true)
    expect(validateCommand({
      schemaVersion: 2,
      type: 'config.update',
      payload: { mpThresholdMode: 'percent', mpThreshold: 101 },
    })).toBe(false)
    expect(validateCommand({
      schemaVersion: 2,
      type: 'config.update',
      payload: { mpThresholdMode: 'absolute', mpThreshold: 101 },
    })).toBe(true)
    expect(validateEvent(vision)).toBe(true)
    expect(validateEvent({ ...vision, payload: { ...vision.payload, diagnostic: 'x'.repeat(129) } })).toBe(false)
    expect(validateEvent({
      schemaVersion: 2,
      type: 'input.status.updated',
      payload: { scanCode: 75 },
    })).toBe(false)
    expect(validateEvent({
      schemaVersion: 2,
      type: 'cloud.status.updated',
      payload: { actionSequence: ['Attack'] },
    })).toBe(false)
    expect(validateEvent({
      schemaVersion: 2,
      type: 'input.status.updated',
      payload: {
        provider: 'inputBroker',
        status: 'ready',
        integrity: 'high',
        activeKeys: [],
        lastReleaseSucceeded: true,
        hotkeys: { pauseResume: 'F9', emergencyStop: 'F12' },
        errorCode: null,
      },
    })).toBe(true)
    expect(validateEvent({
      schemaVersion: 2,
      type: 'cloud.status.updated',
      payload: {
        provider: 'bailian',
        enabled: true,
        credentialConfigured: true,
        modelId: 'qwen3-vl-plus',
        connectionStatus: 'ready',
        requestInFlight: false,
        lastErrorCode: null,
      },
    })).toBe(true)
  })

  test('accepts only the closed production input broker status shape', () => {
    const event = {
      schemaVersion: 2,
      type: 'input.status.updated',
      payload: {
        provider: 'inputBroker',
        status: 'ready',
        integrity: 'high',
        activeKeys: [],
        lastReleaseSucceeded: true,
        hotkeys: { pauseResume: 'F9', emergencyStop: 'F12' },
        errorCode: null,
      },
    }

    expect(hostEventSchema.safeParse(event).success).toBe(true)
    expect(hostEventSchema.safeParse({
      ...event,
      payload: {
        ...event.payload,
        hotkeys: { pauseResume: 'Ctrl+Shift+F9', emergencyStop: 'Ctrl+Shift+F12' },
      },
    }).success).toBe(true)
    expect(hostEventSchema.safeParse({
      ...event,
      payload: { ...event.payload, hotkeys: { pauseResume: 'Alt+F9', emergencyStop: 'Alt+F12' } },
    }).success).toBe(false)
    expect(hostEventSchema.safeParse({
      ...event,
      payload: { ...event.payload, scanCode: 0x4b },
    }).success).toBe(false)
  })

  test('uses schema version 2 and rejects version 1 envelopes', () => {
    expect(CONTRACT_SCHEMA_VERSION).toBe(2)
    expect(uiCommandSchema.safeParse({ schemaVersion: 1, type: 'snapshot.request', payload: {} }).success).toBe(false)
  })

  test('requires explicit profiles for attack and potion actions', () => {
    const baseAction = {
      actionId: 'action-v2',
      issuedAtMonoMs: 100,
      holdMs: 80,
      maxDurationMs: 200,
    }

    expect(abstractActionSchema.safeParse({ ...baseAction, type: 'Attack' }).success).toBe(false)
    expect(abstractActionSchema.safeParse({ ...baseAction, type: 'Attack', profileId: 'singleAttack' }).success).toBe(true)
    expect(abstractActionSchema.safeParse({ ...baseAction, type: 'UsePotion', profileId: 'mpPotion' }).success).toBe(true)
    expect(abstractActionSchema.safeParse({ ...baseAction, type: 'MoveUp' }).success).toBe(false)
    expect(abstractActionSchema.safeParse({ ...baseAction, type: 'MoveDown' }).success).toBe(false)
  })

  test('validates bridge percentage thresholds in the 0 to 100 unit', () => {
    const command = {
      schemaVersion: 2,
      type: 'config.update',
      payload: { hpThresholdMode: 'percent', hpThreshold: 35 },
    }

    expect(uiCommandSchema.safeParse(command).success).toBe(true)
    expect(uiCommandSchema.safeParse({ ...command, payload: { ...command.payload, hpThreshold: 101 } }).success).toBe(false)
  })

  test('accepts a complete native combat configuration snapshot', () => {
    const event = {
      schemaVersion: 2,
      type: 'config.updated',
      payload: {
        schemaVersion: 2,
        attackMode: 'auto',
        hpThresholdMode: 'percent', hpThreshold: 50,
        mpThresholdMode: 'percent', mpThreshold: 30,
        singleAttackKey: 'J', areaAttackKey: 'A', hpPotionKey: '1', mpPotionKey: '2',
        jumpKey: 'Alt', pickupEnabled: true, pickupKey: 'Z',
        preferredDistancePx: 70, areaTargetCount: 3, switchCooldownMs: 1200,
      },
    }

    expect(hostEventSchema.safeParse(event).success).toBe(true)
    expect(hostEventSchema.safeParse({ ...event, payload: { ...event.payload, scanCode: 75 } }).success).toBe(false)
  })

  test('accepts only fixed Bailian models and never exposes a credential in status', () => {
    expect(uiCommandSchema.safeParse({
      schemaVersion: 2,
      type: 'cloud.config.update',
      payload: { enabled: true, modelId: 'qwen3-vl-plus', uploadConsent: true },
    }).success).toBe(true)
    expect(uiCommandSchema.safeParse({
      schemaVersion: 2,
      type: 'cloud.config.update',
      payload: { enabled: true, modelId: 'custom-model', uploadConsent: true },
    }).success).toBe(false)

    const safeStatus = {
      schemaVersion: 2,
      type: 'cloud.status.updated',
      payload: {
        provider: 'bailian',
        enabled: true,
        credentialConfigured: true,
        modelId: 'qwen3-vl-plus',
        connectionStatus: 'ready',
        requestInFlight: false,
        lastErrorCode: null,
      },
    }
    expect(hostEventSchema.safeParse(safeStatus).success).toBe(true)
    expect(hostEventSchema.safeParse({ ...safeStatus, payload: { ...safeStatus.payload, ['api' + 'Key']: 'redacted-test-value' } }).success).toBe(false)
  })

  test('accepts a complete observation while keeping Self tracking private', () => {
    const result = validateObservationSnapshot(validObservation)

    expect(result.success).toBe(true)
    if (result.success) {
      expect(result.data.self).not.toHaveProperty('trackId')
      expect(result.data.players[0]).toHaveProperty('trackId')
      expect(result.data.monsters[0]).toHaveProperty('targetId')
    }
  })

  test('rejects a public Self tracking id', () => {
    const result = observationSnapshotSchema.safeParse({
      ...validObservation,
      self: { ...validObservation.self, trackId: 'self-1' },
    })

    expect(result.success).toBe(false)
  })

  test('rejects malformed normalized boxes and stale TTL values', () => {
    const malformedBox = observationSnapshotSchema.safeParse({
      ...validObservation,
      self: { ...validObservation.self, box: [0.95, 0.1, 0.2, 0.1] },
    })
    const staleTtl = observationSnapshotSchema.safeParse({
      ...validObservation,
      self: { ...validObservation.self, freshUntilMonoMs: 441238 + 5001 },
    })

    expect(malformedBox.success).toBe(false)
    expect(staleTtl.success).toBe(false)
  })

  test('rejects unknown UI commands and emergency stop without a message', () => {
    expect(uiCommandSchema.safeParse({ schemaVersion: 1, type: 'raw.key', payload: {} }).success).toBe(false)
    expect(emergencyStopCommandSchema.safeParse({ schemaVersion: 1, type: 'session.emergencyStop', payload: {} }).success).toBe(false)
  })

  test('accepts an emergency stop only with a bounded operator message', () => {
    const result = emergencyStopCommandSchema.safeParse({
      schemaVersion: 2,
      type: 'session.emergencyStop',
      timestamp: '2026-08-14T12:00:00Z',
      payload: { message: 'Operator requested stop' },
    })

    expect(result.success).toBe(true)
  })

  test('rejects action durations outside safety bounds', () => {
    const result = actionPlanSchema.safeParse({
      schemaVersion: 1,
      planId: 'plan-1',
      createdAtMonoMs: 100,
      actions: [
        {
          actionId: 'action-1',
          type: 'MoveLeft',
          issuedAtMonoMs: 100,
          holdMs: 5001,
          maxDurationMs: 5000,
        },
      ],
    })

    expect(result.success).toBe(false)
  })

  test('keeps JSON Schema and TypeScript validation aligned for shared fixtures', () => {
    const ajv = new Ajv2020({ allErrors: true, strict: false })
    addFormats(ajv)
    const schema = JSON.parse(readFileSync(resolve(process.cwd(), '../schemas/observation.schema.json'), 'utf8')) as object
    const validateJsonSchema = ajv.compile(schema)
    const validFixture = readFixture('valid-observation.json')
    const selfTrackFixture = readFixture('invalid-self-track-id.json')
    const staleFixture = readFixture('invalid-stale-observation.json')

    expect(validateJsonSchema(validFixture)).toBe(true)
    expect(observationSnapshotSchema.safeParse(validFixture).success).toBe(true)
    expect(validateJsonSchema(selfTrackFixture)).toBe(false)
    expect(observationSnapshotSchema.safeParse(selfTrackFixture).success).toBe(false)
    expect(validateJsonSchema(staleFixture)).toBe(true)
    expect(observationSnapshotSchema.safeParse(staleFixture).success).toBe(false)
  })

  test('validates shared command fixtures through the typed bridge', () => {
    expect(emergencyStopCommandSchema.safeParse(readFixture('valid-emergency-stop.json')).success).toBe(true)
    expect(actionPlanSchema.safeParse(readFixture('invalid-action-duration.json')).success).toBe(false)
    expect(uiCommandSchema.safeParse(readFixture('invalid-command.json')).success).toBe(false)
  })

  test('compiles and applies all three published JSON Schemas', () => {
    const ajv = new Ajv2020({ allErrors: true, strict: false })
    addFormats(ajv)
    const bridgeValidator = ajv.compile(JSON.parse(readFileSync(resolve(process.cwd(), '../schemas/bridge.schema.json'), 'utf8')))
    const observationValidator = ajv.compile(JSON.parse(readFileSync(resolve(process.cwd(), '../schemas/observation.schema.json'), 'utf8')))
    const actionValidator = ajv.compile(JSON.parse(readFileSync(resolve(process.cwd(), '../schemas/action.schema.json'), 'utf8')))

    expect(bridgeValidator(readFixture('valid-emergency-stop.json'))).toBe(true)
    expect(bridgeValidator(readFixture('invalid-command.json'))).toBe(false)
    expect(observationValidator(readFixture('valid-observation.json'))).toBe(true)
    expect(actionValidator(readFixture('invalid-action-duration.json'))).toBe(false)
  })
})
