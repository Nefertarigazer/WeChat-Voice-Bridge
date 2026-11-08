# 使用说明与更新弹窗界面验收

本轮以独立 WPF 工程加载实际的 `App.xaml` 资源、`ReleaseNotesDialog`、`UserGuide` 和 `ReleaseHistory` 源文件；版本标识在验收工程中设为 1.4.0。它不加载主程序的设置、凭据、微信模块或语音服务，也不启动监听。工程和截图仅写入本地 scratch。

## 结果

| 项目 | 结果 |
| --- | --- |
| 独立 Release 构建 | PASS，TreatWarningsAsErrors 下 0 警告、0 错误 |
| 指南章节 | PASS，13 个详细分组和 6 条版本记录，首节默认展开 |
| 快捷跳转 | PASS，点击「多人和开头话」展开对应章节并将标题移入视口；「更新日志」可定位历史区 |
| 最新日志 | PASS，最新版本默认展开；历史版本可逐条展开 |
| 宽度适应 | PASS，920 DIP 和 600 DIP 验收窗口截图检查，正文换行、无横向裁切 |
| 弹窗层次 | PASS，独立暖白底色、1 DIP 边框、单独阴影底层和外部留白可见 |
| 窗口开销 | PASS，未启用 AllowsTransparency；阴影作用于单独背景层，不对文字应用效果 |
| 原生拖动命中 | PASS，向实际测试 HWND 发送 WM_NCHITTEST，标题空白区域为 HTCAPTION，关闭按钮为 HTCLIENT |
| 三种关闭 | PASS，右上叉号、知道了按钮和实际 Esc 键消息均可关闭弹窗 |
| 工作区限制 | PASS，实际尺寸不超过按所属显示器逻辑尺寸计算的上限，正文区可滚动，底部关闭按钮保留 |

截图目录：`D:\USERNAME\CodexScratch\Temp\wvb-guide-ui-a609640d9ee14e00b7c61056bc796bbe\publish`，包括 `release-dialog.png`、`guide-start.png`、`guide-multi.png`、`guide-history.png`。独立工程的 `App.xaml.cs` 保留可重复执行的验收逻辑。

本次未调用 Fish、其他云端 API 或真实微信数据库。完整主窗口集成、首次配置判定和多服务/多人业务测试由相应模块的集成验收覆盖。

窗口边框使用 WPF 的 [WindowChrome](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome)；阴影背景与正文分离，避免给正文添加模糊效果。
