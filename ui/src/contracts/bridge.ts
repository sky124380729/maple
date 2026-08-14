import { z } from 'zod'

export const CONTRACT_SCHEMA_VERSION = 2 as const
export const MAX_OBSERVATION_TTL_MS = 5_000
export const MAX_ACTION_DURATION_MS = 5_000

const schemaVersion = z.literal(CONTRACT_SCHEMA_VERSION)
const monoMs = z.number().int().nonnegative()
const confidence = z.number().min(0).max(1)
const timestamp = z.string().datetime({ offset: true })

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
    captureBackend: z.enum(['WGC', 'BitBlt', 'PrintWindow']),
    captureDurationMs: z.number().nonnegative(),
    droppedReason: z.enum(['backpressure', 'occluded', 'invalid', 'none']).default('none'),
  })
  .strict()

export const lootObservationSchema = z
  .object({ visible: z.boolean(), confidence, freshUntilMonoMs: monoMs })
  .strict()

const resourceObservationSchema = z
  .object({ mode: z.literal('percent'), value: z.number().min(0).max(1), confidence, freshUntilMonoMs: monoMs })
  .strict()
  .or(z.object({ mode: z.literal('absolute'), value: z.number().nonnegative(), confidence, freshUntilMonoMs: monoMs }).strict())

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
    droppedFrames: z.number().int().nonnegative(),
    queueAgeMs: z.number().nonnegative(),
    state: sessionStateSchema,
    pauseReason: pauseReasonSchema,
  })
  .strict()

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

const commandEnvelope = { schemaVersion, timestamp: timestamp.optional() }
const emptyPayload = z.object({}).strict()

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
  emergencyStopCommandSchema,
  z.object({ ...commandEnvelope, type: z.literal('map.scan.start'), payload: emptyPayload }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('map.calibration.start'), payload: emptyPayload }).strict(),
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
      jumpKey: z.string().min(1).max(32).optional(),
      pickupEnabled: z.boolean().optional(),
      pickupKey: z.string().min(1).max(32).optional(),
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
  z.object({ ...commandEnvelope, type: z.literal('session.stateChanged'), payload: z.object({ state: sessionStateSchema, pauseReason: pauseReasonSchema }).strict() }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('input.result'), payload: inputResultSchema }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('log.appended'), payload: z.object({ level: z.enum(['debug', 'info', 'warn', 'error']), message: z.string().min(1).max(500), code: z.string().max(64).optional() }).strict() }).strict(),
  z.object({ ...commandEnvelope, type: z.literal('preview.availabilityChanged'), payload: z.object({ available: z.boolean(), backend: z.enum(['native', 'browser-mock']).optional(), reason: z.string().max(200).optional() }).strict() }).strict(),
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
export type SessionState = z.infer<typeof sessionStateSchema>
export type PauseReason = z.infer<typeof pauseReasonSchema>
export type AbstractAction = z.infer<typeof abstractActionSchema>
export type ActionPlan = z.infer<typeof actionPlanSchema>
export type InputResult = z.infer<typeof inputResultSchema>
export type HostEvent = z.infer<typeof hostEventSchema>
export type UiCommand = z.infer<typeof uiCommandSchema>

export function validateObservationSnapshot(input: unknown) {
  return observationSnapshotSchema.safeParse(input)
}
