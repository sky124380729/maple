import '@testing-library/jest-dom/vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, test, vi } from 'vitest'
import type { InputBrokerStatus } from '../../contracts/bridge'
import { SessionControls } from './SessionControls'

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
  test('resumes a paused ready session and shows native hotkeys', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Paused" inputStatus={ready} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('button', { name: '恢复并切回游戏' }))

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'session.resume', payload: {} })
    expect(screen.getByText('F9')).toBeInTheDocument()
    expect(screen.getByText('F12')).toBeInTheDocument()
  })

  test('shows start label and disables resume when broker is faulted', () => {
    const faulted: InputBrokerStatus = { ...ready, status: 'faulted', integrity: 'unknown', errorCode: 'INPUT_BROKER_CONNECT_FAILED' }
    const { rerender } = render(<SessionControls sessionState="Stopped" inputStatus={{ ...ready, status: 'disconnected', integrity: 'unknown' }} sendCommand={vi.fn()} />)
    expect(screen.getByRole('button', { name: '开始运行' })).toBeEnabled()

    rerender(<SessionControls sessionState="Paused" inputStatus={faulted} sendCommand={vi.fn()} />)
    expect(screen.getByRole('button', { name: '恢复并切回游戏' })).toBeDisabled()
  })

  test('allows lazy start while disconnected but blocks duplicate start while starting', () => {
    const { rerender } = render(<SessionControls sessionState="Stopped" inputStatus={{ ...ready, status: 'disconnected', integrity: 'unknown' }} sendCommand={vi.fn()} />)
    expect(screen.getByRole('button', { name: '开始运行' })).toBeEnabled()

    rerender(<SessionControls sessionState="Stopped" inputStatus={{ ...ready, status: 'starting', integrity: 'unknown' }} sendCommand={vi.fn()} />)
    expect(screen.getByRole('button', { name: '开始运行' })).toBeDisabled()
  })

  test('pauses and releases before applying a setting while running', () => {
    const sendCommand = vi.fn()
    render(<SessionControls sessionState="Observing" inputStatus={ready} sendCommand={sendCommand} />)

    fireEvent.click(screen.getByRole('radio', { name: '单体' }))

    expect(sendCommand.mock.calls.map(([command]) => command.type)).toEqual(['session.pause', 'config.update'])
    expect(screen.getByRole('status')).toHaveTextContent('修改设置时已暂停')
  })
})
