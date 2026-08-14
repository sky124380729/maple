# 百炼模型配置与地图视觉接入设计

日期：2026-08-14
状态：已确认设计，等待实施
产品决策来源：`docs/MAPLE_PROJECT_SPEC.md`

## 1. 目标

Maple 首版云端大模型固定使用阿里云百炼。用户只需在中文设置页选择一个内置视觉模型、粘贴 API Key、启用服务并测试连接。百炼只处理未知地图的低频结构初始化和持续低置信度复核，不进入实时采集、人物/怪物识别、动作时长计算或虚拟 HID 控制链路。

本功能必须满足：API Key 不进入普通配置、日志、回放或导出包；云端失败不降低本地安全门；模型响应只有通过 schema、来源帧、坐标系和本地拓扑校验后才能成为 candidate 地图。

## 2. 官方接口基线

实现依据阿里云百炼公开文档：

- 中国站 OpenAI 兼容接口：`https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions`；
- 认证：`Authorization: Bearer <DASHSCOPE_API_KEY>`；
- 视觉消息：`type=image_url`，支持公网 URL 或 `data:image/<format>;base64,...`；
- 官方建议 API Key 不硬编码到源代码；
- 官方视觉模型目录包含 `qwen3-vl-plus`、`qwen3-vl-flash`、`qwen-vl-max`、`qwen-vl-plus`。

为保持“只粘贴 API Key”的产品承诺，首版只支持可直接访问上述中国站兼容接口的百炼默认业务空间密钥，不增加 WorkspaceId 输入。需要专属业务空间域名的密钥在连接测试中返回 `WORKSPACE_ENDPOINT_UNSUPPORTED`，不能让用户输入自定义 Base URL 绕过域名白名单。

Maple 首版白名单固定为：

| modelId | 界面名称 | 用途 |
| --- | --- | --- |
| `qwen3-vl-plus` | 千问视觉 Plus | 默认，地图结构质量优先 |
| `qwen3-vl-flash` | 千问视觉 Flash | 速度与成本优先 |
| `qwen-vl-max` | 千问视觉 Max | 兼容回退 |

模型目录随应用版本发布，不从网络动态下载，也不允许手填任意 modelId。模型别名若被百炼下线，连接测试返回明确的 `MODEL_UNAVAILABLE`，应用保持本地观察模式并等待应用更新。

## 3. 组件边界

```text
React 设置页
  -> 版本化 CloudCommand
Maple.Host
  -> CredentialStore（只在设置/清除时接触明文）
  -> BailianConfigurationStore（只存 enabled/modelId/consentVersion）
Maple.Cloud
  -> BailianModelCatalog
  -> BailianHttpClient
  -> MapAnnotationPromptBuilder
  -> StrictMapAnnotationParser
Maple.Map
  -> 本地几何/拓扑校验
```

React 不直接访问百炼。`Maple.Cloud` 不读取 UI 状态，不生成路线和按键。Host 是凭据、上传同意、请求限流和会话暂停的唯一协调者。

## 4. Bridge 契约 v2

新增命令：

| 命令 | payload | 行为 |
| --- | --- | --- |
| `cloud.credential.set` | `{ apiKey }` | 一次性提交，Host 安全保存后立即清除前端输入 |
| `cloud.credential.clear` | `{}` | 删除凭据并自动禁用云端能力 |
| `cloud.config.update` | `{ enabled, modelId, uploadConsent }` | 更新非敏感配置 |
| `cloud.connection.test` | `{}` | 使用当前模型发起最小文本测试，不上传游戏画面 |
| `cloud.map.annotate` | `{ mapId, sourceFrameIds }` | Host 从已批准会话帧仓库解析图片，不接受任意路径/URL |

新增事件 `cloud.status.updated`：

```json
{
  "schemaVersion": 2,
  "type": "cloud.status.updated",
  "payload": {
    "provider": "bailian",
    "enabled": true,
    "credentialConfigured": true,
    "modelId": "qwen3-vl-plus",
    "connectionStatus": "ready",
    "requestInFlight": false,
    "lastErrorCode": null
  }
}
```

事件不得包含 API Key、Authorization header、请求图片、原始响应或异常堆栈。schemaVersion 2 同时完成主规格要求的动作 `profileId` 升级；Host 不兼容地拒绝 v1 命令，不做隐式字段猜测。

## 5. 密钥生命周期

1. 用户在 `Input.Password` 中粘贴 API Key；字段不提供明文回显和复制按钮。
2. React 只在点击“保存密钥”时构造 `cloud.credential.set`，不写入 Zustand、localStorage、sessionStorage、URL、埋点或日志。
3. Host 校验长度为 16-256、无空白字符；生产 WebView2 禁用开发者工具和远程调试参数。
4. Windows 使用当前用户范围的 Windows Credential Manager 或 DPAPI 保存，凭据名称固定为 `Maple/BailianApiKey`。普通权限进程只能读取当前用户凭据。
5. Host 返回成功事件后 React 立即把输入框置空，只显示“密钥已保存”。密钥永不回传前端。
6. 清除凭据时删除安全存储、取消在途请求、禁用云端配置并清除内存副本。
7. macOS 自动测试使用进程内 `InMemoryCredentialStore`；它不落盘，退出即丢失，且不得用于发布包。

日志过滤器必须同时屏蔽 `Authorization`、`apiKey`、`DASHSCOPE_API_KEY` 和符合 `sk-` 特征的长字符串。网络错误只保留状态码、百炼 request id、内部错误码和耗时。

## 6. 图片上传与请求

用户首次启用云端能力时确认固定版本的上传说明，Host 保存 `uploadConsent=true` 和 `consentVersion`。撤回同意后立即取消请求并阻止后续上传。每次调用仍在日志中记录 mapId、frameId、图片数量、压缩后字节数、模型、用途、耗时和 request id，但不保存 Base64。

Host 只允许从当前会话的只读帧仓库按 frameId 取图，不接受 React 提供本地路径或远程 URL。每次最多 4 张 JPEG/PNG，单张原图不超过 7MB；发送前缩放到最长边不超过 1920px并压缩，单张编码前目标不超过 1.5MB。请求总并发固定为 1，连接测试超时 10 秒，地图标注超时 45 秒。

只对 HTTP 429、502、503、504 自动重试，最多 2 次，退避 1 秒和 3 秒；401/403、模型不可用、无效 JSON、用户取消和本地校验失败不重试。请求期间状态机保持 `Paused/MapCalibrating`，任何云端结果都不能直接触发动作。

## 7. Prompt 与响应门禁

系统提示固定在应用资源中并带版本号。它只要求输出 `InitialMapAnnotation`：来源 frameId、`mapworld-px` 坐标系、平台、梯子、边界、连接、confidence、coverage 和 calibrationErrorPx。提示明确禁止输出路线、按键、脚本、攻击目标和解释性 Markdown。

客户端只接受一个完整 JSON 对象，不从 Markdown code fence 或自然语言中猜测 JSON。解析顺序固定为：

1. HTTP 与响应大小检查；
2. OpenAI 兼容 envelope 检查；
3. content 严格 JSON 解析；
4. `InitialMapAnnotation` schema 校验；
5. sourceFrameIds 必须是请求集合的子集且非空；
6. 坐标系、数值范围、引用完整性检查；
7. Maple.Map 本地覆盖率、标定误差和拓扑验证；
8. 保存为 candidate，等待本地验证流程。

任一步失败都返回结构化错误并保持暂停。不得截取模型文本中的局部 JSON，也不得自动修补不存在的平台或连接。

## 8. 中文设置界面

系统设置页增加“百炼视觉”区域：启用开关、模型下拉框、密码输入框、“保存密钥”、“测试连接”和“清除密钥”。默认关闭；未保存密钥时启用开关和测试按钮禁用。测试状态只显示“未配置、检查中、可用、不可用”及简短中文原因。

界面不展示 Base URL、温度、system prompt、token 参数或任意高级字段。上传同意采用一个明确复选框，未同意时不能启用。保存成功后密码框清空，刷新页面仍只显示凭据已配置状态。

## 9. 测试设计

实现按 TDD 进行：

- 契约测试：白名单模型通过，任意模型/URL/空密钥/v1 命令被拒绝；状态事件不允许敏感字段；
- UI 测试：未配置禁用、保存后清空、模型选择、测试连接、清除凭据、页面刷新不回显密钥；
- 凭据测试：保存/读取/删除、覆盖时清零旧缓冲、日志脱敏；
- HTTP 测试：固定域名、Bearer header、超时、取消、并发 1、可重试与不可重试错误；
- 解析测试：纯 JSON 成功，Markdown、未知字段、来源帧越权、坐标错误和拓扑冲突失败；
- 编排测试：请求前暂停、失败保持暂停、成功只产生 candidate、EmergencyStop 取消请求且不恢复；
- 安全扫描：源码、产物、日志夹具中不存在真实 API Key 或 Authorization 值。

macOS 使用假 HTTP handler 和内存凭据完成确定性验证，不调用真实百炼产生费用。真实连接测试、Windows Credential Manager/DPAPI 和代理/证书兼容性留到 Windows 实机，证据必须单独标记。

## 10. 失败处理

| 错误码 | 用户状态 | 自动行为 |
| --- | --- | --- |
| `CREDENTIAL_MISSING` | 未配置 | 禁用云端能力 |
| `AUTH_REJECTED` | 密钥不可用 | 不重试，不删除用户密钥 |
| `MODEL_UNAVAILABLE` | 模型不可用 | 不切换到未选择模型 |
| `WORKSPACE_ENDPOINT_UNSUPPORTED` | 该密钥需要专属业务空间 | 保持禁用并提示使用默认空间密钥 |
| `RATE_LIMITED` | 请求受限 | 按预算重试后暂停 |
| `NETWORK_TIMEOUT` | 网络超时 | 按预算重试后暂停 |
| `RESPONSE_INVALID` | 响应无效 | 丢弃响应并保存脱敏诊断 |
| `UPLOAD_NOT_APPROVED` | 未同意上传 | 阻止请求 |
| `LOCAL_VALIDATION_FAILED` | 地图验证失败 | candidate 不得升级 |

所有失败都允许用户继续使用完全离线的观察、回放和本地模型能力；云端不可用不能导致自动动作绕过地图门禁。
