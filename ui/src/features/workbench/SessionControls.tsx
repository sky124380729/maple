import { ControlOutlined, EnvironmentOutlined, KeyOutlined, PauseCircleOutlined, PlayCircleFilled, RightOutlined, SettingOutlined } from '@ant-design/icons'
import { Button, Divider, InputNumber, Segmented, Space, Switch, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import type { CombatConfiguration, CombatRhythmSnapshot, InputBrokerStatus, InputResult, MapRuntimeStatus, ObservationSnapshot, SessionState, UiCommand } from '../../contracts/bridge'
import { KeyBindingEditor } from './KeyBindingEditor'
import { SectionHeading, StatusPill } from './presentation'

const { Text } = Typography

export interface SessionControlsProps {
  sessionState: SessionState
  inputStatus: InputBrokerStatus
  lastInputResult?: InputResult
  resumeCountdown?: number | null
  configuration: CombatConfiguration
  observation?: ObservationSnapshot
  mapStatus?: MapRuntimeStatus
  rhythm?: CombatRhythmSnapshot
  onOpenMap?(): void
  sendCommand(command: UiCommand): void
}

export function SessionControls({ sessionState, inputStatus, lastInputResult, resumeCountdown, configuration, observation, mapStatus, rhythm, onOpenMap, sendCommand }: SessionControlsProps) {
  const movementSpeedPxPerSecond = 320
  const [settingsPaused, setSettingsPaused] = useState(false)
  const [keyEditorOpen, setKeyEditorOpen] = useState(false)
  const [stationaryAttackEnabled, setStationaryAttackEnabled] = useState(false)
  const stopped = sessionState === 'Stopped'
  const paused = sessionState === 'Paused'
  const arming = sessionState === 'Arming'
  const emergency = sessionState === 'EmergencyStop'
  const running = !stopped && !paused && !arming && !emergency
  const brokerConnecting = inputStatus.status === 'starting'
  const brokerUnavailable = inputStatus.status !== 'ready' && inputStatus.status !== 'paused'
  const startLabel = arming ? (resumeCountdown ? `${resumeCountdown} 秒后开始` : '正在启动') : running ? '运行中' : '开始自动运行'
  const brokerLabel = inputStatus.status === 'ready' ? '输入服务已就绪' : inputStatus.status === 'starting' ? '输入服务连接中' : inputStatus.status === 'paused' ? '输入服务已暂停' : inputStatus.status === 'faulted' ? '输入服务异常' : '输入服务待连接'
  const updateConfig = (payload: Extract<UiCommand, { type: 'config.update' }>['payload']) => {
    if (!stopped && !paused) {
      sendCommand({ schemaVersion: 2, type: 'session.pause', payload: {} })
    }
    if (!stopped) setSettingsPaused(true)
    sendCommand({ schemaVersion: 2, type: 'config.update', payload })
  }

  const openKeyEditor = () => {
    if (!stopped && !paused) sendCommand({ schemaVersion: 2, type: 'session.pause', payload: {} })
    if (!stopped) setSettingsPaused(true)
    setKeyEditorOpen(true)
  }

  const displayResource = (resource: ObservationSnapshot['hp'] | undefined) => {
    if (!resource || resource.confidence <= 0) return '--'
    const percent = resource.mode === 'percent' ? Math.round(resource.value * 100) : resource.maximumValue ? Math.round(resource.value / resource.maximumValue * 100) : undefined
    const absolute = resource.currentValue !== undefined && resource.maximumValue !== undefined ? `${Math.round(resource.currentValue)}/${Math.round(resource.maximumValue)}` : undefined
    return [absolute, percent === undefined ? undefined : `${percent}%`].filter(Boolean).join(' · ') || `${Math.round(resource.value)}`
  }
  const hpValue = displayResource(observation?.hp)
  const mpValue = displayResource(observation?.mp)
  const mapState = mapStatus?.state ?? observation?.map.state
  const mapStateLabel = mapState === 'validated' ? '已验证' : mapState === 'candidate' ? '待标定' : mapState === 'archived' ? '已归档' : '未识别'
  const observedMapName = observation?.map.mapId && observation.map.mapId !== 'unknown' ? observation.map.mapId : undefined
  const mapName = mapStatus?.mapId ?? observedMapName ?? '未识别地图'
  const inputTestDisabled = (!stopped && !paused) || brokerUnavailable
  const inputTest = (kind: Extract<UiCommand, { type: 'input.test' }>['payload']['kind'], holdMs: number) =>
    sendCommand({ schemaVersion: 2, type: 'input.test', payload: { kind, holdMs } })
  const inputResultLabel = lastInputResult?.status === 'completed'
    ? '完成并已释放'
    : lastInputResult?.status === 'rejected'
      ? '已拒绝'
      : lastInputResult?.status === 'cancelled'
        ? '已取消'
        : lastInputResult?.status === 'failed'
          ? '失败'
          : lastInputResult?.status === 'accepted'
            ? '执行中'
            : '尚未测试'

  return (
    <aside className="side-panel side-panel--controls">
      <div className="side-panel__scroll">
        <SectionHeading icon={<ControlOutlined />} kicker="工作区" title="运行控制" action={<StatusPill state={sessionState} />} />
        <div className="session-summary">
          <div><span>运行模式</span><strong>自动战斗</strong></div>
          <Text className="session-summary__broker"><i />{brokerLabel}</Text>
        </div>
        <Space orientation="vertical" size={10} className="control-actions">
          <Button className="action-button action-button--primary" type="primary" aria-label={startLabel} icon={<PlayCircleFilled />} onClick={() => { setStationaryAttackEnabled(false); sendCommand({ schemaVersion: 2, type: 'combat.trial.start', payload: {} }) }} disabled={emergency || arming || running || brokerConnecting} loading={arming} block>{startLabel}</Button>
          <Button className="action-button action-button--secondary" aria-label="停止自动运行" icon={<PauseCircleOutlined />} onClick={() => { setStationaryAttackEnabled(false); sendCommand({ schemaVersion: 2, type: 'session.pause', payload: {} }) }} disabled={emergency || stopped || paused} block>停止自动运行</Button>
        </Space>
        <div className="hotkey-strip hotkey-strip--stacked" aria-label="全局快捷键"><span><kbd>{inputStatus.hotkeys.pauseResume}</kbd> 暂停/恢复</span><span><kbd>{inputStatus.hotkeys.emergencyStop}</kbd> 紧急停止</span></div>
        {settingsPaused && <div className="settings-pause-notice" role="status">修改设置时已暂停</div>}
        <Divider className="section-divider" />
        <section className="resource-summary" aria-label="生命与魔法识别">
          <div><span>HP</span><strong>{hpValue}</strong><small>补药阈值 {configuration.hpThreshold}{configuration.hpThresholdMode === 'percent' ? '%' : ''}</small></div>
          <div><span>MP</span><strong>{mpValue}</strong><small>补药阈值 {configuration.mpThreshold}{configuration.mpThresholdMode === 'percent' ? '%' : ''}</small></div>
        </section>
        <section className="settings-section" aria-labelledby="combat-settings-title">
          <div className="section-label-row"><Text id="combat-settings-title" className="section-label">战斗设置</Text><Tooltip title="动作时长由程序根据距离和画面自动计算"><SettingOutlined className="section-label__icon" /></Tooltip></div>
          <div className="setting-field">
            <div><Text className="field-title">攻击策略</Text><Text className="field-hint">按目标距离自动规划</Text></div>
            <Segmented aria-label="攻击策略" className="mode-segmented" value={configuration.attackMode} onChange={(value) => updateConfig({ attackMode: value as 'single' | 'auto' | 'group' })} options={[{ label: '单体', value: 'single' }, { label: '自动', value: 'auto' }, { label: '群攻', value: 'group' }]} />
          </div>
          <div className="setting-field setting-field--inline">
            <div><Text className="field-title">生命值下限</Text><Text className="field-hint">低于此值时使用 HP 药水</Text></div>
            <div className="threshold-control"><Segmented size="small" value={configuration.hpThresholdMode} onChange={(value) => updateConfig({ hpThresholdMode: value as 'percent' | 'absolute' })} options={[{ label: <span aria-label="HP 百分比">%</span>, value: 'percent' }, { label: <span aria-label="HP 固定值">数值</span>, value: 'absolute' }]} /><InputNumber aria-label="生命值下限" min={1} max={configuration.hpThresholdMode === 'percent' ? 100 : undefined} value={configuration.hpThreshold} onChange={(value) => { if (value !== null) updateConfig({ hpThreshold: value }) }} suffix={configuration.hpThresholdMode === 'percent' ? '%' : '点'} controls={false} /></div>
          </div>
          <div className="setting-field setting-field--inline"><div><Text className="field-title">魔法值下限</Text><Text className="field-hint">低于此值时使用 MP 药水</Text></div><div className="threshold-control"><Segmented size="small" value={configuration.mpThresholdMode} onChange={(value) => updateConfig({ mpThresholdMode: value as 'percent' | 'absolute' })} options={[{ label: <span aria-label="MP 百分比">%</span>, value: 'percent' }, { label: <span aria-label="MP 固定值">数值</span>, value: 'absolute' }]} /><InputNumber aria-label="魔法值下限" min={1} max={configuration.mpThresholdMode === 'percent' ? 100 : undefined} value={configuration.mpThreshold} onChange={(value) => { if (value !== null) updateConfig({ mpThreshold: value }) }} suffix={configuration.mpThresholdMode === 'percent' ? '%' : '点'} controls={false} /></div></div>
          <div className="setting-field setting-field--inline"><div><Text className="field-title">自动拾取</Text><Text className="field-hint">检测到掉落物时执行</Text></div><Switch aria-label="自动拾取" checked={configuration.pickupEnabled} onChange={(checked) => updateConfig({ pickupEnabled: checked })} /></div>
          <div className="setting-field setting-field--inline"><div><Text className="field-title">定点攻击</Text><Text className="field-hint">持续约 30 秒，间歇左右回位</Text></div><Switch aria-label="定点攻击" checked={stationaryAttackEnabled && !stopped && !paused && !emergency} disabled={emergency || brokerConnecting} onChange={(checked) => { setStationaryAttackEnabled(checked); sendCommand({ schemaVersion: 2, type: 'stationary.attack.set', payload: { enabled: checked } }) }} /></div>
          {stationaryAttackEnabled && rhythm && <div className="rhythm-countdown" role="status"><div className="rhythm-countdown__head"><span>当前节奏</span><strong>{rhythm.phase === 'attackHolding' ? '攻击中' : rhythm.phase === 'moveLeft' ? '向左回位' : rhythm.phase === 'moveRight' ? '向右回位' : rhythm.phase === 'resting' ? '短暂休息' : '动作间隔'}</strong></div><div className="rhythm-countdown__time">{(rhythm.remainingMs / 1000).toFixed(1)}<small>秒</small></div><div className="rhythm-countdown__bar"><i style={{ width: `${rhythm.sampledDurationMs > 0 ? Math.max(0, Math.min(100, rhythm.remainingMs / rhythm.sampledDurationMs * 100)) : 0}%` }} /></div><small>{rhythm.phase === 'moveLeft' || rhythm.phase === 'moveRight' ? `本段预计移动 ${Math.round(rhythm.sampledDurationMs / 1000 * movementSpeedPxPerSecond)} px` : rhythm.phase === 'attackHolding' ? `攻击完成后执行完整左右回位 · 周期 #${rhythm.cycleId}` : `回位流程进行中 · 周期 #${rhythm.cycleId}`}</small></div>}
          <div className="setting-field setting-field--inline"><div><Text className="field-title">攻击距离</Text><Text className="field-hint">同平台目标的期望像素距离</Text></div><InputNumber aria-label="期望攻击距离" min={20} max={500} value={configuration.preferredDistancePx} onChange={(value) => { if (value !== null) updateConfig({ preferredDistancePx: value }) }} suffix="px" controls={false} /></div>
        </section>
        <section className="settings-section" aria-labelledby="profile-title">
          <div className="section-label-row"><Text id="profile-title" className="section-label">地图档案</Text><Text className="section-label__meta">{mapStateLabel}</Text></div>
          <button className="map-profile" type="button" aria-label="打开地图档案" onClick={onOpenMap}><span className="map-profile__icon"><EnvironmentOutlined /></span><span className="map-profile__text"><strong>{mapName}</strong><small>{mapStatus ? `覆盖 ${Math.round(mapStatus.coverage * 100)}% · 平台 ${mapStatus.platformCount} · 梯子 ${mapStatus.ladderCount}` : observation ? `置信度 ${Math.round(observation.map.confidence * 100)}% · ${mapStateLabel}` : '等待真实视觉观察'}</small></span><RightOutlined className="map-profile__arrow" /></button>
          {mapStatus?.state === 'candidate' && (mapStatus.errors.length === 0
            ? <Button className="map-confirm-button" onClick={() => sendCommand({ schemaVersion: 2, type: 'map.calibration.confirm', payload: { mapId: mapStatus.mapId } })} block>确认标定并启用</Button>
            : <div className="map-validation-warning" role="status">需继续扫描：{mapStatus.errors[0]}</div>)}
        </section>
        <section className="settings-section" aria-labelledby="keys-title">
          <div className="section-label-row"><Text id="keys-title" className="section-label">按键配置</Text><Button type="text" size="small" aria-label="编辑按键配置" icon={<KeyOutlined />} className="compact-link" onClick={openKeyEditor}>编辑</Button></div>
          <div className="key-grid" aria-label="按键配置列表"><div><small>移动</small><strong>方向键</strong></div><div><small>跳跃</small><strong>{configuration.jumpKey}</strong></div><div><small>拾取</small><strong>{configuration.pickupEnabled ? configuration.pickupKey : '关闭'}</strong></div><div><small>单攻</small><strong>{configuration.singleAttackKey}</strong></div><div><small>群攻</small><strong>{configuration.areaAttackKey}</strong></div><div><small>药水</small><strong>{configuration.hpPotionKey} / {configuration.mpPotionKey}</strong></div></div>
        </section>
        <section className="settings-section" aria-labelledby="input-test-title">
          <div className="section-label-row"><Text id="input-test-title" className="section-label">输入自检</Text><Text className="section-label__meta">单次动作 · 自动释放</Text></div>
          <Text className="field-hint">点击后程序会切回游戏，倒计时结束仅执行一次所选动作。</Text>
          <div className="input-test-grid">
            <Button size="small" aria-label="测试向左" disabled={inputTestDisabled} onClick={() => inputTest('moveLeft', 180)}>向左</Button>
            <Button size="small" aria-label="测试向右" disabled={inputTestDisabled} onClick={() => inputTest('moveRight', 180)}>向右</Button>
            <Button size="small" aria-label="测试向上" disabled={inputTestDisabled} onClick={() => inputTest('climbUp', 180)}>向上</Button>
            <Button size="small" aria-label="测试向下" disabled={inputTestDisabled} onClick={() => inputTest('climbDown', 180)}>向下</Button>
            <Button size="small" aria-label="测试跳跃" disabled={inputTestDisabled} onClick={() => inputTest('jump', 90)}>跳跃</Button>
            <Button size="small" aria-label="测试攻击" disabled={inputTestDisabled} onClick={() => inputTest('attack', 90)}>攻击</Button>
            <Button size="small" aria-label="测试拾取" disabled={inputTestDisabled} onClick={() => inputTest('pickup', 90)}>拾取</Button>
            <Button size="small" aria-label="测试HP药" disabled={inputTestDisabled} onClick={() => inputTest('hpPotion', 90)}>HP 药</Button>
            <Button size="small" aria-label="测试MP药" disabled={inputTestDisabled} onClick={() => inputTest('mpPotion', 90)}>MP 药</Button>
          </div>
          <div className={`input-test-result input-test-result--${lastInputResult?.status ?? 'idle'}`} role={lastInputResult ? 'status' : undefined}>
            <span>{inputResultLabel}</span>
            <small>{lastInputResult?.message ?? '等待选择测试动作'}</small>
          </div>
        </section>
      </div>
      {keyEditorOpen && <KeyBindingEditor configuration={configuration} onCancel={() => setKeyEditorOpen(false)} onSave={(keys) => { setKeyEditorOpen(false); sendCommand({ schemaVersion: 2, type: 'config.update', payload: keys }) }} />}
    </aside>
  )
}
