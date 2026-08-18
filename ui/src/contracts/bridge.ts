import { z } from 'zod'

export const CONTRACT_SCHEMA_VERSION = 2 as const
export const MAX_OBSERVATION_TTL_MS = 5_000
export const MAX_ACTION_DURATION_MS = 5_000
export const MAX_ATTACK_DURATION_MS = 30_000

const schemaVersion = z.literal(CONTRACT_SCHEMA_VERSION)
const monoMs = z.number().int().nonnegative()
const confidence = z.number().min(0).max(1)
const timestamp = z.string().datetime({ offset: true })
const inferenceProviderSchema = z.enum(['none', 'cpu', 'directml', 'cuda'])
const captureBackendSchema = z.enum(['WGC', 'BitBlt', 'PrintWindow'])

export const normalizedBoxSchema = z
  .tuple([z.number().min(0).max(1), z.number().min(0).max(1), z.number().min(0).max(1), z.number().min(0).max(1)])
  .superRefine(([x, y, width, height], context) => {
    if (x + width > 1) context.addIssue({ code: z.ZodIssueCode.custom, message: 'box exceeds right edge' })
    if (y + height > 1) context.addIssue({ code: z.ZodIssueCode.custom, message: 'box exceeds bottom edge' })
  })

export const targetBindingSchema = z
  .object({
    schemaVersion,
    hwnd: z.string().regex(/^0x[0-9a-f]+$/i),
    pid: z.number().int().positive(),
    startedAtUtc: timestamp.optional(),
    executablePath: z.string().min(1).optional(),
    clientWidth: z.number().int().positive(),
    clientHeight: z.number().int().positive(),
    dpi: z.number().int().min(48).max(768),
  })
  .strict()

const detectionBase = {
  box: normalizedBoxSchema,
  confidence,
  freshUntilMonoMs: monoMs,
}

export const selfDetectionSchema = z.object(detectionBase).strict()
export const playerDetectionSchema = z.object({ ...detectionBase, trackId: z.string().min(1).max(128) }).strict()
export const monsterDetectionSchema = z
  .object({ ...detectionBase, class: z.string().min(1).max(128), targetId: z.string().min(1).max(128) })
  .strict()

export const captureFrameMetadataSchema = z
  .object({
    schemaVersion,
    frameId: z.number().int().nonnegative(),
    capturedAtMonoMs: monoMs,
    clientWidth: z.number().int().positive(),
    clientHeight: z.number().int().positive(),
    dpi: z.number().int().min(48).max(768),
    captureBackend: captureBackendSchema,
    captureDurationMs: z.number().nonnegative(),
    droppedReason: z.enum(['backpressure', 'occluded', 'invalid', 'none']).default('none'),
  })
  .strict()

export const lootObservationSchema = z
  .object({ visible: z.boolean(), confidence, freshUntilMonoMs: monoMs })
  .strict()

const resourceObservationSchema = z
    .object({ mode: z.literal('percent'), value: z.number().min(0).max(1), currentValue: z.number().nonnegative().optional(), maximumValue: z.number().positive().optional(), confidence, freshUntilMonoMs: monoMs })
  .strict()
  .or(z.object({ mode: z.literal('absolute'), value: z.number().nonnegative(), currentValue: z.number().nonnegative().optional(), maximumValue: z.number().positive().optional(), confidence, freshUntilMonoMs: monoMs }).strict())

export const mapObservationSchema = z
  .object({
    mapId: z.string().min(1).max(256),
    state: z.enum(['candidate', 'validated', 'archived']),
    confidence,
    freshUntilMonoMs: monoMs,
  })
  .strict()

export const sessionStateSchema = z.enum([
  'Stopped',
  'Arming',
  'Observing',
  'MapScanning',
  'MapCalibrating',
  'Navigating',
  'Attacking',
  'Looting',
  'UsingPotion',
  'Paused',
  'ManualIntervention',
  'EmergencyStop',
])

export const pauseReasonSchema = z.enum([
  'None',
  'CalibrationRequired',
  'StaleFrame',
  'TargetLost',
  'WindowNotForeground',
  'BlackFrame',
  'MapNotValidated',
  'InputUnavailable',
  'HealthUnknown',
  'UnknownPopup',
  'WatchdogTimeout',
  'OperatorRequested',
  'SafetyViolation',
])

export const observationSnapshotSchema = z
  .object({
    schemaVersion,
    frameId: z.number().int().nonnegative(),
    capturedAtMonoMs: monoMs,
    target: targetBindingSchema,
    self: selfDetectionSchema,
    players: z.array(playerDetectionSchema).max(64),
    monsters: z.array(monsterDetectionSchema).max(128),
    loot: lootObservationSchema,
    hp: resourceObservationSchema,
    mp: resourceObservationSchema,
    map: mapObservationSchema,
    state: sessionStateSchema,
  })
  .strict()
  .superRefine((observation, context) => {
    const freshnessValues = [
      ['self', observation.self.freshUntilMonoMs],
      ...observation.players.map((item, index) => [`players.${index}`, item.freshUntilMonoMs] as const),
      ...observation.monsters.map((item, index) => [`monsters.${index}`, item.freshUntilMonoMs] as const),
      ['loot', observation.loot.freshUntilMonoMs],
      ['hp', observation.hp.freshUntilMonoMs],
      ['mp', observation.mp.freshUntilMonoMs],
      ['map', observation.map.freshUntilMonoMs],
    ] as const

    for (const [path, freshUntilMonoMs] of freshnessValues) {
      if (freshUntilMonoMs < observation.capturedAtMonoMs || freshUntilMonoMs - observation.capturedAtMonoMs > MAX_OBSERVATION_TTL_MS) {
        context.addIssue({ code: z.ZodIssueCode.custom, path: path.split('.'), message: 'observation freshness TTL is invalid' })
      }
    }
  })

export const overlaySnapshotSchema = z
  .object({
    schemaVersion,
    frameId: z.number().int().nonnegative(),
    generatedAtMonoMs: monoMs,
    selectedTargetId: z.string().min(1).max(128).nullable().optional(),
    modelVersion: z.string().min(1).max(128).optional(),
    self: selfDetectionSchema.optional(),
    players: z.array(playerDetectionSchema).max(64),
    monsters: z.array(monsterDetectionSchema).max(128),
  })
  .strict()

export const telemetrySnapshotSchema = z
  .object({
    schemaVersion,
    timestamp,
    captureFps: z.number().nonnegative(),
    renderFps: z.number().nonnegative(),
    recognitionFps: z.number().nonnegative(),
    frameLatencyMs: z.number().nonnegative(),
    detectorLatencyMs: z.number().nonnegative(),
    droppedFrames: z.number().int().nonnegative(),
    queueAgeMs: z.number().nonnegative(),
    processMemoryMb: z.number().nonnegative(),
    inferenceProvider: inferenceProviderSchema,
    captureBackend: captureBackendSchema,
    lastAction: z.string().max(128).nullable(),
    warningCode: z.string().max(128).nullable(),
    state: sessionStateSchema,
    pauseReason: pauseReasonSchema,
  })
  .strict()

export const combatRhythmSnapshotSchema = z.object({
  schemaVersion,
  cycleId: z.number().int().nonnegative(),
  phase: z.enum(['idle', 'attackHolding', 'moveLeft', 'moveRight', 'movementGap', 'resting']),
  sampledDurationMs: z.number().int().min(0).max(MAX_ATTACK_DURATION_MS),
  remainingMs: z.number().int().min(0).max(MAX_ATTACK_DURATION_MS),
  updatedAtMonoMs: monoMs,
  earlyReleaseReason: z.string().max(200).nullable().optional(),
}).strict()

const actionBase = {
  actionId: z.string().min(1).max(128),
  issuedAtMonoMs: monoMs,
  holdMs: z.number().int().min(0).max(MAX_ACTION_DURATION_MS),
  maxDurationMs: z.number().int().positive().max(MAX_ACTION_DURATION_MS),
}

export const abstractActionSchema = z
  .discriminatedUnion('type', [
    z.object({ ...actionBase, type: z.enum(['MoveLeft', 'MoveRight', 'Jump', 'ClimbUp', 'ClimbDown', 'Pickup']) }).strict(),
    z.object({ ...actionBase, type: z.literal('Attack'), profileId: z.enum(['singleAttack', 'areaAttack']) }).strict(),
    z.object({ ...actionBase, type: z.literal('UsePotion'), profileId: z.enum(['hpPotion', 'mpPotion']) }).strict(),
    z.object({ ...actionBase, type: z.enum(['Pause', 'Replan']) }).strict(),
  ])
  .superRefine((action, context) => {
    if (action.holdMs > action.maxDurationMs) {
      context.addIssue({ code: z.ZodIssueCode.custom, path: ['holdMs'], message: 'hold duration exceeds action limit' })
    }
    if (!['Pause', 'Replan'].includes(action.type) && action.holdMs < 10) {
      context.addIssue({ code: z.ZodIssueCode.custom, path: ['holdMs'], message: 'active actions require at least 10ms' })
    }
  })

export const actionPlanSchema = z
  .object({
    schemaVersion,
    planId: z.string().min(1).max(128),
    createdAtMonoMs: monoMs,
    actions: z.array(abstractActionSchema).max(16),
  })
  .strict()

export const inputResultSchema = z
  .object({
    schemaVersion,
    actionId: z.string().min(1).max(128),
    status: z.enum(['accepted', 'rejected', 'completed', 'cancelled', 'failed']),
    startedAtMonoMs: monoMs.optional(),
    endedAtMonoMs: monoMs.optional(),
    releasedKeys: z.array(z.string().min(1).max(32)).max(16),
    message: z.string().max(200).optional(),
  })
  .strict()

export const inputBrokerStatusSchema = z
  .object({
    provider: z.literal('inputBroker'),
    status: z.enum(['disconnected', 'starting', 'ready', 'paused', 'faulted']),
    integrity: z.enum(['unknown', 'medium', 'high']),
    activeKeys: z.array(z.string().min(1).max(32)).max(16),
    lastReleaseSucceeded: z.boolean(),
    hotkeys: z.object({
      pauseResume: z.enum(['F9', 'Ctrl+Shift+F9']),
      emergencyStop: z.enum(['F12', 'Ctrl+Shift+F12']),
    }).strict(),
    errorCode: z.string().min(1).max(64).nullable(),
  })
  .strict()

const commandEnvelope = { schemaVersion, timestamp: timestamp.optional() }
const emptyPayload = z.object({}).strict()

export const previewBoundsSchema = z.object({
  left: z.number().finite().min(0).max(10_000),
  top: z.number().finite().min(0).max(10_000),
  width: z.number().finite().min(320).max(10_000),
  height: z.number().finite().min(180).max(10_000),
  devicePixelRatio: z.number().finite().min(0.5).max(4),
}).strict()

export const visionStatusSchema = z.object({
  status: z.enum(['notConfigured', 'inspecting', 'ready', 'repairing', 'faulted']),
  modelId: z.string().max(128).nullable(),
  provider: inferenceProviderSchema,
  diagnostic: z.string().max(128).nullable(),
}).strict()

export const combatConfigurationSchema = z.object({
  schemaVersion,
  attackMode: z.enum(['single', 'auto', 'group']),
  hpThresholdMode: z.enum(['percent', 'absolute']),
  hpThreshold: z.number().finite().nonnegative(),
  mpThresholdMode: z.enum(['percent', 'absolute']),
  mpThreshold: z.number().finite().nonnegative(),
  singleAttackKey: z.string().min(1).max(32),
  areaAttackKey: z.string().min(1).max(32),
  hpPotionKey: z.string().min(1).max(32),
  mpPotionKey: z.string().min(1).max(32),
  jumpKey: z.string().min(1).max(32),
  pickupEnabled: z.boolean(),
  pickupKey: z.string().min(1).max(32),
  preferredDistancePx: z.number().int().min(20).max(500),
  areaTargetCount: z.number().int().min(2).max(20),
  switchCooldownMs: z.number().int().min(100).max(10_000),
}).strict().superRefine((configuration, context) => {
  if (configuration.hpThresholdMode === 'percent' && configuration.hpThreshold > 100) context.addIssue({ code: z.ZodIssueCode.custom, path: ['hpThreshold'], message: 'percentage must use 0..100' })
  if (configuration.mpThresholdMode === 'percent' && configuration.mpThreshold > 100) context.addIssue({ code: z.ZodIssueCode.custom, path: ['mpThreshold'], message: 'percentage must use 0..100' })
})

export const mapRuntimeStatusSchema = z.object({
  mapId: z.string().min(1).max(256),
  state: z.enum(['candidate', 'validated', 'archived']),
  coverage: z.number().min(0).max(1),
  calibrationErrorPx: z.number().nonnegative(),
  platformCount: z.number().int().nonnegative(),
  ladderCount: z.number().int().nonnegative(),
  errors: z.array(z.string().min(1).max(256)).max(128),
  canProduceActions: z.boolean(),
}).strict()
export const mapScanStatusSchema = z.object({ scanning: z.boolean(), frameIds: z.array(z.number().int().nonnegative()).max(256) }).strict()

export const emergencyStopCommandSchema = z
  .object({
    ...commandEnvelope,
    type: z.literal('session.emergencyStop'),
    payload: z.object({ message: z.string().trim().min(1).max(200) }).strict(),
  })
  .strict()

const uiCommandVariants = [
  z.object({ ...commandEnvelope, type: z.literal('snapshot.request'), payload: emptyPayload }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('session.arm'), payload: emptyPayload }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('session.pause'), payload: emptyPayload }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('session.resume'), payload: emptyPayload }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('combat.trial.start'), payload: emptyPayload }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('stationary.attack.set'), payload: z.object({ enabled: z.boolean() }).strict() }).strict(),
  emergencyStopCommandSchema,
  z.object({ ...commandEnvelope, type: z.literal('map.scan.start'), payload: emptyPayload }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('map.calibration.start'), payload: emptyPayload }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('map.calibration.confirm'), payload: z.object({ mapId: z.string().trim().min(1).max(256) }).strict() }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('preview.boundsChanged'), payload: previewBoundsSchema }).strict(),
  z.object({
    ...commandEnvelope,
    type: z.literal('input.test'),
    payload: z.object({
      kind: z.enum(['moveLeft', 'moveRight', 'climbUp', 'climbDown', 'jump', 'attack', 'pickup', 'hpPotion', 'mpPotion']),
      holdMs: z.number().int().min(50).max(600),
    }).strict(),
  }).strict(),
  z.object({
    ...commandEnvelope,
    type: z.literal('config.update'),
    payload: z.object({
      attackMode: z.enum(['single', 'auto', 'group']).optional(),
      hpThresholdMode: z.enum(['percent', 'absolute']).optional(),
      hpThreshold: z.number().nonnegative().optional(),
      mpThresholdMode: z.enum(['percent', 'absolute']).optional(),
      mpThreshold: z.number().nonnegative().optional(),
      attackKey: z.string().min(1).max(32).optional(),
      singleAttackKey: z.string().min(1).max(32).optional(),
      areaAttackKey: z.string().min(1).max(32).optional(),
      hpPotionKey: z.string().min(1).max(32).optional(),
      mpPotionKey: z.string().min(1).max(32).optional(),
      jumpKey: z.string().min(1).max(32).optional(),
      pickupEnabled: z.boolean().optional(),
      pickupKey: z.string().min(1).max(32).optional(),
      preferredDistancePx: z.number().int().min(20).max(500).optional(),
      areaTargetCount: z.number().int().min(2).max(20).optional(),
      switchCooldownMs: z.number().int().min(100).max(10_000).optional(),
    }).strict().superRefine((configuration, context) => {
      if (configuration.hpThresholdMode === 'percent' && configuration.hpThreshold !== undefined && configuration.hpThreshold > 100) {
        context.addIssue({ code: z.ZodIssueCode.custom, path: ['hpThreshold'], message: 'percentage must use the 0..100 bridge unit' })
      }
      if (configuration.mpThresholdMode === 'percent' && configuration.mpThreshold !== undefined && configuration.mpThreshold > 100) {
        context.addIssue({ code: z.ZodIssueCode.custom, path: ['mpThreshold'], message: 'percentage must use the 0..100 bridge unit' })
      }
    }),
  }).strict(),
  z.object({
    ...commandEnvelope,
    type: z.literal('cloud.credential.set'),
    payload: z.object({ apiKey: z.string().min(16).max(256).regex(/^\S+$/) }).strict(),
  }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('cloud.credential.clear'), payload: emptyPayload }).strict(),
  z.object({
    ...commandEnvelope,
    type: z.literal('cloud.config.update'),
    payload: z.object({
      enabled: z.boolean(),
      modelId: z.enum(['qwen3-vl-plus', 'qwen3-vl-flash', 'qwen-vl-max']),
      uploadConsent: z.boolean(),
    }).strict(),
  }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('cloud.connection.test'), payload: emptyPayload }).strict(),
  z.object({
    ...commandEnvelope,
    type: z.literal('cloud.map.annotate'),
    payload: z.object({
      mapId: z.string().min(1).max(256),
      sourceFrameIds: z.array(z.number().int().nonnegative()).min(1).max(4),
    }).strict(),
  }).strict(),
] as const

export const uiCommandSchema = z.discriminatedUnion('type', uiCommandVariants)

const hostEventVariants = [
  z.object({ ...commandEnvelope, type: z.literal('target.updated'), payload: targetBindingSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('capture.frameMetadata'), payload: captureFrameMetadataSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('overlay.updated'), payload: overlaySnapshotSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('observation.updated'), payload: observationSnapshotSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('telemetry.updated'), payload: telemetrySnapshotSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('combat.rhythm.updated'), payload: combatRhythmSnapshotSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('session.stateChanged'), payload: z.object({ state: sessionStateSchema, pauseReason: pauseReasonSchema, resumeCountdown: z.number().int().min(1).max(3).nullable().optional() }).strict() }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('input.result'), payload: inputResultSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('input.status.updated'), payload: inputBrokerStatusSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('log.appended'), payload: z.object({ level: z.enum(['debug', 'info', 'warn', 'error']), message: z.string().min(1).max(500), code: z.string().max(64).optional() }).strict() }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('preview.availabilityChanged'), payload: z.object({ available: z.boolean(), backend: z.enum(['native', 'browser-mock']).optional(), reason: z.string().max(200).optional() }).strict() }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('vision.status.updated'), payload: visionStatusSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('config.updated'), payload: combatConfigurationSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('map.status.updated'), payload: mapRuntimeStatusSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('map.scan.updated'), payload: mapScanStatusSchema }).strict(),
  z.object({
    ...commandEnvelope,
    type: z.literal('cloud.status.updated'),
    payload: z.object({
      provider: z.literal('bailian'),
      enabled: z.boolean(),
      credentialConfigured: z.boolean(),
      modelId: z.enum(['qwen3-vl-plus', 'qwen3-vl-flash', 'qwen-vl-max']),
      connectionStatus: z.enum(['notConfigured', 'checking', 'ready', 'unavailable']),
      requestInFlight: z.boolean(),
      lastErrorCode: z.string().max(64).nullable(),
    }).strict(),
  }).strict(),
] as const

export const hostEventSchema = z.discriminatedUnion('type', hostEventVariants)

export type TargetBinding = z.infer<typeof targetBindingSchema>
export type CaptureFrameMetadata = z.infer<typeof captureFrameMetadataSchema>
export type OverlaySnapshot = z.infer<typeof overlaySnapshotSchema>
export type ObservationSnapshot = z.infer<typeof observationSnapshotSchema>
export type TelemetrySnapshot = z.infer<typeof telemetrySnapshotSchema>
export type CombatRhythmSnapshot = z.infer<typeof combatRhythmSnapshotSchema>
export type SessionState = z.infer<typeof sessionStateSchema>
export type PauseReason = z.infer<typeof pauseReasonSchema>
export type AbstractAction = z.infer<typeof abstractActionSchema>
export type ActionPlan = z.infer<typeof actionPlanSchema>
export type InputResult = z.infer<typeof inputResultSchema>
export type InputBrokerStatus = z.infer<typeof inputBrokerStatusSchema>
export type PreviewBounds = z.infer<typeof previewBoundsSchema>
export type VisionStatus = z.infer<typeof visionStatusSchema>
export type CombatConfiguration = z.infer<typeof combatConfigurationSchema>
export type MapRuntimeStatus = z.infer<typeof mapRuntimeStatusSchema>
export type MapScanStatus = z.infer<typeof mapScanStatusSchema>
export type HostEvent = z.infer<typeof hostEventSchema>
export type UiCommand = z.infer<typeof uiCommandSchema>

export function validateObservationSnapshot(input: unknown) {
  return observationSnapshotSchema.safeParse(input)
}
