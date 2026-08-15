import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import type { OverlaySnapshot } from '../../contracts/bridge'
import { MockPreviewCanvas } from './MockPreviewCanvas'

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

describe('MockPreviewCanvas', () => {
  test('exposes a deterministic preview canvas and accessible overlay legend', () => {
    render(<MockPreviewCanvas snapshot={snapshot} nowMonoMs={10_100} />)

    expect(screen.getByLabelText('实时模拟预览画布')).toBeInTheDocument()
    expect(screen.getByText('自己 94%')).toBeInTheDocument()
    expect(screen.getByText('其他玩家 81% #player-7')).toBeInTheDocument()
    expect(screen.getByText('蜗牛 88% #monster-12')).toBeInTheDocument()
    expect(screen.getByText('攻击目标')).toBeInTheDocument()
  })

  test('does not expose stale overlay labels', () => {
    render(<MockPreviewCanvas snapshot={snapshot} nowMonoMs={10_200} />)

    expect(screen.getByLabelText('实时模拟预览画布')).toBeInTheDocument()
    expect(screen.queryByText('自己 94%')).not.toBeInTheDocument()
    expect(screen.queryByText('其他玩家 81% #player-7')).not.toBeInTheDocument()
    expect(screen.queryByText('蜗牛 88% #monster-12')).not.toBeInTheDocument()
  })
})
