# IsLet-Todo

常驻 Windows 屏幕顶部的本地日程工具，项目代号 **TaskIsland**。通过灵动岛完成日程查看、编辑、提醒、归档和附件管理，强调低打扰、低资源占用和本地数据可靠性。

## 当前状态

2026-10-28：M2 基础任务与持久化已实现，74 项自动化测试通过。岛内支持添加、编辑、完成、置顶、续期、归档和恢复，SQLite 保存数据，后台调度处理时间规则。用户已确认动画流畅。日期时间滚轮、正式提醒和附件仍待 M3/M4；通知点击、多 DPI、干净机器及性能门槛尚未通过，当前不是完整 v1 发布版。

## 文档入口

- [产品与技术需求基线](IsLet_Todo.md)：功能、交互、时间规则、数据模型及验收标准的事实来源。
- [开发目标与阶段门槛](docs/DEVELOPMENT_PLAN.md)：范围、模块边界、执行顺序、交付物及风险。
- [Agent 开发规则](AGENTS.md)：代码与协作约束，每次开发前阅读。
- [M1 验证记录](docs/validation/M1-2026-10-28.md)：执行命令、实测结果及阶段阻断项。
- [M2 验证记录](docs/validation/M2-2026-10-28.md)：任务、SQLite、调度、界面回归与当前限制。
- [悬停动画修复记录](docs/validation/MOTION-2026-10-28.md)：20→2 次窗口尺寸变化、回归与未验证项。
- [开合闪动修复记录](docs/validation/FLASH-2026-10-28.md)：固定原生表面的取舍与动画终值回归。

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
./artifacts/publish/m2-win-x64/TaskIsland.exe
```

`Build.ps1` 顺序执行锁定依赖还原、全部测试、构建和可选发布。发布会清理并重新创建 `artifacts/publish/m2-win-x64`，请先退出应用，不要在其中存放个人文件。无任务时顶部显示“暂无日程”；展开后添加和管理任务，托盘提供设置、归档与退出。当前新建任务无结束时间；位置与动画设置仅在本次运行有效。

```powershell
./scripts/Smoke.ps1
./scripts/Measure-Idle.ps1 -Seconds 300
./scripts/Build.ps1 -Configuration Debug
```

Smoke 脚本启动并正常关闭自己创建的进程；资源测量完成后保留应用运行，请通过托盘退出。执行前请退出已有 IsLet-Todo。Debug 构建的岛内设置页提供测试通知按钮，Release 不包含该入口。数据库保存在 `%LOCALAPPDATA%\TaskIsland\data.db`，迁移备份保存在同一用户数据目录内；日志位于 `logs\integration.log`，不记录任务正文或来源文件路径。备份或移动整个数据目录前请正常退出应用，勿单独复制正在运行的数据库文件。

按用户继续开发指令推进 M2，同时保留 M1 未通过项。M2 发布目录为 205.52 MiB，超过 200 MiB 目标；启动与完整数据集性能仍需最终验收，不放宽预算。

## 仓库

- GitHub：<https://github.com/syyyclover/IsLet-Todo>
- SSH：`git@github.com:syyyclover/IsLet-Todo.git`
- 主分支：`main`。后续功能使用短期分支，经验证后集成。
