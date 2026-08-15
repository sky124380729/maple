import { ControlOutlined, EnvironmentOutlined, KeyOutlined, PauseCircleOutlined, PlayCircleFilled, RightOutlined, SettingOutlined } from '@ant-design/icons'
import { Button, Divider, InputNumber, Segmented, Space, Switch, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import type { InputBrokerStatus, SessionState, UiCommand } from '../../contracts/bridge'
import { SectionHeading, StatusPill } from './presentation'

const { Text } = Typography

export interface SessionControlsProps {
  sessionState: SessionState
  inputStatus: InputBrokerStatus
  resumeCountdown?: number | null
  sendCommand(command: UiCommand): void
}

export function SessionControls({ sessionState, inputStatus, resumeCountdown, sendCommand }: SessionControlsProps) {
  const [attackMode, setAttackMode] = useState<'single' | 'auto' | 'group'>('auto')
  const [hpThreshold, setHpThreshold] = useState<number | null>(35)
  const [pickupEnabled, setPickupEnabled] = useState(false)
  const [settingsPaused, setSettingsPaused] = useState(false)
  const stopped = sessionState === 'Stopped'
  const paused = sessionState === 'Paused'
  const arming = sessionState === 'Arming'
  const emergency = sessionState === 'EmergencyStop'
  const running = !stopped && !paused && !arming && !emergency
  const brokerUnavailable = inputStatus.status === 'faulted' || inputStatus.status === 'starting'
  const primaryLabel = stopped ? '开始运行' : paused ? '恢复并切回游戏' : arming ? (resumeCountdown ? `${resumeCountdown} 秒后切回游戏` : '正在切回游戏') : '运行中'
  const brokerLabel = inputStatus.status === 'ready' ? '输入服务已就绪' : inputStatus.status === 'starting' ? '输入服务连接中' : inputStatus.status === 'paused' ? '输入服务已暂停' : inputStatus.status === 'faulted' ? '输入服务异常' : '输入服务待连接'

  const updateConfig = (payload: Extract<UiCommand, { type: 'config.update' }>['payload']) => {
    if (!stopped && !paused) {
      sendCommand({ schemaVersion: 2, type: 'session.pause', payload: {} })
    }
    if (!stopped) setSettingsPaused(true)
    sendCommand({ schemaVersion: 2, type: 'config.update', payload })
  }

  const startOrResume = () => sendCommand({
    schemaVersion: 2,
    type: paused ? 'session.resume' : 'session.arm',
    payload: {},
  })

  return (
    <aside className="side-panel side-panel--controls">
      <div className="side-panel__scroll">
        <SectionHeading icon={<ControlOutlined />} kicker="工作区" title="运行控制" action={<StatusPill state={sessionState} />} />
        <div className="control-hero">
          <div className="control-hero__topline"><span className="eyebrow">当前模式</span><span className="live-mark"><i />实时</span></div>
          <div className="control-hero__mode">自动运行</div>
          <Text className="control-hero__copy">{brokerLabel}</Text>
        </div>
        <Space orientation="vertical" size={10} className="control-actions">
          <Button className="action-button action-button--primary" type="primary" aria-label={primaryLabel} icon={<PlayCircleFilled />} onClick={startOrResume} disabled={emergency || arming || running || brokerUnavailable} loading={arming} block>{primaryLabel}</Button>
          <Button className="action-button action-button--secondary" aria-label="暂停并释放按键" icon={<PauseCircleOutlined />} onClick={() => sendCommand({ schemaVersion: 2, type: 'session.pause', payload: {} })} disabled={emergency || stopped || paused} block>暂停并释放按键</Button>
        </Space>
        <div className="hotkey-strip" aria-label="全局快捷键"><span><kbd>{inputStatus.hotkeys.pauseResume}</kbd> 暂停/恢复</span><span><kbd>{inputStatus.hotkeys.emergencyStop}</kbd> 紧急停止</span></div>
        {settingsPaused && <div className="settings-pause-notice" role="status">修改设置时已暂停</div>}
        <Divider className="section-divider" />
        <section className="settings-section" aria-labelledby="combat-settings-title">
          <div className="section-label-row"><Text id="combat-settings-title" className="section-label">战斗设置</Text><Tooltip title="动作时长由程序根据距离和画面自动计算"><SettingOutlined className="section-label__icon" /></Tooltip></div>
          <div className="setting-field">
            <div><Text className="field-title">攻击策略</Text><Text className="field-hint">按目标距离自动规划</Text></div>
            <Segmented aria-label="攻击策略" className="mode-segmented" value={attackMode} onChange={(value) => { const next = value as 'single' | 'auto' | 'group'; setAttackMode(next); updateConfig({ attackMode: next }) }} options={[{ label: '单体', value: 'single' }, { label: '自动', value: 'auto' }, { label: '群攻', value: 'group' }]} />
          </div>
          <div className="setting-field setting-field--inline">
            <div><Text className="field-title">生命值下限</Text><Text className="field-hint">低于此值时暂停动作</Text></div>
            <InputNumber aria-label="生命值下限百分比" min={1} max={100} value={hpThreshold} onChange={(value) => { setHpThreshold(value); if (value !== null) updateConfig({ hpThresholdMode: 'percent', hpThreshold: value }) }} suffix="%" controls={false} />
          </div>
          <div className="setting-field setting-field--inline"><div><Text className="field-title">自动拾取</Text><Text className="field-hint">检测到掉落物时执行</Text></div><Switch aria-label="自动拾取" checked={pickupEnabled} onChange={(checked) => { setPickupEnabled(checked); updateConfig({ pickupEnabled: checked }) }} /></div>
        </section>
        <section className="settings-section" aria-labelledby="profile-title">
          <div className="section-label-row"><Text id="profile-title" className="section-label">地图档案</Text><Text className="section-label__meta">已验证</Text></div>
          <button className="map-profile" type="button" aria-label="打开地图档案"><span className="map-profile__icon"><EnvironmentOutlined /></span><span className="map-profile__text"><strong>森林东部</strong><small>地图版本 0.1 · 自动加载</small></span><RightOutlined className="map-profile__arrow" /></button>
        </section>
        <section className="settings-section" aria-labelledby="keys-title">
          <div className="section-label-row"><Text id="keys-title" className="section-label">按键配置</Text><Button type="text" size="small" icon={<KeyOutlined />} className="compact-link">编辑</Button></div>
          <div className="key-grid" aria-label="按键配置列表"><div><small>移动</small><strong>方向键</strong></div><div><small>跳跃</small><strong>Alt</strong></div><div><small>拾取</small><strong>Z</strong></div></div>
        </section>
      </div>
    </aside>
  )
}
