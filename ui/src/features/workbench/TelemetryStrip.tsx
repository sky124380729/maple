import { ThunderboltFilled } from '@ant-design/icons'
import type { TelemetrySnapshot } from '../../contracts/bridge'

const formatFps = (value?: number) => value === undefined ? '--' : `${Number.isInteger(value) ? value : value.toFixed(1)} 帧/秒`
const formatMs = (value?: number) => value === undefined ? '--' : `${Number.isInteger(value) ? value : value.toFixed(1)} 毫秒`

export function TelemetryStrip({ telemetry }: { telemetry?: TelemetrySnapshot }) {
  const metrics = [
    ['采集帧率', formatFps(telemetry?.captureFps)],
    ['绘制帧率', formatFps(telemetry?.renderFps)],
    ['识别帧率', formatFps(telemetry?.recognitionFps)],
    ['端到端延迟', formatMs(telemetry?.frameLatencyMs)],
    ['队列年龄', formatMs(telemetry?.queueAgeMs)],
    ['丢帧', String(telemetry?.droppedFrames ?? 0)],
  ]
  return <footer className="telemetry-strip" aria-label="性能遥测"><div className="telemetry-title"><ThunderboltFilled /><span>性能</span></div>{metrics.map(([label, value]) => <div className="telemetry-item" key={label}><span>{label}</span><strong>{value}</strong></div>)}</footer>
}
