import { describe, expect, test } from 'vitest'
import Ajv2020 from 'ajv/dist/2020'
import addFormats from 'ajv-formats'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  CONTRACT_SCHEMA_VERSION,
  abstractActionSchema,
  actionPlanSchema,
  emergencyStopCommandSchema,
  hostEventSchema,
  observationSnapshotSchema,
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

  test('allows a thirty second attack without extending movement limits', () => {
    const longDuration = {
      actionId: 'stationary-attack',
      issuedAtMonoMs: 100,
      holdMs: 30_000,
      maxDurationMs: 30_000,
    }

    expect(abstractActionSchema.safeParse({ ...longDuration, type: 'Attack', profileId: 'singleAttack' }).success).toBe(true)
    expect(abstractActionSchema.safeParse({ ...longDuration, type: 'MoveLeft' }).success).toBe(false)
  })

  test('accepts strict combat rhythm countdown events', () => {
    const event = {
      schemaVersion: 2,
      type: 'combat.rhythm.updated',
      payload: {
        schemaVersion: 2,
        cycleId: 7,
        phase: 'attackHolding',
        sampledDurationMs: 26_430,
        remainingMs: 18_620,
        updatedAtMonoMs: 120_000,
        earlyReleaseReason: null,
      },
    }

    expect(hostEventSchema.safeParse(event).success).toBe(true)
    expect(hostEventSchema.safeParse({ ...event, payload: { ...event.payload, direction: 'Left' } }).success).toBe(false)
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
