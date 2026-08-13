# Maple Visual Automation

当前仓库先提供一个安全的 Windows `SendInput` 单键测试器，用于验证授权测试客户端是否能接收普通前台模拟输入。

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
