import assert from 'node:assert/strict'

const settings = {
  clientWidthPx: 1280,
  attackRangePx: 80,
  observedSpeedPxPerSecond: 320,
  minMoveHoldMs: 60,
  maxMoveHoldMs: 400,
  selfConfidenceThreshold: 0.9,
  targetConfidenceThreshold: 0.8,
  hpPotionThreshold: 0.35,
  mpPotionThreshold: 0.3,
}

const centerX = ([x, , width]) => x + width / 2
const activeKeys = new Set()
const inputTrace = []

function keyDown(action, key, nowMonoMs) {
  activeKeys.add(key)
  inputTrace.push({ action: action.type, profileId: action.profileId ?? null, phase: 'KeyDown', key, at: nowMonoMs })
}

function keyUp(action, key, nowMonoMs) {
  activeKeys.delete(key)
  inputTrace.push({ action: action.type, profileId: action.profileId ?? null, phase: 'KeyUp', key, at: nowMonoMs })
}

function press(action, key, nowMonoMs) {
  keyDown(action, key, nowMonoMs)
  keyUp(action, key, nowMonoMs + action.holdMs)
}

function releaseAll(nowMonoMs) {
  for (const key of activeKeys) inputTrace.push({ action: 'Pause', profileId: null, phase: 'KeyUp', key, at: nowMonoMs })
  activeKeys.clear()
}

function decide(observation, nowMonoMs) {
  if (!observation.windowForeground) return { type: 'Pause', reason: 'WindowNotForeground' }
  if (observation.freshUntilMonoMs < nowMonoMs) return { type: 'Pause', reason: 'StaleFrame' }
  if (observation.self.confidence < settings.selfConfidenceThreshold) return { type: 'Pause', reason: 'CalibrationRequired' }
  if (observation.mapState !== 'validated') return { type: 'Pause', reason: 'MapNotValidated' }
  if (!observation.inputHealthy) return { type: 'Pause', reason: 'InputUnavailable' }
  if (observation.hp <= settings.hpPotionThreshold) return { type: 'UsePotion', profileId: 'hpPotion', holdMs: 100 }
  if (observation.mp <= settings.mpPotionThreshold) return { type: 'UsePotion', profileId: 'mpPotion', holdMs: 100 }

  const validMonsters = observation.monsters.filter((monster) => monster.confidence >= settings.targetConfidenceThreshold && monster.freshUntilMonoMs >= nowMonoMs)
  if (validMonsters.length === 0) return { type: 'Pause', reason: 'TargetLost' }
  const selfCenter = centerX(observation.self.box)
  const target = validMonsters.sort((left, right) => Math.abs(centerX(left.box) - selfCenter) - Math.abs(centerX(right.box) - selfCenter))[0]
  const distancePx = (centerX(target.box) - selfCenter) * settings.clientWidthPx

  if (Math.abs(distancePx) <= settings.attackRangePx) return { type: 'Attack', profileId: validMonsters.length >= 3 ? 'areaAttack' : 'singleAttack', holdMs: 80 }

  const travelPx = Math.max(0, Math.abs(distancePx) - settings.attackRangePx)
  const holdMs = Math.max(settings.minMoveHoldMs, Math.min(settings.maxMoveHoldMs, Math.round(travelPx / settings.observedSpeedPxPerSecond * 1000)))
  return { type: distancePx < 0 ? 'MoveLeft' : 'MoveRight', holdMs }
}

function observation(overrides = {}) {
  return {
    frameId: 1,
    freshUntilMonoMs: 1_200,
    windowForeground: true,
    inputHealthy: true,
    mapState: 'validated',
    self: { box: [0.2, 0.5, 0.08, 0.18], confidence: 0.96 },
    players: [{ box: [0.22, 0.5, 0.08, 0.18], confidence: 0.99, trackId: 'player-1' }],
    monsters: [{ box: [0.7, 0.5, 0.08, 0.18], confidence: 0.94, freshUntilMonoMs: 1_200, targetId: 'monster-1' }],
    hp: 0.9,
    mp: 0.8,
    ...overrides,
  }
}

const move = decide(observation(), 1_000)
assert.equal(move.type, 'MoveRight')
assert.equal(move.holdMs, 400)
keyDown(move, 'Right', 1_000)
assert.equal(activeKeys.has('Right'), true)

const inRangeObservation = observation({ frameId: 2, self: { box: [0.645, 0.5, 0.08, 0.18], confidence: 0.97 } })
keyUp(move, 'Right', 1_080)
assert.equal(inputTrace.at(-1).at < 1_000 + move.holdMs, true)
assert.equal(activeKeys.size, 0)

const attack = decide(inRangeObservation, 1_120)
assert.deepEqual(attack, { type: 'Attack', profileId: 'singleAttack', holdMs: 80 })
press(attack, 'J', 1_120)

const targetGone = decide(observation({ frameId: 3, freshUntilMonoMs: 1_400, monsters: [] }), 1_220)
assert.deepEqual(targetGone, { type: 'Pause', reason: 'TargetLost' })
assert.equal(activeKeys.size, 0)

const lowConfidence = decide(observation({ self: { box: [0.2, 0.5, 0.08, 0.18], confidence: 0.4 } }), 1_000)
assert.deepEqual(lowConfidence, { type: 'Pause', reason: 'CalibrationRequired' })

const hpPriority = decide(observation({ hp: 0.2, mp: 0.1 }), 1_000)
assert.deepEqual(hpPriority, { type: 'UsePotion', profileId: 'hpPotion', holdMs: 100 })

const stale = decide(observation({ freshUntilMonoMs: 999 }), 1_000)
assert.deepEqual(stale, { type: 'Pause', reason: 'StaleFrame' })

const interruptedMove = { type: 'MoveLeft', holdMs: 400 }
keyDown(interruptedMove, 'Left', 1_300)
releaseAll(1_320)
assert.equal(activeKeys.size, 0)
assert.equal(inputTrace.filter((event) => event.phase === 'KeyDown').length, 3)
assert.equal(inputTrace.filter((event) => event.phase === 'KeyUp').length, 3)
assert.equal(inputTrace.some((event) => event.action === 'Attack' && event.profileId === 'singleAttack'), true)

console.log('PORTABLE_CLOSED_LOOP_SPEC_ORACLE=PASS')
console.log('PRODUCTION_RUNTIME_CLOSED_LOOP=NOT_VERIFIED')
