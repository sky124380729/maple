import '@testing-library/jest-dom/vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, test, vi } from 'vitest'
import type { HostEvent, UiCommand } from '../../contracts/bridge'
import { SettingsPage } from './SettingsPage'

type CloudStatus = Extract<HostEvent, { type: 'cloud.status.updated' }>['payload']

const status = (overrides: Partial<CloudStatus> = {}): CloudStatus => ({
  provider: 'bailian',
  enabled: false,
  credentialConfigured: false,
  modelId: 'qwen3-vl-plus',
  connectionStatus: 'notConfigured',
  requestInFlight: false,
  lastErrorCode: null,
  ...overrides,
})

describe('百炼视觉设置', () => {
  test('keeps enable and connection test unavailable before a credential is saved', () => {
    render(<SettingsPage cloudStatus={status()} sendCommand={vi.fn()} />)

    expect(screen.getByRole('switch', { name: '启用百炼视觉' })).toBeDisabled()
    expect(screen.getByRole('button', { name: '测试连接' })).toBeDisabled()
    expect(screen.getByLabelText('百炼 API Key')).toHaveAttribute('type', 'password')
    expect(screen.queryByText(/Base URL|system prompt|temperature/i)).not.toBeInTheDocument()
  })

  test('submits the credential once and clears the password input', () => {
    const sendCommand = vi.fn<(command: UiCommand) => void>()
    const credential = 'abcdefghijklmnop'
    render(<SettingsPage cloudStatus={status()} sendCommand={sendCommand} />)

    const input = screen.getByLabelText('百炼 API Key')
    fireEvent.change(input, { target: { value: credential } })
    fireEvent.click(screen.getByRole('button', { name: '保存密钥' }))

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'cloud.credential.set', payload: { apiKey: credential } })
    expect(input).toHaveValue('')
  })

  test('requires upload consent before enabling cloud vision', () => {
    const sendCommand = vi.fn<(command: UiCommand) => void>()
    render(<SettingsPage cloudStatus={status({ credentialConfigured: true })} sendCommand={sendCommand} />)

    const enable = screen.getByRole('switch', { name: '启用百炼视觉' })
    expect(enable).toBeDisabled()
    fireEvent.click(screen.getByRole('checkbox', { name: '允许上传地图截图' }))
    expect(enable).toBeEnabled()
    fireEvent.click(enable)

    expect(sendCommand).toHaveBeenCalledWith({
      schemaVersion: 2,
      type: 'cloud.config.update',
      payload: { enabled: true, modelId: 'qwen3-vl-plus', uploadConsent: true },
    })
  })

  test('can test and clear an existing credential without showing it', () => {
    const sendCommand = vi.fn<(command: UiCommand) => void>()
    render(<SettingsPage cloudStatus={status({ credentialConfigured: true, connectionStatus: 'ready' })} sendCommand={sendCommand} />)

    expect(screen.getByText('可用')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '测试连接' }))
    fireEvent.click(screen.getByRole('button', { name: '清除密钥' }))

    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'cloud.connection.test', payload: {} })
    expect(sendCommand).toHaveBeenCalledWith({ schemaVersion: 2, type: 'cloud.credential.clear', payload: {} })
    expect(screen.queryByDisplayValue('abcdefghijklmnop')).not.toBeInTheDocument()
  })
})
