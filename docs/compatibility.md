# 微信 4.2 与 Windows 兼容性

| 对象 | 结论 |
| --- | --- |
| 本机 OS | Windows 11 家庭中文版 x64，Build 22631。 |
| 本机微信 | 运行中的 `Weixin.exe` 文件版本为 **4.2.0.13**。实机版在已选一对一会话完成新消息监听、免费合成和 Windows 播放。 |
| 微信 4.2.0.11 | 上游 README 记载该版本的实机验证，本项目未复测。 |
| 微信真实监听 | 已接入 **4.2.0.13**。限制在明确配置的账号和单一会话，仅处理新入站普通文字。分片集合变化或微信进程重启时要求重新开始。 |
| 模拟 Worker | Windows x64 Python 3.12.10，SQLite 3.49.1。固定合成账号 `demo-local` 和一对一会话 `demo-peer`。 |
| WPF 发布 | `win-x64` 自包含发布，含独立的 Python 3.12 Worker 运行时。 |

上游版本依据见 [v1.2.9 中文 README](https://github.com/fanyuantaier/wechatauto-replica/blob/bc2e6f2461330eb1a53996d2f2afc18416376b52/README.zh-CN.md)。

本机结果见 [单会话实机报告](wechat-live-test.md)。其他微信版本及无需重启的分片轮换仍待验证。
