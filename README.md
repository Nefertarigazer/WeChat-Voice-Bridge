# WeChat Voice Bridge V1.2 · 微信语音桥

Windows x64 单会话朗读器。支持微信 **4.2.0.13**：把指定联系人新发来的文字交给 Fish Audio 克隆音色合成，并用 Windows 默认输出设备播放。

> **📄 完整项目介绍与交接文档：[`docs/PROJECT-HANDOVER.md`](docs/PROJECT-HANDOVER.md)**
> 含背景、技术选型与理由、系统架构、代码结构、发布形态、安全姿态、当前状态、代码位置速查、已知坑清单、验证方法与遗留项。新接手本项目请先读这一份。

## 立即运行

界面左侧栏分四页：**首页 / 音效 / 词库 / 高级**。

1. 安装下文的单文件安装包，或完整解压实机版 ZIP 后运行其中的 `WeChatVoiceBridge.exe`。解压版需要保留同目录的文件、`worker` 和 `runtime`。发布版自带 .NET 8、Python 3.12 x64 和 SQLCipher。
2. 「高级」页配置：**Fish API Key**、**微信数据目录**（点「浏览…」选文件夹）。应用会自动发现数据根目录；**账号目录也自动填好，只有一个账号时这一整行不显示**，有多个账号时才出现下拉框让你选。
3. 回「首页」在「对方」区搜索昵称、备注或微信号，或直接粘贴对方的微信号后按回车。结果里第二行显示的是**微信号**（没有微信号时才显示 wxid）。
4. 点「开始」，等待状态变成「正在监听」。此后对方发来的新文字会自动朗读。点「结束」停止当次监听。
5. 「试听最后一条消息」会读取该会话最新一条可读的入站消息并朗读，用来验证整条链路是否通畅。

「音效」页调音色、模型、音量与输出设备；「词库」页一张表管所有自定义读法——每行是「原文 → 读作」，加一档「出现方式」（单独出现时 / 句中出现时 / 单独 + 句中）。**行内不能直接改，点「编辑」才进入编辑态**（按钮变成「完成」），底部居中的「＋」加一条空记录，没填完就点「完成」会自动删掉。符号和词不再分开两处。

「高级」页还有**缓存大小上限**（默认 4 GB）：缓存超过上限会自动清掉最旧的会话，退出应用时也会清空，所以不会把 C 盘撑爆。

音色、模型、音量、输出设备、联系人和自定义读法都会自动保存，**下次打开就是上次的选择**。运行中切换音色，下一条消息即用新音色。

停止与退出：点「结束」停止当次监听；**最小化只缩到任务栏，任务栏按钮保留**；托盘可「显示窗口 / 结束监听 / 退出程序」；关闭窗口等价于退出，会做完整清理（停止播放、回收 Worker、删除会话缓存副本）。

合成模型默认使用免费模型 `s2.1-pro-free`，可在「合成模型」手动切换到按量付费的 Drama 3；付费额度不足时不会自动回退或充值，需要你手动处理。文字会发送到 Fish Audio 云端合成，声音克隆需要声音本人的同意。[Fish Audio 计价](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits) · [隐私政策](https://fish.audio/privacy/)

## 分发与安装包

给别人用，直接转 **`artifacts/WeChat-Voice-Bridge-V1.4.1-Setup.exe`**。双击即可打开中文安装向导，安装包内已包含运行时。

安装是**按用户安装**：装到 `%LOCALAPPDATA%\Programs\微信语音桥`，不需要管理员权限、不弹 UAC，双击一路点「下一步」即可。安装向导是中文的，装完会：

- 自动打开应用（也可以在最后一步取消勾选「启动 微信语音桥」）；
- 在桌面和开始菜单建快捷方式，并在「设置 → 应用 → 卸载程序」里留一个卸载入口。

应用左侧最后一项是「使用说明」。监听时保存词条或切换 Fish API Key，下一条开始合成时使用新设置。

升级后首次打开会显示一次更新说明，关闭后登记已读。说明内容位于 `ReleaseNotesDialog.xaml`，发布新版本时同步维护。

卸载时可以选择是否一并删除本机设置和 API Key，默认保留。重装后可继续使用这些设置。

> 安装包没做代码签名，转发后 Windows SmartScreen 可能提示「未知发布者」。

`artifacts/WeChat-Voice-Bridge-V1.3.3-win-x64.zip` 是免安装的完整包，解压后直接跑 `WeChatVoiceBridge.exe`，适合不便安装的场景。

## 开发与发布

需要 Windows x64、.NET 8 SDK。开发 Worker 需要 Python 3.12 x64 和 `py` 启动器。真实模式还需要发布脚本安装的 SQLCipher。PowerShell 在项目根目录运行：

```powershell
.\scripts\New-WorkerVenv.ps1
.\scripts\Run.ps1 -Development
.\scripts\Test.ps1 -Benchmark -AudioDevice
.\scripts\Publish.ps1
.\scripts\Build-Installer.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File scripts\Smoke-Published.ps1
.\scripts\Verify-Source.ps1
```

测试脚本优先使用 `.venv`，也支持发布包自带的 Python。省略 `-AudioDevice` 时跳过需要 Windows 音频设备的测试。省略 `-Benchmark` 时跳过基准测试。可给脚本传入 `-Dotnet` 指向独立安装的 `dotnet.exe`。

`Verify-Source.ps1` 是安全边界守卫：它会拦截写内存/注入/钩子调用与疑似密钥，并强制 `ReadProcessMemory` 只能出现在 `worker/audited_wechat_keys.py` 一个文件里。交付前请务必跑通。

## 文档索引

- [**项目介绍与交接文档**](docs/PROJECT-HANDOVER.md) — 背景、技术选型、架构、交接要点（**入口文档**）
- [会话生命周期与 IPC](docs/architecture.md)
- [微信 4.2 兼容性](docs/compatibility.md)
- [本机微信单会话实测](docs/wechat-live-test.md)
- [上游源码安全审计](docs/security-review.md)
- [测试结果与覆盖范围](docs/test-report.md)
- [模拟性能基准](docs/benchmark.md)
- [排障与已知限制](docs/troubleshooting.md)
- [首次配置与声音授权](docs/setup.md)

> `docs/` 下除本 README 外其余文档写于项目早期版本，个别界面描述（页签、授权勾选、模拟模式）与当前版本不一致，以 `docs/PROJECT-HANDOVER.md` 为准。
