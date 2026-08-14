import { describe, expect, test } from 'vitest'
import type { OverlaySnapshot } from '../../contracts/bridge'
import { buildOverlayRenderItems } from './overlay'

const snapshot: OverlaySnapshot = {
  schemaVersion: 2,
  frameId: 42,
  generatedAtMonoMs: 10_000,
  self: { box: [0.4, 0.5, 0.08, 0.18], confidence: 0.94, freshUntilMonoMs: 10_200 },
  players: [{ box: [0.2, 0.5, 0.08, 0.18], confidence: 0.81, freshUntilMonoMs: 10_200, trackId: 'player-7' }],
  monsters: [{ class: '蜗牛', box: [0.66, 0.54, 0.07, 0.13], confidence: 0.88, freshUntilMonoMs: 10_200, targetId: 'monster-12' }],
}

describe('preview overlay semantics', () => {
  test('renders Self, player and monster with fixed colors and labels', () => {
    const items = buildOverlayRenderItems(snapshot, 10_100)

    expect(items).toHaveLength(3)
    expect(items.map((item) => item.kind)).toEqual(['self', 'player', 'monster'])
    expect(items[0]).toMatchObject({ color: '#42d392', label: '自己 94%' })
    expect(items[1]).toMatchObject({ color: '#55c7f7', label: '其他玩家 81% #player-7' })
    expect(items[2]).toMatchObject({ color: '#ff6474', label: '蜗牛 88% #monster-12' })
  })

  test('hides detections after their frame freshness TTL', () => {
    const items = buildOverlayRenderItems(snapshot, 10_200)

    expect(items).toHaveLength(0)
  })
})
