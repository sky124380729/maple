import { CheckCircleFilled, RadarChartOutlined, ReloadOutlined, SafetyCertificateOutlined, WarningOutlined } from '@ant-design/icons'
import { Button, Progress, Tag, Tooltip, Typography } from 'antd'
import type { HostEvent, InputBrokerStatus, ObservationSnapshot, PauseReason, SessionState, TelemetrySnapshot, VisionStatus } from '../../contracts/bridge'
import { MetricRow, SectionHeading, sessionStateLabels } from './presentation'

const { Text } = Typography
type LogEntry = Extract<HostEvent, { type: 'log.appended' }>['payload']
type PreviewAvailability = Extract<HostEvent, { type: 'preview.availabilityChanged' }>['payload']

const pauseReasonLabels: Record<PauseReason, string> = {
  None: '无', CalibrationRequired: '需要自动校准', StaleFrame: '画面已过期', TargetLost: '目标丢失', WindowNotForeground: '窗口不在前台', BlackFrame: '黑屏', MapNotValidated: '地图未验证', InputUnavailable: '输入不可用', HealthUnknown: '生命值未知', UnknownPopup: '未知弹窗', WatchdogTimeout: '监控超时', OperatorRequested: '用户请求', SafetyViolation: '安全门阻止',
}

export function HealthPanel({ sessionState, pauseReason, observation, telemetry, visionStatus, preview, inputStatus, logs, onRefresh }: { sessionState: SessionState; pauseReason: PauseReason; observation?: ObservationSnapshot; telemetry?: TelemetrySnapshot; visionStatus: VisionStatus; preview: PreviewAvailability; inputStatus: InputBrokerStatus; logs: LogEntry[]; onRefresh(): void }) {
  const selfConfidence = observation ? Math.round(observation.self.confidence * 100) : 0
  const latestLog = logs.at(-1)
  const latestLogTitle = latestLog?.message ?? '等待宿主事件'
  const inputReady = inputStatus.status === 'ready'
  const inputLabel = inputReady ? '输入服务已就绪' : inputStatus.status === 'paused' ? '输入服务已暂停' : inputStatus.status === 'faulted' ? '输入服务异常' : '输入服务待命'
  const visionLabel = visionStatus.status === 'ready' ? visionStatus.modelId ?? '模型已就绪' : visionStatus.status === 'repairing' ? '正在重新识别' : visionStatus.status === 'faulted' ? '模型异常' : visionStatus.status === 'inspecting' ? '正在检查模型' : '模型未配置'
  return (
    <aside className="side-panel side-panel--diagnostics">
      <div className="side-panel__scroll">
        <SectionHeading icon={<RadarChartOutlined />} kicker="视觉引擎" title="识别概览" action={<Tooltip title="刷新状态"><Button type="text" shape="circle" icon={<ReloadOutlined />} aria-label="刷新状态" onClick={onRefresh} /></Tooltip>} />
        <section className="identity-card" aria-labelledby="self-confidence-title">
          <div className="identity-card__head"><div className="identity-card__name"><span className="identity-dot identity-dot--green" /><div><Text id="self-confidence-title" className="field-title">自己角色</Text><Text className="field-hint">唯一观察目标 · 自动维护</Text></div></div><strong className="identity-card__score">{selfConfidence}<small>%</small></strong></div>
          <Progress percent={selfConfidence} showInfo={false} strokeColor="#42d392" railColor="#273b35" size={[0, 5]} />
          <div className="identity-card__foot"><span className="text-good"><CheckCircleFilled />{selfConfidence >= 90 ? '置信度良好' : '程序正在自动修复'}</span><span>最近一帧</span></div>
        </section>
        <section className="diagnostic-section" aria-labelledby="target-count-title">
          <div className="section-label-row"><Text id="target-count-title" className="section-label">目标概览</Text><span className="section-label__meta">实时</span></div>
          <div className="metric-list">
            <MetricRow label="其他玩家" value={String(observation?.players.length ?? 0)} detail="不参与目标选择" />
            <MetricRow label="怪物" value={String(observation?.monsters.length ?? 0)} detail={observation ? '目标检测正常' : '等待画面'} tone="warn" />
            <MetricRow label="所在平台" value="待拓扑识别" />
            <MetricRow label="地图档案" value={observation?.map.mapId && observation.map.mapId !== 'unknown' ? observation.map.mapId : '未识别'} detail={observation ? (observation.map.state === 'validated' ? '已验证' : observation.map.state === 'candidate' ? '待标定' : '已归档') : undefined} />
            <MetricRow label="采集后端" value={preview.backend === 'browser-mock' ? '浏览器模拟' : preview.backend === 'native' ? '原生采集' : '不可用'} />
            <MetricRow label="视觉模型" value={visionLabel} detail={visionStatus.diagnostic ?? undefined} />
            <MetricRow label="推理性能" value={telemetry ? `${Math.round(telemetry.recognitionFps)} FPS / ${Math.round(telemetry.detectorLatencyMs)} ms` : '等待首帧'} detail={telemetry?.inferenceProvider ?? 'none'} />
          </div>
        </section>
        <section className="safety-card" aria-labelledby="safety-title">
          <div className="safety-card__head"><span className="safety-card__icon"><SafetyCertificateOutlined /></span><div><Text id="safety-title" className="field-title">安全门</Text><Text className="field-hint">{inputLabel}</Text></div><Tag className={`gate-tag ${inputReady ? 'gate-tag--ready' : ''}`}>{inputReady ? '可运行' : '已锁定'}</Tag></div>
          <Text className="safety-card__copy">当前为{sessionStateLabels[sessionState]}，暂停原因：{pauseReasonLabels[pauseReason]}。最近释放{inputStatus.lastReleaseSucceeded ? '成功' : '失败'}，活动按键 {inputStatus.activeKeys.length} 个。</Text>
        </section>
        <section className="event-section" aria-labelledby="event-title">
          <div className="section-label-row"><Text id="event-title" className="section-label">最近事件</Text><span className="section-label__meta">最新</span></div>
          <div className="event-item"><span className="event-item__icon"><WarningOutlined /></span><div><strong>{latestLogTitle}</strong><small>{latestLog?.code ?? '等待结构化日志'}</small></div></div>
        </section>
      </div>
    </aside>
  )
}
