# Maple 开发入口

1. 产品行为和架构只以 `docs/MAPLE_PROJECT_SPEC.md` 为准。
2. Windows 接手先读 `docs/WINDOWS_IMPLEMENTATION_HANDOFF_2026-08-14.md`。
3. 已执行证据见 `docs/maple-runtime/VERIFICATION_2026-08-14.md`。
4. 旧 WinForms、SendInput 探针、net48 静态测试、历史设计和执行计划已删除；不得从 Git 历史恢复到生产路径。
5. React 不能发送原始按键或逐帧图像；生产输入只能经过已验收的 `Maple.InputBroker.exe`，并只提交抽象动作。
6. `SOURCE_READY` 或 macOS 交叉编译不等于 Windows PASS。WGC/WebView2、30-60 FPS、真实模型、DPAPI 和生产 Broker 必须保留独立实机证据。
7. 修改后运行 `node tools/verify-portable.mjs`；生产输入边界另运行 `tests/windows/production_input_contract.tests.ps1`，真实输入按当前实施计划保留证据。
