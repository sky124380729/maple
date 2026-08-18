import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import type { CombatRhythmSnapshot } from '../../contracts/bridge'
import { RhythmCountdown } from './RhythmCountdown'

const attack: CombatRhythmSnapshot = {
  schemaVersion: 2,
  cycleId: 7,
  phase: 'attackHolding',
  sampledDurationMs: 26_430,
  remainingMs: 18_620,
  updatedAtMonoMs: 120_000,
  earlyReleaseReason: null,
}

describe('RhythmCountdown', () => {
  test('shows the sampled attack hold and authoritative remaining time', () => {
    render(<RhythmCountdown rhythm={attack} sessionState="Attacking" />)

    expect(screen.getByText('攻击键按住中')).toBeInTheDocument()
    expect(screen.getByText('本轮 26.43 秒')).toBeInTheDocument()
    expect(screen.getByText('剩余 18.62 秒')).toBeInTheDocument()
  })

  test.each([
    ['moveLeft', '左移'],
    ['moveRight', '右移'],
    ['movementGap', '动作间隔'],
    ['resting', '休息中'],
  ] as const)('labels the %s phase', (phase, label) => {
    render(<RhythmCountdown rhythm={{ ...attack, phase }} sessionState="Attacking" />)

    expect(screen.getByText(label)).toBeInTheDocument()
  })

  test('shows paused state without an old countdown', () => {
    render(<RhythmCountdown sessionState="Paused" />)

    expect(screen.getByText('已暂停')).toBeInTheDocument()
    expect(screen.queryByText(/剩余/)).not.toBeInTheDocument()
  })
})
