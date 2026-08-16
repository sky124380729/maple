import { Button, Progress, Space, Tag, Typography } from 'antd'
import type { HostEvent, MapRuntimeStatus, MapScanStatus, ObservationSnapshot, UiCommand } from '../../contracts/bridge'

const { Text, Title } = Typography
type CloudStatus = Extract<HostEvent, { type: 'cloud.status.updated' }>['payload']

export function MapCalibrationDrawer({ observation, mapStatus, mapScan, cloudStatus, sendCommand }: { observation?: ObservationSnapshot; mapStatus?: MapRuntimeStatus; mapScan?: MapScanStatus; cloudStatus: CloudStatus; sendCommand(command: UiCommand): void }) {
  const mapId = mapStatus?.mapId ?? (observation?.map.mapId !== 'unknown' ? observation?.map.mapId : undefined)
  const frameIds = mapScan?.frameIds ?? []
  const canAnnotate = Boolean(mapId && frameIds.length >= 2 && !mapScan?.scanning && cloudStatus.enabled && cloudStatus.connectionStatus === 'ready')
  return <section className="map-drawer-content" aria-labelledby="map-drawer-title">
    <Title id="map-drawer-title" level={3}>地图视觉标定</Title>
    <Text className="map-drawer-copy">地图包只提供初始结构，坐标必须通过多帧视觉录制配准。标定完成前，自动输入保持锁定。</Text>
    <div className="map-drawer-status"><span>当前地图</span><strong>{mapId ?? '等待地图识别'}</strong><Tag color={mapStatus?.state === 'validated' ? 'success' : 'warning'}>{mapStatus?.state === 'validated' ? '已验证' : mapStatus ? '待标定' : '未识别'}</Tag></div>
    <div className="map-scan-card"><div><Text className="field-title">视觉录制</Text><Text className="field-hint">覆盖不同镜头位置，至少保留 2 帧</Text></div><strong>{frameIds.length} 帧</strong><Progress percent={Math.min(100, Math.round(frameIds.length / 8 * 100))} showInfo={false} /></div>
    <Space.Compact block className="map-drawer-actions">
      <Button type="primary" onClick={() => sendCommand({ schemaVersion: 2, type: 'map.scan.start', payload: {} })} disabled={mapScan?.scanning}>开始录制</Button>
      <Button onClick={() => sendCommand({ schemaVersion: 2, type: 'map.calibration.start', payload: {} })} disabled={!mapScan?.scanning}>停止录制</Button>
    </Space.Compact>
    <Button block onClick={() => sendCommand({ schemaVersion: 2, type: 'cloud.map.annotate', payload: { mapId: mapId!, sourceFrameIds: frameIds.slice(-4) } })} disabled={!canAnnotate}>调用百炼分析结构</Button>
    {!cloudStatus.enabled && <Text className="map-drawer-warning">请先在系统设置启用百炼并允许上传地图截图。</Text>}
    {mapStatus && <div className="map-validation-summary"><div><span>覆盖率</span><strong>{Math.round(mapStatus.coverage * 100)}%</strong></div><div><span>标定误差</span><strong>{mapStatus.calibrationErrorPx.toFixed(1)} px</strong></div><div><span>平台 / 梯子</span><strong>{mapStatus.platformCount} / {mapStatus.ladderCount}</strong></div>{mapStatus.errors.length > 0 && <Text className="map-drawer-warning">{mapStatus.errors[0]}</Text>}</div>}
    {mapStatus?.state === 'candidate' && mapStatus.errors.length === 0 && <Button type="primary" block onClick={() => sendCommand({ schemaVersion: 2, type: 'map.calibration.confirm', payload: { mapId: mapStatus.mapId } })}>确认标定并启用自动导航</Button>}
  </section>
}
