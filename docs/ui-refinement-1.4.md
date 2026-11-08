# 1.4 本地体验版交互修订验证

日期：2026-11-07。当前交付为本地体验版，尚未发布到更新服务器。

## 已实现

- 已保存头像地址重新加载；旧资料缺失地址时，在用户主动搜索或开始监听后，按选定联系人 ID 补全。未开始时不启动数据库 Worker。HTTP 形式的官方头像地址统一转成 HTTPS 后交给已有域名白名单校验，不携带语音密钥。
- 首页音色下拉框、音色库和 API 列表采用分组小标题。行内不重复服务名称、音色 ID 或接口地址；详细值在编辑页面查看，没有项目的分组不会生成。
- 新建音色/API 为两步向导。第一页选择服务；第二页只呈现相关必需项，非必需项在「更多选项」。所有音色名称必填，Fish 音色填写名称与 Voice ID。新增 API 会加入音色服务选择；各类别独立卡片，添加入口放在卡片外。
- 最近记录统一显示已确认联系人的头像及备注名，原始搜索词不再作为联系人行混排。公共按钮模板尊重左对齐设置；历史头像列固定宽度。提示词菜单以「“备注名”说+消息」及「消息+“备注名”留言」表示先后顺序。
- 提示词选项为无提示词、实际备注名说（句首）、实际备注名留言（句尾）、自定义。单人/多人默认行为内部处理；自定义文字和位置按联系人保存并可复用。
- 联系人选择框可以取消，零联系人时也可关闭；保留输入过程中的联想，仅确认搜索或选中联系人时登记搜索历史。已产生的旧零碎记录可通过原有历史删除入口清除。
- 联系人元数据采用哈希倒排索引（一/二元字符候选，再核对完整子串），用于大小写无关及中文子串匹配。只在主动联系人浏览中创建，保存在 Worker 内存中，结束浏览后销毁。每次检查文件元数据，定期复核快照哈希；数据库实际提供数值修改时间字段时才用于排序，不用文件日期冒充联系人日期。
- 当前 Windows 用户/会话只运行一个交互实例。第二次启动通过 CurrentUserOnly 本机命名管道请求已有窗口显示，然后退出。更新辅助进程独立处理，不占用交互实例锁；此机制不能改变仍在运行的旧构建。

## 验证结果

- .NET 模拟自动测试：120 PASS，0 FAIL。
- Python 模拟自动测试：54 PASS，0 FAIL。覆盖索引中英文子串、字面百分号、候选复核、修改时间排序、真实分片逻辑的合成数据库夹具及头像白名单投影。
- WPF 隔离流程：PASS。五种 API 和音色的两步向导、Fish 精简字段、提示词默认规则/保存复用/句尾、自定义位置记忆、取消选择框。
- 原有隔离整链路：PASS。假 HTTP/假音频配合真实 Python fixture IPC，验证每人音色、队列不重叠、配置热切换、取消以及 Worker 释放。
- 第二次启动：PASS，新进程退出、原进程保持运行。
- WPF 实际截图检查：PASS，音色仅在分组标题显示服务归属；发现句首下拉框换行后已增宽并禁止该标签折行。
- 发布目录构建和源码边界/明显密钥扫描：PASS。

实机微信头像字段、不同服务商云账户权限与真实合成仍需对应账户验收，不能将上述模拟结果视为真实 API 成功。没有读取真实数据库、上传消息或消费云合成额度来执行本轮 UI 测试。

## 官方字段依据

- Fish： https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech
- OpenAI Speech： https://developers.openai.com/api/reference/resources/audio/subresources/speech/methods/create
- Azure： https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech
- 豆包 HTTP v3： https://docs.volcengine.com/docs/doubao-voice/http-chunked-sse-unidirectional-streaming-v3?lang=en
- GPT-SoVITS API v2： https://github.com/RVC-Boss/GPT-SoVITS/blob/main/api_v2.py

这是已实现协议的字段核对，不代表所有语音服务通用；普通聊天接口和 OpenAI Realtime 不会自动当成 Speech REST 处理。
