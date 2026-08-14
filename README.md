# Maple Visual Automation

当前仓库包含两个彼此独立的 Windows 程序：

- `dist\MapleVisualPrototype.exe`：新的三栏视觉原型。它自动查找标题包含“冒险岛怀旧服”的窗口，只读捕获客户区，展示识别叠加、地图视觉标定、状态安全门和性能指标。原型明确禁用所有键盘/鼠标注入，不会发送 HID 报告。
- `dist\MapleInputProbe.exe`：早期的前台 `SendInput` 单键诊断工具，仅用于授权测试环境的兼容性排查，不属于新的视觉原型。

## 运行视觉原型

直接双击 `dist\MapleVisualPrototype.exe`。程序会自动发现目标窗口，不要求手动选择；目标窗口最小化、失焦或被遮挡时，预览进入暂停状态。点击“地图标定”可查看多帧视觉录制后的 MapWorld 结构标注演示。

当前版本是 UI 与采集验证原型：OpenCV/YOLO、虚拟 HID 协议和自动战斗闭环尚未接入，所有动作按钮只更新状态日志。

## 运行输入测试器

在 PowerShell 中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\input-probe-ui.ps1
```

使用前：

1. 先打开并进入授权测试客户端，窗口标题需为 `冒险岛怀旧服`。
2. 启动测试器，程序会自动捕获这个窗口，不需要手动选择。
3. 勾选授权确认。
4. 点击“开始监听全局测试热键”。
5. 最小化测试器，让游戏窗口保持前台，再按一次对应热键。

热键如下：

- `Ctrl+Shift+Left`：发送一次左方向键；
- `Ctrl+Shift+Right`：发送一次右方向键；
- `Ctrl+Shift+J/A/D/Space`：发送一次对应按键。

每次热键只发送一次按下和释放事件，按键持续时间默认为 80ms。窗口失去前台、标题不匹配或窗口不存在时不会发送。`Alt` 没有用于测试热键，因为它在你的客户端中是跳跃键。

这个工具不读取或修改游戏内存，不发送后台窗口消息，也不执行连续自动化动作。

## 运行逻辑测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\input_probe_logic.tests.ps1
```
