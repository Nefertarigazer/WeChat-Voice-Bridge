# 微信依赖安全审计

审计日期为 2026-11-05。以 Git 拉取源码检查，未安装或执行上游包。最新可见稳定标签 `v1.2.9` 固定到 `bc2e6f2461330eb1a53996d2f2afc18416376b52`，审计时默认分支头为 `4931f98d4b4b68dc4af8cfc10f76805eb9c743da`。两提交之间 `db.py` 和 `accessibility.py` 无变化，差异主要涉及 UIA、GUI 和测试。[稳定版提交](https://github.com/fanyuantaier/wechatauto-replica/commit/bc2e6f2461330eb1a53996d2f2afc18416376b52) · [审计时分支头](https://github.com/fanyuantaier/wechatauto-replica/commit/4931f98d4b4b68dc4af8cfc10f76805eb9c743da)

**上游整包接入判定为拒绝。** 实机版复用了固定提交中经审核的四个只读方法，单独运行 SQLCipher 查询器。模拟读取使用自行生成的 SQLite 数据库。

真实模式只在用户授权并开始会话后访问经过版本核验的微信进程和指定账号的消息分片。SQLCipher 打开会话目录中的加密主库和 WAL 副本，只查询所选联系人。会话结束时清理副本，异常退出后可手动清理。密钥只留在 Worker 进程内存中。[实机测试与探针边界](wechat-live-test.md)

| 检查项 | 源码结论 |
| --- | --- |
| 导入链 | 包入口 `__init__.py` 导入 `wx`、`db`、`guia`、媒体、撤回和朋友圈模块。直接使用 `wechatauto.db` 也会先执行包入口。日志模块初始化控制台日志，另有文件日志能力。 |
| 密钥路径 | `WeChatDB.__init__` 收集库并调用 `_load_or_extract_keys`。默认可能枚举其他账号缓存，使用 `ReadProcessMemory` 扫描微信，读取 Weixin 模块及配置并调用派生/解密逻辑。传入主密钥的分支仍保存缓存。 |
| 写内存与 UI | `accessibility.py` 的热激活路径在运行中的微信进程内调用 `WriteProcessMemory`。`uia_driver.py` 可调用热激活。`guia.py`、`sender.py` 等含 `SendInput`、鼠标键盘及发送路径。 |
| DLL 与 Hook | DB 路径有模块枚举和 DLL 读取。该审计路径未发现磁盘上的 Weixin.dll 改写、远程线程注入或 Windows Hook 安装。此结论限于检查的源码与调用路径。 |
| 密钥持久化 | 默认 `%TEMP%/wechatauto_db/<account>/keys.json`，包括备份及 `WECHATAUTO_KEYS_DIR` 或 `%LOCALAPPDATA%/wechatauto_keys` 稳定副本。DPAPI 封装失败时退回明文，兼容历史明文缓存。 |
| 解密副本与日志 | 默认在 `workdir` 写出解密主库、WAL 合并副本、时间戳和中间文件，之后以 `mode=ro` 打开副本。副本覆盖整库，可能含其他会话。上游 Listener 默认持久化水印。 |
| 查询契约 | `get_new_messages` 用 `sort_seq > since_seq` 与有界 LIMIT，严格边界遇到同序号分页或迟到行可能漏读。方向需要校验分片内 `Name2Id` 及 `sender_username`。 `get_sessions` 读多会话摘要，`search_contact` 按多个字段广泛匹配。 |
| 进程与远端 | 查到本地进程枚举及其他功能的 subprocess 调用，也有朋友圈图片下载代码。检查的 DB 初始化与增量查询未发现自动下载更新或执行远端代码。未动态审计第三方二进制和全部可选功能。 |
| 许可证与依赖 | Apache-2.0。发布元数据依赖 `uiautomation`、`pywin32`、`cryptography`、`zstandard`、`Pillow`、`opencv-python` 等，主要用下限约束。原生包、界面能力和可选媒体路径扩大审计边界。 |

源码定位见 [包入口](https://github.com/fanyuantaier/wechatauto-replica/blob/bc2e6f2461330eb1a53996d2f2afc18416376b52/wechatauto/__init__.py) · [数据库与 Listener](https://github.com/fanyuantaier/wechatauto-replica/blob/bc2e6f2461330eb1a53996d2f2afc18416376b52/wechatauto/db.py) · [界面热激活](https://github.com/fanyuantaier/wechatauto-replica/blob/bc2e6f2461330eb1a53996d2f2afc18416376b52/wechatauto/accessibility.py) · [包元数据](https://github.com/fanyuantaier/wechatauto-replica/blob/bc2e6f2461330eb1a53996d2f2afc18416376b52/pyproject.toml)

## 本程序的数据边界

- 应用自己的模拟夹具位于 `%LOCALAPPDATA%/WeChatVoiceBridge/fixtures/<session>`。它们含合成数据，目录继承当前用户本地应用数据目录权限。正常结束清理已知夹具文件，异常退出后的合成残留可手动清理。清理只针对带本程序租约标记的 GUID 子目录和已知文件名。
- Fish API Key 使用 Windows 当前用户 DPAPI 持久化。授权、云端模拟开关与聊天正文不落盘，声音和可选预览仅留在处理所需的内存中。处理记录默认隐藏正文，结束后清空。输出设备异常时使用原选项重新打开。
- 通过 HTTPS 发送合成文字与授权 Voice ID 至 Fish。处理者是第三方，免费请求可能用于改进模型。[Fish 隐私政策](https://fish.audio/privacy/) · [免费模型公告](https://fish.audio/zh-CN/blog/s2-1-pro-free-api/?articleLocale=en)
- 独立 Python 进程与 Job Object 约束生命周期，权限仍由 Windows 用户决定。将来即使获得合格的只读实现，只读访问也不保证符合微信平台规范，亦不保证账号不会被限制。
