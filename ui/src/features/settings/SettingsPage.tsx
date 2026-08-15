import { CheckCircleFilled, CloudOutlined, DeleteOutlined, KeyOutlined } from '@ant-design/icons'
import { Button, Checkbox, Input, Select, Space, Switch, Typography } from 'antd'
import { useMemo, useState } from 'react'
import type { HostEvent, UiCommand } from '../../contracts/bridge'

const { Text, Title } = Typography
type CloudStatus = Extract<HostEvent, { type: 'cloud.status.updated' }>['payload']
type BailianModelId = CloudStatus['modelId']

export interface SettingsPageProps {
  cloudStatus: CloudStatus
  pausedForSettings?: boolean
  sendCommand(command: UiCommand): void
}

const modelOptions = [
  { label: 'Qwen3 VL Plus', value: 'qwen3-vl-plus' },
  { label: 'Qwen3 VL Flash', value: 'qwen3-vl-flash' },
  { label: 'Qwen VL Max', value: 'qwen-vl-max' },
] satisfies { label: string; value: BailianModelId }[]

const statusLabels: Record<CloudStatus['connectionStatus'], string> = {
  notConfigured: '未配置',
  checking: '检查中',
  ready: '可用',
  unavailable: '不可用',
}

export function SettingsPage({ cloudStatus, pausedForSettings = false, sendCommand }: SettingsPageProps) {
  const [credential, setCredential] = useState('')
  const [uploadConsent, setUploadConsent] = useState(false)

  const credentialValid = useMemo(
    () => credential.length >= 16 && credential.length <= 256 && !/\s/.test(credential),
    [credential],
  )
  const cloudControlsDisabled = !cloudStatus.credentialConfigured || cloudStatus.requestInFlight

  const updateCloudConfiguration = (enabled: boolean, nextModelId = cloudStatus.modelId) => {
    sendCommand({
      schemaVersion: 2,
      type: 'cloud.config.update',
      payload: { enabled, modelId: nextModelId, uploadConsent },
    })
  }

  const saveCredential = () => {
    if (!credentialValid) return
    sendCommand({ schemaVersion: 2, type: 'cloud.credential.set', payload: { apiKey: credential } })
    setCredential('')
  }

  return (
    <section className="settings-sheet" aria-labelledby="settings-title">
      <div className="settings-sheet__heading">
        <span className="settings-sheet__icon"><CloudOutlined /></span>
        <div>
          <Text className="settings-sheet__eyebrow">云端视觉</Text>
          <Title id="settings-title" level={3}>百炼</Title>
        </div>
        <span className={`cloud-state cloud-state--${cloudStatus.connectionStatus}`}>
          {cloudStatus.connectionStatus === 'ready' && <CheckCircleFilled />}
          {statusLabels[cloudStatus.connectionStatus]}
        </span>
      </div>

      {pausedForSettings && <div className="settings-pause-notice" role="status">修改设置时已暂停</div>}

      <div className="settings-row settings-row--split">
        <div><Text className="settings-label">启用服务</Text><Text className="settings-value">地图结构复核</Text></div>
        <Switch
          aria-label="启用百炼视觉"
          checked={cloudStatus.enabled}
          disabled={cloudControlsDisabled || !uploadConsent}
          loading={cloudStatus.requestInFlight}
          onChange={(checked) => updateCloudConfiguration(checked)}
        />
      </div>

      <div className="settings-row">
        <Text className="settings-label">视觉模型</Text>
        <Select<BailianModelId>
          aria-label="百炼视觉模型"
          value={cloudStatus.modelId}
          options={modelOptions}
          onChange={(nextModel) => {
            updateCloudConfiguration(cloudStatus.enabled, nextModel)
          }}
        />
      </div>

      <div className="settings-row">
        <Text className="settings-label"><KeyOutlined /> API Key</Text>
        <Space.Compact block>
          <Input.Password
            aria-label="百炼 API Key"
            autoComplete="off"
            value={credential}
            visibilityToggle={false}
            maxLength={256}
            onChange={(event) => setCredential(event.target.value)}
            onPressEnter={saveCredential}
          />
          <Button type="primary" aria-label="保存密钥" disabled={!credentialValid} onClick={saveCredential}>保存</Button>
        </Space.Compact>
        <Text className="credential-state">{cloudStatus.credentialConfigured ? '密钥已保存' : '未保存密钥'}</Text>
      </div>

      <Checkbox
        aria-label="允许上传地图截图"
        checked={uploadConsent}
        onChange={(event) => setUploadConsent(event.target.checked)}
      >允许上传地图截图</Checkbox>

      <div className="settings-actions">
        <Button
          aria-label="测试连接"
          loading={cloudStatus.connectionStatus === 'checking'}
          disabled={cloudControlsDisabled}
          onClick={() => sendCommand({ schemaVersion: 2, type: 'cloud.connection.test', payload: {} })}
        >测试连接</Button>
        <Button
          aria-label="清除密钥"
          danger
          type="text"
          icon={<DeleteOutlined />}
          disabled={!cloudStatus.credentialConfigured || cloudStatus.requestInFlight}
          onClick={() => sendCommand({ schemaVersion: 2, type: 'cloud.credential.clear', payload: {} })}
        >清除密钥</Button>
      </div>
    </section>
  )
}
