# 微信语音桥 1.3.3 发布记录

## 修改

- 更新说明弹窗改用自绘标题栏，与正文共享 `PageBg` 底色。
- 顶部仅保留右侧关闭按钮，移除可见图标和标题，保留窗口拖动、系统关闭和确认关闭。
- 保持升级后每个版本只展示一次的逻辑。
- `Directory.Build.props` 版本升至 1.3.3，README 下载文件名同步更新。

## 本地验证

- Release 构建通过，0 警告、0 错误。
- .NET 59 项、Python 41 项测试全部 PASS。
- 静态边界和明显敏感信息扫描 PASS。
- 隔离 WPF 实例验证旧配置首次升级、同版再次打开、较低已读版本升级、全新安装四种情况，全部 PASS。
- 实际窗口的命中测试验证顶部可拖动、关闭按钮可点击；关闭按钮与系统关闭均登记已读。截图确认底色一致、顶部无图标标题。
- ZIP 完整性和私人配置文件排除检查 PASS，内置 Python、SQLCipher 和 zstandard 冒烟检查 PASS。
- 桌面「微信语音桥」快捷方式已指向 `artifacts/win-x64-v1.3.3/WeChatVoiceBridge.exe`。

本次界面验收使用隔离配置，关闭自动联网检查和微信账号发现，未访问真实微信聊天或调用 Fish。

## 发布文件

| 文件 | 字节大小 | SHA-256 |
| --- | ---: | --- |
| `WeChat-Voice-Bridge-V1.3.3-win-x64.zip` | 91925310 | `8d61dbba54269d9e3eddb90d0063809ce0a93186d2d5856790f2f2d0821fb702` |
| `WeChat-Voice-Bridge-V1.3.3-Setup.exe` | 65210018 | `e2566c14fe88b31eb5040569a7f8468d95bdcea0684541de3b7fac2575b88035` |

文件位于项目 `artifacts` 目录。安装包供首次安装，ZIP 用于软件内更新。正式清单地址保持 `https://iceland.de5.net/voice-bridge/latest.json`。

## 上线

2026-11-07 已上线并完成客户端正式 HTTPS 验收。

Muse 对原发布单 `codex_20261107_085222_bcf7a0` 和状态查询单 `codex_20261107_092301_4d3041` 均回传 `status=ok`。回执说明，首单耗时来自安全确认和传输路线切换，最终采用 Legion 直推 A1；服务器上两份文件的 SHA-256 与上述本地交接值一致。正式清单 `publishedAt=2026-11-07T08:57:34Z`，原清单备份为 `latest.json.bak-1.3.0`。

桌面端独立实测结果如下，均使用正式域名及默认 TLS 证书校验。

| 验收 | 结果 |
| --- | --- |
| 清单 | HTTPS 成功读取 1.3.3，ZIP URL、SHA-256 和 91925310 字节与本地交接一致。PASS |
| 当前版检查 | 调用发布包实际的 UpdateClient，客户端版本 1.3.3 返回无更新。PASS |
| 旧版本发现 | 同一组件使用 1.3.0 版本标识，发现 1.3.3，返回元数据一致。PASS |
| 完整更新包下载 | 调用实际 DownloadAsync，从正式公网 URL 下载到隔离 scratch 目录，进度到 100%，文件大小和 SHA-256 校验通过。PASS |
| 文件可达性 | ZIP 和 EXE 的公网 HEAD 均为 200，Content-Length 分别为 91925310 和 65210018。PASS |
| 下载页 | `https://iceland.de5.net/` 可访问，含 1.3.3 EXE 安装包链接。PASS |

公网文件地址：

- 安装包：`https://iceland.de5.net/voice-bridge/WeChat-Voice-Bridge-V1.3.3-Setup.exe`
- 更新 ZIP：`https://iceland.de5.net/voice-bridge/WeChat-Voice-Bridge-V1.3.3-win-x64.zip`

本次正式联调未在用户正在使用的应用目录执行覆盖安装；安装助手和回滚的隔离测试记录见 `update-release.md`。客户端独立完整下载校验覆盖 ZIP，EXE 的完整服务端哈希核验依据 Muse 回执。
