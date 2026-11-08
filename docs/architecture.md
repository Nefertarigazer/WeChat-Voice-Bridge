# 会话与处理链

```text
WPF → SessionController → Python Worker（真实 SQLCipher WAL 或模拟 SQLite WAL）
            ↑               ↓ stdout JSON Lines
            └── 准入 / ACK → 有界 Channel → Fish 或本地提示音 → NAudio WASAPI
```

## 生命周期

启动只装载自有设置、显示界面和枚举输出设备。用户授权并开始后，真实模式检查微信版本及所选账号目录。模拟模式使用固定账号与会话。创建 Session ID、单调 Generation 和取消源后，宿主启动 Python 并分配到 Windows Job Object，随后才发出 `start`。

Worker 在真实模式使用经过认证的加密主库和 WAL 副本，模拟模式则初始化两份合成 SQLite WAL 数据库。两种模式都建立水印并发出 `capabilities` 和 `ready`。宿主启用监听器后切换 RUNNING，启动即查询一次以覆盖基线到 RUNNING 的时间差。测试消息请等到 GUI 显示「正在监听」后再发。

结束在状态锁内切换 STOPPING、撤销 Generation、关闭并清空 Channel、取消会话，提交停止音频操作。播放提交也要经过同一状态锁及独立语音取消检查。旧会话在合成、播放提交、IPC 准入等边界都会被拒绝。Worker 先结束查询并释放监听器，再尝试用 `stop` 正常退出。650 ms 宽限期后关闭启用了 kill-on-close 的 Job 句柄，由 Windows 回收整个进程树。进程崩溃会关闭 Job 句柄，Worker 也会在管道 EOF 退出。停止完成才回到 IDLE，致命错误进入 ERROR 并由用户重置。

## 查询与去重

模拟源使用已知的两份分片及其 WAL 名称。FileSystemWatcher 回调只合并信号和记录本地 Stopwatch 时间。文件变更只是查询线索。增量查询、模拟写入命令、ACK 和轮询都由同一个调度器串行执行。默认 30 ms 合并窗口、50/100/200 ms 短重试、1000 ms 兜底。监听器错误触发额外查询，兜底轮询持续运行。

每个合成分片按插入 ID 读取有界页面。模拟写入按分片分配非递减排序序号，页面按序号、分片和本地 ID 合并。确认上一批已经登记后才推进分片游标。同序号稍后提交仍可读到。宿主二次验证账号、目标、入站和文本类型，使用分片与本地 ID 复合键去重。队列默认 30 条，超长（1000 UTF-16 单元）和队满新消息登记为丢弃并显示结果。已登记消息在播放失败后结束处理，下一会话以新的基线开始。当前实现遇到进程掉线时结束会话，尚未提供同会话断线续接。

## IPC 与音频

UTF-8 JSON Lines 经重定向的 stdin/stdout，逐帧携带 `session` 和 `generation`。命令 `start`、`query`、`demo`、`ack`、`stop`。应答包括 `capabilities`、`ready`、`message`、`heartbeat`、`warning`、`error`、`ack`、`injected` 和 `stopped`。旧代际包丢弃，坏包与超时导致停止。stderr 排空后丢弃，GUI 只显示固定的脱敏状态。

完整 MP3 在内存收集和解码。实验性 PCM 请求 24 kHz、单声道、16 位原始数据，跨网络块保留半个采样，最多缓冲约一秒，80 ms 启播阈值。欠载时填静音，EOF 后读完尾部，取消或失败丢弃剩余音频。用 Stopwatch 记录收到首个完整 MP3 帧或 PCM 采样，以及向 WASAPI 提交播放和播放完成。提交时刻是软件侧指标。
