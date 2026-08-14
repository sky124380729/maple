import '@fontsource/noto-sans-sc/chinese-simplified-400.css'
import '@fontsource/noto-sans-sc/chinese-simplified-600.css'
import '@fontsource/noto-sans-sc/chinese-simplified-700.css'
import { ConfigProvider, theme } from 'antd'
import type { HostBridge } from '../bridge/HostBridge'
import { WorkbenchPage } from '../features/workbench/WorkbenchPage'
import './app.css'

export function App({ bridge }: { bridge?: HostBridge }) {
  return (
    <ConfigProvider theme={{
      algorithm: theme.darkAlgorithm,
      token: {
        colorPrimary: '#4da3ff',
        colorBgBase: '#0a0d11',
        colorText: '#f4f7fb',
        colorTextSecondary: '#8793a1',
        borderRadius: 10,
        fontFamily: '"Noto Sans SC", "PingFang SC", sans-serif',
      },
      components: {
        Button: { controlHeight: 38, fontWeight: 600 },
        Segmented: { itemSelectedBg: '#273c52', trackBg: '#171f28' },
        InputNumber: { colorBgContainer: '#161d25', activeBorderColor: '#4da3ff' },
        Switch: { colorPrimary: '#42d392' },
      },
    }}>
      <WorkbenchPage bridge={bridge} />
    </ConfigProvider>
  )
}
