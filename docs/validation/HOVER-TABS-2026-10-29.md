# 页签悬停切换

日期：2026-10-29。用户要求进入下一阶段前，日程、归档、设置页签无需点击，鼠标移入即切换。

## 设计与执行

沿用现有页签位置、样式与约 220ms 左右滑动。MouseEnter 直接选择页面，不增加计时器、不主动激活窗口；点击与键盘激活使用同一路径。同一页面重复进入不重启动画，快速切换由已有动画代次处理最后目标。

浏览页签保留编辑草稿与未确认的时间选择，回到日程列表后通过添加入口继续。放弃编辑仍通过取消/返回的双击确认。离开岛后回到日程列表并收起。外部拖入期间不通过页签悬停切走，保留具体任务的拖放目标。

执行顺序：先增加悬停即时切换、快速往返最终状态、点击保留输入测试，观察旧实现两项失败；再实现共用页面选择路径；补充真实 SQLite 装配下编辑草稿及时间选择往返测试。此次只调整页签导航，不进入下一阶段。

## 验证记录

- 新增两项回归测试先失败，修复后两项通过。
- 首次全套 Release：Domain 19、Application 20、Infrastructure 56、WindowsIntegration 23 均通过；Presentation 73 通过、1 失败。失败为既有开合动画单调性测试 `CompletingMotionNeverPublishesOppositeShape(expanding: true)`；失败原因尚未确定，不据此宣称开合完全无回归。
- 单独重跑该开合测试的展开/收起两种情况：2 项通过。
- 全套 Release 重跑：192 项通过（Domain 19、Application 20、Infrastructure 56、WindowsIntegration 23、Presentation 74），无失败及跳过。
- `dotnet publish ... -c Release -r win-x64 --self-contained true --no-restore` 发布成功，未修改依赖。退出旧版后更新原发布路径，程序集 SHA256 与暂存发布一致。原路径新版已启动（PID 34588）；实际鼠标体验待联合验证。

开合动画实现未改动。真实悬停切换手感、快速往返、其他应用焦点及减少动画仍需 Windows 联合体验；既有 DPI、干净机器和性能门槛继续保留。
