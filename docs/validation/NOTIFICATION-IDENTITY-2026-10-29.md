# 未打包通知身份与联合验收

日期：2026-10-29。用户明确反馈：系统通知应用列表没有 TaskIsland/IsLet-Todo，免打扰已关闭。原实现虽然收到非零通知 ID，SDK 与 Windows 原生历史都有记录，实际界面仍不可见。

## 修订及边界

原 SDK 默认按可执行文件路径生成身份，不同发布目录已有多个独立身份。改为在首次访问 AppNotificationManager.Default 前调用 Windows API 设置稳定 `TaskIsland.Desktop` 身份，并用原生 IShellLinkW/IPropertyStore 在当前用户开始菜单创建 `IsLet-Todo.lnk`，使用同一个 AppUserModelID。仍由 Windows App SDK 注册通知 COM 激活器，没有更换通知技术栈或引入 MSIX/管理员安装。

自有快捷方式可随发布路径更新，同名非本应用快捷方式不覆盖；保存先写临时副本再替换。旧注册与旧通知历史保留，不重发旧记录，不修改系统通知开关。若旧路径对应的通知偏好已关闭且用户未明确为稳定身份启用通知，停止新的注册，避免借换身份绕过偏好。

这是对身份和 Shell 可发现性的受控修订，不能据旧版 Win32 文档推断 Windows App SDK 在所有环境都强制要求快捷方式。最终以本机对照实测作为修订有效性的证据。

## 验证结果

- 项目本地 SDK Release 全套测试：133 项通过，无跳过（Domain 19、Application 10、Infrastructure 37、WindowsIntegration 23、Presentation 44）。新增原生快捷方式创建、Unicode/空格路径、更新、非自有快捷方式保护及旧通知偏好测试；测试使用临时目录/注册表键，不发送测试通知、不修改实际开始菜单或进程身份。
- Release 自包含发布到 `artifacts/publish/m3-win-x64` 成功；启动时注册 `identity=TaskIsland.Desktop supported=True setting=Enabled`。
- 本机 `Get-StartApps` 返回 `IsLet-Todo / TaskIsland.Desktop`，实际开始菜单快捷方式存在，SDK COM 激活器注册存在。
- 用户通过正常编辑把任务时间改到未来并保存，明确反馈：**“通知出现，点击能定位”**。到期通知可见性和点击定位的本次联合验收通过；不将其外推为所有机器及通知场景通过。
- 首次启动日志中窗口出现约 4614ms，不代表可交互或冷启动测量，仍未达到可据此宣称性能通过的条件。性能预算保持原值。

全屏、多 DPI/多显示器、重新登录自启动、Explorer 重启、干净机器和完整性能验收仍未完成。完整 v1 尚未交付。

依据：[Windows App SDK 默认身份源码](https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppNotifications/AppNotificationUtility.cpp)、[Win32 桌面通知身份与快捷方式](https://learn.microsoft.com/en-us/windows/win32/shell/enable-desktop-toast-with-appusermodelid)。
