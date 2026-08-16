import { describe, expect, test } from 'vitest'
import type { OverlaySnapshot } from '../../contracts/bridge'
import { buildOverlayRenderItems, formatCanvasOverlayLabel } from './overlay'

const snapshot: OverlaySnapshot = {
  schemaVersion: 2,
  frameId: 42,
  generatedAtMonoMs: 10_000,
  selectedTargetId: 'monster-12',
  modelVersion: 'snail-v1',
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
    expect(items[1]).toMatchObject({ color: '#55c7f7', label: '玩家 81%' })
    expect(items[2]).toMatchObject({ color: '#ff6474', label: '怪 88%' })
    expect(items.map((item) => item.selected)).toEqual([false, false, true])
  })

  test('hides detections after their frame freshness TTL', () => {
    const items = buildOverlayRenderItems(snapshot, 10_200)

    expect(items).toHaveLength(0)
  })

  test('uses short canvas labels on narrow previews while preserving the legend label', () => {
    const items = buildOverlayRenderItems(snapshot, 10_100)

    expect(items.map((item) => formatCanvasOverlayLabel(item, true))).toEqual(['自己 94%', '玩家 81%', '怪 88%'])
    expect(items[1].label).toBe('玩家 81%')
  })
})
