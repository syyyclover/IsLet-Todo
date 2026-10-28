# IsLet-Todo

常驻 Windows 屏幕顶部的本地日程工具，项目代号 **TaskIsland**。通过灵动岛完成日程查看、编辑、提醒、归档和附件管理，强调低打扰、低资源占用和本地数据可靠性。

## 当前状态

2026-10-28：已整理需求基线、开发路线和协作规则。尚未创建应用工程、安装依赖或实现功能，构建和产品验收均未开始。

## 文档入口

- [产品与技术需求基线](IsLet_Todo.md)：功能、交互、时间规则、数据模型及验收标准的事实来源。
- [开发目标与阶段门槛](docs/DEVELOPMENT_PLAN.md)：范围、模块边界、执行顺序、交付物及风险。
- [Agent 开发规则](AGENTS.md)：代码与协作约束，每次开发前阅读。

## 首版方向

- Windows 11 x64；简体中文，固定深色主题，普通用户权限。
- C# / .NET 10 / WPF / MVVM；SQLite 本地存储。
- Windows Forms 托盘、Windows App SDK 通知、Windows 原生单实例及窗口集成。
- xUnit 自动化测试；Windows x64 自包含、未打包目录发布。
- 单机使用，首版不包含账号、云同步、重复任务、AI 或自动更新。

## 后续开发起点

先执行开发路线中的 M1，验证窗口、托盘、通知激活和自包含发布。通过 Windows 实测后再推进任务业务；具体构建、测试和发布命令在工程创建并实际执行后补充。

## 仓库

- GitHub：<https://github.com/syyyclover/IsLet-Todo>
- SSH：`git@github.com:syyyclover/IsLet-Todo.git`
- 主分支：`main`。后续功能使用短期分支，经验证后集成。
