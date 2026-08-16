import '@testing-library/jest-dom/vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, test, vi } from 'vitest'
import type { HostEvent, InputBrokerStatus } from '../../contracts/bridge'
import { createMockSessionEvents } from '../../mock/mockSession'
import { SessionControls } from './SessionControls'
import { defaultCombatConfiguration } from './combatConfiguration'

const ready: InputBrokerStatus = {
  provider: 'inputBroker',
  status: 'ready',
  integrity: 'high',
  activeKeys: [],
  lastReleaseSucceeded: true,
  hotkeys: { pauseResume: 'F9', emergencyStop: 'F12' },
  errorCode: null,
}

describe('SessionControls production input interaction', () => {
  test('starts continuous same-platform combat without exposing raw input', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Stopped" inputStatus={ready} configuration={defaultCombatConfiguration} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('button', { name: '开始自动运行' }))

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'combat.trial.start', payload: {} })
  })

  test('shows current and maximum resources together with percentages', () => {
    const observationEvent = createMockSessionEvents().find((event): event is Extract<HostEvent, { type: 'observation.updated' }> => event.type === 'observation.updated')!
    const observation = {
      ...observationEvent.payload,
      hp: { ...observationEvent.payload.hp, value: 66 / 98, currentValue: 66, maximumValue: 98 },
      mp: { ...observationEvent.payload.mp, value: 17 / 20, currentValue: 17, maximumValue: 20 },
    }

    render(<SessionControls sessionState="Stopped" inputStatus={ready} configuration={defaultCombatConfiguration} observation={observation} sendCommand={vi.fn()} />)

    expect(screen.getByText('66/98 · 67%')).toBeInTheDocument()
    expect(screen.getByText('17/20 · 85%')).toBeInTheDocument()
  })

  test('switches HP potion threshold between percent and absolute units', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Stopped" inputStatus={ready} configuration={defaultCombatConfiguration} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('radio', { name: 'HP 固定值' }))

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'config.update', payload: { hpThresholdMode: 'absolute' } })
  })
  test('starts combat from a paused session and shows native hotkeys', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Paused" inputStatus={ready} configuration={defaultCombatConfiguration} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('button', { name: '开始自动运行' }))

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'combat.trial.start', payload: {} })
    expect(screen.queryByRole('button', { name: '恢复并切回游戏' })).not.toBeInTheDocument()
    expect(screen.getByText('F9')).toBeInTheDocument()
    expect(screen.getByText('F12')).toBeInTheDocument()
  })

  test('keeps only start and stop controls when broker is faulted', () => {
    const faulted: InputBrokerStatus = { ...ready, status: 'faulted', integrity: 'unknown', errorCode: 'INPUT_BROKER_CONNECT_FAILED' }
    const { rerender } = render(<SessionControls sessionState="Stopped" inputStatus={{ ...ready, status: 'disconnected', integrity: 'unknown' }} configuration={defaultCombatConfiguration} sendCommand={vi.fn()} />)
    expect(screen.getByRole('button', { name: '开始自动运行' })).toBeEnabled()

    rerender(<SessionControls sessionState="Paused" inputStatus={faulted} configuration={defaultCombatConfiguration} sendCommand={vi.fn()} />)
    expect(screen.getByRole('button', { name: '开始自动运行' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: '开始运行' })).not.toBeInTheDocument()
  })

  test('keeps continuous combat available as a retry entry when broker is faulted', () => {
    const sendCommand = vi.fn()
    const faulted: InputBrokerStatus = { ...ready, status: 'faulted', integrity: 'unknown', errorCode: 'INPUT_BROKER_CONNECT_FAILED' }
    render(<SessionControls sessionState="Stopped" inputStatus={faulted} configuration={defaultCombatConfiguration} sendCommand={sendCommand} />)

    const trial = screen.getByRole('button', { name: '开始自动运行' })
    expect(trial).toBeEnabled()
    fireEvent.click(trial)

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'combat.trial.start', payload: {} })
  })

  test('allows lazy start while disconnected but blocks duplicate start while starting', () => {
    const { rerender } = render(<SessionControls sessionState="Stopped" inputStatus={{ ...ready, status: 'disconnected', integrity: 'unknown' }} configuration={defaultCombatConfiguration} sendCommand={vi.fn()} />)
    expect(screen.getByRole('button', { name: '开始自动运行' })).toBeEnabled()

    rerender(<SessionControls sessionState="Stopped" inputStatus={{ ...ready, status: 'starting', integrity: 'unknown' }} configuration={defaultCombatConfiguration} sendCommand={vi.fn()} />)
    expect(screen.getByRole('button', { name: '开始自动运行' })).toBeDisabled()
  })

  test('pauses and releases before applying a setting while running', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Observing" inputStatus={ready} configuration={defaultCombatConfiguration} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('radio', { name: '自动' }))

    expect(sendCommand.mock.calls.map(([command]) => command.type)).toEqual(['session.pause', 'config.update'])
    expect(screen.getByRole('status')).toHaveTextContent('修改设置时已暂停')
  })

  test('pauses before opening the key editor and submits logical keys only', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Observing" inputStatus={ready} configuration={defaultCombatConfiguration} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('button', { name: '编辑按键配置' }))
    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'session.pause', payload: {} })
    expect(screen.getByRole('dialog', { name: '按键配置' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '保存配置' }))

    const submitted = sendCommand.mock.calls.at(-1)?.[0]
    expect(submitted.type).toBe('config.update')
    expect(submitted.payload).toMatchObject({
      singleAttackKey: 'Ctrl', areaAttackKey: 'Ctrl',
      hpPotionKey: 'Delete', mpPotionKey: 'End',
      jumpKey: 'Alt', pickupKey: 'Z',
    })
    expect(submitted.payload).not.toHaveProperty('scanCode')
  })

  test('shows the observed map lifecycle instead of a hard-coded validated map', () => {
    const observationEvent = createMockSessionEvents().find((event): event is Extract<HostEvent, { type: 'observation.updated' }> => event.type === 'observation.updated')!
    const observation = { ...observationEvent.payload, map: { ...observationEvent.payload.map, mapId: '彩虹岛-蜗牛打猎场', state: 'candidate' as const } }
    render(<SessionControls sessionState="Stopped" inputStatus={ready} configuration={defaultCombatConfiguration} observation={observation} sendCommand={vi.fn()} />)

    expect(screen.getByText('彩虹岛-蜗牛打猎场')).toBeInTheDocument()
    expect(screen.getAllByText('待标定').length).toBeGreaterThan(0)
    expect(screen.queryByText('森林东部')).not.toBeInTheDocument()
  })

  test('only offers map confirmation for a locally valid candidate', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Stopped" inputStatus={ready} configuration={defaultCombatConfiguration} mapStatus={{ mapId: 'forest-east', state: 'candidate', coverage: 0.92, calibrationErrorPx: 2, platformCount: 8, ladderCount: 3, errors: [], canProduceActions: false }} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('button', { name: '确认标定并启用' }))

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'map.calibration.confirm', payload: { mapId: 'forest-east' } })
  })

  test('submits a bounded abstract input test from the diagnostics matrix', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Stopped" inputStatus={ready} configuration={defaultCombatConfiguration} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('button', { name: '测试跳跃' }))

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'input.test', payload: { kind: 'jump', holdMs: 90 } })
  })
})
