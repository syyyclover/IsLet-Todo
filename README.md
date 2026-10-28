# IsLet-Todo

常驻 Windows 屏幕顶部的本地日程工具，项目代号 **TaskIsland**。通过灵动岛完成日程查看、编辑、提醒、归档和附件管理，强调低打扰、低资源占用和本地数据可靠性。

## 当前状态

2026-10-28：M1 系统骨架开发中。已建立 WPF 主窗口、托盘、每用户单实例、通知注册和自包含发布配置；14 项自动化测试通过，进程启动、二次启动转发和关闭退出已验证。任务管理、持久化、附件仍未实现。通知点击、DPI、鼠标交互和干净机器运行尚待验收。

## 文档入口

- [产品与技术需求基线](IsLet_Todo.md)：功能、交互、时间规则、数据模型及验收标准的事实来源。
- [开发目标与阶段门槛](docs/DEVELOPMENT_PLAN.md)：范围、模块边界、执行顺序、交付物及风险。
- [Agent 开发规则](AGENTS.md)：代码与协作约束，每次开发前阅读。
- [M1 验证记录](docs/validation/M1-2026-10-28.md)：执行命令、实测结果及阶段阻断项。

## 首版方向

- Windows 11 x64；简体中文，固定深色主题，普通用户权限。
- C# / .NET 10 / WPF / MVVM；SQLite 本地存储。
- Windows Forms 托盘、Windows App SDK 通知、Windows 原生单实例及窗口集成。
- xUnit 自动化测试；Windows x64 自包含、未打包目录发布。
- 单机使用，首版不包含账号、云同步、重复任务、AI 或自动更新。

## 构建与运行

在 Windows 11 x64 上使用 `global.json` 指定的 .NET 10 SDK。若本机没有该 SDK，可用项目本地安装脚本，不修改全局 SDK：

```powershell
./scripts/Install-Sdk.ps1
./scripts/Build.ps1 -Publish
./artifacts/publish/m1-win-x64/TaskIsland.exe
```

`Build.ps1` 顺序执行锁定依赖还原、全部测试、构建和可选发布。发布会清理并重新创建 `artifacts/publish/m1-win-x64`，不要在其中存放个人文件。普通启动顶部显示“暂无日程”；托盘提供设置、归档与退出。当前设置仅在本次运行有效，任务入口为明确的阶段占位。

```powershell
./scripts/Smoke.ps1
./scripts/Measure-Idle.ps1 -Seconds 300
./scripts/Build.ps1 -Configuration Debug
```

Smoke 脚本启动并关闭自己创建的进程；资源测量脚本退出时清理自己创建的进程。执行前请退出已有 IsLet-Todo。Debug 构建的岛内设置页提供测试通知按钮，Release 不包含该入口。日志保存在 `%LOCALAPPDATA%\TaskIsland\logs\integration.log`，不记录任务正文或来源文件路径。

M1 尚未通过全部门槛：本机首启和发布体积已出现目标偏差，见验证记录。完成集成与部署验收后再推进 M2。

## 仓库

- GitHub：<https://github.com/syyyclover/IsLet-Todo>
- SSH：`git@github.com:syyyclover/IsLet-Todo.git`
- 主分支：`main`。后续功能使用短期分支，经验证后集成。
