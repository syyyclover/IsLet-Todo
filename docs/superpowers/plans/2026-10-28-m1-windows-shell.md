# M1 Windows shell Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 交付可构建、可发布的 Windows 灵动岛系统骨架，证明平台集成路径。

**Architecture:** WPF 单主窗口；Application 保存与平台无关的定位和状态规则，WindowsIntegration 实现 Win32、托盘、通知和每用户 IPC。Presentation 装配所有能力，后续阶段再加入任务业务。

**Tech Stack:** .NET 10、WPF、CommunityToolkit.Mvvm、Windows App SDK、xUnit。

**Spec:** `IsLet_Todo.md` 第 2.5、2.10、2.11、3.7、5.1 节；`docs/DEVELOPMENT_PLAN.md` M1。

## Global Constraints

- Windows 11 x64，普通用户权限；单主窗口、不抢焦点、用户操作留在岛内。
- 收起 240×40 DIP、展开 360 DIP，顶部 8 DIP、左右 16 DIP；悬停 150ms、离开 400ms、动画约 220ms。
- 测试通知入口仅在 Debug 编译；Release 不包含演示任务或调试入口。
- 自包含目录发布，锁定实际稳定依赖；未完成实机测试不得标为通过。

## Review Focus

- 负显示器坐标和高 DPI：定位始终留在工作区，左右锚点在尺寸变化期间保持稳定。
- 编辑锁定和快速移入移出：离开时不关闭受保护页面，不排队播放动画。
- 同一用户二次启动：转发请求后退出，不出现第二个岛；IPC 拒绝其他用户。
- 通知异常和无效参数：启动不崩溃，岛内反馈，参数不能变为任意命令。
- 退出和系统显示设置变化：释放托盘、IPC 和通知订阅，窗口重新定位。

## Tasks

### 1. SDK、工程和纯规则测试

Files: `global.json`、`Directory.Build.props`、`TaskIsland.slnx`、`src/TaskIsland.Application/`、`tests/TaskIsland.Application.Tests/`。

- [x] 从官方元数据安装项目本地 SDK，查询 NuGet 稳定版本，建立锁文件。
- [x] 为工作区定位、编辑页面防收起、通知参数验证写失败测试。
- [x] 实现 `IslandPlacement.Calculate(...)`、`IslandState`、`ActivationRequest.Parse(string)`，运行整个测试项目。

### 2. Windows 系统能力

Files: `src/TaskIsland.WindowsIntegration/`。

- [x] 实现每用户互斥锁、仅当前用户命名管道和有界消息，实测第二次启动转发。
- [x] 实现托盘、工作区/DPI 原生定位与通知注册/点击事件；异常返回岛内状态。

### 3. WPF 主窗口

Files: `src/TaskIsland.Presentation/`。

- [x] 实现深色胶囊、空状态、岛内设置与归档占位，明确标记尚未实现业务。
- [x] 实现一次性悬停/离开计时、可中断尺寸动画、锚点、减少动画和键盘返回。
- [x] 通知、托盘和 IPC 请求统一派发到 UI 线程，退出释放资源。

### 4. 构建、发布和验收

Files: `scripts/`、`docs/validation/`、`README.md`、`docs/DEVELOPMENT_PLAN.md`。

- [x] 运行测试、Release 构建、自包含发布，检查 Debug 入口未进入 Release。
- [x] 启动发布产物，检查进程、单实例、启动/资源基线和错误日志。
- [x] 记录实际环境及不能执行的鼠标、DPI、通知点击、干净机器验收，不跨过 M1 门槛启动 M2。
- [x] 独立代码审查，修复重要问题，检查 Git 差异后提交。

## Execution ledger

- 2026-10-28：当前工作区无修改，创建 `feat/m1-windows-shell`；复用当前 checkout，未创建额外 worktree。
- 环境：Windows build 26100 x64，初始仅有 .NET 8 SDK；从微软发布元数据选定 10.0.403 SDK，安装到忽略的 `.tools/dotnet`。
- 本计划执行到 M1 实机门槛；M2–M5 依照原路线，M1 未实测通过前不提前标记或扩展。
- Task 1: complete — 纯规则 10 项先因 NotImplementedException 失败，再 10/10 通过。
- Task 2/3: implemented — 真实管道集成测试、进程级 Smoke 通过；交互及通知点击仍按验收清单待验证。
- 独立审查：发现 UI 上下文退出死锁与窗口关闭残留进程，均已复现并修复。
- Final: fixed UI shutdown deadlock — DisposalDoesNotWaitForUiSynchronizationContext RED→GREEN, suite 14/14。
- Final: fixed window-close process leak — Smoke WM_CLOSE 超过 5 秒未退出 RED→GREEN，suite 14/14。
- Ruling: 保持 WPF / 自包含 / 通知基线，超预算时记录证据而不手工删除运行库 — 需要后续优化并复测，M1 门槛暂未通过。
- Ruling: Foundation 子包替代 Windows App SDK 总包 — 仅引用本阶段 API，消除未用 AI/ML/WinUI 运行依赖；若遗漏运行依赖，干净机器验证会暴露，当前不能宣称通过该验收。
- 最终测量与未验证项见 `docs/validation/M1-2026-10-28.md`。没有合并到 main。
