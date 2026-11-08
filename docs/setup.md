# 首次配置与声音克隆

## 本地试音

打开「设置与试音」，选择 Windows 默认输出或一个明确的扬声器、USB 或蓝牙设备。用「测试本地提示音」确认输出，测试期间顶部可以取消。显式选定的设备断开时会提示，下一条音频重新打开同一个设备。选择 Windows 默认输出时，每条音频重新解析系统默认设备。

## 配置私有音色

1. 先取得声音本人的明确授权，范围应涵盖录音、语音克隆、云端处理及使用场景。可以准备约 30–60 秒的自然普通话独白，尽量避免混响、背景音乐和其他人的声音。
2. 使用 Fish 的音色管理页面创建私有音色，核对可见性并等待可用，复制 Voice ID。应用仅使用 Voice ID 发起合成。创建音色和删除云端音色都由用户在 Fish 完成。[官方声音克隆文档](https://docs.fish.audio/features/voice-cloning)
3. 在「设置与试音」输入 Fish API Key 和私有 Voice ID，点击「保存设置」。密钥使用 Windows 当前用户 DPAPI 加密写入本地文件。密码框留空时会使用已保存的密钥。
4. 编辑一段获授权的试音文字，勾选单独的 Fish 试音授权，点击「测试 Fish 语音」。默认完整 MP3，延迟参数为 `balanced`，也可选择 `normal` 或 `low`。启用实验性 PCM 时，遇到格式或设备问题可取消该选项回到完整 MP3。
5. 如需用模拟消息测试 Fish，把「对模拟会话使用 Fish Audio」显式勾选。启动每次会话时仍要确认当次授权。

Fish 官方支持用 MessagePack 临时附带参考录音和逐字稿。若账户无法创建所需的私有持久音色，可在服务商官方工具或 SDK 中明确选择临时参考方案。应用的试音和消息朗读目前只接受已建立的 Voice ID。此账户是否能创建私有持久音色仍需实测。[临时参考音频接口](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech) · [声音克隆文档](https://docs.fish.audio/features/voice-cloning)

## 免费服务与数据

应用默认设置 `model: s2.1-pro-free`，也可从「会话」页手动选择按量付费的 `drama-3`。切换适用于下一条合成。重新打开程序时恢复免费。Fish 按输入文本的 UTF-8 字节数为 Drama 3 计价，促销单价为 10 美元/百万字节，官方列出的促销截止时间为 2026 年 11 月 12 日 17:00 UTC，之后标价为 50 美元/百万字节。[计价和限流](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits) · [免费模型公告](https://fish.audio/zh-CN/blog/s2-1-pro-free-api/?articleLocale=en) · [隐私政策](https://fish.audio/privacy/)

设置页可删除本地 Voice ID、删除 API Key 或清理模拟缓存。本地删除仅影响本机配置。云端音色和参考录音的处理以 Fish 的账户功能和隐私政策为准。
