# 微信 4.2.0.13 单会话实机测试

实机发布版的后续验收已经通过。它为本机当前配置的一对一会话建立水印，持续监听，并对两条新入站文字完成 S2.1 Pro Free 合成和 Windows 音频播放。界面记录的 SQLite 查询分别约 115 ms，收到首个音频帧约 0.77 秒和 1.91 秒。后两个数值从合成阶段开始计时。Drama 3 的切换由自动化测试验证请求头。

测试日期为 2026-11-05。用户授权测试，并让对方发送一条新测试文字。Windows 微信 4.2.0.13 显示该消息为左侧入站气泡。常规 Windows UI Automation 树未返回消息正文。

普通 SQLite 以只读方式检查 4 个消息分片，全部返回 `SQLITE_NOTADB`。隔离的 SQLCipher 探针随后认证了 4 个消息分片的加密首页，在读取的第一个消息分片上确认了目标会话表，并查到 **1 条**与测试文字匹配的入站纯文本。查询用目标的稳定账号标识与分片内 `Name2Id` 确认发送者，结果只输出计数。

| 检查 | 实测结果 |
| --- | --- |
| 合成加密库中尚在 WAL 的已提交记录 | PASS，探针成功读到记录并验证源文件元数据未变 |
| 4.2.0.13 加密消息分片密钥认证 | PASS，4 个分片 |
| 目标联系人绑定及消息表确认 | PASS，在本次查读的分片内确认 |
| 对方新发的指定普通文本 | PASS，匹配 1 条 |
| 发布版自动获取及微信到免费 Fish 的持续链路 | PASS，两条新文字完成播放 |
| 真实分片轮换 | 未实测。当前适配器在检测到分片集合变化后结束会话。 |

探针使用固定提交 `4931f98d4b4b68dc4af8cfc10f76805eb9c743da` 中的只读候选密钥读取和首页认证方法。它检查源码 SHA-256，只载入审核过的函数，不导入上游包，也不运行其构造函数或缓存、界面自动化和发送逻辑。Windows 进程操作限于对核对过版本的 Weixin 进程进行查询及只读内存访问，密钥只保留在测试进程内存。查询器为 `sqlcipher3 0.6.4` Windows x64 wheel，SHA-256 为 `23f867c0d14c9f0274fea62425c1b02fb812a56e766ea3e6e0c7d5e4e3a90cd3`，其 SQLCipher 版本为 4.19.0 Community。

SQLCipher 使用 `immutable=1` 时，在合成测试中漏掉了仅在 WAL 的记录。通过合成测试的方案把加密的主库与 WAL 临时复制到当前用户目录下，在副本上使用 SQLCipher `mode=ro` 和 `query_only`。这避免在微信原目录生成或更新 WAL 索引。探针没有创建明文数据库或密钥缓存，临时加密副本在查询结束时删除。原始测试代码保存在本地 `artifacts/wechat-live-probe.py`，未纳入发行包。

最初的独立探针只验证了单次读取。上方的后续实机验收覆盖了持续监听和播放。账号切换及真实分片轮换仍待验证。

[固定的上游源码](https://github.com/fanyuantaier/wechatauto-replica/blob/4931f98d4b4b68dc4af8cfc10f76805eb9c743da/wechatauto/db.py) · [SQLCipher 只读查询所用依赖](https://pypi.org/project/sqlcipher3/0.6.4/) · [SQLite WAL 文档](https://www.sqlite.org/wal.html)
