# 悬停动画卡顿修复

需求：用户反馈 M1 悬停展开卡顿，希望接近 Lyricify 灵动词岛的流畅感。沿用原基线 150ms 悬停、400ms 离开、约 220ms 动画及减少动画支持，不扩展产品范围。

观察：现实现直接动画 Window.Width/Height；SizeChanged 每次查询工作区/DPI 并重设 Left/Top；内容随窗口尺寸重新排版。微软 WPF 布局性能文档确认布局属性变化会增加测量/排列工作。

修复路线：

- [x] 在真实 STA WPF 测试中统计展开的窗口 SizeChanged 次数，复现逐帧窗口尺寸变化。
- [x] 仅在展开开始、收起结束改变原生窗口边界；动画期只改变胶囊的圆角裁剪形状、文字位移和内容透明度。
- [x] 内容固定最终尺寸，不缩放文字；折叠后窗口恢复 240×40 DIP，避免常驻大透明区域。动画期余白保持零 alpha，跨进程穿透仍待人工实测，HTTRANSPARENT 不作为跨进程保证。
- [x] 保持固定锚点、中途反向切换、页面保护、减少动画、键盘返回；移除每帧原生 DPI/工作区查询。
- [x] 运行 WPF 回归、全套测试、构建、发布和进程 Smoke；记录结构性改进与仍需人工确认的流畅感，不把单测当主观动效验收。

参考：[Lyricify 公开仓库](https://github.com/WXRIW/Lyricify-App)、[WPF 布局性能](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-layout-and-design)。未复制其非公开实现、素材或动效曲线。

## 执行记录

- RED：真实 WPF 窗口展开 SizeChanged=20，超出 0–2 次边界更新要求；不是纯数学模拟。
- GREEN：渲染裁剪方案 SizeChanged=2，仅边界宽高配置产生通知；不以此推断 FPS。
- RED→GREEN：关闭动画设置时立即停止当前动效；新增属性订阅后通过。
- 独立审查发现收起时页面先 Collapsed 导致正文闪退。回归测试先失败，保留离场页直到父级淡出和收起结束后通过。
- 全套 19/19 通过；Release 构建 0 警告、0 错误，自包含发布通过。
- 仍未通过用户机器上的主观顺滑度、不同刷新率/DPI、跨进程透明区穿透和完整 M1 验收。
