# WeChat Voice Bridge V1.2 测试报告

测试环境为 Windows 11 x64 Build 22631、.NET SDK 8.0.427、发布包自带 Python 3.12.10 x64 与 SQLite 3.49.1。测试使用合成 SQLite 数据和 HTTP 替身。Windows 音频测试使用真实默认 WASAPI 端点，应用设置为静音。

| 检查 | 结果 |
| --- | --- |
| `dotnet build` Release | PASS，0 警告、0 错误 |
| `dotnet test` Release | PASS，26 项通过，0 项失败或跳过 |
| Python unittest | PASS，10 项通过，包含加密 WAL 适配器测试 |
| x64 发布版 WPF 冒烟 | PASS，启动无 Worker，授权后插入模拟消息，结束、再开始及关闭窗口均符合预期 |
| 发布版意外退出 | PASS，强制终止测试父进程后 Worker 退出，重开保持 IDLE |
| 本地音频 | PASS，WASAPI 测试覆盖 WAV、MP3、分块 PCM 和 PCM 播放中取消，音量为 0 |
| 源码扫描 | PASS，未命中禁用的微信进程 API、上游导入或明显明文 Bearer 密钥模式 |
| NuGet 漏洞扫描 | PASS，当前源未报告直接或传递依赖漏洞 |
| 真微信数据库及微信消息链路 | **PASS**，发布版为指定联系人完成两条新消息的读取与播放。[实机记录](wechat-live-test.md) |
| 真实 Fish API、私有克隆音色 | **PASS**，S2.1 Pro Free 实机返回有效音频。Drama 3 的请求头切换使用 HTTP 替身测试。 |
| 音质、物理首音、真实蓝牙或 USB 拔插 | NOT TESTED |

可复现命令为 `scripts/Test.ps1 -Benchmark -AudioDevice`、`scripts/Smoke-Published.ps1` 和 `scripts/Verify-Source.ps1`。完整 .NET 测试项与运行时间见 `artifacts/TestResults/automated.trx`。

## 需求用例对照

| 原用例 | 模拟或本机测试 | 结果 |
| --- | --- | --- |
| 1–3 | WPF 启动保持 IDLE、无 Worker，模拟监听器尚未建立 | PASS |
| 4–7 | STARTING、就绪后 RUNNING、失败回滚、拒绝第二次开始 | PASS |
| 8–11 | 开始结束时立即关闭准入、取消合成、停止播放、清空 Channel | PASS |
| 12–14 | 真实模拟监听器释放、Worker 正常退出、无响应 Worker 与后代由 Job 回收 | PASS |
| 15–17 | 迟到的合成响应和旧会话消息被拒绝，旧模拟源失去监听器和消费者 | PASS |
| 18–20 | 新 ID 和 Generation、旧队列失效、关闭正在监听的发布版窗口后退出 Worker | PASS |
| 21–24 | 过滤出站、其他会话、系统、未知方向和非文本，覆盖图片与语音 | PASS |
| 25–29 | 同文不同键、连续五条、重复查询、分片分页合并、同序号晚到及 ACK 规则 | PASS |
| 30–31 | 注入账号变化，修改模拟数据库会话绑定；真实微信账号切换仍待验证 | PASS |
| 32 | 未提交 WAL 数据不可见，提交后增量查询发现新消息 | PASS |
| 33–35 | 注入忙碌与 watcher 错误，模拟查询恢复；SQLite 表丢失及致命协议错误 | PASS |
| 36–37 | 非法 JSON、无响应 Worker 及其子进程的回收 | PASS |
| 38–40 | HTTP 替身覆盖超时、429、5xx、402、401/403 和 404 | PASS |
| 41 | 失效设备 ID 显示错误且不改用扬声器；真实拔插仍待验证 | PASS |
| 42–44 | 合成中结束、播放中结束、准备中取消及快速重启 | PASS |
| 45 | 强制结束测试版应用，确认 Job 回收 Worker，重启无自动监听 | PASS |

音频夹具 `tests/VoiceBridge.Tests/assets/tone.mp3` 为 440 Hz 正弦测试音，通过 FFmpeg 生成。测试运行无需 FFmpeg。
