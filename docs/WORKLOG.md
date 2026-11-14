# 工作日志（按版本）

记录本次会话中实际完成、构建与验证过的改动。用户可见的精简版在 `src/VoiceBridge.App/ReleaseHistory.cs`；接手说明见 [HANDOVER-CURRENT.md](HANDOVER-CURRENT.md)。

---

## 1.8.3 · 节 5：打包（2026-11-15）

- **打包**：`Publish.ps1 -OutputDirectory artifacts\win-x64-v1.8.3 -ZipPath artifacts\WeChat-Voice-Bridge-V1.8.3-win-x64.zip`，再 `Build-Installer.ps1 -PayloadDirectory artifacts\win-x64-v1.8.3`。Clean-payload **PASS**（1993 文件，无本机设置、无真实 Key，比对了 1 个本机凭据）。
- **台账**：
  - payload `artifacts\win-x64-v1.8.3\`，ProductVersion `1.8.3+24ec5f8933c4c51ce77025484649d4e6d433f9c0`（与 HEAD `24ec5f8` 一致），FileVersion `1.8.3.0`，1993 文件 / 224,131,070 字节。
  - ZIP `artifacts\WeChat-Voice-Bridge-V1.8.3-win-x64.zip` = **92,199,780** 字节 / `3e5a7d3cdcd9f722aa4e85e1b87e9fdfc7483ec0034f776eeded6ddcc9f5e180`（1996 项）。
  - Setup `artifacts\WeChat-Voice-Bridge-V1.8.3-Setup.exe` = **67,127,439** 字节 / `93ee5890509208e022d45310a1d44e615e1cfe616a92fd4fcc0adb7bd3128194`。
- **包内抽查**（解包到 OneDrive 外的临时目录，查完已删）：`worker\audited_wechat_keys.py` 与仓库逐字节相同（注意：该 ZIP 内这个条目用反斜杠分隔）；`WeChatVoiceBridge.dll` 与 payload 逐字节相同；包内 exe `ProductVersion 1.8.3+24ec5f8…`。
- **没动的东西（已核对）**：`git diff 5da1b1a -- worker/ src/VoiceBridge.WeChat/` **为空**——本版没改任何提取参数，微信支持范围仍 4.1.11–4.2.0.52。
- **未做**：不发布、不推送、不上传、不碰线上 `latest.json`、不建 GitHub Release；README 版本徽章与两个下载链接未改（发布后由 Muse 改）；未做真机监听（Muse 复验）。
- **下一节**：等 Muse 真机复验 + 发布（发布时把 `wechatSupport: "4.1.11-4.2.0.52"` 写进线上清单）。

---

## 1.8.3 · 节 4：全量验证（2026-11-15）

- `scripts/Test.ps1`（Release build + C# xunit + Python unittest）：构建 **0 警告 0 错误**；.NET **322/322 PASS**（1.8.2 基线 277 + 新增门禁 45 项）；Python **55 项**（13 过 + 42 跳过，与基线同）。连跑两遍结果一致。
- `scripts/Verify-Source.ps1`：`PASS`（exit 0）。
- wx-multi 等价性自检（内置 Python 3.12）：`ALL SELF-CHECKS PASSED (58 checks)`。
- **既有用例时序问题（重要）**：节 4 首次全量运行时 `MultiSessionTests.AllVoicesShareOneSerialQueueAndCannotOverlapPlayback` 失败（`Pending` 期望 2、实际 1）。
  - **归因**：在 1.8.2 基线 `5da1b1a` 的干净 worktree 里单独跑该用例 **同样失败**（且连续 6 次均失败），证明与 1.8.3 改动无关。原因是该用例断言的“队列里正好 2 条”与原设计冲突：`SessionController` 在当前一条还在合成时就会**预取**下一条（`Prefetched`），预取时就把 `Pending` 减 1，所以 `Pending == 2` 只存在于消费者尚未被调度的窄窗口里——机器繁忙时过，空闲时必败。
  - **修法（只改用例，不改产品代码）**：用已有能力（`RoutedProbe` 的 synthesize 回调 + `ProbeSource.NewSignal`）把第一条消息的合成**卡住**，消费者因此无法预取，队列深度变成事实而非竞态；随后释放并断言真正的不变量：两条消息都在排队等待（`Pending == 2`、此时只有一次合成、尚未开始播放）、一次只播一条、播放顺序为 A-one → B-one → A-two、`MaxActive == 1`、共完成 3 条。删掉了“播放已开始时仍只有一次合成”——它与预取设计相矛，原来能过只是碰巧。连跑 6 次全绿。
  - 此事已在交接里标出：如有人不认可这项测试修改，可单独 revert 这个提交（产品行为不受影响）。
- **下一节**：打包 1.8.3 ZIP 与 Setup。

---

## 1.8.3 · 节 3：升版本到 1.8.3，写更新说明，同步文档（2026-11-15）

- `Directory.Build.props` 升到 **1.8.3**（实测 `FileVersion 1.8.3.0`、`ProductVersion 1.8.3+fe61dab…`）。
- `ReleaseHistory.cs` 在首位新增 1.8.3 条目「版本不受支持时，先提示更新语音桥」五条：框内一键更新、点叉即退出（已选补救动作除外）、「更新微信」只在低于下限时出现、启动不再被版本检查拖慢、本版不扩大支持范围（仍 4.1.11–4.2.0.52）。**1.8.2 及更早的条目一字未改**（`git diff` 只有 +8 行）。
- 文档同步（只改描述门禁行为的段落）：
  - `docs/compatibility.md`：新增「版本不受支持时的提示」一行，写清异步启动、主按钮更新语音桥、三种文案、「更新微信」的条件、点叉退出语义，以及清单可选字段 `wechatSupport`（缺失时按“有新版本就提供更新入口”）。
  - `docs/user-guide.md`：版本表最后一行改为「不支持，打开时会提示更新语音桥」；「提示版本不支持」一条重写为新行为。
  - `src/VoiceBridge.App/UserGuide.xaml` 表格芯片同步；`UserGuide.xaml.cs` 的同一条问答重写，其中下界用 `WeChatSession.MinSupportedVersion` 拼接，不写死。
  - `docs/PROJECT-BRIEF.md` §4 源码版本行改为 1.8.3。
- **未改**：README 的版本徽章与两个下载链接（按约定由 Muse 发布后再改）。
- **验证**：`dotnet build -c Release` **0 警告 0 错误**；`tools/DialogPreview` 离屏渲染 1.8.3 更新说明全部 PASS（居中、半行渐隐、滚动条），`使用说明` 页检查通过；构建出的 `WeChatVoiceBridge.exe` 版本 1.8.3。
- **下一节**：全量验证（`Test.ps1`、`Verify-Source`、wx-multi）。

---

## 1.8.3 · 节 2：门禁自救改造（异步启动、框内一键更新、点叉即退出）（2026-11-15）

- **启动不阻塞、先查自己再拦人**：`src/VoiceBridge.App/App.xaml.cs` 的 `ContentRendered` 只挂一个 `ShowStartupDialogs`，依次做「门禁 → 更新说明」。门禁走 `VersionGate.TryShowOnStartupAsync`：`Task.Run` 读微信版本 → 不支持才 `await` 更新通道的结论（`MainWindow.ProbeUpdateForGateAsync`）→ 再弹框。主窗口不等它，弹框允许晚到（实测：主窗口 2.36 s 出现，门禁在更新检查返回后弹出）。门禁占用弹窗位时**本次不弹更新说明**，避免两个模态框叠在一起。
- **复用而不是另写下载器**：`UpdateProcess.CreateUpdateHttp()` 统一 HTTP 配置（不跟随重定向、无 Cookie、无总超时），`MainWindow.Updates` 与门禁框共用；`MainWindow.ProbeUpdateForGateAsync` 优先 `await` 已在跑的启动检查（`updateWork`），其次若 `updateChecked` 仍为 false 才走一次 `RunUpdateAsync(CheckUpdateAsync)`——启动一次运行只会有一个网络往返（`CheckUpdateAsync` 加了 `updateChecked` 标记）。框内「更新语音桥」点下后：`UpdateClient.CheckAsync` → `DownloadAsync`（带进度与 SHA-256 校验）→ `UpdateInstaller.Prepare` → `UpdateProcess.ValidatePackageVersion` → `UpdateProcess.StartAsync` 交接安装助手，与设置页同一套；无新版、下载失败、权限错误、助手超时均在框内写明确状态文字（不静默）。
- **按钮与文案**：主按钮「更新语音桥」（有新版时带版本号「更新语音桥到 vX」）；「更新微信」**仅**在微信低于 `MinSupportedVersion` 时出现（高于上限一律不给，避免误导）；「退出应用」保留。文案分三档：清单声明支持当前微信（“更新语音桥即可解决”）、已发布但仍不支持（“已发布 vX，但目前仍不支持微信 X”）、已是最新（“语音桥已是最新（vX），暂时还不支持微信 X”）；支持范围文字仍取自 `WeChatSession.Min/MaxSupportedVersion`。
- **点叉即退出**：`VersionGateDialog.OnClosing` 统一调 `VersionGatePolicy.OnDialogClosed(remediationChosen, installStarted)`——未选任何补救动作关闭（×、Alt+F4）= `Application.Shutdown`；点过「更新语音桥」或「更新微信」（且网页确实打开）后的关闭保留应用；安装交接已启动则必须退出以便替换文件。
- **测试专用入口**：环境变量 `WVB_TEST_WECHAT_VERSION`（形如 `9.9.9.9`）非空时，`VersionGate` 的启动检测直接用它冒充检测到的微信版本，**只影响门禁判断**（`WeChatSession.Bind` 与 worker 仍读真实版本）。用法：`$env:WVB_TEST_WECHAT_VERSION='9.9.9.9'; .\WeChatVoiceBridge.exe`，然后看门禁是否弹出、按钮与文案是否对应。**此变量只记在这里，不写进用户文档。**
- **真机实测**（开发版 `bin\Release\net8.0-windows`，微信 4.2.0.52 真在运行；测前备份了 `settings.json`/`app.log`/`worker.log`/`release-notes-seen.json`，测后逐字比对**全部未变**）：
  - `9.9.9.9`（过新）+ 线上 1.8.2 无新版：框内文案“语音桥已是最新（v1.8.2），暂时还不支持微信 Windows 9.9.9.9”，按钮为「退出应用 / 更新语音桥」；点「更新语音桥」后框内出现“正在检查更新…”→“语音桥已是最新（v1.8.2），暂时没有可安装的更新。…”，且此后关闭框**应用继续运行**。
  - `9.9.9.9` 直接关框：**应用退出**（进程在 8 s 内结束）。
  - `4.1.10.99`（过旧）：三个按钮「退出应用 / 更新微信 / 更新语音桥」，文案说“低于支持范围，需要更新微信”；关框**应用退出**。
  - `4.2.0.52`（支持）：8 s 后进程只有主窗口一个可见窗口，**门禁未弹**。
- **工具**：`tools/DialogPreview` 新增 `--gate`（离屏渲染五种决策情形并断言按钮个数/名称，本轮全部 PASS）；`tools/UiSmokeTest` 新增 `gate <pid> <前缀> [update]`（等门禁窗口 → PrintWindow 截图 → 可选点「更新语音桥」等状态文字 → 点× → 报应用是否退出）。均不入产品代码。
- **验证**：`scripts/Test.ps1` 构建 0 警告 0 错误、.NET **322/322**、Python **55**（42 跳过）；`Verify-Source.ps1` PASS。
- **下一节**：升 1.8.3 + `ReleaseHistory` 条目 + `docs/compatibility.md`、`docs/user-guide.md` 同步。

---

## 1.8.3 · 节 1：门禁决策抽成纯函数 + 清单可选 wechatSupport（2026-11-15）

- **背景**：1.8.2 的门禁框只给「退出应用」「更新微信」两个按钮——被拦住的用户真正需要的是更新**语音桥**；「更新微信」对高于上限的版本是误导；模态框也挡住了侧栏的更新提示。用户已拍板改造方案，本版按条执行。
- **改动**：
  - 新增 `src/VoiceBridge.Core/VersionGatePolicy.cs`（无 WPF 依赖，可单测）：
    - `WeChatSupportRange.TryParse`：解析清单里的 `wechatSupport`（形如 `4.1.11-4.2.0.52`）。只认「两段、各至少 major.minor.build、min ≤ max」；兼容半角/全角/长横线；任何不合格文本一律返回 false（**不得抛异常**，按字段缺失处理）。
    - `VersionGatePolicy.Decide(clientRaw, client, min, max, publishedWeChatSupport, appVersion, publishedVersion)` → `VersionGateDecision`：是否弹框、`Reason`（None / WeChatTooOld / WeChatTooNew / VersionUnreadable）、主按钮是否「更新语音桥」、新版本号、`BridgeUpdateCoversWeChat`（清单声明覆盖该微信版本；字段缺失/解析失败退化为「有更新就提供入口」）、以及 `OffersWeChatUpdate`（**仅**微信低于下限时为 true）。
    - `VersionGatePolicy.OnDialogClosed(remediationChosen, installStarted)` → 「点叉是否退出」也变成纯函数：未选任何补救动作 → 退出；选了更新语音桥/更新微信 → 保留应用；已进入安装交接 → 必须退出。
  - `src/VoiceBridge.Core/UpdateManifest.cs`：`wechatSupport` 作为**第 7 个可选字段**加入六字段契约（`UpdateManifest` 记录新增 `string? WeChatSupport = null`）。解析规则：仍必须是六个必需字段齐全、字段名不重复、总数 6 或 7，且第 7 个必须是 `wechatSupport`；`wechatSupport` 不是字符串时按缺失处理，不让整次检查失败。
- **单测**：新增 `tests/VoiceBridge.Tests/VersionGateTests.cs` 45 项，覆盖：区间内不弹框（含两端点 4.1.11 / 4.2.0.52）、过旧弹框且给「更新微信」、过新弹框且**不**给「更新微信」、版本不可读、有更新且清单覆盖该微信版本（应提示更新即可解决）、有更新但清单声明不覆盖（文案应说仍需适配）、已是最新（无更新可给）、线上版本不比当前新、`wechatSupport` 缺失/空/乱写/单段/反向/三段/只有 major.minor（一律退化为「提供更新入口」）、版本号非法不抛异常、区间解析的 7 种坏输入、点叉动作四种组合、清单可选字段（正常/缺失/非字符串/未知第 7 字段仍拒绝）。
- **验证**：`scripts/Test.ps1` 构建 **0 警告 0 错误**；.NET **322/322 PASS**（1.8.2 基线 277 + 新增 45）；Python **55 项**（13 过 + 42 跳过，与基线同）。
- **下一节**：`VersionGate.cs` / `VersionGateDialog` 改造（启动异步先查更新再拦人、框内一键更新、点叉即退出、`WVB_TEST_WECHAT_VERSION` 测试入口）。

---

## 1.8.2 · 节 5：打包（2026-11-14）

- **打包**：`Publish.ps1 -OutputDirectory artifacts\win-x64-v1.8.2 -ZipPath artifacts\WeChat-Voice-Bridge-V1.8.2-win-x64.zip`，再 `Build-Installer.ps1 -PayloadDirectory artifacts\win-x64-v1.8.2`。Clean-payload **PASS**（1993 文件，无本机设置、无真实 Key，比对了 1 个本机凭据）。
- **台账**：
  - payload `artifacts\win-x64-v1.8.2\`，ProductVersion `1.8.2+e5811fcd6135c9049eda3d1e71b86fe988e6d2fa`（与 HEAD `e5811fc` 一致）。
  - ZIP `artifacts\WeChat-Voice-Bridge-V1.8.2-win-x64.zip` = **92,189,654** 字节 / `a0d1aea0630d6a1e0ca41b75dd0aff8716bfe160613694e3de218c1b9d547ad6`（1996 项）。
  - Setup `artifacts\WeChat-Voice-Bridge-V1.8.2-Setup.exe` = **67,121,939** 字节 / `e9ec6ea486652499ca920aca4670e13daeb0af29056bcd203215f2fead96d6fb`。
- **包内抽查**（解包到 OneDrive 外的临时目录，查完已删）：`worker/audited_wechat_keys.py` 与仓库逐字节相同（含 `max_version=(4, 2, 0, 52)` 两处）；`WeChatVoiceBridge.dll` 与 payload 逐字节相同；包内 `WeChatVoiceBridge.exe` ProductVersion 为 `1.8.2+e5811fc…`。
- **未做**：不发布、不推送、不上传、不碰线上 `latest.json`、不建 GitHub Release（按用户要求由 Muse 执行）；未启动 GUI 做真机监听（Muse 负责）。
- **下一节**：等 Muse 发布；发布后改 README 版本徽章与两个下载链接，并回写发布回执。

---

## 1.8.2 · 节 4：全量验证（2026-11-14）

- `scripts/Test.ps1`（Release build + C# xunit + Python unittest）：构建 **0 警告 0 错误**；.NET **277/277 PASS**（与 1.8.1 基线相同，无新增/失效用例）；Python **55 项**（13 过 + 42 跳过，与基线同）→ `.NET tests: PASS` + `Python tests: PASS`。
- `scripts/Verify-Source.ps1`：`Static boundary and obvious-secret scan: PASS`（exit 0）。
- wx-multi 等价性自检（内置 Python 3.12）：`ALL SELF-CHECKS PASSED (58 checks)`——仍是 58 项，切片等价性、XOR 掩码、合成内存场景 A/B/C、拒绝版本零句柄全部成立。
- 额外核对（只读）：“4.2.0.52 profile = wcdb-config-cipher-4.1.11+，参数与 4.2.0.13 **完全相同**（`to_xcipher_params` 逐字段相等）、掩码同为 `CONFIG_XOR_MASK`；`4.2.0.52` 命中 master-cfg profile；`4.2.0.53` 被拒并报「不支持此版本」”。
- 结论：未发现 4.2.0.52 上提取参数的异常迹象；与 Muse 的真机实测结论一致。
- **下一节**：打包 1.8.2 ZIP 与 Setup。

---

## 1.8.2 · 节 3：升版本号到 1.8.2，写更新说明（2026-11-14）

- `Directory.Build.props` 升到 **1.8.2**（程序集 1.8.2.0，实测 `FileVersion 1.8.2.0`）。
- `ReleaseHistory.cs` 在首位新增 1.8.2 条目「适配微信 4.2.0.52」三条：范围扩到 4.1.11–4.2.0.52；4.2.0.52 真机实测（26 个库全部通过 page1 HMAC，布局/掩码/偏移与 4.2.0.13 一致）；其他功能不变、范围外仍明确提示。
- **历史条目未动**：1.8.1 及更早的条目一字未改（`git diff` 确认只新增了上面这一段）。
- 同步：PROJECT-BRIEF §4 的源码版本改 1.8.2（并注明未发布）；HANDOVER 1.8.2 段补节 1–3 状态与待做。
- **验证**：`dotnet build -c Release` **0 警告 0 错误**；构建出的 `WeChatVoiceBridge.dll` 版本 `1.8.2.0` / `1.8.2+a2ace74…`。
- **下一节**：全量验证（`Test.ps1`、`Verify-Source`、wx-multi）。

---

## 1.8.2 · 节 2：用户可见的支持范围改为「4.1.11 至 4.2.0.52」（2026-11-14）

- **改动**：
  - `src/VoiceBridge.WeChat/PythonFixtureSource.cs`、`src/VoiceBridge.WeChat/WeChatDirectory.cs`：`unsupported_version` 的中文文案改为「（4.1.11 至 4.2.0.52）」。
  - `README.md`：微信版本徽章、下载一节的「微信」一条、英文摘要里的版本区间改为 4.2.0.52（正文保留「4.2.0.13 与 4.2.0.52 已实测」这句事实描述）。**版本徽章与两个下载链接仍是 1.8.1**，等 Muse 发布后再改（现在改会指到不存在的文件）。
  - `docs/user-guide.md`：版本表仅改数字（4.2.0.52 已实测 / 区间 / 不支持），**仍是三行**，与软件里那张由 `MinSupportedVersion`/`MaxSupportedVersion` 生成的表结构一致。
  - `docs/compatibility.md`：本机微信改为 4.2.0.52，并写清边界：4.2.0.52 上只实测了密钥提取（26/26 库通过 page1 HMAC，监听的完整链路是在 4.2.0.13 上做的），不把未做过的实机监听写成做过。
  - `docs/troubleshooting.md`：「找不到真实微信会话」一条改为「版本在 4.1.11 至 4.2.0.52 之间」。
  - `docs/HANDOVER-CURRENT.md`：风险段旧的「受审计支持版本仅 4.2.0.13（`WeChatSession.SupportedVersion`）」改为按 `MinSupportedVersion`–`MaxSupportedVersion` 表述（当前 4.1.11–4.2.0.52）。
- **未改**：`ReleaseHistory.cs` 里 1.8.1 及更早的历史条目（1.4.9 条目里的 4.1.11–4.2.0.13 是当时的真实记录）；`docs/PROJECT-HANDOVER.md`、`docs/NEXT-AGENT-HANDOVER-1.4.md`、`docs/wechat-live-test.md` 等历史快照。
- **验证**：`dotnet build -c Release` **0 警告 0 错误**；`grep` 确认 `src/`、`worker/`、README、使用说明、兼容性、排错里再无遗留的旧上限说法。
- **下一节**：升到 1.8.2 并写 `ReleaseHistory` 条目。

---

## 1.8.2 · 节 1：放行上限改到微信 Windows 4.2.0.52（2026-11-14）

- **背景**：用户本机微信已升到 4.2.0.52（`D:\Weixin\Weixin.exe`）。Muse 已用现行 `audited_wechat_keys.py`（4.2.0.13 profile 参数）对运行中的 4.2.0.52 主进程做过只读实测：70 个候选密钥，当前账号 26 个数据库全部通过 page1 HMAC（26/26）。结论是 Config.Cipher 布局、XOR 掩码、各偏移与 4.2.0.13 完全一致，**提取参数一个都不用改**，本次只放行新版本。
- **改动**：
  - `src/VoiceBridge.WeChat/WeChatSession.cs`：`MaxSupportedVersion` → `4.2.0.52`；上限注释改为「与 4.2.0.13 逐字节同一布局、已在真机验证」，区间注释与拒绝文案的范围随之更新。`MinSupportedVersion` 仍为 4.1.11。
  - `worker/audited_wechat_keys.py`：`PROFILES[0].max_version` 与 `MASTER_CFG_PROFILES[0].max_version` → `(4, 2, 0, 52)`；`profile_for()` 的 `high` → `(4, 2, 0, 52)`；文件头/版本表/函数 docstring 里的上限说法同步；`unsupported_version` 中文报错改为「4.1.11 至 4.2.0.52」。**红线核对**：`git diff` 逐行看过，`CONFIG_CIPHER_NAME`、`CONFIG_XOR_MASK`、`node_back`、`config_ptr_offset`、`blob_object_offset/ptr/len`、`blob_max`、`cfg_ptr_back`、`cfg_offset`、`PAGE_SZ`、`RESERVE_SZ`、PBKDF2/SQLCipher 参数**一处未动**。
  - `worker/wechat_adapter.py`：仅两处注释里的区间说法同步。
  - `tests/wx-multi/test_audited_wechat_keys.py`：范围检查改为上限 `(4, 2, 0, 52)`（同时校验 `MASTER_CFG_PROFILES` 上限与 `profile_for('4.2.0.52')` 命中），越界样本 `4.2.0.14` 改为 `4.2.0.53`（4.2.0.14 现在落在支持区间内），master-cfg 断言补 4.2.0.52。**检查项仍是 58 项**（新断言并在既有检查里，没有新增项，保住 58 项等价性基线）。
- **验证**：`dotnet build -c Release` **0 警告 0 错误**；wx-multi 自检 `ALL SELF-CHECKS PASSED (58 checks)`。
- **下一节**：用户可见文案里的支持范围（`PythonFixtureSource.cs`、`WeChatDirectory.cs`、README、`docs/user-guide.md`、`docs/compatibility.md`）。

---

## 1.8.1 · 节 27：经 Muse 发布上线，公网复核通过，README 下载链接改为 1.8.1（2026-11-12）

- **发布单** `codex_20261112_045808_d8a56a`：04:58 发出，约半分钟后回 `info`（流水线接手），05:00 回 `status=ok`。只发了这一张。
- **Muse 回执**：ZIP sha256 `ca2b07…fbd0`、size 92,189,272，哈希链三方一致（申报 = PC 实测 = A1 落地）；Setup 一并发布（`d8ce0b…5ef2`，67,121,299）；`latest.json` 更新为 1.8.1，旧版备份 `latest.json.bak-1.8.0`；Muse 侧公网复核清单和两个下载链接都是 200、大小一致。
- **本助手独立公网复核**：
  - `latest.json` HTTP 200，六字段齐全：version `1.8.1`，publishedAt `2026-11-12T04:00:25Z`，url 指向 V1.8.1 ZIP，size 与 sha256 和本地一致，notes 109 字中文正常。
  - HEAD：ZIP 200、长度 92,189,272；Setup 200、长度 67,121,299，都与本地一致。
  - 完整下载 ZIP（放在 OneDrive 外），重算 SHA-256 与清单一致，下载文件已删除。
- **README**：版本徽章和两个下载链接从 1.8.0 改成 1.8.1（两个新链接上面刚核对过，都是 200）。
- 桌面快捷方式未动（仍指向 1.4.7 r3）。PROJECT-BRIEF §4 与 HANDOVER 已同步。
- **下一节**：提交推送这份记录，回 Muse 确认（带这次的文档提交号）。
- **续（05:06）**：记录提交 `a344870` 已推送、远端一致；公网核对 README 和两张新截图与提交内容逐字节一致，两个 1.8.1 下载链接 200。回 Muse 的确认 `codex_20261112_050354_96b122` 先 `info` 后 `ok`：Muse 独立复核清单、下载、README 徽章与链接、两张截图、HEAD 与匿名作者，全部一致，1.8.1 发布闭环完成。

---

## 1.8.1 · 节 26：门禁与打包（2026-11-12）

- **门禁**（在 `92e96e9` 上）：应用 Release 0 警告 0 错误；.NET **277/277**；Python 55（42 跳过）；`Verify-Source` PASS；wx-multi **58/58**；`DialogPreview 1.8.1` 浅色、深色全过。
- **打包**：`wvb-package.ps1 -Version 1.8.1`。Clean-payload 两次都是 PASS（1993 文件，没有本机设置，没有真实 Key，比对了 1 个本机凭据）。
- **台账**：
  - payload ProductVersion `1.8.1+92e96e95a0e283017fad3cde7112b137ba675482`，与 HEAD 一致。
  - ZIP `artifacts\WeChat-Voice-Bridge-V1.8.1-win-x64.zip` = **92,189,272** 字节 / `ca2b07fea5d8d619bd12b5412fad315d29536ec60d1fac5af775e2b92e24fbd0`。共 1996 项，6 个应用程序集与 payload 逐字节相同，没有设置、凭据或数据库文件。
  - Setup `artifacts\WeChat-Voice-Bridge-V1.8.1-Setup.exe` = **67,121,299** 字节 / `d8ce0b7c61edcfffe09a0b44d6d1e1be2a9321f7055c268070ffa9b4b4025ef2`。
- **打包产物实跑**：用用户真实设置启动 `artifacts\win-x64-v1.8.1\WeChatVoiceBridge.exe`，版本 `1.8.1+92e96e9`，「更新说明」对话框（544×474）弹出 1.8.1 的说明。没有点「知道了」，直接结束进程；前后备份并复原 5 个设置文件，哈希一致，`release-notes-seen.json` 仍是 `"1.7.0"`。
- 桌面快捷方式未动。
- **下一节**：发布前四项自查，然后发一张 Muse 发布单。
- **续（04:58）**：四项自查全绿（WORKLOG 到节 26、HANDOVER 是当前状态、工作区干净、远端 HEAD = 本地 `cbbd788`）。已发 Muse 发布单 `codex_20261112_045808_d8a56a`（`publish-voicebridge`，version 1.8.1，ZIP 与上面的 SHA-256，中文说明），等回执。

---

## 1.8.1 · 节 25：升版本号，写更新说明（2026-11-12）

- `Directory.Build.props` 升到 **1.8.1**。
- `ReleaseHistory.cs` 加 1.8.1 条目「深色模式下的昵称更清楚」，两条：昵称改白色；再把上一版 1.8.0 的外观和使用说明简要提一句。原因：更新说明弹窗只显示当前版本这一条，1.8.0 才发布三个小时，大多数人（包括用户自己那份 1.7.0）会直接从 1.7.0 跳到 1.8.1，不提一句就看不到「外观」这个新功能。
- **验证**：`DialogPreview 1.8.1` 浅色、深色各 4 项全过：说明框 241.5 DIP，放得下，不截断、不渐隐；深色截图看过。
- **下一节**：节 26，门禁与打包。

---

## 1.8.1 · 节 24：深色模式下联系人昵称改成白色，README 截图同步（2026-11-12）

- **用户反馈**：护眼的深色模式下，人物卡片里的昵称看不清，改成白色；README 里对应的截图也要换；推一个新的小版本。
- **原因**：昵称用的是显式样式 `ContactName`（为了用 Segoe UI Symbol 兜底生僻符号），显式样式不会叠加 `App.xaml` 里那个隐式 TextBlock 样式，而它自己没写颜色；主窗口也没设默认文字颜色，于是退回系统默认的黑色。浅色模式下黑色和正文颜色几乎一样，看不出来；深色模式下就是黑字压在深灰卡片上。
- **改法**：`ContactName` 加上 `Foreground = {DynamicResource Label}`，跟着主题走：深色模式下是接近白色的正文色（深色默认 `#F2F2F7`，其他主题色也都是近白），浅色模式下和原来一样。首页联系人卡片（多人、单人两种布局）和「添加」里的联系人列表都用这个样式，一起改好。
- **排查其他地方**：所有带名字的 TextBlock 样式里，只有 `ContactName` 没写颜色（`GuideGlyph` 也没写，但每处用到时都单独设了颜色）。深色模式下的音效页、词库页、设置页、使用说明、对话框之前都看过，没有黑字。
- **README 截图**：用演示数据（虚构的妈妈、小林）重新截首页的五种外观，重做 `docs/images/home.png` 和 `docs/images/themes.png`；深色两格里的昵称现在是白色。截图前后备份并复原了 5 个设置文件，哈希一致。
- **验证**：应用编译 0 错误 0 警告；`Test.ps1`：.NET **277/277**，Python 55（42 跳过）；`Verify-Source` PASS；深色默认首页截图里两个昵称都是白色，看过。
- **下一节**：节 25，升到 1.8.1，写更新说明。

---

## 第三轮 · 节 23：仓库公开（2026-11-12）

- **放行**：用户在主聊天里向 Muse 确认后，Muse 在任务单 `codex_20261112_033119_f7b681` 上回 `info`「放行推送」。他先独立抽查了改写用的克隆：HEAD `e24892c` 与报告一致，全部作者、提交者和两个标签的标注人都是匿名地址，全对象扫描里原作者邮箱和姓名、服务器 IP、本机用户目录路径、OpenAI key、GitHub token 都是 0，被删路径命中 0。他宽口径正则多查到的 6 处 `wxid_` 是 `tools/DialogPreview/Program.cs` 里的示例串 `wxid_preview`，不是真实 wxid。
- **GitHub 一侧**（Muse 办）：旧仓库改名为 `Nefertarigazer/WeChat-Voice-Bridge-private-history`，仍是私有，114 个提交原样保留；用原名新建 `Nefertarigazer/WeChat-Voice-Bridge`，公开、空仓库，简介已填。Muse 还把本机项目的 git 身份改成了匿名地址（项目级 `user.name` / `user.email`），以后本机的提交不会再带真实姓名和邮箱。
- **推送**：从改写用的克隆推 `master`（`e24892c`）和标签 `v1.5.3`、`pre-multi-provider`，远端三个引用和本地逐一相同；GitHub 把默认分支设成了 `master`。
- **公网核对**（不登录）：
  - 仓库页 200；API 显示公开，默认分支 `master`，许可证识别为 Apache-2.0，简介正确。
  - README、LICENSE 和提交里的内容逐字节相同（工作区是 CRLF，所以要和提交里的内容比，不能和工作区文件比）；首页图、外观图、应用图标逐字节相同。
  - 旧历史的提交 `5c3ad2e`、`dd263df` 在公开仓库里打不开（404）；私有备份仓库不登录打不开（404）。
- **本机仓库切到新历史**：`git fetch origin`（`origin/master` 强制更新到 `e24892c`）、`git fetch --force --tags origin`、`git reset --mixed origin/master`、`git restore -- .`。工作区干净；已忽略的内部文件（`.workbuddy` 等）和 `artifacts` 都还在；跟踪的文件里没有本机用户名。本机还留着别的工具的检查点引用（`refs/codex/…`、`refs/t3/…`），指向旧历史，只在本机，**不要推送它们**。
- **还没做**：GitHub 话题（topics）是空的，任务单里列了，要请 Muse 补上。
- **下一节**：回 Muse 这张单，请他做最后的公网复核、补话题，闭环。
- **续（04:25）**：记录提交 `acb0f7f` 已推送、远端一致。回 Muse 的单 `codex_20261112_041602_92c33e` 先 `info` 后 `ok`：Muse 不登录独立复核全部通过（公开、默认分支 `master`、Apache-2.0、master 与两个标签的哈希一致、旧提交 404、私有备份 404、作者只有匿名身份；公网抽查 8 个文件，wxid 长串、原作者邮箱、服务器 IP、真名、密钥格式命中都是 0）。话题已补上，API 显示 9 个：accessibility、chinese、dotnet、text-to-speech、tts、voice、wechat、windows、wpf。**第三轮全部完成。**

---

## 第三轮 · 节 22：改写历史，清掉个人信息和内部文件（2026-11-12）

- **备份在先**：原始历史 `git bundle create --all`（46 个引用，含本机的检查点引用），`git bundle verify` 通过，放在 OneDrive 外的 `D:\USERNAME\CodexScratch\wvb-rewrite\original-history.bundle`。
- **做法**：从本机仓库 `git clone --no-local` 出一份干净克隆（只有 `master` 和两个标签），用 git-filter-repo 2.47.0 一次完成：
  - 从所有版本里删掉 `.workbuddy/`、`CONTINUE-1.4.txt`、`pelican-cycling.html`、`temp/`、`wx-multi-handoff/`、`artifacts/`（两张历史截图，含真实名单那张）、`docs/HANDOVER-FROM-MUSE.md`。
  - 在所有版本里把个人信息换成占位：用户自己的 wxid（两种写法）、一位联系人的 wxid、昵称和微信号，本机用户名换成 `USERNAME`，服务器 IP 换成 `服务器IP`。替换清单是从本机 `settings.json` 里取出、在历史里逐个确认出现过的值；只在常用词里出现的两字名字、旧的出厂预置音色名和音色 ID 是公开信息，没有动。
  - 提交作者、提交者和两个标签的标注人统一换成 GitHub 匿名地址 `Nefertarigazer <97020488+Nefertarigazer@users.noreply.github.com>`（账号没有公开真实姓名）。
- **结果**：`master` 从 114 个提交变成 110 个（4 个只改了被删文件的提交变空，被去掉了）；两个标签跟着改写。新旧对照：1.8.0 的构建提交 `5c3ad2e` → `2a6642b`，1.7.0 `dd263df` → `a58c46c`，1.6.0 `f279d5e` → `de3a280`，标签 `v1.5.3` `67a74f0` → `a34d419`，`pre-multi-provider` `d608aca` → `d4da05b`。已发布安装包里写的提交号（如 `1.8.0+5c3ad2e`）指的是旧历史，只能在私有备份里查到。
- **复查**（改写后的克隆，先 `reflog expire` + `gc --prune=now`）：
  - 全部对象 30.6 MB 里：上面每个个人信息值 0 处，本机用户名 0、服务器 IP 0、原作者姓名和邮箱 0、任何长 wxid 0、私钥块 / `sk-` / GitHub token 0。用 DPAPI 解出的真实 Key 0 处（脚本只比对、不打印）。
  - 历史里出现过的路径 199 个，被删的路径 0 个；历史里的图片只剩 `docs/images` 两张演示截图和应用图标；没有 ZIP。
  - 当前文件和改写前逐个比对：都是 198 个文件，内容不同的只有 7 个，正好是做过替换的文档和 `tools/probes/outgoing_probe.py`。
  - 门禁在克隆里重跑：.NET **277/277**，Python 55（42 跳过），`Verify-Source` PASS，58 项密钥自检 PASS。
- **顺手修的**：`tools/probes/outgoing_probe.py` 的临时目录原来写死成本机路径，替换后成了不存在的 `D:\USERNAME\…`；改成优先用环境变量 `WVB_SCRATCH`，否则用系统临时目录（仍在 OneDrive 外）。
- **下一节**：节 23，请 Muse 改名旧仓库、建新仓库，推送并公开核对，然后把本机仓库切到新历史。
- **续（03:32）**：Muse 任务单 `codex_20261112_033119_f7b681` 最终回 `needs-user`：仓库改名、新建、改可见性不在桥白名单里，要用户本人在主聊天里确认，Muse 没有动任何东西。为了让清理后的历史有一份在 GitHub 上、也方便 Muse 核对，把它推到了旧私有仓库的 `clean-history` 分支（`master` 不变）。核对证据写在 `wvb-rewrite\evidence-for-muse.txt`。等用户确认。

---

## 第三轮 · 节 21：开源准备：Apache-2.0、README 许可一节、内部文件移出仓库（2026-11-12）

- **用户决定**：「A + Apache-2.0」，即先清理历史再公开，许可证用 Apache-2.0。
- **做了什么**：
  - 新增 `LICENSE`：apache.org 的 LICENSE-2.0 原文（11,358 字节），和 `worker/WECHATAUTO-LICENSE.txt` 的正文逐字一致（只差换行符）。
  - README 加许可证徽章和「许可」一节（本项目 Apache-2.0；参考的 wechatauto-replica 同为 Apache-2.0，原许可证留在 `worker/`；edge-tts 是 LGPL-3.0，作为单独的 Python 组件运行），English 一段也加了一句。
  - `.gitignore` 加 `.workbuddy/`、`CONTINUE-1.4.txt`、`pelican-cycling.html`、`temp/`、`wx-multi-handoff/`，并从索引里移除（`git rm --cached`，文件都还在本机）。
  - PROJECT-BRIEF、HANDOVER 里提到 `.workbuddy/memory` 和 `wx-multi-handoff` 的地方改成「只在本机，不入库」。
- **公开方式的调整**（为什么不直接强推）：GitHub 文档写明，改写历史并强推以后，旧提交仍然可以凭 SHA 在 GitHub 的缓存视图里打开，要找 GitHub 客服才能清掉，而且客服只在泄露的是凭据这类敏感数据时才协助（[Removing sensitive data from a repository](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)）。WORKLOG 里记着大量旧提交号，改成公开以后等于给出了查看旧内容的路径。所以改成：请 Muse 把现在的仓库**改名并保持私有**（作为原始历史的备份），再用原来的名字新建一个仓库，推入清理后的历史再公开。这样不强推、不删除任何东西，公开的地址不变，旧提交在公开仓库里不存在。
- **验证**：`Verify-Source` 见下一节一起跑。
- **下一节**：节 22，在 OneDrive 外的干净克隆里用 git-filter-repo 改写历史（删内部文件和截图；把 wxid、微信号、昵称、本机用户名、服务器 IP 换成占位；提交作者换成 GitHub 匿名地址），全面复查后再交给 Muse。

---

## 第三轮 · 节 20：仓库改公开前的历史审计（2026-11-12）

- **为什么**：改公开以后，所有提交的所有版本都能被看到和复制，删不回来。所以先查整个历史，再请用户决定。只读检查，没有改动任何提交。
- **密钥**：用 DPAPI 在内存里解出本机凭据库里的真实 Key（只比对，不打印），在全部 1,624 个 git 对象（30.4 MB，每个文件的每个版本）和历史里那个 ZIP 解开后的 19 个文件里查找，**0 处**。常见密钥格式（`sk-`、AWS、GitHub token、私钥块、Bearer、`api_key = "…"`）也是 0 处。脚本 `D:\USERNAME\CodexScratch\Temp\wvb-key-history-scan.ps1`（不入库），临时文件已删。
- **会公开的个人信息**（现在的文件里有，历史里每个版本也都有；这里不写出具体值）：
  - `docs/PROJECT-HANDOVER.md`：用户自己的 wxid、一位联系人的 wxid 和微信号。
  - `artifacts/screenshots/task2-multi-a-print.png`：真实名单的主窗口截图（联系人头像和名字），已不在当前文件里，**但还在历史里**。
  - 本机用户名的路径（10 个文件）、更新服务器的 IP 和服务器操作记录（HANDOVER、WORKLOG）、Muse 工作流的细节。
- **内部文件**：`.workbuddy/memory/` 三份代理笔记、`CONTINUE-1.4.txt`、`pelican-cycling.html`、`temp/`、`wx-multi-handoff/`（另一位代理的交接包，含一个 ZIP）、历史里的 `docs/HANDOVER-FROM-MUSE.md`。
- **误报，已排除**：安装向导中文翻译文件里译者的公开邮箱、测试里的示例地址、`Publish.ps1` 里的下载链接数字。
- **另外**：本地还有 `refs/codex/…`、`refs/t3/…` 一批别的工具留下的检查点引用，只在本机，远端只有 `master` 和两个标签（`v1.5.3`、`pre-multi-provider`）。
- **待拍板**（已报给用户）：
  1. 怎么公开：A. 先改写历史，删掉上面的内部文件和截图、把 wxid / 路径 / IP 换成占位，再强推 `master` 和两个标签，然后改公开（推荐，改写前先把原历史打包备份）；B. 另建一个只有当前代码的新公开仓库，现在这个继续私有；C. 原样公开（不推荐）。
  2. 开源许可证：建议 Apache-2.0（读取组件参考的上游就是 Apache-2.0），也可以 MIT 或 GPL-3.0。
- **还没做**：没有请 Muse 改公开，也没有改写任何历史。
- **下一节**：用户选定后，按选择执行，并补上 LICENSE 和 README 的许可证一节。

---

## 1.8.0 · 节 19：经 Muse 发布上线，公网复核通过（2026-11-12）

- **发布单** `codex_20261112_020446_0bf7a9`：02:04 发出，约 1 分钟后回 `info`（常驻流水线接手），02:07 回 `status=ok`。只发了这一张。
- **Muse 回执**：ZIP sha256 `3548a7…bb10`、size 92,188,901，哈希链三方一致（申报 = PC 实测 = A1 落地）；Setup 一并发布（`de8532…b551`，67,120,906）；`latest.json` 更新为 1.8.0，旧版备份 `latest.json.bak-1.7.0`；Muse 侧公网复核清单和两个下载链接都是 200、大小一致。
- **本助手独立公网复核**：
  - `latest.json` HTTP 200，六字段齐全：version `1.8.0`，publishedAt `2026-11-12T01:07:29Z`，url 指向 V1.8.0 ZIP，size 与 sha256 和本地一致，notes 164 字中文正常。
  - HEAD：ZIP 200、长度 92,188,901；Setup 200、长度 67,120,906，都与本地一致。README 里的两个下载链接因此可用。
  - 完整下载 ZIP（放在 OneDrive 外的 `D:\USERNAME\CodexScratch\Temp`），重算 SHA-256 与清单一致，下载文件已删除。
- 桌面快捷方式未动（仍指向 1.4.7 r3）。PROJECT-BRIEF §4、§7 与 HANDOVER 已同步。
- **下一节**：提交推送这份记录，用 `muse-call -Type message` 回 Muse 确认（带这次的文档提交号）。
- **续（02:11）**：记录提交 `b93682f` 已推送、远端一致。回 Muse 的确认 `codex_20261112_021009_019491` 收到 `ok`：Muse 侧已收到复核结果和提交情况，1.8.0 发布闭环完成，无进一步动作。

---

## 1.8.0 · 节 18：门禁与打包（2026-11-12）

- **门禁**（在 `5c3ad2e` 上）：应用 Release 0 警告 0 错误；.NET **277/277**；Python 55（42 跳过）；`Verify-Source` PASS；wx-multi **58/58**；`DialogPreview 1.8.0 1.7.0` 全过。
- **打包**：`wvb-package.ps1 -Version 1.8.0`，即 `Publish.ps1 -OutputDirectory artifacts\win-x64-v1.8.0 -ZipPath …`，再 `Build-Installer.ps1 -PayloadDirectory artifacts\win-x64-v1.8.0`。Clean-payload 两次都是 PASS（1993 文件，没有本机设置，没有真实 Key，比对了 1 个本机凭据）。
- **台账**：
  - payload ProductVersion `1.8.0+5c3ad2e896f063c2cdc09c9617232f0ecbc97caa`，与 HEAD 一致。
  - ZIP `artifacts\WeChat-Voice-Bridge-V1.8.0-win-x64.zip` = **92,188,901** 字节 / `3548a75646c6fc05bef293426e43b3cfc444681e1eb0a0e5422ac8776d55bb10`。共 1996 项，6 个应用程序集与 payload 逐字节相同，没有设置、凭据或数据库文件。
  - Setup `artifacts\WeChat-Voice-Bridge-V1.8.0-Setup.exe` = **67,120,906** 字节 / `de8532a06f1015e94ea6f1d7255694b8ffa922abc5cd3b19278d003c857cb551`。
- **打包产物实跑**（这次开发版目录没被占用，可以另起一份）：用用户真实设置启动 `artifacts\win-x64-v1.8.0\WeChatVoiceBridge.exe`，主窗口是新的默认主题，「更新说明」对话框（544×554，属于主窗口）弹出 v1.8.0 的说明。没有点「知道了」，直接结束进程；前后都备份并复原了 5 个设置文件，`release-notes-seen.json` 仍是 `"1.7.0"`，用户自己更新时会看到说明。
- 桌面快捷方式未动。
- **下一节**：发布前四项自查，然后发一张 Muse 发布单。
- **续（02:04）**：四项自查全绿（WORKLOG 到节 18、HANDOVER 是当前状态、工作区干净、远端 HEAD = 本地 `079f903`）。已发 Muse 发布单 `codex_20261112_020446_0bf7a9`（`publish-voicebridge`，version 1.8.0，ZIP 与上面的 SHA-256，中文说明 164 字），等回执。

---

## 1.8.0 · 节 17：升版本号，写更新说明（2026-11-12）

- **做了什么**：
  - `Directory.Build.props` 升到 **1.8.0**。
  - `ReleaseHistory.cs` 加 1.8.0 条目「外观与主题 · 使用说明重新编写」，五条：外观（默认换成新的浅色主题，原来的绿色在「浅绿」）、使用说明重写、更新说明渐隐、自定义暗语不再自动聚焦且留空按「只报来消息」处理、更新状态点打开即上色。
  - `tools/DialogPreview` 的「滚到中间两头都渐隐」检查原来固定滚到 162 DIP；1.8.0 的说明只能滚 140，于是滚到了底。改成按可滚动高度取中间一段，1.8.0 与 1.7.0 都在中间检查。
- **验证**：
  - `Test.ps1`：.NET **277/277**，Python 55（42 跳过）；58 项密钥自检 PASS。
  - `Verify-Source` 第一次报错：扫描时 `obj` 里一个编译中间文件刚好被删掉（`WeChatVoiceBridge_Content.g.cs` 找不到），单独重跑 PASS。
  - `DialogPreview 1.8.0 1.7.0` 全过：1.8.0 的说明框 321.5 DIP，底边正好切在半行，打开时底部渐隐，滚到中间两头渐隐，滚到底只有顶部渐隐。截图看过。
- **下一节**：节 18，门禁、打包、核对产物版本。

---

## 第三轮 · 节 16：GitHub README 重写（2026-11-12）

- **用户要求**：项目要开源，README 要精心设计，吸引更多星星；尤其开头几段，要清楚简练地讲明项目的定位和用途。
- **做了什么**：
  - `README.md` 整篇重写（旧的还停在 V1.2）。开头是居中的图标、名字、一句话定位、三个徽章（系统、支持的微信版本、版本号）和导航；接着一段话说清是什么，三条说清和别的做法有什么不同（只读不碰微信、19 种语音服务、装上就能用），然后是首页截图。
  - 后面依次是：适合这些时候、功能、四种外观截图、下载（1.8.0 的安装包和 ZIP）、三步开始、支持的语音服务、工作原理、隐私与安全、从源码构建（命令和目录表）、免责声明、致谢、English。
  - 「隐私与安全」里联网的三种情况是对着代码核对的：语音服务、微信官方头像服务器（`AvatarCache` 只允许 qlogo.cn / qpic.cn / weixin.qq.com）、检查更新（`UpdateManifest.Feed`）。
  - 截图 `docs/images/home.png`、`docs/images/themes.png` 用的是**演示数据**：临时把 `settings.json` 换成两位虚构联系人（妈妈、小林）、中性音色名、无头像、空的搜索记录，开发版截完立刻复原，5 个设置文件哈希一致。截图里没有用户真实的联系人、头像、微信号和音色名。生成脚本在 `D:\USERNAME\CodexScratch\Temp\wvb-demo-settings.ps1`、`wvb-readme-images.ps1`（不入库）。
  - 许可证一节暂缺，等用户选定（见「待拍板」）。
- **改了哪些文件**：`README.md`；新增 `docs/images/home.png`、`docs/images/themes.png`。
- **验证**：`Verify-Source` PASS；README 里的命令、安装目录（`.iss` 的 `DefaultDirName`）、Inno Setup 6、第三方组件都和仓库里的一致。下载链接要等 1.8.0 发布后才有效，发布后复核。
- **待拍板**：开源许可证（建议 Apache-2.0，和读取组件参考的上游一致；也可以 MIT 或 GPL-3.0）。仓库改公开前，要先给用户看历史记录的隐私审计结果。
- **下一节**：节 17，升到 1.8.0、门禁、打包、经 Muse 发布。

---

## 第三轮 · 节 15：外观（新的默认主题；浅色 / 深色 / 跟随系统；四种主题色）（2026-11-12）

- **用户要求**：在「设置 → 软件更新」下面加「外观」。新的默认主题要清新、圆润、顺滑（苹果 / 鸿蒙风格），布局基本不动；再把它做成几种颜色：浅绿（现在的样子）、浅粉、浅蓝。深色模式提一级，和「跟随系统」并列，与默认主题同一层；颜色是下一层。
- **做了什么**：
  - **两层**：第一行「外观模式」是浅色 / 深色 / 跟随系统三段按钮；第二行「主题色」是默认 / 浅绿 / 浅粉 / 浅蓝四个圆形色块（左上是底色，右下是强调色，选中的外面加一圈强调色）。默认组合是浅色 + 默认。
  - **「默认」主题色**是新加的中性主题：苹果式浅灰底、白卡片、系统蓝。这是我对「把这个主题颜色化」的理解，默认本身不带颜色；浅绿就是 1.8 之前的样子。待用户确认。
  - **机制**：`Theme.cs` 按模式和主题色生成全部颜色（`ThemePalette.Build`：每种主题色只定 15 个底色，其余按比例混出来，几套之间结构一致），写进应用资源。XAML 全部改成 `DynamicResource`，一换整个窗口立刻重画。代码里自己上色的部分（左下状态胶囊、更新状态点、隐私 / 提醒胶囊和宽卡、设置卡片、对话框外壳）在 `Theme.Changed` 时重画，或者用到时再取色。
  - **不随主题色变的颜色**：绿（正常、使用中）、橙（注意）、红（错误）、赤陶（移除、删除），任何主题下意思都一样；隐私的靛蓝和提醒的橙也固定。
  - **跟随系统**：读 Windows 的「应用模式」，Windows 在浅色和深色之间切换时跟着换。
  - **保存**：`settings.json` 新增 `Appearance { Mode, Tint }`，存数字，只能往后加。启动时在任何窗口出现之前先读它，所以第一帧就是保存的主题，更新助手窗口也一样。老用户升级后第一次打开看到的是新的默认主题。
  - 使用说明「常用功能」加了一张「外观」卡片，`docs/user-guide.md` 同步。
- **改了哪些文件**：
  - 新增 `Theme.cs`、`TintSwatch.cs`、`MainWindow.Appearance.cs`。
  - `App.xaml`（色板和所有样式）、`MainWindow.xaml`（外观组）、`ReleaseNotesDialog.xaml`、`AddItemDialog.xaml`、`App.xaml.cs`、`LocalSettings.cs`、`MainWindow.xaml.cs`、`MainWindow.Updates.cs`、`DialogShell.cs`、`ModeSettingsDialog.cs`、`ProfileEditors.cs`、`Disclosure.cs`、`ModeCapsule.cs`、`ModeCard.cs`、`UserGuide.xaml.cs`、`docs/user-guide.md`。
  - 工具：`DialogPreview` 加 `--theme 模式/主题色`；`UiSmokeTest` 加 `pick` 模式（打开某页、滚到并选中某个单选项，用来切主题，会改设置）。
- **验证**：
  - 编译 0 错误 0 警告。`Test.ps1`：.NET **277/277**，Python 55（42 跳过）；`Verify-Source` PASS；58 项密钥自检 PASS。
  - `DialogPreview`：浅色默认、深色默认、深色浅粉三套，每套 25 项检查全过。看过的截图有更新说明、两张设置卡片、下拉菜单、使用说明整页。
  - **实机巡检**（开发版，用户真实名单）：先把 `settings.json`、`release-notes-seen.json`、两个 `.dpapi`、`app.log` 备份到 `D:\USERNAME\CodexScratch\Temp\wvb-tour-backup-r3`，结束后原样复原，5 个文件哈希一致。
    - 截图看过：浅色和深色各四种主题色的「设置 → 外观」，浅色默认与深色默认的首页、音效、词库、使用说明，深色浅绿的添加语音服务两页。截图在 `artifacts/screenshots/theme-live`（不入库）。
    - 选跟随系统时，本机 Windows 是浅色，软件显示浅色；选深色 + 浅绿后重启，一打开就是深色浅绿。
  - **实机发现并修好**：主题色色块一开始是四个空白圆圈。原因是模板里渐变色标上的绑定找不到模板的父级；改成两个圆形叠放，下面的填底色，上面的填强调色并裁成右下半边，都用 TemplateBinding。
  - 巡检中开发版有一次自己退出了。事件日志里没有它的崩溃记录，只有 UiSmokeTest 因为目标进程不在而报错；同样的步骤又做了三次都没再出现。**原因未查明**，也可能是被人手动关掉的。
  - **未验证**：Windows 自己切换浅色 / 深色时的自动跟随（没去改用户的系统设置）。另外，模态对话框开着时系统切换主题，对话框里少数代码上色的部分要关掉重开才会更新。
- **另外**：用户自己那份 `artifacts\win-x64-v1.5.1` 已在 23:40 用软件内更新升到 1.7.0（`ProductVersion 1.7.0+dd263df`，备份在 `updates\backup-1.5.2`），1.5.2 → 1.7.0 的应用内更新在真机上走通了。
- **下一节**：节 16，GitHub README。

---

## 第三轮 · 节 14：使用说明重做（快速开始在最前，其余只讲必要的）（2026-11-12）

- **用户要求**：第一部分是「快速开始」（第一次安装的人一进来就看到这里，没配置好时软件会先打开这一页），分三步：打开微信并确认版本（附支持的微信版本小表）、获取 API Key 和音色（附支持的厂商、通用协议和本地部署）、添加联系人选音色和最基础的设置。不用太详细。后面那套又怪又长的写法不要了，只解释有必要解释的，一眼能看懂的不说。样式要美观。
- **做了什么**：
  - `UserGuide.xaml` / `.xaml.cs` 重写，旧的 17 个折叠段落和「按需查阅」按钮全部删除。
  - **快速开始**：放在一张大白卡片里，三步用带数字的圆点和竖线串起来。
    - 第 1 步附版本表：4.2.0.13 已实测、4.1.11 至 4.2.0.13 之间支持、范围外不支持。数字直接取 `WeChatSession.MinSupportedVersion/MaxSupportedVersion`，和软件实际的检查是同一处。
    - 第 2 步附服务表：主流厂商、通用协议、自建模型三行，每家一个标签。标签从 `ProviderCatalog.All` 生成，目录里加一家这里就多一个。需海外网络的带琥珀色地球，有免费额度或完全免费的带绿色「免费」。
    - 第 3 步一段话，再加两句提示：不补读旧消息；没声音去「音效」调设备和音量。
  - **常用功能**：两列卡片，提示词、多人朗读、隐私模式、提醒模式、词库，各一两句。
  - **遇到问题**：四问四答：没有朗读、提示版本不支持、合成失败（401/403/402/429）、听不到声音。
  - **隐私**：一段灰底说明。
  - **更新日志**：样式改成版本号加标题，默认只列最近 4 个版本，「查看更早的 N 个版本」点开其余。
  - 新写的部分全部用 `DynamicResource`，为下一节的主题切换做准备。
  - `docs/user-guide.md` 改成和软件里同样的内容，从 296 行减到约 70 行；HANDOVER 里一处引用旧第 12 章的地方同步改掉。
- **验证**：
  - `DialogPreview` 新增使用说明整页截图（不经过窗口直接渲染，否则超出屏幕高度的部分会画坏），并检查目录里 19 家服务都有标签，全过。
  - `Test.ps1`：.NET **277/277**，Python 55（42 跳过）；`Verify-Source` PASS。
- **下一节**：节 15，外观（新的默认主题、浅色/深色/跟随系统、浅绿/浅粉/浅蓝）。

---

## 第三轮 · 节 13：更新说明底部渐隐、暗语留空也能完成、更新状态点回到绿色（2026-11-12）

- **用户要求**（第三轮的第一批）：更新说明框底部加虚化，截断处不要像撕开的纸；隐私模式选了「自定义」却不写内容时点不了「完成」；「设置 → 软件更新」的状态点平时应是绿色，有更新是红色，下载完待重启安装是橙色，现在却是灰色。
- **更新说明**（`ReleaseNotesDialog`）：
  - 底部 30 DIP 渐隐，与「正好露出半行」并存：半行文字淡出，看得出下面还有内容。
  - 滚动后顶部 24 DIP 渐隐。原来的白色渐变盖层删掉，改成文字自身的透明度遮罩，不会盖住滚动条和边框，换主题时也不用跟着改颜色。
  - 只有那一侧还有内容时才渐隐：打开时只有底部，滚到中间两头都有，滚到底只有顶部，放得下就都没有。
- **暗语留空**（`ModeSettingsDialog`）：
  - 复现：屏幕外点击、真实鼠标点击（模态打开、从按钮点开、先点选项再点输入框、输入法切到中文），在现在的代码和用户那份开发版的同一提交 `d9c0951` 上都点得动「完成」，**没有复现**。
  - 判断：只在「暗语框为空且有焦点」时出现，换别的选项（框被收起、失去焦点）就好，这符合输入法在空输入框旁边弹出浮窗、正好盖住「完成」的情况。本机装的是微信输入法 2.1.1.8。这个原因**未证实**。
  - 改法：选「自定义暗语」时不再自动把焦点放进输入框，要写时自己点；关闭卡片时暗语是空的，就存成「只报来消息」（播报本来就一样），下次打开不会停在一个空的自定义上。
- **更新状态点**（`MainWindow.Updates.cs` / `MainWindow.xaml`）：
  - 原因：状态点在 XAML 里的初始颜色是灰色，只有检查更新结束后才上色。更新重启后的那次启动不检查更新，于是**整个会话都是灰色**；平时启动时，检查完成前的几秒也是灰色。
  - 改法：启动时立刻按当前状态上色，初始颜色也改成绿色。三种颜色的对应关系没变：绿色已是最新、红色发现新版本、橙色下载完待重启。
  - 另外：用户现在开着的是 `artifacts\win-x64-v1.5.1` 那份旧版本，不是 1.7.0。
- **验证**：
  - `Test.ps1` 全过（开发版目录这次没被锁）：.NET **277/277**，Python 55（42 跳过）；`Verify-Source` PASS。
  - `DialogPreview` 新增渐隐检查和暗语检查，25 项全过。新增 `--realclick` 模式：卡片移到鼠标现在的位置下面，鼠标不动，只有确认最上层是这张卡片才点。
- **下一节**：节 14，使用说明重做（快速开始）。

---

## 1.7.0 · 节 12：经 Muse 发布上线，公网复核通过（2026-11-11）

- **发布单** `codex_20261111_211423_9ceb57`：21:14 发出，在 inbox 里放了约 21 分钟，21:35 被 Muse 领走，21:37 回 `status=ok`。这次没有先回 `info`。`muse-call` 自己的 480 秒等待先超时退出（退出码 2，属正常），回执是直接读 outbox 文件拿到的。只发了这一张。
- **Muse 回执**：ZIP sha256 `2a0940…6029a`、size 92,186,434，哈希链三方一致（申报 = PC 实测 = A1 落地）；Setup 一并发布（`763909…5114`，67,123,954）；`latest.json` 更新为 1.7.0，旧版备份 `latest.json.bak-1.6.0`。
- **本助手独立公网复核**：
  - `latest.json` HTTP 200，六字段齐全：version `1.7.0`，publishedAt `2026-11-11T20:37:01Z`，url 指向 V1.7.0 ZIP，size 与 sha256 和本地一致，notes 177 字中文正常。
  - HEAD：ZIP 200、长度 92,186,434；Setup 200、长度 67,123,954，都与本地一致。
  - 完整下载 ZIP（放在 OneDrive 外的 `D:\USERNAME\CodexScratch\Temp`），重算 SHA-256 与清单一致，下载文件已删除。
- 桌面快捷方式未动（仍指向 1.4.7 r3）。PROJECT-BRIEF §4、§7 与 HANDOVER 已同步。
- **下一节**：提交推送这份记录，用 `muse-call -Type message` 回 Muse 确认（带这次的文档提交号）。
- **续（21:41）**：记录提交 `fbb856e` 已推送、远端一致。回 Muse 的确认 `codex_20261111_213907_514b37` 收到 `ok`：Muse 侧独立复核清单、哈希、ZIP 长度都一致，1.7.0 闭环完成，无进一步动作。

---

## 1.7.0 · 节 11：门禁与打包（2026-11-11）

- **门禁**（在 `dd263df` 上）：应用 Release（`-r win-x64`，因为用户那份开发版锁着 `bin\Release`）0 警告 0 错误；.NET **277/277**；Python 55（42 跳过）；`Verify-Source` PASS；wx-multi **58/58**。
- **打包**：`Publish.ps1 -OutputDirectory artifacts\win-x64-v1.7.0 -ZipPath …`，再 `Build-Installer.ps1 -PayloadDirectory artifacts\win-x64-v1.7.0`。worker 依赖与 edge-tts 冒烟检查通过；Clean-payload 两次都是 PASS（1993 文件，没有本机设置、没有真实 Key）。
- **台账**：
  - payload ProductVersion `1.7.0+dd263dfdf21e0e153709129a886289f74a2aa6b8`，与 HEAD 一致。
  - ZIP `artifacts\WeChat-Voice-Bridge-V1.7.0-win-x64.zip` = **92,186,434** 字节 / `2a0940984d95bc72fb05ca4f69ad3fa9e9a422fe75121832db171b8af166029a`。ZIP 共 1996 项，里面的 `WeChatVoiceBridge.exe` 与 `.dll` 和 payload 逐字节相同，没有设置、凭据或数据库文件。
  - Setup `artifacts\WeChat-Voice-Bridge-V1.7.0-Setup.exe` = **67,123,954** 字节 / `763909e1c9814277112b945a627b042f19fbd31dbc09ed7c9979ca1b7dac5114`。
- **没做**：没有运行打包出来的程序。单实例会把启动请求转给用户正在用的那份，所以没法在这台机器上另起一份。桌面快捷方式未动。
- **下一节**：发布前四项自查，然后发一张 Muse 发布单。
- **续（21:14）**：四项自查全绿（WORKLOG 到节 11、HANDOVER 是当前状态、工作区干净、远端 HEAD = 本地 `bec3102`）。已发 Muse 发布单 `codex_20261111_211423_9ceb57`（`publish-voicebridge`，version 1.7.0，ZIP 与上面的 SHA-256，中文说明 177 字），等回执。

---

## 1.7.0 · 节 10：升版本号，写更新说明（2026-11-11）

- **做了什么**：
  - `Directory.Build.props` 1.6.0 → **1.7.0**。
  - `ReleaseHistory` 新增 1.7.0「隐私模式与提醒模式」，共 6 条：两个开关在哪、隐私模式、提醒模式与持续提醒、怎样算回复与「✓ 已读」和角标、提醒音与提醒词、更新说明的滚动条与半行。
  - 第 3 条删了两个字（「也可以」→「也可」，「直到你回复」→「直到回复」）。原来这一条最后一行只剩一个「复」字，底边正好切在这一行，露出的只有半个字；现在切在下一条的第一整行中间。
  - `tools/DialogPreview` 打印排版用的 DPI。这次是 2x，与用户屏幕一致，所以截图里的折行就是用户会看到的。
- **验证**：`DialogPreview` 1.7.0 全过，底边切在第 4 条第 1 行的 50% 处。
- **下一节**：先提交这一节（打包要带这个提交号），再跑门禁、打包、写台账，然后发一张 Muse 发布单。

---

## 任务二 · 节 9b：更新说明的滚动条只在滚动时出现，底部正好露出半行（2026-11-11）

- **用户要求**：更新说明里的小滑块不滑动时消失；需要滚动时，底部要正好只露出半行文字。
- **做了什么**（`ReleaseNotesDialog.xaml` / `.xaml.cs`）：
  - 滚动条：去掉「鼠标在框内就显示」的触发器，改由代码控制。内容移动时 120 ms 淡入，停下 0.9 秒后 320 ms 淡出；指针在滚动条上或正在拖动时不淡出；指针移到那条细条上也会显示，方便拖动。应用的平滑滚动每帧都会移动内容，所以淡入只启动一次，不会每帧重来。
  - 半行露出：上下内边距挪进滚动内容里，可视区一直延伸到框的边线。内容放不下时，在所有正文行里找出不超过高度上限、离底最近的一行，把框的高度定在这一行的正中，于是底部正好露出半行。项目符号「•」不参与计算。放得下的版本保持原来的高度。
  - 底部原来 30 DIP 的渐隐会把这半行洗掉，所以删除了；顶部渐隐保留，滚上去的字仍然淡出。
- **新工具** `tools/DialogPreview`：引用应用本身，在屏幕外打开真实的更新说明和两张设置卡片，量出结果并存 2 倍截图。资源来自 `App.xaml`，**不构造 `App`**：它的构造函数会排队执行 `OnStartup`，第一次跑时就因此敲了正在运行的那份应用的单实例管道，把它的窗口唤到前面（两次，无其他影响）；已改掉。只能用 Debug 编译（Release 会写进被锁的 `bin\Release`）。居中检查需要窗口在屏幕上，所以那一步用完全透明、不激活、点击可穿透的窗口，停留不到 1 秒。
- **验证**（`DialogPreview artifacts\screenshots\round9 1.6.0 1.5.3 1.3.3`，21 项全过）：
  - 1.6.0 和 1.5.3 的底边都切在一行的 50%（行高 23），1.3.3 放得下，保持原高度。
  - 打开时滚动条透明度 0；滚动时 1；停下 1.5 秒后回到 0；逐帧滚动（模拟平滑滚动）时也到 1。
  - 窗口仍然在所属窗口正中：WPF 在框高定下来之后才计算居中位置（实测事件顺序：高 572.5 → 565 → 定位）。
  - 应用 Release（`-r win-x64`）0 警告 0 错误；`Verify-Source` PASS。截图 `artifacts/screenshots/round9/notes-*`（不入库）。
- **下一节**：升 1.7.0，写 ReleaseHistory，打包，经 Muse 发布。

---

## 任务二 · 节 9a：设置卡片精简、提醒词改书面语、试听可取消（2026-11-11）

- **用户要求**（第二轮打磨的一部分）：精简不必要的小字；提醒词选项太口语；提醒音试听中变成「取消」、可以取消；「选择本机音频」仿照提示词菜单最后的「自定义」，和上面的铃声不是一类，样式要不一样。
- **做了什么**：
  - `ModeSettingsDialog.cs`：
    - 小字精简：两个「开启」开关行去掉说明；持续提醒只留「直到回复或标为已读」；时间下方只写「共提醒 N 次」（持续提醒时隐藏）；删掉提醒音说明和隐私合并说明；暗语示例改为「一句只有你懂的话」。
    - 提醒词改书面语：复述消息内容 / 仅播报条数 / 播报联系人与条数。预览行改成「示例：“…”」。
    - 提醒音菜单最后一项「选择本机音频」做成动作项：上方一条分隔线、绿色「＋」、绿色加粗字，与提示词菜单的「自定义」同一种模板写法。选中它时，收起框立刻回到正在用的提醒音（和提示词一样，只把选中项放回去，不重建列表），再打开文件选择框。
    - 试听按钮播放中变「取消」（无障碍名「取消试听」），点它停止；关掉卡片也会停止。
  - `MainWindow.Attention.cs`：`PreviewSoundAsync` 接收取消令牌并交给播放器；取消不算播放失败。
  - `ContactCard.cs`：模式未开启时，卡片摘要只写「未开启」。
  - 使用说明第 10 节与 `docs/user-guide.md`：换成新的提醒词名字，写上「选择本机音频」和试听可取消。
- **验证**：
  - 离屏检查（`tools/DialogPreview`，下一节提交）：提醒音菜单 22 项，最后一项是「选择本机音频」，收起框显示「闹钟 1」；试听中按钮是「取消」，取消后 14 ms 内停下、按钮回到「试听」；播放中关卡片，25 ms 内停下；开隐私后提醒词只剩两项。试听用真实播放器、音量 0。截图在 `artifacts/screenshots/round9/settings-*`（不入库）。
  - 用户正在运行开发版（pid 50916，占用 `bin\Release\net8.0-windows`），所以门禁按 `Test.ps1` 的同样步骤跑，只是应用改用 `-r win-x64` 编译：应用 0 警告 0 错误；.NET **277/277**；Python 55（42 跳过）；`Verify-Source` PASS。
  - 未验证：「选择本机音频」打开文件框（离屏测会弹出真实对话框，没测；写法与提示词的「自定义」相同）；实机巡检（单实例，用户那份正在运行）。
- **下一节**：节 9b，更新说明的滚动条与半行露出。

---

## 任务二 · 节 8：使用说明与文档收尾（2026-11-11）

- **做了什么**：
  - 软件内使用说明新增第 10 节「隐私模式和提醒模式」，原来的第 10–16 节顺延为 11–17 节。内容包括：开关位置、隐私三种播报、提醒计时规则、怎样算回复、已读与角标、持续提醒、提醒音和提醒词。
  - `docs/user-guide.md` 加入同样一节。这份文档的编号本来就落后于软件内说明，这次没有整体重排。
  - `privacy-reminder.md` 状态改为「已实现，等用户实测」。
  - HANDOVER 与 PROJECT-BRIEF 同步。
- **验证**：`Test.ps1` .NET **277/277**、Python 55（42 跳过）；`Verify-Source` PASS。
- **状态**：任务二代码完成，**未升版本号、未打包、未上线**，桌面快捷方式未动。等用户实测（清单见 HANDOVER §00）。
- **下一步**：根据用户实测反馈修改；用户同意后再升版本、打包、经 Muse 发布。

---

## 任务二 · 节 7：两张设置卡片 + 实机巡检与动画调校（2026-11-11）

- **做了什么**：
  - `src/VoiceBridge.App/ModeSettingsDialog.cs`（新）：⋮ 打开的设置卡片，用 `DialogShell` 做外壳，和「更新说明」同一种外观（米白卡、细边、圆角 14、右上角关闭、22 号标题 + 灰色副标题、内嵌白色圆角框、底部居中「完成」）。改动立即生效，「完成」和关闭都只是关掉。
    - **隐私**：顶部「开启隐私模式」开关；三选一（只报来消息 / 播报名字 / 自定义暗语）；选暗语时出现输入框。换播报方式时，提醒词回到对应的默认值。
    - **提醒**：顶部开关；「每隔」「最多持续」两个步进器（1–10 每格 1 分钟，到 60 每格 5 分钟，到 3 小时每格 15 分钟），每隔不会超过总时长；一行写明「最多提醒 N 次」；持续提醒开关，打开后两个时间变灰；提醒音下拉（不响 / 闹钟 1–10 / 铃声 1–10 / 本机文件 / 选择本机音频…）+ 试听；提醒词下拉（开隐私时没有「读出没回的消息」）+ 一行「提醒时会说：……」预览。
  - `MainWindow.Attention.cs`：`OpenModeSettings` 打开卡片，关上后立刻保存；`PreviewSoundAsync` 用朗读设备播一遍提醒音，读不到时给出提示。
  - `ModeCapsule`：用鼠标点开关后不再留下绿色焦点框，键盘切换时才显示。
  - `MainWindow.Morph.cs` 调校：
    - 留下的那张卡直接以新形状从旧位置滑过去，旧图在约 0.1 秒内融化，不再两张叠着看。
    - 「朗读对象」标题行也跟着滑，修好了它先跳到位、盖住卡片的问题。
    - 缓动从五次改成三次，时长 360 ms。原来宽卡缩进胶囊的动作在头几帧就结束了，肉眼看不到。
  - `tools/UiSmokeTest` 新增 `task2` 巡检模式：关掉更新说明，依次截首页、两张设置卡片、两个模式打开后的样子，再移除第二个人、从最近联系人加回来，按 0/60/120/180/260/520 ms 逐帧截变形过程。它会改 `settings.json`，调用前要先备份。
- **实机巡检**（开发版，用户真实名单）：
  - 先把 `settings.json`、`release-notes-seen.json` 备份到 `D:\USERNAME\CodexScratch\Temp\wvb-tour-backup`，巡检后用 `Stop-Process` 结束，再原样复原。三次巡检后都核对过哈希一致。
  - 截图在 `artifacts/screenshots/tour*`（不入库）：两张设置卡片、两个模式开启（隐私靛蓝、提醒橙色）、二变一（宽卡从胶囊行展开）、一变二（宽卡缩回胶囊、新卡从右侧滑入）。
- **验证**：应用编译 0 错误；`Test.ps1` .NET 277/277、Python 55（42 跳过）；`Verify-Source` PASS。
- **仍未实机验证**：监听中收到消息后的「✓ 已读」与角标、提醒音与提醒词的实际播报、手机端回复后提醒停下。这些要真的开始监听、有人发消息，需要用户配合。
- **下一节**：节 8，使用说明新增一节、文档收尾。

---

## 任务二 · 节 6：单人方案 B（两张宽卡）+ 单人 ↔ 多人变形动画（2026-11-11）

- **做了什么**：
  - `src/VoiceBridge.App/ModeCard.cs`（新）：模式宽卡，高 64、圆角 16，与状态卡同一层柔和阴影。
    - 内容：30×30 圆角色块（关闭浅色、开启深色）、标题「隐私模式 / 提醒模式」、一行当前设置、46×28 标准开关、竖排 ⋮。
    - 关闭时白底，开启时淡靛蓝 / 淡橙底、标题变色。有人待回复时，那一行变琥珀色。
    - 点整张卡切换开关，⋮ 打开设置，和胶囊用同一个事件。
  - `IModeSwitch` 接口：胶囊与宽卡共用，`ModeSettingsRequested` 不再关心是哪一种。
  - `MainWindow.xaml`：
    - 状态卡下面加 `SingleModes`：两张宽卡，间距 12，绑定唯一那位联系人。只有一个人时显示（`UpdateContactsCaption` 里切换）。
    - 朗读记录的标题行和卡片取名 `RecordsHeader` / `RecordsCard`。
    - 内容区上面加一层不接收点击的 `MorphLayer`，动画用。
  - `ContactCard` 的两行说明：关闭时写「未开启，照常朗读内容」「未开启，没回也不再提醒」。
  - `src/VoiceBridge.App/MainWindow.Morph.cs`（新）：人数在 1 和 2 之间变化时，用变形代替跳变。
    - 切换前，把旧的宽卡、旧卡片和打开着的选人面板拍成图片，放在原位。
    - 切换后，每张旧图滑向自己新身体的位置并淡出，新元素从旧位置滑回并淡入。340 ms，与「高级」展开同一种五次缓出。
    - 一变二：两张宽卡整体缩进留下那位的胶囊行，新加的人从右侧 28 DIP 滑入。
    - 二变一：胶囊行放大展开成两张宽卡，被移除的那张原地缩小淡出。
    - 下面的朗读记录从旧位置滑到新位置。
    - 系统关闭动画、窗口最小化或不在首页时直接切换；动画出错只会退回直接切换，不影响名单本身。
  - 添加、移除联系人的两处代码包进 `MorphContacts(...)`。添加时把关闭选人面板也放进去，这样量到的是最终布局。
- **验证**：
  - 应用编译 0 错误；`Verify-Source` PASS。
  - 宽卡三态预览（离屏渲染真实控件）：`artifacts/screenshots/task2-wide-cards.png`，依次为都关、都开、暗语 + 2 条没回（琥珀色）。
  - **未验证**：单人布局的实机样子，以及变形动画的实际效果。这需要在用户真实名单上移除再加回一个人，放到节 8 做：先备份设置、逐帧截图、最后复原。
- **下一节**：节 7，两张设置卡片（复用「更新说明」外观）。

---

## 任务二 · 节 5 续：胶囊关闭态不再是千篇一律的灰（2026-11-11）

- **用户反馈**：没开启时两个胶囊一样灰，有点单调、不美观，要小小优化；其他没问题，继续。
- **改法**（`ModeCapsule.cs`）：学 iOS 设置里的行，图标放进 18×18 的圆角色块，白色图标。关闭时也保留各自颜色，但更浅：隐私是浅靛蓝色块，提醒是浅橙色块。胶囊底色是带一点冷 / 暖调的近白（`#F8F9FB` / `#FBF9F6`），⋮ 也随之带一点色调。开启时色块加深，整个胶囊染上本色，名字加粗。开关仍然是表示开或关的那一部分。
- **验证**：`tools/CapsulePreview` 四态预览 `artifacts/screenshots/task2-capsules-3.png`；应用编译 0 错误；`Verify-Source` PASS。
- **下一节**：节 6（不变）。

---

## 任务二 · 节 5：多人卡片方案 A（胶囊、已读、角标）（2026-11-11）

- **做了什么**：
  - `src/VoiceBridge.App/ModeCapsule.cs`（新）：胶囊控件，代码里搭出来，包含锁 / 铃图标、两个字的名字、34×20 小开关和竖排 ⋮。
    - 开关部分是一个透明模板的 `ToggleButton`，键盘和读屏可用。
    - ⋮ 是单独按钮，发出 `SettingsRequested` 事件。
    - 颜色：关闭时浅灰；隐私开启时靛蓝（`#EFEFFC` 底、`#5E5CE0` 开关）；提醒开启时橙色（`#FFF3E4` 底、`#EE8B12` 开关）。开启时名字加粗。
    - 动画：开关 160 ms 过渡；系统关闭动画时直接到位。
  - `ContactCard`：新增 `PrivacyOn`、`ReminderOn`、`ShowPreview`、`ShowRead`、`ShowBadge`、`Badge`，以及给单人宽卡用的 `PrivacySummary` / `ReminderSummary`。
    - 切换隐私时，提醒词改回跟随默认。
    - `SetListening`、`SetUnanswered` 由主窗口推送。
  - `MainWindow.xaml` 多人卡片：
    - 「试听」「移除」挪到名字右侧，保留原样式与文字。
    - 空出来的一行放两个对称胶囊。
    - 头像换成带红色角标的 `BadgedAvatar`。
    - 新增琥珀色药丸样式 `ContactReadAction`（✓ 已读）。
  - 单人卡片同样：监听时隐藏试听、待回复时出现已读、头像角标。
  - `MainWindow.Attention.cs`：`RenderAttention` 在每次 `Render` 时推送监听状态和条数；`AttentionChanged` 只更新那一位；`MarkReadClick`。⋮ 的设置卡片留到节 7，目前点了没有反应。
  - `ContactPreferenceChanged`：开关提醒后立刻刷新角标。
- **工具**：
  - `tools/CapsulePreview`（新）：链接真实的 `ModeCapsule.cs`，离屏按 2 倍渲染四种开关组合，不碰用户数据。
  - `tools/UiSmokeTest` 新增 `print` 模式：用 PrintWindow 截主窗口当前的样子，不点任何东西，弹窗开着也能截。
- **验证**：
  - 应用编译 0 错误；`Test.ps1` .NET **277/277**、Python 55（42 跳过）；`Verify-Source` PASS。
  - 实机截图 `artifacts/screenshots/task2-multi-a-print.png`：两位联系人时，名字右侧是试听 / 移除，下方两个灰色胶囊，页面仍不滚动。
  - 胶囊四态预览 `task2-capsules-2.png`。初版预览发现开启态滑块没移到右边：离屏渲染不跑动画时钟，零时长动画不生效。已改为不做动画时直接设值。
  - **未验证**：监听中「✓ 已读」与角标的实际样子（要真的开始监听并收到消息，放到节 8 实机验证）。
- **副作用与复原**：
  - 开发版启动时弹出了「已更新到 v1.6.0」。截完图我直接结束了进程，没有点「知道了」。但 `release-notes-seen.json` 已被写成 1.6.0，原因没查明：确认操作只在对话框关闭后才写这个记录，而我没关它。
  - 为了让用户自己打开 1.6.0 时仍能看到更新说明，我把它改回 `"1.5.3"`。
  - 以后实机截图：先用 `print` 模式，截完用 `Stop-Process` 结束，不要点掉弹窗。
- **下一节**：节 6，单人方案 B（两张宽卡放在状态卡与朗读对象之间）+ 单人 ↔ 多人切换动画。

---

## 任务二 · 节 4：应用接线（隐私播报、提醒调度、提醒音；还没有新界面）（2026-11-11）

- **做了什么**：
  - `src/VoiceBridge.App/MainWindow.Attention.cs`（新）：`MainWindow` 持有一个 `ReplyTracker`；订阅会话的 `Received`、`Replied`、`Finished`，都经 `Dispatcher.InvokeAsync` 回到界面线程，并核对会话戳。`controller.Merge` 只读不可变的路由快照，在 worker 线程上判定「这个人开了隐私」。1 秒一次的 `DispatcherTimer` 取到期提醒，组句后 `controller.Announce`，朗读记录加一行「提醒：……」。记录里永远不出现内容：读内容的提醒，记录改用说人名的说法。提醒音 `ReminderSounds`：「不响提醒音」、Windows 自带的闹钟 1–10 与铃声 1–10（`C:\Windows\Media\Alarm01–10.wav`、`Ring01–10.wav`），或本机 .wav/.mp3 全路径；读一次后缓存，读不到就不响。
  - `RoutedSpeech.Resolve`：提醒原样使用（入队时已组好、已过词库）；开了隐私的联系人改说 `AttentionText.Privacy(…, message.Merged)`，并过词库；其他照旧加提示词。
  - 朗读记录 `Friendly("已接收")`：开了隐私的联系人只显示「某某发来一条新消息」，即使设置里开了显示内容。
  - 试听：开了隐私的联系人不读最后一条消息，只读隐私播报句。
  - 结束监听时（`Render` 里会话收尾那一段）清空待回复。
- **对没开两个模式的用户**：行为不变（默认都关：不合并、不提醒、不改说法），只多一个每秒一次的空检查。
- **改动文件**：`MainWindow.Attention.cs`（新）、`MainWindow.xaml.cs`、`MainWindow.Library.cs`。
- **验证**：应用编译 0 错误；`Test.ps1` .NET **277/277**、Python 55（42 跳过，基线）；`Verify-Source` PASS。接线本身要等界面能打开开关后实机验证（节 8）。
- **下一节**：节 5，多人卡片（方案 A）：「试听」「移除」挪到名字右侧、监听时隐藏「试听」、有没回的消息时换成琥珀色「✓ 已读」、头像红色角标、两个对称胶囊（隐私 = 靛蓝、提醒 = 橙色，关时浅灰，小开关 + 竖排 ⋮）。

---

## 任务二 · 节 3：Core 的提醒与隐私逻辑（2026-11-11，界面未接）

- **做了什么**：
  - `src/VoiceBridge.Core/Attention.cs`（新）：`PrivacyStyle`（微信 / 说人名 / 暗语）、`ReminderWording`（读内容 / 微信 / 说人名）、`MessageKind`，枚举显式编号、只能末尾追加；每人设置 `PrivacySettings`、`ReminderSettings`（默认每 3 分钟、最多 20 分钟，钳在 1 分钟–3 小时，间隔不超过总时长；持续提醒；提醒音默认 `Alarm01`；`Wording` 为空时跟随隐私）；`AttentionText` 生成所有播报句（「微信来消息了 / 微信来了 3 条消息」「争渡给你发消息了」「天王盖地虎」「微信在 3 分钟前来了一条消息」「争渡在 6 分钟前给您发了 3 条消息」「来自 3 分钟前的消息，……」，时长说成「1小时20分钟」）。开隐私时提醒词没有「读内容」，旧的「读内容」选择会自动退回默认，内容不会被读出来。
  - `src/VoiceBridge.Core/ReplyTracker.cs`（新）：按人记待回复，规则即用户定稿：从最近一条算一轮、一轮走完停但不清零、新消息重起一轮、回复只清掉早于它的消息（按 `sort_seq`）、晚到但早于回复的消息不算、「已读」只清这个人、结束监听全清；持续提醒最近一条 1 分钟后开始，每次播完 5 秒后下一次，播放没回报时 2 分钟后自动放开。
  - `ContactProfile` 加 `Privacy`、`Reminder`（新联系人默认都关；旧设置文件没有这两项时取默认）。
  - `ChatMessage` 末尾加可选的 `Kind`、`Merged`、`Cue`。
  - `SessionController`：
    - `Announce` 往同一队列插提醒，仍是单线程依次播放；
    - `Received` / `Finished` 事件；
    - `Merge` 判定：隐私模式下还在排队的同一人消息合成一条，开始播时定下条数；
    - 提醒不再走 worker 重渲染；
    - 提醒音只在第一段前播一次；
    - 清空队列与停止时清掉合并组。
- **改动文件**：上述 4 个 Core 文件 + `tests/VoiceBridge.Tests/AttentionTests.cs`（新，21 项：播报句、提醒词默认与隐私屏蔽、时长说法、范围钳制、设置文件往返与旧文件、默认时间表 3…18、1 小时每 5 分钟 12 次含第 60 分钟、新消息重起一轮、一轮结束不清零且 10 小时后从新消息重算、回复只清早于它的、已读只清一个人、持续提醒节奏、关提醒仍计数、队列合并「b×3」、提醒先响提醒音再说最终文字并回报完成）。
- **验证**：`Test.ps1` .NET **277/277**、Python 55（42 跳过，基线）；`Verify-Source` PASS。本节没改 worker。
- **下一节**：节 4，应用接线（不含新界面）：隐私播报接入 `RoutedSpeech` 与试听、朗读记录不显示内容、`ReplyTracker` 接会话事件与 1 秒定时器、提醒音（Windows 自带 / 本机文件）载入。

---

## 任务二 · 节 2：worker 报告「你回复了」（2026-11-11）

- **做了什么**：
  - `worker/wechat_adapter.py`：`WeChatStore` 新增与来信游标分开的回复游标 `reply_cursor`（开始监听时与基线相同，所以开始前的回复不算）。`query()` 在同一个快照连接里，为每个联系人查「游标之后、发送者不是对方、发送者不为 0、类型不是 10000/10002」的最大 `local_id` 与 `sort_seq`，只读元数据不读正文；`take_replies()` 交出每个联系人的最新回复时间戳。回复不需要确认，游标立刻前进。
  - `worker/main.py`：`query` 的回应（`message` 或 `heartbeat`）在有回复时多带一个 `replies` 字段；没有回复时报文与原来完全一样（测试替身与旧报文不受影响）。
  - `PythonFixtureSource.QueryAsync`：读 `replies`，每条转成一条 `Direction.Outgoing` 的空消息交给 `admit`，**不进确认列表**。
  - `SessionController`：新增 `Replied` 事件（`ReplySignal(ConversationId, Sequence)`）；`Admit` 收到本会话联系人的 Outgoing 时触发它并返回 Ignored，不朗读、不入队；会话结束后返回 Closed。
- **顺带修了一个不稳定的老测试**：`LifecycleTests.QueueOverflowAndClearAreVisibleAndSkipCancelsCurrent` 假设第 3 条一定被丢弃，但播放时的预取会在线程池上把第 2 条从队列里取走，谁先谁后看运气；在原始代码上单独跑也是 5 次失败 5 次。改为先等预取真正发生（第 2 条开始合成），再验证第 3 条入队、第 4 条被丢弃、重复的第 4 条判为重复。
- **改动文件**：`worker/wechat_adapter.py`、`worker/main.py`、`src/VoiceBridge.Core/Contracts.cs`、`src/VoiceBridge.Core/SessionController.cs`、`src/VoiceBridge.WeChat/PythonFixtureSource.cs`、`tests/python/test_live_adapter.py`（新测试：开始前的回复不算、引用回复算、系统消息不算、对方撤回不算、通话算、游标前进后不重复）、`tests/VoiceBridge.Tests/ReplyTests.cs` + `replies_worker.py`（真实进程 IPC：回复随心跳与消息到达、不被确认、不朗读；别的聊天与停止后的回复被忽略）、`tests/VoiceBridge.Tests/LifecycleTests.cs`、`VoiceBridge.Tests.csproj`。
- **验证**：`Test.ps1` .NET **256/256**、Python 55（42 跳过，与基线同）；需要 SQLCipher 的适配器测试用构建输出里的 Python 单独跑：`test_live_adapter` **33/33**、`test_multi_contact` **11/11**；wx-multi **58/58**；`Verify-Source` PASS。
- **坑**：用 `Copy-Item` 把源文件复原后，文件时间比已编译的 DLL 旧，MSBuild 会当成没改而不重新编译；复原后要先把文件时间改成现在。
- **下一节**：节 3，Core 的提醒逻辑（按人记待回复、时间表、持续提醒、合并隐私播报、往队列插提醒）与每位联系人的隐私 / 提醒设置。

---

## 任务二 · 节 1：只读探针——自己发出的消息在电脑端数据库里的样子（2026-11-11）

- **用户定稿**（`docs/privacy-reminder.md` §1「补充四」）：监听时「试听」隐藏，开提醒且有没回的消息时该位置显示「✓ 已读」；胶囊隐私 = 靛蓝、提醒 = 橙色；更多按钮改竖排 ⋮；计时按最近一条消息算，一轮走完不清零、继续累加，再来消息时从新消息重新起一轮；读内容时全部重读；开隐私时提醒词没有「读内容」。用户说「达成共识就直接干」，从本节起进入实现。
- **做了什么**：新增 `tools/probes/outgoing_probe.py`。复用 worker 的 `WeChatStore`（同一套只读快照与密钥读取），只看设置里已选的联系人，只输出计数与类型。按 AGENTS.md：`tempfile.TemporaryDirectory()` 放在 `D:\USERNAME\CodexScratch\Temp`（OneDrive 之外），每个分片一份快照，连接用 `closing` 关闭后才离开临时目录。worker 第三方包从构建输出的 `worker\deps` 取（源码树里没有）。
- **结论**（详见 `privacy-reminder.md` §7）：自己发出的消息在同一张 `Msg_` 表，发送者不是对方；`origin_source` 有 1/2/4 三种来源，手机端发出的也在；系统消息 10000 两边都有，判断回复必须排除；`sort_seq` 是毫秒时间戳，用它比较回复与来信的先后。
- **验证**：探针 exit 0；`Verify-Source.ps1` PASS。临时目录已随上下文删除。
- **下一节**：节 2，worker 在 `message` 包里带回每个联系人的最新回复 `sort_seq`（`replies`），加 Python 测试。

---

## 任务二 · 节 0 续二：用户选定方案并定下提醒词与计时规则（2026-11-11，未动代码）

- **用户决定**（原话整理进 `docs/privacy-reminder.md` §1「补充三」）：多人卡片选 A，但「试听」「移除」保留文字药丸、胶囊也保留文字，「确认已读」要更醒目；单人卡片选 B（模式放在状态卡与朗读对象之间），单人变多人的动画要做丝滑；持续提醒 = 响提醒音 → 说提醒词 → 停 5 秒循环；提醒词改为下拉三选一（读内容 / 微信 / 说人名），取消自定义提醒词，默认值按隐私播报方式对应；计时按「人」成组，条数只增不减，N 从第一条算，最后一条过了设定时长才停；新联系人两个模式默认关。
- **修订**：`privacy-reminder.md` §8 增「第三版修订」（多人卡片药丸挪到名字右侧、两个对称胶囊、琥珀色「✓ 已读」替换试听、单人宽卡、动画思路、提醒卡改下拉）。会话里新画一张多人卡片修订图。
- **改动文件**：`docs/privacy-reminder.md`、`docs/WORKLOG.md`、`docs/HANDOVER-CURRENT.md`。
- **验证**：纯文档，无构建。
- **待用户确认**：一条时间线示例（提醒从最近一条算、N 从第一条算）；读内容时多条是否全部重读；开隐私时提醒词是否还允许选「读内容」；提醒到时仍没回时角标是否清零。
- **下一节**：节 1 只读探针（不变）。

---

## 任务二 · 节 0 续：用户改为「每位联系人各自设置」+ 第二版效果图（2026-11-11，未动代码）

- **用户反馈**：不要首页磁贴，两个模式都放进每位朗读对象的卡片里、各自开关；胶囊 = 开关 + 设置小按钮（⋯）；设置弹出复用「更新说明」小卡片；能用图标就不用文字；提醒间隔与总时长可调（1 分钟–3 小时，默认每 3 分钟、最多 20 分钟），加「持续提醒」；提醒音可选（闹钟式音乐 / 提醒音，持续时间隔 5 秒）；提醒词跟随隐私模式（隐私自定义时提醒也给输入框）；「确认已读」只管单个人。原话整理进 `docs/privacy-reminder.md` §1「补充二」，第一版 §5 标为已否决，新增 §8 第二版设计。
- **第二版效果图**（会话产物 SVG，1:1）：多人卡片两种放法（A 胶囊在名字下方、试听与移除改成右上角图标，卡片不加高；B 胶囊吸附在卡片底栏）；单人卡片两种放法（A 胶囊进标题行；B 按用户想法放在状态卡与朗读对象之间）；两张设置小卡片（复用更新说明的米白卡、标题、内嵌白框、底部「完成」）。
- **提醒音来源**：苹果闹钟铃声有版权，不能打包；改用 Windows 自带 `C:\Windows\Media\Alarm01–10.wav`、`Ring01–10.wav`（本机已确认存在）+「选择本机音频…」。
- **改动文件**：`docs/privacy-reminder.md`、`docs/WORKLOG.md`、`docs/HANDOVER-CURRENT.md`。
- **验证**：纯文档，无构建。
- **待用户拍板**：选 A 还是 B（多人、单人各选一个）；「提醒音一直响、间隔 5 秒」的确切含义；没开隐私时提醒词怎么说；隐私模式默认选项；自定义提醒词里是否要 `{分钟}`。
- **下一节**：用户选定后，节 1 只读探针（不变）。

---

## 任务二 · 节 0：调研 + 设计稿（2026-11-11，未动代码）

**用户要求**：隐私模式（不读内容，只说「谁给你发了一条消息」，可自定义）与提醒模式（没回复就按 1、3、5、7、10、15、20 分钟、之后每 5 分钟到 1 小时提醒；自己回复或在界面上「标为已读」就停；隐私模式下说条数），两个开关放首页。用户强调：按钮位置与设置的融入方式「美观最重要」，要设想真实使用场景，画风与现有的苹果式设计统一，可以为此适当调整布局；可借鉴 GitHub 上的类似项目。

- **做了什么**：
  - 两份代码调查：首页布局与设计系统（尺寸、配色、可复用控件），消息管线（worker 只取对方消息、游标、队列、朗读记录、设置持久化）。
  - 两份网上调研：开源项目（Telegram 重复通知、MissedNotificationsReminder、VoiceNotify、BuzzKill、Signal 讨论、qiweidoc 企业微信超时提醒）与系统产品（iOS 重复提醒 / 显示预览 / 播报通知、Gmail 提醒回复、Apple 邮件、Slack、苹果设计规范）。GitHub 上没有成熟的「微信未回复提醒」项目可直接复用。
  - 写成 `docs/privacy-reminder.md`：用户要求、代码现状、调研与采纳的想法、使用场景、设计（首页模式磁贴、设置面板、联系人卡角标与「标为已读」、播报规则、数据要点）、8 节实施计划、待核实点。
  - 两张 1:1 效果图（SVG，会话产物）：首页整体（状态卡 + 两块磁贴与联系人卡上下对齐）、两个设置面板。
- **关键设计决定**：状态卡与两块「控制中心式」磁贴同排同高，不增加页面高度（默认窗口两位联系人仍不滚动）；点磁贴开关、点右上角 ⌄ 打开设置面板（改了即生效）；提醒按联系人记待回复、同时到期合并成一句、播报不含内容；「好的 / 收到」这类消息不触发提醒（默认开）；结束监听即清零。
- **改动文件**：`docs/privacy-reminder.md`（新增）、`docs/WORKLOG.md`、`docs/HANDOVER-CURRENT.md`。
- **验证**：纯文档，无构建。
- **待用户拍板**：磁贴方案与外观；时间表是否加「20 分钟」档；「无需回复」默认开；不开隐私模式时提醒也不重读内容；群聊不提醒（本来也不朗读）。
- **下一节**：节 1 只读探针，确认自己发出的消息（含手机端）在电脑端 `Msg_` 表里的发送者、类型与 `sort_seq`。

---

## 1.6.0 · 2026-11-11（多厂商语音接入，已发布上线）

**用户授权**：「联系 muse 推新版本吧」。

### 版本与内容
- `Directory.Build.props` → **1.6.0**；`ReleaseHistory` 新增 1.6.0 条目（8 条：三组入口、新增厂商、按厂商/地址区分服务、多模型与固定模型、常用音色、过时选项更新、需海外网络标签、对话框与教程打磨）。顺手更正 1.5.3 条目的一处错字：变成字幕条的是「操作提示」，不是「朗读记录」。
- 源码 commit `f279d5e`（先提交再打包，产物内嵌 `1.6.0+f279d5e…`）。

### 门禁与产物
- `Test.ps1` **.NET 254/254 PASS** + Python 54 PASS（41 skip 基线）；`Verify-Source` PASS；wx-multi **58/58 PASS**。
- `Publish.ps1` → `artifacts\win-x64-v1.6.0\` + ZIP **92,137,109 字节 / `f58cdf57605d692e15619b87584c0da03ed92ed4b6ea1cd7e553e2d0c44a6d7d`**；`worker dependencies ok`、`speech dependencies ok`（edge-tts 7.3.1）、Clean-payload PASS（1993 文件，比 1.5.3 多 260 个，是 `worker\tts-deps`）。
- `Build-Installer.ps1 -PayloadDirectory` → Setup **67,090,008 字节 / `d96a0cff6b4d74202704723dbdf91555bd289ae13b8d7a3a6c5cf66eaf5f1378`**。
- 产物核验：EXE `ProductVersion=1.6.0+f279d5eae23de32e385234aab9b1021a1efa6e53`、`FileVersion=1.6.0.0`，Setup `ProductVersion=1.6.0`；ZIP 1996 项，含 `worker/edge_speak.py`、`worker/tts-deps/edge_tts`、`worker/audited_wechat_keys.py`、`runtime/python/python.exe`，无 settings.json / .dpapi / .key / 数据库 / WAL / SHM；1.6.0 > 线上 1.5.3。
- 坑：OneDrive 目录里刚写完的 ZIP / Setup 偶尔要等几秒才能被下一条命令看见（第一次 `Build-Installer` 报找不到发布目录，稍后重跑正常），不是打包失败。

### 上线结果（Muse 发布单 `codex_20261111_111550_686f2b`）
- 发布前四项核对：WORKLOG / HANDOVER 已记（commit `40d01ef`）、工作区干净、远端 HEAD = 本地 `40d01ef`；本地 ZIP 再算一次哈希仍是 `f58cdf57…6a6d7d`；Muse 收件箱里此前没有 1.6.0 发布单，只发了这一次。
- Muse：先 `status=info`（流水线接手），约 3 分钟后 `status=ok`（`publishedAt=2026-11-11T10:19:18Z`），称**哈希链三方一致**（申报＝PC 实测＝A1 落地）、Setup 一并发布、旧清单备份 `latest.json.bak-1.5.3`。
- **独立公网复核**（2026-11-11 10:30 UTC）：`GET latest.json` **200**（1029 字节），六字段齐全：`version=1.6.0`、`publishedAt=2026-11-11T10:19:18Z`、`size=92137109`、`sha256=f58cdf57…6a6d7d`、url 指向 1.6.0 ZIP，notes 321 字中文正常（UTF-8 无乱码）。
- ZIP / Setup `HEAD` 均 **200**，`Content-Length` = 92,137,109 / 67,090,008，与本地一致。
- **完整下载 ZIP 重算 SHA-256 = `f58cdf57…6a6d7d`，与清单一致**（字节数一致）；临时文件已删。
- 桌面快捷方式仍未动（指向 1.4.7 r3，供用户本人点一次更新实测）。
- 坑：本机 PowerShell 工具对长时间等待（`Start-Sleep` 超过约 45 秒、`muse-call` 等回执）偶尔提前返回 exit -1 且无输出，但进程仍在后台跑完。不要因此重发发布单；用 `Tee-Object` 写日志、看 `muse-bridge\sent|claimed|archive|outbox` 里同一 req_id 的文件判断进度。

---

## 任务一 · 打磨续：Fish 标海外网络、底部「露头」、空记录时清空按钮显示禁止光标（2026-11-11）

**用户反馈**：① Fish Audio 也需要外网；② 默认状态下底部应该是下一项「露个头」（约 1/3 高度），而不是上一项尾巴被藏一点；③ 首页朗读记录的垃圾桶在没有记录时，鼠标指针要变成和监听中点「试听」一样的红色禁止样式。

### 改动
- **Fish 标「需海外网络」**：`ProviderCatalog` 的 fish 预设 `Overseas = true`（按用户的实际使用经验）；教程 05 节 Fish 标题加「（需海外网络）」；测试改为四家（elevenlabs、fish、gemini、openai）。本机 fish-speech 不标。
- **底部露头**（`ProfileEditors.cs` 的 `PeekNextRow`）：页面比对话框高时，不再在任意位置截断，而是把可视区的下沿放在某一「行」的 48% 处（行 = 列表项、输入框、下拉框、模型卡片等高度 ≥ 28 DIP 的元素；标签和单行提示不算），选可视区上限内最靠下的那一行，所以下一项只露出头部（列表项的名称、卡片的上半）。为此对话框最多缩短不到一行，且不会缩到上限的 60% 以下。下沿渐隐改为只覆盖露出部分的下 60%（三段 0 → 0x48 → 0xE6），露出的名称仍能看清。换页时先恢复上限再重新计算；展开「高级」等后续变化不再重算，避免动画中途跳动。
- **空记录时的禁止光标**（`MainWindow.Interaction.cs`）：按钮禁用后自己不会再请求光标，所以由窗口级 `QueryCursor`（原本给监听中受限控件用的那套）多判断一次：指针在「清空朗读记录」按钮上且它是禁用状态时返回 `Cursors.No`。`MainWindow.xaml` 的 `IconDangerButton` 去掉了禁用时设箭头光标的无效 Setter，并在注释里指明由谁负责。
- **工具**：`UiSmokeTest cursor <pid> <按钮名> [页]`——把指针停到按钮上，用 Win32 `GetCursorInfo` 与系统 `IDC_NO` 光标比对，再移开确认恢复。

### 验证
- 光标：`[清空朗读记录] enabled=False cursor=NO (not allowed)`，移开后 `cursor=other`。
- 截图：`peek3-0.png`（列表底部露出「MiniMax（国内）」名称、渐隐只在下半）、`peek3-1.png`（MiniMax 详情页露出第 6 张模型卡的上半）。上一版 `peek2-0.png` 露出 38% 时名称被渐隐洗得太淡，因此改为 48% + 更轻的渐隐。
- `Test.ps1` .NET 254/254 + Python 54 PASS；`Verify-Source` PASS。
- 期间一次增量编译出现 34 个「文件被占用、重试复制」警告（刚关掉的进程还没完全释放），随后 `--no-incremental` 重编译 0 警告 0 错误，截图以重编译后的为准。

---

## 任务一 · 用户检查后的打磨（2026-11-11）：对话框去滚动条、分组改名、「高级」动画、海外网络标记

**用户要求**：① 删掉对话框的滚动条，但不用任何文字或图标也能看出下面还有内容；② 分组顺序改为「通用协议 → 主流厂商 → 自建模型」，标题只留这几个字；③ 「高级」展开的交互动画不好，要优化；④ 在中国大陆需要海外网络才能注册或调用的厂商，要有形象的标记。

### 改动
- **去滚动条 + 边缘渐隐**（`ProfileEditors.cs` 基类）：滚动区改为 `ScrollBarVisibility.Hidden`（滚轮、触控板、键盘照常可滚）；外面叠两层与卡片同色（`#FFFEFB`）的渐变——下边 64 DIP、四段（0 → 36% → 78% → 不透明），只要下面还有内容就显示；上边 28 DIP，离开顶部才显示；显隐用 140–180 ms 淡入淡出。被切到的那一行逐渐「融进」卡片，这就是「还有下文」的提示，页面完整可见时两层都不出现。顺带消除了滚动条出现时内容变窄的问题。
- **分组**：`PresetChoice.Group` 改为「通用协议 / 主流厂商 / 自建模型」，列表按 `GroupOrder` 稳定排序（组内保持目录顺序）。默认选中项随之变为第一项「OpenAI 兼容接口」。
- **「高级」展开**：新增 `src/VoiceBridge.App/Disclosure.cs` 取代 WPF 默认 Expander（一帧弹开、圆圈箭头）：
  - 标题行 =「高级」+ 会转的 V 形箭头 + 延伸到右边的细线；悬停变深色；键盘 Tab 聚焦时才显示一圈淡绿描边（`FocusVisualStyle`，鼠标点击不留绿色）。
  - 展开：内容区高度 0 → 实测高度，300 ms ease-out-quint，裁切显示；字段晚 70 ms 淡入并下移 8 px 到位；箭头 260 ms 旋转 180°。收起：200 ms ease-in-out 反向，字段先淡出。动画结束恢复自动高度。系统关闭动画时直接开合。
  - 展开后若新字段超出可视区，用 `SmoothScroll.To`（新增，与滚轮同一缓动）滑到刚好露出，不会把标题滑出视野。
  - **对话框不再每帧重新居中**：`DialogShell.Hold` 在动画期间锁住上沿、只往下长；碰到屏幕底边时在同一帧内上推（`KeepOnScreen`，原来排在 Loaded 优先级的居中要等动画结束才执行，表现为最后「跳一下」）；释放延后到 Background 优先级，避免收起结束时被重新居中。
- **海外网络标记**：`ProviderPreset.Overseas`，目前标给 OpenAI、Google Gemini（两家官方都不支持中国大陆）和 ElevenLabs（官方没有限制中国大陆，但大陆直连普遍不稳定，按常见情况标注）。列表里名字右侧是淡琥珀色小胶囊「地球图标 + 需海外网络」；详情页标题旁同样的胶囊，下面一行琥珀色说明「在中国大陆，注册账号、获取 Key 和合成语音都需要海外网络环境；网络断开时这一家的音色会读不出来」。国内厂商与自建模型一律不标（测试锁定）。
- **教程**：03 节按新顺序介绍三组并解释「需海外网络」标记；05 节三家标题后加「（需海外网络）」；07 节改名「自建模型」，并补一句安装时 GitHub / Hugging Face 可能需要海外网络、可用 ModelScope 镜像。
- **工具**：`UiSmokeTest dialog` 在 `WVB_SAMPLE_DISCLOSURE=1` 时会切换「高级设置」开合，每 15 ms 记录对话框上沿与高度，并截开 / 合两张图。

### 验证
- 逐帧采样（物理像素，DPI 2.0）：Edge 页展开 797 → 1067、收起 1067 → 797，**上沿始终 554 不动**，曲线先快后缓；硅基流动页展开到屏幕底时上沿 208 → 200 → 164 → 143 逐帧上推、高度封顶 1729 后转为滚动，**结束时无跳动**；收起时上沿保持 143。改前同样的采样在结束时有 42 px 的一次性跳动。
- 截图：`polish-0.png`（三组新顺序、海外胶囊、底部渐隐）、`polish-2-open.png`（Edge 页展开后的「高级」标题行）、`polish-b-1-open.png`（展开后自动滑到新字段，顶部渐隐出现）。
- `Test.ps1` **.NET 254/254**（+1：海外标记只给这三家）+ Python 54 PASS；`Verify-Source` PASS。
- 检查期间用户正开着开发版（PID 51640），为了重新编译，先用正常关闭（等同点 ×，会保存设置并清理）把它关掉，改完后重新打开了新版。

---

## 任务一 · 多厂商语音 API 融合（2026-11-11 起，工作区，未升版本号）

**用户要求**（原话整理见 `docs/tts-integration.md` §1）：支持通用协议、主流厂商专属入口、本地模型；保留现有「多厂商同时用、每厂商只启用一个 Key、音色挂厂商、模型可热切换」的机制；修正过时选项；教程页写清接入方式；优先复用 GitHub 上成熟、高星、活跃的轮子。任务二（隐私模式 / 提醒模式）等任务一稳定后再做。

### 节 1：存档 + 调研 + 设计文档

- **存档**：打了两个附注标签并推送：`v1.5.3` → `67a74f0`（1.5.3 发布源码），`pre-multi-provider` → `d608aca`（动工前的完整状态）。远端 `git ls-remote --tags` 已核对。
- **调研**：三路并行——成熟项目的服务/模型/音色组织方式（Dify、LobeHub、Open WebUI、LibreChat、SillyTavern、Open-LLM-VTuber、AstrBot、LiteLLM）；可复用轮子（LiteLLM、edge-tts、RealtimeTTS、tetos、官方 SDK、MEAI）；国内外厂商与本地引擎的官方接口逐字段核对。另亲自核对了 MiniMax 国内地址（默认 `api.minimax.cn`，华北就近 `api-bj.minimaxi.com`）、百炼 CosyVoice HTTP 与千问 TTS 的接口和返回、Gemini 3.8 的请求体、fish-speech 本机服务的请求解析、edge-tts 的许可证（LGPLv3，7.3.1 于 2026-10-13 发布）。
- **结论**：没有一个能直接装进来的万能库（LiteLLM 太重且国内只有 MiniMax）；真正值得复用的是 OpenAI 兼容协议（等于复用 LiteLLM proxy / LocalAI / Kokoro-FastAPI 等成熟服务端）、edge-tts 库、Dify 的厂商描述结构。现状最大的问题是厂商身份用协议类型 `Kind` 判断，导致所有 OpenAI 兼容服务被当成一家。
- **设计**：新文档 `docs/tts-integration.md`（调研结论、四层关系、厂商目录字段、协议适配器清单、安全约束、8 节实施计划）。
- **改动文件**：`docs/tts-integration.md`（新）、本文件、`docs/HANDOVER-CURRENT.md`。
- **验证**：纯文档，无构建。
- **下一节**：节 2 数据模型（`Vendor` / `Preset` / 音色固定模型 + 迁移 + 按服务身份的启用规则 + 测试）。

### 节 2：厂商身份从「协议」改成「服务」+ 音色可固定模型

- **为什么**：原来 `ProviderActivation`、`VoiceLibraryMigration`、删除/添加服务都按协议类型 `Kind` 分组，所有 OpenAI 兼容服务（硅基流动、本机 Kokoro……）会被当成同一家：只能启用其中一个 Key，音色还会串用。这是推广到多厂商的根本障碍。
- **新增 `src/VoiceBridge.Core/ServiceIdentity.cs`**：
  - `ServiceIdentity.Of/Same/Derive/Legacy/NormalizeEndpoint/CacheVendor`。已存的 `Vendor` 优先；老配置按协议推导：Fish 官方→`fish`、Azure→`azure`、豆包→`volcengine`、OpenAI 官方或空地址→`openai`，其他 OpenAI 兼容 / GPT-SoVITS / 非官方 Fish 按「协议@规范化地址」各成一个服务（大小写、末尾 `/`、`/audio/speech`、`/v1` 不区分）。
  - `SpeechModels.Current/Use/Listed`：统一「当前模型」的读写——豆包的模型是资源 ID，其他协议是 `Model`；老 Fish 配置没有列表时仍给出原来的两个模型。
- **数据字段**（`SpeechProfiles.cs`）：`SpeechProviderProfile` 加 `Preset`、`Vendor`、`Template`（自定义 HTTP 模板，节 4 用）；`VoiceProfile` 加 `Vendor`、`Model`（固定模型）；`SpeechProviderKind` 末尾追加 `ElevenLabs, MiniMax, DashScope, Gemini, EdgeTts, CustomHttp`（只追加不重排，保证老配置数字不变；添加服务界面暂时只列原有 5 种，新协议在节 4/5 接上）。新增 `CustomHttpTemplate` 记录。
- **规则改按服务身份**：`ProviderActivation`（加 Key 不应用、≥2 个 Key 才需选择、启用只切同一服务）、`VoiceLibraryMigration`（`VendorOf` 改返回字符串；`ActiveKeyFor`/`BindOrphanVoices`/`UseActiveKey` 按身份比较并把身份写回音色；`CacheVendor` 同时补 `ProviderKind` 与 `Vendor`）、`MainWindow.Library.cs`（`EditProvider` 给新 Key 赋身份、孤儿音色只绑同一服务；`DeleteProviderById` 先把身份缓存到音色再找同服务的替补 Key）。
- **模型**：`ContactProfile.ApplySpeechPreferences` 对所有协议生效，但只接受服务列出的模型（旧的、被移除的选择不会把请求切到未勾选的模型）；`SpeechFor` 最后套用音色固定的模型（优先级：音色固定 > 联系人选择 > 服务默认）；`ContactCard.RefreshSpeechOptions` 新增 `pinnedModel`，音色固定模型时卡片只显示该模型、不给切换；非 Fish 协议的模型列表统一走 `SpeechModels.Listed`（豆包首页会显示资源 ID）。
- **分组显示**：服务列表与音色库都按「服务」分组（新 `ServiceLabel`：内置四家用厂商名，其他服务带上地址，例如「OpenAI 兼容接口 · api.siliconflow.cn」）。音色组名原来是 Key 的名字，现在与服务列表一致。
- **迁移**：`LocalSettings.Load()` 在摘出厂条目后调用 `ServiceIdentity.CacheVendor`，有变化就保存一次。本机实际配置（1 个 Fish Key + 4 个 Fish 音色）会得到 `fish`。老版本读到新字段会忽略，不影响回退。
- **测试**：`ProviderActivationTests` 更新 4 处断言为字符串身份，新增 7 例（两个 OpenAI 兼容服务是两家；同地址两个 Key 是一家；已存身份优先于地址；老配置推导与幂等；首页模型只接受列出的；豆包用资源 ID 切模型；孤儿音色按协议推出身份）。
- **验证**：`Test.ps1` **.NET 216/216 PASS**（209 + 7）+ Python 54 PASS（41 skip 基线）；`Verify-Source` PASS。界面未改动外观，未截图。
- **下一节**：节 3 厂商目录 `ProviderCatalog`。

### 节 3：厂商目录 ProviderCatalog

- **新增 `src/VoiceBridge.Core/ProviderCatalog.cs`**：参照 Dify 模型插件的厂商描述，每个预设声明分组、协议、地址（首项默认，可带地域选项）、是否允许自填地址、Key 是否必填与叫法、获取 Key / 文档 / 音色列表链接、模型列表（含免费/付费/停用说明、默认勾选）、是否允许自填模型、音色形态（音色 ID / 参考音频）、音色示例（可限定适用模型）、注意事项、音频格式。
- **19 个预设**：
  - 主流厂商 13 个：Fish Audio、OpenAI、ElevenLabs、Azure、Google Gemini、火山豆包、阿里云百炼（CosyVoice + 千问 TTS）、MiniMax 国内、MiniMax 国际、硅基流动、阶跃星辰、智谱 GLM-TTS、微软 Edge 朗读（免费）。
  - 通用协议 2 个：OpenAI 兼容接口、自定义 HTTP 接口。
  - 本地模型 4 个：GPT-SoVITS、fish-speech / OpenAudio、Kokoro-FastAPI、LocalAI。
- **据官方文档修正的过时项**（详见 `docs/tts-integration.md` §3.4）：Fish 增加 `s2.1-pro`、`s2-pro`，去掉 2026-12-31 退役的 `s1`；OpenAI 标注 2027-01-06 停用、音色按模型区分（marin/cedar/ballad/verse 只配 gpt-4o-mini-tts）；Gemini 只给 3.8 系列（2.5 预览版 2026-11-17 停用）；豆包的资源 ID 作为可切换的「模型」；MiniMax 国内默认 `api.minimax.cn`、华北就近 `api-bj.minimaxi.com`；阶跃星辰模型 `step-tts-mini / step-tts-2 / stepaudio-2.5-tts / stepaudio-3-tts`；智谱音色 `tongtong` 等、只返回 WAV。
- **身份规则**：`ProviderPreset.VendorFor`——主流厂商 = 预设 Id（MiniMax 国内两个地址算同一家），通用/本地 = 按地址推导。`ProviderCatalog.For(profile)` 给老配置找回所属预设（Fish 官方→fish、非官方 Fish→fish-speech、OpenAI 官方→openai、其他兼容→通用入口……）。
- **测试**：新增 `ProviderCatalogTests` 11 例（Id 唯一、三组都有、地址只允许 HTTPS 或本机、无固定地址的允许自填、模型与音色引用一致、需要 Key 的主流厂商都给出获取链接、Fish 模型清单、OpenAI 音色按模型、硅基流动音色带模型前缀、身份分组、老配置找回预设）。
- **验证**：`Test.ps1` **.NET 227/227 PASS** + Python 54 PASS。
- **说明**：目录里少数链接和示例（火山控制台入口、豆包示例音色、硅基流动 Key 页面、阶跃星辰/智谱 Key 页面）来自常见入口，未逐一打开核实；示例音色都标了「示例」或以官方页面为准。
- **下一节**：节 4 协议适配器。

### 节 4：新协议适配器 + Fish 扩模型 / 放开本机 fish-speech

- **`src/VoiceBridge.FishAudio/SpeechProviderFactory.cs`**（重写，公共接口不变）：
  - **ElevenLabs**：`POST {base}/v1/text-to-speech/{voice_id}?output_format=mp3_44100_128`，`xi-api-key` 头，`model_id`；语速≠1 时发 `voice_settings.speed`（夹在 0.7–1.2）。
  - **MiniMax**：`model / text / stream=false / voice_setting / audio_setting(mp3, 32k) / output_format=hex`；HTTP 200 里看 `base_resp.status_code`，1004/2049（密钥）、1008/2053/2056（余额）、1002/1039（限流）、2054/2042/2043/2069（音色/模型）分别给出中文说明，不回显服务端原文。
  - **阿里云百炼**：模型以 `cosyvoice` 开头走 `/api/v1/services/audio/tts/SpeechSynthesizer`（mp3、24 kHz、`rate`、可选 `instruction`），否则走千问 `/api/v1/services/aigc/multimodal-generation/generation`；返回的 `output.audio.url` 再下载：**只允许 `*.aliyuncs.com`、http 自动升 https、下载时不带任何认证头**；错误码 InvalidApiKey/Arrearage/Throttling 有中文说明。
  - **Gemini**：`/v1beta/models/{model}:generateContent`，`x-goog-api-key`，`responseModalities=["AUDIO"]` + `prebuiltVoiceConfig.voiceName`，音色描述放 `speech_metadata.style`；返回 base64，RIFF 直接用，裸 PCM 按 mimeType 里的采样率（缺省 24 kHz）包成 WAV。
  - **自定义 HTTP**（`CustomTemplates`）：请求头逐行 `名称: 值`，`{{key}}` 只允许出现在请求头（写在请求体或地址里直接拒绝）；Key 留空时含 `{{key}}` 的头整行不发；请求体占位符 `{{text}} {{voice}} {{model}} {{speed}}` 按 JSON 字符串转义（保留中文），JSON 类型的请求体填好后会再解析一次；返回可选「直接音频」或「JSON 字段（点路径，支持 `[0]`）+ base64/hex/url」；禁止自定义 Host/Content-Length 等头。
  - **Edge 朗读**：工厂新增可选的 `edge` 委托（`EdgeSpeechRequest`），节 5 由宿主接上 edge-tts；未接上时提示「缺少 Edge 朗读组件」。
  - **Fish**：模型校验改用 `ProviderCatalog.FishModelIds`（s2.1-pro-free / s2.1-pro / s2-pro / drama-3），勾选列表外的模型照旧拒绝；**本机 loopback 地址允许（fish-speech）**，此时 Key 可选、不发 model 头、`FishModels` 返回空；非本机的非官方地址仍拒绝（Fish 密钥只发官方）。重试只针对免费模型，任何付费 Fish 模型都不自动重试。
  - 通用：`ResolveEndpoint` 对 ElevenLabs/Gemini/百炼只保留来源（路径由模型/音色决定），地址里出现 `{{` 一律拒绝；`RequiresCredential` 对 GPT-SoVITS/Edge/自定义 HTTP 为可选；协议显示名改为「Azure 语音服务」「火山引擎 · 豆包语音」「GPT-SoVITS」。
- **`AudioSignature.cs`**：新增 `WaveFromPcm16`（裸 PCM 包 WAV）与 `RepairStreamedWave`（只修大小字段是 0 / 0xFFFFFFFF 的流式占位头；声明长度大于实际的截断文件仍拒绝）。
- **测试**：新增 `VendorAdapterTests` 23 例（ElevenLabs 路径与头、MiniMax 十六进制解码与 3 种错误、百炼 CosyVoice 下载不带 Key、千问 WAV、百炼外域链接拒绝下载、Gemini PCM 包 WAV 与无音频说明、本机 fish-speech 无 Key 无 model 头、Fish 非官方远程地址拒绝、Fish 新模型与勾选约束、付费 Fish 不重试、自定义模板转义与 Key 只在头里、空 Key 不发认证头、5 种不安全/不完整模板被拒、流式 WAV 修复、目录里每个协议工厂都认识）。原有 `ProviderTests` 全部照过。
- **验证**：`Test.ps1` **.NET 250/250 PASS** + Python 54 PASS。
- **下一节**：节 5 Edge 朗读（随包附带 edge-tts，宿主子进程调用，实测一次）。

### 节 5：微软 Edge 免费朗读（复用 edge-tts）

- **为什么复用**：Edge 朗读服务要算一个会变的校验令牌（2026 年 10 月就有一波 403），这正是用户说的「别造轮子」——交给 edge-tts（约 1.25 万星，LGPLv3，2026-10-13 发 7.3.1）的维护者跟进，我们只升版本。
- **依赖解析**：用本机 pip 按 `--platform win_amd64 --python-version 3.12` 解析出 14 个轮子（edge-tts 7.3.1、aiohttp 3.14.5 及其依赖，合计约 1.2 MB 压缩、解压约 3.7 MB），逐个从 PyPI JSON 取下载地址与 SHA-256 并与本地文件核对一致。
- **实测发现**：单次启动进程每条要 5 秒，其中 **4.2 秒是导入**（`-X importtime`：aiohttp.connector 与 edge_tts 在导入时各自创建 TLS 上下文，Windows 上加载系统证书很慢），与字节码无关（预编译后不变）。所以改成**常驻子进程**。
- **`worker/edge_speak.py`**（新）：常驻、逐行 JSON 协议；启动先回 `{"ready": true}`（缺 edge-tts 回 `dependency`）；请求 `{text, voice, speed}`，回 `{"ok": true, "audio": base64}` 或 `{"ok": false, "code": request|no-audio|too-large|network}`；音色名、长度、语速在 Python 侧再校验一次；stdout 只走协议，stderr 只写短代码，不写消息正文；`-I` 隔离，只把同目录的 `tts-deps` 加入路径。
- **`src/VoiceBridge.WeChat/EdgeSpeechHost.cs`**（新）：一个进程服务整个会话，`SemaphoreSlim` 保证一次一条；Job Object 绑定随应用结束；请求被取消或超时就杀掉进程（防止下一条读到上一条的回复），下次自动重启；启动等待 `ready` 最多 30 秒；`Warm()` 后台预热。错误码映射成中文提示。
- **接线**：`MainWindow.Library.cs` 的 `speechFactory` 改为 `new(edge: …EdgeHost.SpeakAsync…)`；启动时和保存服务后，如有 Edge 服务就 `WarmEdgeIfUsed()`；窗口关闭时释放。
- **打包**：`scripts/Publish.ps1` 新增 14 个锁定 URL + SHA-256 的轮子，解到 `worker\tts-deps`（与微信 worker 的 `worker\deps` 分开），并做导入与版本冒烟检查（`edge_tts.__version__ == 7.3.1`）。edge-tts 以未修改的独立包形式随附，`edge_tts-7.3.1.dist-info/licenses` 里带 LGPL 许可证文本。轮子已预置到 `artifacts\build-tools`。
- **测试**：新增 `EdgeSpeechHostTests` 4 例——假助手的一问一答与失败后仍同步、取消后重启不读旧回复、缺组件时 `Available=false`（这三例走 `WVB_PYTHON`，不联网）；以及 **`Category=Network` 的真实联网测试**（随包脚本 + 真 edge-tts 合成两句中文，第二句复用同一进程）。`Test.ps1` 新增 `-Network` 开关，默认不跑联网测试。
- **验证**：
  - `Test.ps1` **.NET 253/253 PASS** + Python 54 PASS。
  - 真实联网：`dotnet test --filter Category=Network`（`WVB_TTS_DEPS` 指向解压目录）**通过，6 秒**（含约 4 秒启动）；此前手工直跑脚本得到 15,408 字节 MP3（`FF F3` 帧头，MPEG-2 Layer III），`zh-CN-XiaoxiaoNeural`。
- **开发版前提（新增）**：`bin\Release\net8.0-windows\worker\tts-deps` 已放好依赖；清理过 bin 后需重新放（可从 `artifacts\build-tools` 的轮子解压）。
- **下一节**：节 6 界面（添加服务分组目录、按预设生成字段、模型多选、音色示例与固定模型）。

### 节 6：添加服务 / 添加音色界面接上厂商目录

- **`src/VoiceBridge.App/ProfileEditors.cs`**（重写两个对话框，基类 `ProfileEditor` 保留并加了 `Hint` 返回值、`Link`、`MultiLine`、`MarkupTemplate`、`Advance`）：
  - **添加服务第一页**：19 个预设按「主流厂商 / 通用协议 · 小众厂商或自建网关 / 本地模型 · 在自己电脑上运行」分组列出，每项一行名称 + 一行说明；双击直接进入下一步。编辑已有服务跳过这一页（预设不可改，避免协议变化把音色弄乱）。
  - **第二页字段全部由预设决定**：通用 / 本地必须起服务名（主流厂商的显示名放「高级」，用来区分同厂商多个 Key）；多个地址时给下拉（地域 / 国内站国际站 / 就近接入），允许自填的再给地址框；Key 按「必填 / 可选 / 不需要」显示，附「去哪里获取」链接；模型是可多选的卡片（带说明，免费/付费有小标签，没有标签就不显示空白气泡），允许自填的再给「其他模型」；自定义 HTTP 有请求头、请求体模板、返回方式，JSON 字段与编码只在选「返回 JSON」时出现；预设的注意事项逐条显示；文档链接按来源写「官方接入文档」或「项目主页」。
  - **保存**：写入 `Preset`、`Vendor`（编辑保持原身份，新建用 `preset.VendorFor`）、默认模型（豆包写进资源 ID，原来的「模型参数」改叫「复刻 2.0 模型参数」放高级）、`AvailableModels`、音频格式（主流预设固定，通用 OpenAI 兼容可在高级里选）、自定义模板（保存前校验）。Key 名沿用原来的「不铸新名」规则。
  - **添加音色**：字段按预设——参考音频形态（GPT-SoVITS）或音色 ID；有常用音色的厂商多一个「常用音色」下拉，选中自动填入 ID 和名称，**只适用于一个模型的音色（如硅基流动 `模型:音色`）会自动固定该模型**；服务有模型列表时出现「固定模型（可选）」；已知音色与所选模型不匹配时给出橙色提示（只提醒，不拦）；高级里按协议给语气指令 / 语气描述 / CosyVoice 指令、语速。保存时用「套上固定模型后的服务」校验。
  - 两页切换时滚动位置回到顶部（之前从长页面返回再进另一页会停在中间）。
- **其他接线**：`ContactCard` 首页模型名改用目录里的显示名；`MainWindow.Library.cs` 的分组标题改用目录名（主流厂商用厂商名，通用/本地加地址），音色库里固定了模型的音色在 ID 后显示「· 固定 xxx」；`EditProvider` 不再覆盖编辑器给的服务身份。`App.xaml` 的 `ModelSelectionCard` 在 Tag 为空时隐藏标签气泡。
- **工具**：`tools/UiSmokeTest` 新增 `dialog` 模式——打开对话框、按名字选列表项、点下一步、用 `PrintWindow` 截图（不怕被遮挡），最后点取消（不保存任何东西）；用 Win32 `EnumWindows` 找对话框窗口（UIA 顶层枚举偶尔漏掉刚弹出的窗口，旧的 `menuclick` 也因此偶发失败）。
- **截图**（`artifacts/screenshots/`，不入库）：`provider-0.png`（分组列表）、`provider-1.png`（硅基流动）、`provider-2.png`（MiniMax 国内）、`provider-b-1.png`（自定义 HTTP）、`provider-4.png`（Edge）、`provider-5.png`（Kokoro）、`provider-b-2.png`（GPT-SoVITS）、`voice-1.png`（Fish 音色 + 固定模型）。
- **验证**：build 0/0；`Test.ps1` **.NET 253/253 PASS** + Python 54 PASS；`Verify-Source` PASS；真实窗口截图逐页目视检查。截图时没有保存任何服务，用户配置只多了迁移写入的 `Vendor` 字段。
- **没做**：没有用真实 Key 走「添加 → 试听」完整链路（除 Fish 外没有 Key）；硅基流动等厂商的音色下拉只在用户真的添加了该服务后才能看到，本节没有为截图去改用户配置。
- **下一节**：节 7 教程页与 `docs/providers.md`。

### 节 7：使用说明里的逐家接入教程 + 开发者文档

- **`src/VoiceBridge.App/UserGuide.xaml.cs`**：
  - 重写 03「配置语音服务和 API Key」（三组入口、每家只启用一个 Key、「同一家」怎么认、模型多选与首页切换、地址规则、换 Key 与报错排查）与 04「音效与音色配置」（常用音色、音色挂厂商、**固定模型**的几类典型场景）。
  - 新增 05「主流厂商逐家说明」（13 家各一段：去哪拿 Key、模型、音色格式、费用与停用提醒；外加「暂时没有专属入口的」腾讯云/讯飞/百度/Google Cloud/Polly/MiMo 的替代办法）、06「通用协议」（OpenAI 兼容地址补全规则、自定义 HTTP 模板逐项填法 + 一个完整例子 + 安全约定）、07「本地模型」（GPT-SoVITS、fish-speech、Kokoro、LocalAI，以及 CosyVoice/IndexTTS/F5-TTS 走兼容封装）。后续各节顺延为 08–16。
  - 13「免费、付费和本地服务」补上免费选项清单（Fish 免费版、Edge、Azure F0、Gemini 免费层、本地模型）。
  - 顺手改掉过时说法：左栏早已改名「设置」，教程里的「高级 → 语音服务 / 软件更新」统一改为「设置 → …」（对话框里的「高级」折叠区保留原名）；02 节「4.2 大版本不等于都支持」改为当前支持范围 4.1.11–4.2.0.13。
- **`UserGuide.xaml`**：顶部「按需查阅」新增「接入语音服务」跳转按钮；快速开始卡片文案改为「在「设置」添加语音服务」。
- **`docs/providers.md`**：重写为开发者说明——四个概念（服务身份 / Key / 模型 / 音色）、11 种协议的请求与返回对照表、共同安全约束、**加一家新厂商的步骤**、复用的外部项目与官方参考链接。
- **验证**：build 0/0；`Test.ps1` **.NET 253/253 PASS** + Python 54 PASS；`Verify-Source` PASS；真实窗口截图 `artifacts/screenshots/guide-top.png`（新跳转按钮与快速开始文案）。
- **下一节**：节 8 收尾——全量回归、交接文档、`tts-integration.md` 结论更新、推送。

### 节 8：收尾回归 + 交接定稿

- **真实链路**：开发版启动 → `UiSmokeTest` 驱动「设置 → 试听这段文字」→ **「试听完成」**（真实 Fish Key、免费模型）。迁移后 `settings.json`：Fish 服务 `Vendor=fish`，4 个音色 `Vendor=fish`，`CredentialId` 与 `FishKeyName` 仍是 `speech-5f1c37e3…`，没有发生凭据指针变化。
- **打包脚本试跑**：`Publish.ps1 -OutputDirectory %TEMP%\wvb-publish-check`（不生成 ZIP）→ `worker dependencies ok`、`speech dependencies ok`、`Clean-payload check: PASS (1993 files …)`；`worker\tts-deps` 3,750,013 字节。验证后删除了临时发布目录和 Edge 探针目录。
- **回归**：`Test.ps1` .NET 253/253 + Python 54 PASS；`Verify-Source` PASS；58 项 wx-multi 等价性未受影响（worker 微信部分未改）。
- **文档**：`PROJECT-BRIEF.md` 修正 SDK 实际路径（`wvb-sdk\sdk-tmp`）、项目结构补上目录/身份/Edge 助手、§7 标明旧任务已完成并指向现行任务；`HANDOVER-CURRENT.md` §00 写成「8 节完成、等用户检查」与下一步；`tts-integration.md` §5 补执行结果与提交号。
- **清理**：删除了第 7 节一条命令误写出的 0 字节文件 `System.Xml.XmlDocument`（PowerShell 会话里变量类型被前一条命令锁成 XML 所致，未提交过）。
- **状态**：任务一完成，**未升版本号、未打包、未上线、未改桌面快捷方式**，等用户检查效果后再决定发版；任务二尚未开始。

---

## 1.5.3 · 2026-11-11（已发布上线）

**用户授权**：「联系 muse 打包推新」。

### 版本与内容
- `Directory.Build.props` → **1.5.3**；`ReleaseHistory` 新增 1.5.3 条目（8 条：平滑滚动、提示字幕、去掉所有悬停提示、右键菜单统一、词库重做、记录清空图标与设置页按钮、清缓存不再清头像、左栏改名与首页文案精简）。
- 源码 commit `67a74f0`（先提交再打包，产物内嵌 `1.5.3+67a74f0`）。

### 门禁与产物
- `Test.ps1`：**.NET 209/209 PASS + Python 54 PASS（41 skip 基线）**；`Verify-Source` PASS。
- `Publish.ps1` → `artifacts\win-x64-v1.5.3\` + ZIP **90,826,648 字节 / `0c11285164f002717cf00fa03c85d721487ce466b57d4ccbfb398b1d2a7b264f`**；Clean-payload PASS（1733 文件）。
- `Build-Installer.ps1 -PayloadDirectory` → Setup **66,213,417 字节 / `1f7dd8bc988ef99dbaa41484670647339bc1fda98e3df6aa041241322a8d4452`**。
- 产物核验：`ProductVersion=1.5.3+67a74f0`、`FileVersion=1.5.3.0`；1.5.3 > 线上 1.5.2。

### 上线结果（Muse 发布单 `codex_20261111_010936_89c978`）
- Muse：先 `status=info`（流水线接手），约 3 分钟后 `status=ok`（`publishedAt=2026-11-11T00:13:22Z`），称**哈希链三方一致**（申报＝PC 实测＝A1 落地）、Setup 一并发布、旧清单备份 `latest.json.bak-1.5.2`。
- **独立公网复核**：`GET latest.json` **200**（952 字节），六字段齐全：`version=1.5.3`、`publishedAt=2026-11-11T00:13:22Z`、`size=90826648`、`sha256=0c112851…a7b264f`、url 指向 1.5.3 ZIP，notes 234 字中文正常（UTF-8 无乱码）。
- ZIP / Setup `HEAD` 均 **200**，`Content-Length` = 90,826,648 / 66,213,417，与本地一致。
- **完整下载 ZIP 重算 SHA-256 = `0c112851…a7b264f`，与清单一致**（字节数一致）；临时文件已删。
- 桌面快捷方式仍未动（指向 1.4.7 r3，供用户本人点一次更新实测）。

---

## 工作区（1.5.2 之后，未升版本号）续 23：清空图标换成用户提供的矢量垃圾桶

**用户要求**：用他下载的垃圾桶矢量图，放进一个带背景色的小方按钮里；背景色与「移除」按钮一致。

### 改动
- 图标改用用户给的 SVG（viewBox 1024）原样转成 WPF 路径：两个 `PathGeometry` 放进 `GeometryGroup`，`Stretch=Uniform` 缩放到 18×18，填充用它原本的红色 `#D81E06`。
- 按钮外壳：30×30、圆角 9，背景 `#FBF1EE` / 边框 `#F1DDD5`（与「移除」按钮同色），悬停 `#F7E2DB` / 边框 `#E6BCAA`、按下再深一档；禁用时 45% 透明 + 箭头光标。
- 只用了 SVG 代码，**没有引用那张 PNG**，所以不需要往仓库里放图片。

### 验证
- 截图 `artifacts/screenshots/records-icon-svg.png`：红色垃圾桶在淡粉圆角小方按钮里，空列表时整体变淡（禁用态）。
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。

---

## 工作区（1.5.2 之后，未升版本号）续 22：清缓存不再清头像 + 记录清空改成方块图标 + 去掉“保存设置”

**用户要求**：① 清空缓存不能删头像/联系人索引，现在清完首页头像直接没了；② 朗读记录右上角“清空”文字改成卡片内的方形矢量图标（无内容时不可点、有内容一点即清）；③ 去掉没必要的“保存设置”，保留“清理缓存”（换成更合适的红色）。

### 改动
- **清缓存**：`CleanClick` 里原来有 `avatars.Clear()` + 把所有联系人 `Avatar = null`，这就是头像消失的原因；两行删除，只清 `SessionCache` 的数据库副本（缓存目录是 `<微信目录>/.voicebridge-cache`、`<盘>\WeChatVoiceBridgeCache`、`<程序目录>/cache`、`%LOCALAPPDATA%\WeChatVoiceBridge\cache`，**头像目录 `avatars\` 与索引 `contact-index\` 不在其中**）。
- **朗读记录清空**：头部文字按钮删除；卡片内右上角加 `IconDangerButton`（30×30 圆角 9 的方形边框 + 垃圾桶矢量路径，悬停淡红底、按下更深）。`Render()` 里 `ClearButton.IsEnabled = !noResults`，空列表时 45% 透明、光标为箭头；点击改为 `Results.Items.Clear()`（原来误绑到 `controller.ClearQueue()`，清的是播放队列）。
- **设置页底部**：删掉 `SaveButton`（设置本来每次都自动保存）与它的 `SaveClick`，只留 `清理缓存`，改用 `RowDangerButton` 淡红药丸（`Padding 16,8`、`MinWidth 112`）。

### 验证
- 清缓存实测：UIA 点击「清理缓存」→ 回首页截图，**两个联系人头像都还在**（`artifacts/screenshots/avatars-after-clean.png`）。
- 图标实测：`清空朗读记录` 按钮 `IsEnabled=False`（空列表），截图 `records-icon-frame.png` 显示圆角方形边框 + 垃圾桶。
- 设置页截图 `settings-clean-button.png`：只有一个淡红「清理缓存」。
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。

---

## 工作区（1.5.2 之后，未升版本号）续 21：下沿渐隐加强与“按需显示”

**用户反馈**：边距修好了，但底部那层半透明/模糊效果变浅了，要再重一点。

### 改动
- 渐隐遮罩从 36 DIP / 2 段改成 **56 DIP / 4 段**（`#22 → #66 → #B8 → 不透明`），过渡更厚实、不再“忽然出现”。
- 但直接加厚会把首页记录卡的底边洗掉（这正是上一轮“看起来贴着底”的原因），所以改成**按需显示**：`BottomFade.Opacity = ContentScroller.ScrollableHeight > 0.5 ? 1 : 0`，在 `ScrollChanged` 里更新。
  - 能滚动的页面（设置、词库等）：**满强度**渐隐，滚到底不会像被切开；
  - 一屏放得下的页面（首页）：不显示渐隐，卡片底边保持清楚。

### 验证
- 首页截图 `artifacts/screenshots/home-fade-smart.png`：卡片底边清楚、下方留 16 DIP 边距（实测约 54 px ≈ 原间距的 41%）。
- 设置页截图 `artifacts/screenshots/advanced-fade-smart.png`：底部内容仍有渐隐过渡。
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 备注：本轮有一次 `Test.ps1` 出现 1 项失败（`WorkerTests`/`MultiWorkerTests` 计时类用例，`TaskCanceledException`），重跑即全绿；判定为并发跑构建时的偶发抖动，非代码问题。

---

## 工作区（1.5.2 之后，未升版本号）续 20：用标定法把记录卡调到“40% 间距且不滚动”

**用户反馈**：上一版只保留了 4%（太近），后来又大到需要滚动。要求是「原间距的 40%」且**不能滚动**。

### 做法：不再靠猜，用脚本标定
- 写了临时标定脚本：改高度 → 构建 → 启动 → `wvb-scrollcheck.ps1`（UIA 判断是否可滚动）+ `wvb-measure.ps1`（读空状态文本框矩形）→ 输出「高度/是否滚动/底部余量」。逐个试 144 / 146 / 147 / 148 / 150 / 152 / 156 / 160 / 164 / 168。
- 结论：在**不滚动**的前提下，卡片最多到 **160 DIP**（164 就开始滚）。
- 为腾出这 4 个位置，顺手收了几处间距：通讯录标题上边距 24→**16**、朗读记录标题上边距 14→**10**、首页底部边距 4→**0**、下沿渐隐 36→**14**。

### 结果
| | 原状态 | 现在 |
| --- | --- | --- |
| 记录卡高度 | 110 DIP | **160 DIP** |
| 卡片底边到窗口底 | 131 px（65 DIP） | **56 px（28 DIP）≈ 43%** |
| 是否需要滚动 | 否 | **否**（UIA 复测无 scrollable 元素） |

40% 对应的 52 px 与 56 px 差 4 px，是「再高一点就会滚动」的硬边界；要更近只能牺牲上方间距或允许滚动。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图 `artifacts/screenshots/home-records-160.png`。

---

## 工作区（1.5.2 之后，未升版本号）续 19：朗读记录卡片加高并压到底边

**用户反馈**：朗读记录又“上去了”，要更高、离底边只留约 20px。

### 改动
- `Results` 96 → **110**，空状态 88 → **102**；首页 StackPanel 底边距 24 → **10**（合起来总高不变，所以仍然一屏放下、不出现滚动）。
- 同时把页面下沿的渐隐遮罩 36 → **18** DIP：那块渐变会把卡片最后 36 DIP 洗成背景色，看起来像“底部空了一大截”，其实布局里只留了 10 DIP。

### 验证
- UIA 滚动检测：仍**不可滚动**（无 scrollable element 输出）。
- 截图 `artifacts/screenshots/home-records-final2.png`：卡片更高、底边贴合。
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。

---

## 工作区（1.5.2 之后，未升版本号）续 18：编辑态内容靠左 + 下拉可选 + 点字段直接进编辑

**用户反馈**：① 编辑态两个输入框和下拉框应**靠左**（完成态才居中）；② 下拉框选不了——点下拉里的条目被当成“点到了行外”，直接退出编辑；③ 编辑/删除两个按钮再靠近一点；④ 即使没点编辑，直接点前两个字段或第三个下拉，也应该让这一行进入编辑。

### 改动
- **编辑态靠左**：两个 `TextBox` 去掉 `TextAlignment=Center`；`ScopeBox` 去掉 `HorizontalContentAlignment=Center`。只读态的 `TextBlock` 保持居中（用户要的就是这个两态差异）。
- **下拉可选**：`ClearReadingSelectionOutside` 增加 `IsInsideDropDown(source)` 判定（向上找 `ComboBoxItem` / `Popup`），命中则不提交——下拉的弹出列表是独立窗口，原来会被判成“行外”。实测：选完条目仍在编辑态。
- **按钮靠近**：动作两列 76→**70**，行与表头同时改。
- **点字段进编辑**：三个只读 `Border` 挂 `MouseLeftButtonDown="ReadingFieldClick"`，按 `Grid.GetColumn` 区分：第 0 列→原文框、第 2 列→读作框、第 3 列→聚焦并展开下拉（`FocusScope`）。光标 `IBeam`（文本两列）/`Hand`（出现方式）。

### 验证（真实鼠标 + UIA）
- 点原文 → 输入框 **2** 个（自动进编辑）；点出现方式 → 仍 **2**、下拉弹出；用键盘/鼠标选中「单独出现时」→ 仍 **2**；点行外 → 提交，`settings.json` 里该行 `Scope` 从 `both` 变为 `standalone`（测完已手动改回 `both`）。
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 注：测试脚本一开始点“出现方式”点到了表头文字（两者 UIA 名相同），曾误判为下拉不可选；改为按 ControlType=ComboBox 精确定位后复测通过。

---

## 工作区（1.5.2 之后，未升版本号）续 17：编辑态点走即保存 + 表头/内容居中

**用户要求**：① 点「编辑」后再点别的行或表格外，等同于点「完成」并保存；② 表头与列内容居中对齐（原文↔框1、出现方式↔下拉、操作↔两按钮中点）；③ 输入框与下拉框内容在编辑态和完成态都要居中（现在完成态居中、编辑态靠左，不一致）。

### 改动
- **点走即保存**：`ClearReadingSelectionOutside`（窗口级 `PreviewMouseDown`、`handledEventsToo=true`）扩展为——若存在 `IsEditing` 的行，且点击落在表格外或另一行上，直接 `CommitReading(editing)`；点击本行内部（自己的输入框、出现方式、完成、删除）不触发，交给控件本身。原来“点表格外清空选中色”的逻辑保留。
- **表头居中**：四个列名全部 `HorizontalAlignment=Center`；「操作」跨后两列居中。同时把「编辑」「删除」两个按钮从 `Right` 改成 `Center`，这样表头「操作」的中心正好落在两按钮之间空隙的中点。
- **内容居中**：行的两个 `TextBox` 加 `TextAlignment=Center`；只读态两个 `TextBlock` 也加 `HorizontalAlignment=Center`；出现方式只读文字居中、`ScopeBox` 加 `HorizontalContentAlignment=Center`——编辑态与完成态现在完全一致。

### 验证
- UIA + 真实鼠标：点「编辑」→ 输入框 2 个；点第 2 行 → **0**（第 1 行自动提交）；再点「编辑」→ 2；点表格外 → **0**。
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图 `artifacts/screenshots/lexicon-centered.png`、`lexicon-aligned.png`（表头居中、内容居中、操作对齐两按钮中点）。

---

## 工作区（1.5.2 之后，未升版本号）续 16：点表格外取消词库行选中

**用户反馈**：点中词库某一行会选中（淡绿），但点了表格外的区域后选中色还留着；应在点外面时取消。

### 改动
- `MainWindow.Interaction.cs` 新增 `ClearReadingSelectionOutside`，在 `InitializeInteraction` 里用 `AddHandler(Mouse.PreviewMouseDownEvent, ..., handledEventsToo: true)` 挂到窗口：若 `ReadingList.SelectedItem` 非空且点击源不在 `ReadingList` 的视觉子树内（`IsWithin` 向上找祖先），则清空选中。
- 点到行内的输入框、下拉框、编辑/删除按钮都不会被误清（判定的是是否在 ListBox 子树里）。

### 验证
- UIA + 真实鼠标：点击前选中 0 → 点第一行 **1** → 点表格外空白 **0**。
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。

---

## 工作区（1.5.2 之后，未升版本号）续 15：词库回到「编辑/完成/删除」，表头贴顶并逐列对齐

**用户要求**：① 表头与下方内容竖向对齐；② 恢复「编辑 / 完成 / 删除」三个按钮（沿用旧逻辑：点编辑不自动聚焦，点哪个框才聚焦），删除不要打叉；③ 表头的浅色底与卡片顶部之间的白边去掉。

### 改动
- **表头**：`Margin` 里的 `0,10,0,0` 去掉、`CornerRadius=13,13,0,0`、`Padding=17,10`，浅绿底直接贴住卡片上沿（不再有 1px 白缝）。列定义改成与行**完全相同的六列**，`原文/读作` 左对齐到列首（去掉 12px 偏移）、`出现方式` 保留 12px（对齐下拉框内缩）、`操作` 跨后两列居中。
- **按钮恢复**：列宽回到 76/76；第 4 列是 `编辑`（`IsEditing` 时文案变 `完成` 并转强调绿），第 5 列是 `删除`；`RowDangerButton` 去掉 × 图标，只保留文字（淡红药丸外形不变）。新增 `RowSecondaryButton`（中性淡绿药丸）给编辑/完成用，两者同一外形、只差配色。
- **旧编辑逻辑复用**：`EditReadingClick` 恢复为「未编辑→进入编辑且**不**设置焦点；已编辑→提交」，删除点击行进入编辑、失焦自动提交那套（`ReadingRowClick` / `ReadingBoxLostFocus` / `BeginEditing` / `FindReadingItem` / `FindAncestor` / `editingReading` 全部移除）。新增行仍沿用旧的 `FocusReading(item)`（新行光标落在原文框，方便直接打字）。
- 首页那句说明此前已删，`PrefixHint` 保持置空。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图 `artifacts/screenshots/lexicon-restored.png`（表头贴顶、列对齐、三按钮恢复）、`lexicon-final.png`（删除纯文字、编辑/完成药丸）。

---

## 工作区（1.5.2 之后，未升版本号）续 14：词库视觉打磨 + 首页说明句下线

**用户要求**：① 「添加词条」按钮 1:1 复刻「添加音色」；② 词库操作按钮太素要美化；③ 表头不够好看要优化；④ 删掉首页「依次朗读每个人的消息，音色和提示词可分别设置」这句。

### 改动
1. `AddReadingButton` 弃用自造的 `AddReadingAction` 药丸，改为与「＋ 添加音色」**完全相同**的 `AccentButton`（`＋ 添加词条`、`Margin 0,12`、居中对齐）。
2. 表头升级：`Border` 浅绿横带（`#F4F8F6` 底 + `#E6EEE9` 下边线 + `8,8,0,0` 圆角 + `17,9,17,8` 内边距），列名用新 `TableHead` 样式（11px、SemiBold、`#6E8779`）。
3. 「删除」按钮重做：`RowDangerButton` 改为**淡红药丸**——`#FBEFEA` 底、`#F1DCD3` 细边、圆角 9，左侧 9px 的 × 图标，悬停底色加深到 `#F7E3DA`、边框 `#E8BFAF`、文字 `#8F4231`，按下再深一层并缩 0.97。
4. 首页说明句：`PrefixHint` 不再填这句（`Text=""` + `Collapsed`），布局占位不变。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图 `artifacts/screenshots/lexicon-polished.png`：表头横带、带 × 的淡红删除药丸、与「添加音色」同款的添加按钮。

---

## 工作区（1.5.2 之后，未升版本号）续 13：词库重做（表头/点击即编辑/删除配色）+ 若干文案与按钮调整

### A1 已恢复（Muse 修好防火墙顺序）
- 复核：`latest.json` **200**（1.5.2、notes 正常）；ZIP HEAD 200 / 90,820,155；Setup HEAD 200 / 66,215,244。

### 改动
1. **气泡高度再降 16%**：内边距 `16,9` → `16,6`（高度约 40 → 33 DIP）。
2. **首页按钮**：`＋ 添加联系人 / ＋ 添加一个人` 统一成 **`＋ 添加`**（`MainWindow.Library.cs` 里那段按数量切文案的逻辑一并去掉），`Padding 14,7 + MinWidth 0`，按钮明显变窄。
3. **词库重做**：
   - 新增**表头**：原文 / 读作 / 出现方式 / 操作（11.5px 次要色，空列表时隐藏）。
   - **删掉「编辑」按钮**：点行内任意位置直接进入编辑（`ReadingRowClick` → `BeginEditing`），点到行外或焦点离开即自动保存（`ReadingBoxLostFocus` 延迟到 Input 优先级再提交，避免 Tab 在行内两个框之间切换时误提交）。
   - 原来的「完成」按钮随之消失；「删除」换成新的 `RowDangerButton`（平时淡棕、悬停变淡红底 + 深红字）。
   - 列表与加号之间那条 `Hairline` **删除**；加号换成 `AddReadingAction` 药丸样式（淡绿底、细边、圆角 11、悬停加深），文案 `＋ 添加词条`。
4. **左栏「高级」→「设置」**（页面标题同步，`AutomationId` 仍是 `NavAdvanced`，代码无需改）。
5. **朗读记录**：去掉「停止当前语音」按钮（`SkipButton`/`SkipClick` 一并删除），只留「清空」。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 交互实测（UIA + 真实鼠标）：点击第一行 → `Edit` 控件 0 → **2**（进入编辑）；点击空白处 → 回到 **0**（自动保存）。
- 截图：`artifacts/screenshots/lexicon-table.png`（表头 + 红色删除 + 药丸加号）、`home-batch.png`（设置 / ＋添加 / 只剩清空）。

---

## 工作区（1.5.2 之后，未升版本号）续 12：全局平滑滚动 + 删掉所有悬停提示 + 气泡上移

**用户要求**：① 继续跟 Muse 盯 A1；② 气泡上移自身高度 3/5；③ 删掉所有鼠标悬停出现的文字框；④ 版本更新说明向下滚动卡顿，希望所有滚动动画都丝滑。

### A. 平滑滚动（新增 `src/VoiceBridge.App/SmoothScroll.cs`）
- 做法参考 WPF 社区广泛使用的「累积目标偏移 + `CompositionTarget.Rendering` 逐帧缓动」方案（不是给滚动条做动画）：
  - `EventManager.RegisterClassHandler(typeof(ScrollViewer), PreviewMouseWheelEvent, ..., handledEventsToo: true)`，**全应用所有 ScrollViewer 一次覆盖**（主页面、更新说明弹窗、各列表）。
  - 轮子距离累积到目标偏移：连续滚动时从**待到达的目标**继续叠加，保住惯性，不会每次从当前像素重新起步。
  - 缓动改 **ease-out-quint**，时长随距离增长（`clamp(140 + distance*0.22, 160, 380)` ms）——线性或过短的曲线就是“一卡一卡”的观感来源。
  - 尊重系统「在控件中播放动画」无障碍开关；嵌套可滚动区域仍优先（页面里的列表还有余量时让它先滚）。
- 删除了 `MainWindow.Interaction.cs` 里原来只作用于 `ContentScroller` 的那套自绘滚动（`PageMouseWheel`/`ScrollFrame`/`StopScrolling`），避免两套机制打架；切页/关窗改调 `SmoothScroll.Cancel(ContentScroller)`。

### B. 所有悬停提示文字框全部删除
- XAML：`MainWindow.xaml` 7 处、`ReleaseNotesDialog.xaml` 1 处 `ToolTip="…"` 全删。
- 代码：`DialogShell.cs` 关闭按钮、`MainWindow.Updates.cs` 侧栏更新按钮与更新包体积的 `ToolTip` 全删。
- 还有一套「禁用控件点击后弹出浮动文字」的机制（`restrictionTip` + `DismissRestrictionTip` + `RestrictedMouseDown` + `ToolTipService.SetToolTip`）整体移除：禁用控件现在只有变淡 + `No` 光标 + `AutomationProperties.HelpText`（读屏仍可读，界面不再弹字）。
- 复查：`grep -rn ToolTip src/` 已无结果。

### C. 气泡上移
- 距底 26 → **50 DIP**（约自身高度的 3/5）。

### D. A1 状态（Muse 已定位根因）
- Muse 回执：Caddy 正常（pid 1034，80/443 均 LISTEN）、证书有效（到 2027-02-04）、本机回环 `curl --resolve …:127.0.0.1` 得 **200 + 1.5.2**。
- **真正根因：A1 本机防火墙规则顺序** —— INPUT 链第 10 条 REJECT 全部、第 11 条 `tcp 443 ACCEPT (update-https)` 排在 REJECT 之后且计数为 0，等于永不生效；所有新入站 443 被丢弃（80 端口仍 308）。
- 诱因：Caddy 17:46 UTC 重启（与内核补丁重启吻合），防火墙重载后顺序错位。
- 修复需用户本人批准改防火墙；Muse 已上报用户，批准后由 Muse 修改并通知我做完整公网复核。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 滚动实测（UIA 采样 `VerticalScrollPercent`）：一次滚轮后数值 `0.3 → 14.7 → 23.8 → 27 → 27.7 → 27.8`，**多帧渐进**而非一步跳变。
- 截图 `artifacts/screenshots/toast-lifted.png`（气泡上移后位置）。

---

## 工作区（1.5.2 之后，未升版本号）续 11：高度/边距/气泡/提示字号一起收口

**用户要求**：① 不要锁滚动，要让内容高度真的合适；② 底边距再大一点点；③ 继续跟 Muse 盯 A1；④ 气泡更透、偏灰、字号边距更协调；⑤ 朗读记录空提示字号偏小（尤其同时出现气泡时更显小），要放大一点。

### 改动
- **高度与边距**：记录卡 120→**96 DIP**（空状态 88），首页底部边距 0→**24**。用 `wvb-scrollcheck.ps1`（UIA 查 `IsScrollPatternAvailable`）迭代：120+24 时 `VerticalViewSize=97.3%`（要滚），100 时 99.93%（临界），**96 时该容器不再可滚动** → 真正靠“高度刚好”放下，而不是锁滚动。
- **空提示字号**：`ResultsEmpty` 12 → **14**，行高 22，与气泡的 13px 形成合理层级（提示属于页面内容，略大于临时气泡）。
- **气泡**：底色 `#80E8EAE8` → **`#6EE4E7E5`**（更透、更偏灰），边框 `#8CCBD2CE`，内边距 18,10 → **16,9**，最大宽 760 → **720**，文字 `#6B7B75` → **`#5C6B64`**，行高 20。

### A1 状态
- 再次直连复核：`https://iceland.de5.net/voice-bridge/latest.json` 仍 **TLS 握手失败**（http=000）。Muse 已受理（`codex_20261110_214359_76061a`），持续跟进中。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图：`artifacts/screenshots/home-records-96.png`（一屏放下、底部留白）、`toast-final.png`（更透气泡）。

---

## 工作区（1.5.2 之后，未升版本号）续 10：首页真正做到「不滚动」+ 更新服务故障定位更正

**用户反馈**：① 朗读记录还是太高、仍要滚动；② VPN 没问题，不要把锅甩给 VPN。

### A. 首页不滚动（用测量代替猜）
- UIA 实测：「朗读记录」标题下方到窗口底部只有 **216 DIP**，而卡片设 300 → 必然滚动；设 176 仍滚动（ScrollViewer `VerticalViewSize` 89.4%→95.8%）。
- 根因：两张联系人卡片本身就占掉将近一整屏，留给记录区的空间极小。
- 最终方案：记录卡按「三行左右」定 **120 DIP**（空状态 112），同时收紧三处间距补出约 35 DIP——首页标题下边距 22→12、记录标题上边距 24→14、首页底部边距 16→0。
- **验证方式**：`wvb-scrollcheck.ps1` 用 UIA 找 `IsScrollPatternAvailable` 的 Pane，改前 `VerticalViewSize=95.8%`（要滚动），改后**该元素不再可滚动**（无输出）→ 真正不滚动了。截图 `artifacts/screenshots/home-no-scroll.png`。

### B. 「检查更新连不上」归属更正（重要）
- 我上一轮把原因写成「本机代理拦截」，**用户指出 VPN 正常，是我判断错了**。
- 重新做了三层取证：
  1. 本机 `curl --noproxy '*'` 直连：TLS 握手失败；`PowerShell TcpClient` 直连 `服务器IP:443`：TCP 能连上，`AuthenticateAsClient` 报「远程主机已关闭连接」→ 是服务端没完成握手。
  2. 公网 DNS（Cloudflare DoH）：`iceland.de5.net` 真实解析到 **服务器IP**（不是本机 Fake-IP），ICMP 可通。
  3. **外部服务复核**（不走本机网络）：`r.jina.ai` 返回 `net::ERR_ADDRESS_UNREACHABLE`，`api.allorigins.win` 返回 **522**（Cloudflare 连不上源站）。
- 结论：**A1 发布服务器侧故障**（443 不完成 TLS 握手、源站不可达），与用户 VPN 无关。80 端口仍返回 308，说明机器活着但 HTTPS 服务异常。
- 应用内文案改中性：「暂时连不上更新服务（服务器没有完成握手）。稍后再试即可，已下载好的更新包不受影响。」
- 已通过 muse-call 通知 Muse 检查 A1 上的 Caddy/443 服务。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 首页 UIA 复测：不再可滚动。

---

## 工作区（1.5.2 之后，未升版本号）续 9：记录区高度回调 + 更新检查报错更准确

**用户反馈**：① 520px 太高，要不用滚动、留三行左右边距；② 点检查更新报“连接不上网络”。

### 分析
- 高度：520 显然过头，页面需要滚动。回调到 **300**（空白提示 292）——正常屏幕下不滚动、底部边距 16、留有三行余量。
- 网络：**用 curl/PowerShell 独立复测**——`https://iceland.de5.net/voice-bridge/latest.json` TLS 握手失败（schannel: failed to receive handshake），而 `baidu.com`、`github.com`、`de5.net` 均 200。`nslookup` 显示 `iceland.de5.net` 解析到 `198.18.0.95`（保留地址段，是本机代理/DNS 的 Fake-IP 段）。**结论：不是应用代码问题，是本机代理软件对该域名的转发异常。**
- 报错文案改得更准确：不再说笼统的“请检查网络”，改说“代理或防火墙拦截了连接（域名能解析但 TLS 握手失败）”。

### 改动
- `Results` Height 520 → **300**；`ResultsEmpty` Height 512 → **292**。
- `MainWindow.Updates.cs` 报错文案更新。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图 `artifacts/screenshots/home-records-300.png`：高度合理、不滚动。
- **建议用户**：在代理软件里检查 `iceland.de5.net`（或 `*.de5.net`）的分流规则/节点，或直接关闭代理试一次。

---

## 工作区（1.5.2 之后，未升版本号）续 8：找到“朗读记录高度改不动”的真正根因

**用户反馈**：朗读记录的高度还是有问题，一直没改成功。

### 真正根因
`Render()` 里 `ResultsBox.Visibility = noResults ? Collapsed : Visible`——**没有记录时，装列表的整个卡片被折叠**，页面上那张矮卡其实是 `ResultsEmpty`（提示文字自身），它没有高度定义，改 `Results` 的 `Height` 当然“一直没改成功”。

### 改法
`ResultsEmpty` 加 `Height="512"` + `VerticalAlignment="Center"`：空状态填满与列表同高的卡片，文字垂直居中。这样无论有没有记录，卡片高度一致，不再塌陷。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图 `artifacts/screenshots/home-empty-tall.png`：空白卡片高、文字垂直居中、贴近底部。

---

## 工作区（1.5.2 之后，未升版本号）续 7：朗读记录区拉到 520px + 杀虚线焦点框

**用户反馈**：① 朗读记录高度完全越来越小、没往下，距底还很大；② 按按钮有时出现虚线框。

### 根因
- **高度**：之前改了 `MinHeight="190" MaxHeight="420"`，但 StackPanel 给元素的是精确高度，MinHeight 不会拉伸——列表永远停在 190。必须用固定 `Height`。
- **虚线框**：WPF 默认 `FocusVisualStyle` 在键盘 Tab 或点击后画虚线。

### 改动
- `Results` 列表 `Height` → **520**（空白卡片明显变长）；首页底部边距保持 16。
- `FocusVisualStyle="{x:Null}"` 加到：隐式 `Button` 样式、`PlainButton`、`ListBoxItem`、`RowMenuItem`、`ComboBoxItem`（上轮已加）。所有按钮/列表项/菜单项不再出现虚线框。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图：`artifacts/screenshots/home-records-520.png`（朗读记录卡片 520px、贴底）。

---

## 工作区（1.5.2 之后，未升版本号）续 6：气泡改为右栏内居中

**用户反馈**：气泡应该在除去左栏的右栏内容区左右居中，不是整个窗口的左右居中。

### 改动
- `HintToast` 从 `Grid.ColumnSpan="2"` 改为 `Grid.Column="1"`，只占内容列，在右栏内居中。

### 验证
- `dotnet build` 0/0；截图 `artifacts/screenshots/toast-content-center.png` 确认字幕条在右栏居中、左栏无干扰。

---

## 工作区（1.5.2 之后，未升版本号）续 5：气泡上移 + 文字去尾标点

**用户反馈**：① 气泡稍微往上一些（之前贴近底太低了）；② 气泡文字结尾不要有任何标点符号。

### 改动
- 字幕条距底边距 14 → **26**（往上挪了 12px）。
- `HintText.Text` 赋值前 `TrimEnd('。')` 去掉尾部句号（其他标点保留）。「试听完成。」→「试听完成」，「正在合成…」不变。
- 朗读记录区高度 320、首页底部边距 16（上一节已做，本次未动）。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- UIA 读到气泡文字 `正在合成`（无尾标点）。

---

## 工作区（1.5.2 之后，未升版本号）续 4：字幕条更透 + 首页朗读记录区拉高

**用户反馈**：① 气泡更半透明些更合适；② 首页朗读记录空的时候再长一些、往下一些，别离页面底那么远。

### 改动
- 字幕条底色 `#CCEDEDEB` → **`#80E8EAE8`**（更低透明度、偏灰绿），边框 `#D4DCDA` → `#A0D2D8D4`；距底边距 `36` → **`14`**，更贴近底部。
- 首页朗读记录区：`ListBox` 高度 `190` → **`320`**，空白卡片明显变长；首页 StackPanel 底部边距 `48` → **`16`**，朗读记录卡贴近页面底。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图：`artifacts/screenshots/toast-translucent.png`（半透明字幕条 + 贴底）、`home-empty-records.png`（朗读记录空白区加长、贴近底部）。

---

## 工作区（1.5.2 之后，未升版本号）续 3：字幕条配色更低调 + 右键菜单改为样式直挂

**用户反馈两条**：① 气泡要更半透明偏灰、文字更低调；② 输入框右键依旧不对，复用「⋮」菜单的那套设计。

### A. 字幕条配色
- 底色 `#F8FFFFFF` → **`#CCEDEDEB`**（更低透明度 + 偏灰绿），边框 `#E6EDE9` → `#D4DCDA`，文字 `#8A5A00`（琥珀）→ **`#6B7B75`**（灰绿），字号 13.5→13。
- 效果：半透明、色调偏冷灰、不抢眼，像低亮度字幕。

### B. 右键菜单复用「⋮」设计
- **根因**：之前 `FieldMenu.cs` 用 `EventManager.RegisterClassHandler` 在运行时建菜单，但显式指定 `Style=RowMenu` 时又吃到 `Width="112"`（为「⋮」两短词条调的窄宽），菜单挤窄；而且 `Dispatcher.BeginInvoke` 有概率吞掉弹出。
- **改为零 C# 方案**：在 App.xaml 定义共享资源 `x:Key="FieldMenu"`——一个 `ContextMenu`，直接复用 `RowMenu`（白底圆角面板 + 阴影）+ `RowMenuItem`（圆角悬停行）**原封不动**，只改 `Width=Auto / MinWidth=148` 容下快捷键。四个 `MenuItem` 分别绑 `ApplicationCommands.Cut/Copy/Paste/SelectAll`，`InputGestureText` 标 Ctrl+X/C/V/A。
- 然后在**已有的隐式 TextBox / PasswordBox 样式**里加一行 `<Setter Property="ContextMenu" Value="{StaticResource FieldMenu}"/>`，所有输入框自动生效。**不再需要 `FieldMenu.cs`**（已删除）与 `EventManager` 钩子。
- UIA 实测：菜单项 `剪切[禁] 复制[禁] 粘贴[可用] 全选[可用]`（无选区时前两项自动禁用），样式与「⋮」菜单完全一致。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 截图：`artifacts/screenshots/toast-muted.png`（半透明灰绿字幕条 + 干净圆角）。

---

## 工作区（1.5.2 之后，未升版本号）续 2：提示改为底部居中字幕条（弃用 Popup）

**用户反馈**：跟手气泡“一般”，改成底部中间的字幕气泡；并指出三个缺陷：四角有外接矩形（圆角外露方框）、应用最小化后气泡不消失、拖动窗口时气泡不跟随。

### 根因
- 四角矩形：`Popup` 是独立透明窗口，内容再叠 `DropShadowEffect`，渲染边界按外接矩形出图，圆角外会露一圈方框。
- 不随窗口：Popup 是独立 HWND，位置按屏幕坐标摆放，窗口最小化/移动都不会带着它。

### 改法
- 删掉 `Popup HintPopup`，改为**窗口内**的 `Border HintToast`（`Panel.ZIndex=40`、`Grid.ColumnSpan=2`、`HorizontalAlignment=Center`、`VerticalAlignment=Bottom`、`Margin=24,0,24,36`、`MaxWidth=760`、圆角 12、`#F8FFFFFF` 底 + `#E6EDE9` 细边、13.5px `#8A5A00` 文字居中）。
- 因为它就是窗口 VisualTree 的一部分：**跟着窗口移动、最小化就自然不可见、圆角外不会再有方框**，也不需要阴影弹层。
- 交互：`notice` setter 填字 + 淡入（140ms，0→1），5 秒定时器到点淡出（200ms）后 `Collapsed`；切页 `notice = null` 立即淡出。删掉 `hintAnchor` 与 `Keyboard.FocusedElement` 那套锚点逻辑。
- 踩坑：第一次贴到根 Grid 没写 `Grid.ColumnSpan`，落到第 0 列（侧边栏）里，字幕条跑到了左下角；补 `ColumnSpan=2` 后居中。

### 验证
- `dotnet build` 0/0；`Test.ps1` **.NET 209/209 + Python 54 PASS**；`Verify-Source` PASS。
- `UiSmokeTest`：`notice: [试听完成。]` **PASS**（新结构仍能读到 `HintPopupText`）。
- 截图 `artifacts/screenshots/toast-bottom.png`：字幕条位于窗口底部居中、圆角干净无方框。

---

## 工作区（1.5.2 之后，未升版本号）续：词库编辑行“右半边消失” + 右键菜单跟手

**用户反馈**：“这不对吧，用汉语，而且都歪了”。拆成两条具体问题：

### A. 词库编辑行的“读作”输入框看不见（截图里像少了一块）
- **根因**：`ListBoxItem` 的悬停底色是 `Fill`（`#F0F4F2`），而输入框的非聚焦底色**也是** `Fill`——鼠标停在行上时，未聚焦的 `SayBox` 与行底色完全同色，看上去就像不存在（用户截图里正好是鼠标悬停在该行、且 `MatchBox` 获得焦点变白）。
- **修**：行悬停底色改 `FillStrong`（`#E3E9E6`），输入框仍 `Fill`，于是悬停时输入框比行更亮，两个框都看得见。⚠️**别把输入框改成白底**：卡片本身就是白的，白底输入框在非悬停行里直接隐形（本轮先试过，截图里更糟）。
- 验证：`artifacts/screenshots/lexicon-row-fixed.png`（非悬停：两个框清楚）、`lexicon-row-hover.png`（悬停：两个框仍可见）。

### B. 右键菜单不是中文、且位置歪
- 中文：`FieldMenu.cs` 已自建中文菜单（裁剪/复制/粘贴/全选 + 快捷键），不再用框架本地化菜单；UIA 实测菜单项为 `剪切/复制/粘贴/全选`。
- 位置：原来 `Placement = Bottom` + `PlacementTarget = 输入框`，宽输入框下菜单会跑到框的左下角、离鼠标很远；改为 **`Placement = MousePoint`**，跟着鼠标走，与系统菜单一致。

### 验证
- `dotnet build` 0/0；`Test.ps1` .NET 209/209 + Python 54 PASS；`Verify-Source` PASS。
- 未打包、未上线（线上仍为 1.5.2）。

---

## 工作区（1.5.2 之后，未升版本号）：提示改成跟手的小气泡 + 音色下拉修复 + 右键菜单覆盖全部输入框

> 状态：完成、构建通过、UIA/截图验证。**未打包、未上线**。

**用户反馈三条**：① 提示跑到标题上方不对，应该在“点击的那个栏目下面”，而且只出一个；② 人物卡片的音色下拉框坏了，和其他下拉框不一样（分厂商要保留）；③ 输入框（的右键菜单）没修全。

### A. 提示改为跟手小气泡（Popup）
- 删掉四个页级提示 TextBlock（首页 `NoticeText`、音效 `VoiceLibraryNotice`、高级 `ApiNotice`、词库 `ReadingNotice`），把它们补偿过的边距全部还原（首页标题 22、词库 20、两个分组标题恢复样式默认）。
- 新增窗口级 `Popup x:Name="HintPopup"`（`Placement=Bottom`、`IsHitTestVisible=False`、Fade）：白底圆角 9、细边框、柔和阴影，12px 琥珀色文字。
- `notice` setter 用 `Keyboard.FocusedElement` 作为锚点——用户刚点的那个控件还握着焦点，所以气泡就出现在它正下方；找不到锚点（或它已不可见）时退回到内容区。
- Popup 不占布局，所以**任何页面都不会被推开**；同一时刻只有一条；5 秒自动关；切页即关（`NavigationChanged` 里 `notice = null`）。
- 词库的 `ShowReadingNotice` 也改成走同一个气泡，删掉 `readingNoticeTimer` 与页面内提示行。
- `tools/UiSmokeTest` 改为在 RootElement 里按 `HintPopupText` 找气泡文案（气泡在独立弹层窗口里）。

### B. 音色下拉框“坏了”
- 根因：`VoiceChoiceItemStyle` 是显式 `ItemContainerStyle`，它**覆盖**了 App.xaml 的隐式 `ComboBoxItem` 样式，于是这三个音色下拉的选项退回到 WPF 默认灰方块样式。
- 修：`BasedOn="{StaticResource {x:Type ComboBoxItem}}"`。分厂商分组（`VoiceProviderGroup`）与“失效音色灰斜体且不可选”的语义都不变。
- 顺带给 `ComboBoxItem` 样式加了 `FocusVisualStyle=null`，去掉选中项上那圈虚线焦点框（所有下拉一致）。

### C. 右键菜单覆盖全部输入框
- 根因：之前只靠 App.xaml 的隐式 `ContextMenu`/`MenuItem` 样式，仍依赖 WPF 内部生成的文本菜单，文案还是框架本地化（曾出现英文 Cut/Copy/Paste）。
- 修：新增 `src/VoiceBridge.App/FieldMenu.cs`，在 `App.OnStartup` 里 `EventManager.RegisterClassHandler` 挂住 `TextBoxBase` 与 `PasswordBox` 的 `ContextMenuOpening`：第一次右键时给它装上自建菜单（剪切/复制/粘贴/全选，中文 + 快捷键提示，`RowMenu`/`RowMenuItem` 样式，`CommandTarget` 指回该控件），`Handled=true` 后延迟一帧打开（在事件里同步开弹层会被吞掉）。命令仍是系统 `ApplicationCommands`，行为不变。
- 覆盖：所有 TextBox、多行文本、词库行内输入、各对话框、以及 API 密钥的 PasswordBox。

### 验证
- `dotnet build -c Release` 0 警告 0 错误；`Test.ps1` **.NET 209/209 PASS + Python 54 PASS**；`Verify-Source` PASS。
- 气泡：`UiSmokeTest` → `notice: [试听完成。]` **PASS**；截图 `artifacts/screenshots/hint-chip.png` 显示气泡就在“试听这段文字”按钮正下方。
- 右键菜单：UIA 聚焦高级页输入框后按 VK_APPS，菜单项为 `剪切[False] 复制[False] 粘贴[True] 全选[True]`（无选区时前两项自动禁用）。
- 音色下拉：UIA 展开“此联系人音色”，白底圆角面板 + 绿色分组头 `Fish Audio`、选中项浅绿、无虚线框，截图 `artifacts/screenshots/voice-dropdown.png`。

---

## 1.5.2 · 2026-11-10（提示行不再挤动布局 + 打包上线）

### A. 提示行挤动布局（用户报）

- **实测**：用 UIA 量高级页「软件更新」标题的纵坐标：无提示时 370，提示出现时 **409**，消失后回到 370——**提示把下面内容整体下推 39 物理像素（约 19.5 DIP）**，确实是跳一下。
- **最小修复**：四个提示位（首页 NoticeText、音效 VoiceLibraryNotice、高级 ApiNotice、词库 ReadingNotice）都改为**固定高 20 DIP、常驻可见**的留白行，文字填入其中而不改高度；同时把上方标题下边距等量减免（首页 22→2；音效/高级的「播放」「软件更新」分组标题改为 `Margin 14,4,0,8`；词库标题 20→0），所以**无提示时的排版与修前完全一致**。
- C# 对应改动：`Render()` 不再对三个槽切 `Visibility`（切了就拿掉预留高度），`ShowReadingNotice()` 同样不切。
- **重测**：无提示 370 → 提示出现 **370** → 过期后 370，**shift = 0 px**。首页/词库页截图确认间距无变化。

### B. 1.5.2 打包与上线
- 版本号 `Directory.Build.props` → **1.5.2**；`ReleaseHistory` 新增 1.5.2 条目（状态点颜色语义、呼吸灯、字号层级与基线、右键菜单、提示行行为、去掉启动噪音）。
- `Test.ps1`：**.NET 209/209 PASS · Python 54 PASS（41 skipped 基线）**；`Verify-Source` PASS。
- `Publish.ps1` → `artifacts\win-x64-v1.5.2\` + ZIP **90,820,155 字节 / `dad840a1ab28cd5ffb6cc086fcf3769900df95b491ff56b0fd6d84ee9813fb9b`**；Clean-payload PASS（1733 文件）。
- `Build-Installer.ps1 -PayloadDirectory` → Setup **66,215,244 字节 / `a9689e16fb1719ce234ccab9f55d71329d87cc354f680ef18a7952fc25791f88`**。
- 产物核验：`ProductVersion=1.5.2+4ae22b4`（源码 commit `4ae22b4`，先提交再打包）、`FileVersion=1.5.2.0`；1.5.2 > 线上 1.5.1。
- 备注：旧文档里的「211 项」与实际一致性的偏差（现为 209 项、0 失败）已在本节按实测记录；自 1.5.0 以来 `tests/` 没有改动过，不是本轮引入的。

### C. 1.5.2 上线结果（Muse 发布单 `codex_20261110_174906_4f5a16`）

- Muse 发布单：先 `status=info`（流水线接手），约 3 分钟后 `status=ok`（`publishedAt=2026-11-10T16:52:12Z`），称**哈希链三方一致**（申报＝PC 实测＝A1 落地）、Setup 一并发布、旧清单备份 `latest.json.bak-1.5.1`。
- **独立公网复核（不只信回执）**：
  - `GET latest.json` **200**，六字段齐全：`version=1.5.2`、`publishedAt=2026-11-10T16:52:12Z`、`size=90820155`、`sha256=dad840a1…9813fb9b`、`url=…/WeChat-Voice-Bridge-V1.5.2-win-x64.zip`，notes 166 字中文正常（UTF-8 解码无乱码）。
  - ZIP / Setup `HEAD` 均 **200**，`Content-Length` = 90,820,155 / 66,215,244，与本地一致。
  - **完整下载 ZIP 重算 SHA-256 = `dad840a1…9813fb9b`，与清单一致**（字节数也一致）；临时下载文件已删。
- 未动桌面快捷方式（仍指向 1.4.7 r3，供用户亲自点一次更新实测）。

---

## 1.5.1 · 2026-11-10（续）：页面提示小字重做（页内归属 + 5 秒自动消失）

> 状态：完成、构建通过、UIA 实测。**未打包、未上线、未改版本号**。

**用户反馈**：在高级页点「试听」出现的「试听完成。」，切到音效页还能看到；这类小字又乱又不会消。

### 审计找到的问题

1. **一份值渲染到三个地方**：`notice` 是一个全局字符串，`NoticeText`（首页）/`VoiceLibraryNotice`（音效）/`ApiNotice`（高级）三个 TextBlock 每次都一起写入同一文字。只显示一个页，所以看起来就是“每个页面都有这个提示”，并且任何页的动作都会出现在所有页。
2. **没有保质期**：小字只能被下一个动作覆盖，永不自己消失，于是「试听完成。」可以赖到会话结束。
3. **位置错位**：高级页的提示位在「语音服务」卡片里（「添加 API 服务」下面），所以一个试听提示会出现在 API 列表下方，跟上下文无关。
4. **无意义提示**：「已自动找到微信数据目录。」每次启动都弹一下，信息在高级页本来就看得见——删除。
5. **词库有另一套**：`ReadingNotice` 单独一个 TextBlock，同样不会过期（「词库已更新，从下一条开始生效。」一直挂着）。
6. **样式不统一**：首页那条 13px、另两条 12px；空字符串时另两条仍占着 8px 行高。

### 改法

- `notice` 由普通字段改为**属性**（读写语义不变，35 处赋值点不用改），setter 里记下升起的页（`CurrentPageIndex`）并启动 5 秒定时器；`Render()` 只向当前页的提示位输出文字，三个 TextBlock 统一按有无文字切 `Visibility`（空的不占位）。
- **切页即清**：`NavigationChanged` 里清掉提示，不再跟着人走。
- **位置统一**：三个提示位全部挪到各页标题下方，同一尺寸（`Footnote` 12px、`Caution` 色）。
- **词库同步**：`ShowReadingNotice` 加 5 秒定时清除。
- 删掉「已自动找到微信数据目录。」。
- **工具适配**：`tools/UiSmokeTest` 默认模式以前读 `NoticeText`（靠的就是“三处同文”的 bug），现在改为在当前页三个槽里找命中文案。

### 验证
- `dotnet build -c Release`：**0 警告 0 错误**；`Verify-Source` PASS。
- `UiSmokeTest <pid>`：高级页试听 → `notice: [试听完成。]`，**SMOKE TEST PASS**。
- 切页（UIA 脚本）：高级页出现提示 → 切到音效 **找不到** → 切回高级 **也找不到** → `SWITCH_PASS`。
- 过期：提示出现后等 7 秒 → 已消失 → `EXPIRE_PASS`。
- 页面截图：`artifacts/screenshots/page-top-NavHome.png`、`page-top-NavVoice.png`、`notice-hint-top.png`（提示在高级页标题下方）。
- **未实机验**：词库那条 5 秒过期（需造重复词条才能触发提示），代码路径与其它页相同。
- **保持不动的**：更新卡片下方的更新状态行（「已是最新 v1.5.1」等）和首页的状态描述——它们是状态，不是提示，不应自动消失。

---

## 1.5.1 · 2026-11-10（续）：修复开发构建「缺少 Python 3.12 Worker」+ 呼吸幅度加大

> 状态：两项均完成、构建通过、实机验证。**未打包、未上线、未改版本号**。

### A. 「缺少 Python 3.12 Worker」（用户报错）

- **不是 UI 改动弄坏的**：错误文案来自 `PythonFixtureSource.PrepareAsync` 的守卫（`!File.Exists(python) || !File.Exists(script)`，即 `bin\...\runtime\python\python.exe` 或 `worker\main.py` 不在）。开发构建目录（`src\VoiceBridge.App\bin\Release\net8.0-windows`）从来不会由 `dotnet build` 生成 `runtime\`，也不会拷 `worker\deps`——那两个目录只由 `Publish.ps1` 打进发布包。之前能跑靠的是工作区里残留的运行时，本轮发现 `artifacts\win-x64\runtime` 已经不在（旧的无版本目录被清掉了），所以点「开始」就报缺 Worker。
- **修复**（只是把运行时和依赖补回开发目录，不动源码）：
  - `artifacts\win-x64-v1.5.1\runtime` → `src\VoiceBridge.App\bin\Release\net8.0-windows\runtime`（41 MB，Python 3.12.10）
  - `artifacts\win-x64-v1.5.1\worker\deps` → `bin\...\worker\deps`（sqlcipher3 / zstandard）
  - 顺手把文档里引用的 `artifacts\win-x64\runtime` 也补回来了（`Test.ps1 -Python` 要用）。
- **验证**：`runtime\python\python.exe` 为 3.12.10；在 worker 目录下插入 `deps` 后 `import sqlcipher3, zstandard` 与 `import wechat_adapter, audited_wechat_keys, contact_index` 均 OK；应用重启正常，`ResolvePython()` / `WorkerScript` 两个路径均存在，守卫不会再触发。
- **教训**：从 `bin` 跑的开发版不是自包含的，重启（或清理过 artifacts）后必须重做上面两条拷贝，否则一按「开始」就报缺 Worker。

### B. 呼吸幅度加大（用户：不够明显）

- `BreathingLight.cs` 数值：透明度 1 → **0.42**（旧 0.62）、缩放 1 → **0.85**（旧 0.93）、光晕峰值 0.22 → **0.30**、扩散 1.9 → **2.05** 倍；节拍（3.2 s、底部停留）不变。
- 验证：逐帧采样 12 帧（250 ms）：核心色 `#1D9A5B` (30,154,92) ↔ **(160,213,186)**，反推透明度约 1.0 ↔ 0.42，与设定一致；帧条 `artifacts/screenshots/dot-breath-amplitude-strip.png`，放大图 `dot-peak.png` / `dot-dim.png`。
- 其他未动，胶囊点仍然只在绿色（正在监听）时呼吸。

---

## 1.5.1 · 2026-11-10（续）：输入框右键菜单改用「⋮」菜单同款外观

> 状态：代码完成、构建通过、真实应用右键截图验证。**未打包、未上线、未改版本号**。

**用户要求**：输入框右键的剪切/复制/粘贴还是 WPF 默认的灰渐变方角菜单，“不是一个界面的感觉”；要求复用 API 那三个点的弹出框样式。

**做法**（`App.xaml`，共两处）：
- 在 `RowMenuItem` 模板里加了一列**快捷键提示**（右侧、11px、55% 不透明度），并加 `InputGestureText` 为空的 Trigger 把它折叠——所以「⋮」菜单（没有快捷键）看起来一字未变，而剪切/复制/粘贴菜单能把 `Ctrl+X / Ctrl+C / Ctrl+V / Ctrl+A` 显式到右侧。
- 新增两条**隐式样式**：`ContextMenu`（`BasedOn RowMenu`，宽度改 Auto、`MinWidth 132`）与 `MenuItem`（`BasedOn RowMenuItem`）。应用里只有输入框会用默认右键菜单（⋮ 菜单显式指定了样式，不受影响；托盘菜单是 WinForms，也不受影响）。
- 没有改任何 C#，也没有改剪贴板命令——菜单项还是 WPF 自带的 `ApplicationCommands`，只是外观换了；无选区时剪切/复制会自动置灰（模板已有 0.45 透明度的禁用态）。

**验证**：
- `dotnet build -c Release`：**0 警告 0 错误**；`Verify-Source.ps1` PASS。
- 真实应用右键截图：`artifacts/screenshots/context-menu-textbox.png`——白底圆角面板 + 柔和阴影 + 悬停铺底色 + 右侧快捷键，与「⋮」菜单同一套观感。
- 回归确认：「⋮」菜单用 `tools/MenuPreview` 重新渲染（`artifacts/screenshots/menu-preview.png`），静态/悬停编辑/悬停删除三态与修前完全一致（手势列为空时已折叠）。

**注**：本机从 Git Bash 启动时框架默认菜单文案是英文（Cut/Copy/Paste/Select All），用户自己双击运行时是中文「剪切(T)」等；两者共用同一模板，宽度按内容自适应。

---

## 1.5.1 · 2026-11-10（续）：呼吸灯细节重做 + 左下角圆点只在连上时呼吸

> 状态：代码完成、构建通过、实机逐帧验证（灰色静止 + 更新点呼吸）。**未打包、未上线、未改版本号**。

**用户要求（两条）**：
1. 左下角胶囊那个点（连接状态）**只有绿色（已连接）才呼吸**；黄色（连接中）和灰色（未连接）不呼吸。
2. 呼吸灯“再细一点、更细致”，**不是更炫酷**。

**做法**：
- 新建 `src/VoiceBridge.App/BreathingLight.cs`：两个圆点共用的呼吸动画定义（对象引用传参，不再在 XAML 里写两份）。
  - **不对称节拍**：一个呼吸 3.2 s = 慢慢暗下去（1.25 s）→ **在底部停一拍（到 1.7 s）** → 再慢慢亮回来（到 3.2 s）。原来的 `AutoReverse` 是对称的，看起来像“闪”；底部那拍的停留才是“呼吸”。
  - 幅度收敛：透明度 1 → **0.62**（旧 0.35 太深、显得太狠），缩放 1 → **0.93**（旧 0.88），两者都 `SineEase EaseInOut`。
  - 新增**同色柔光晕**：每个点后面多一个同色 `Ellipse`（`Fill` 绑定到点的 `Fill`，所以颜色自动跟随三/四态），`BlurEffect Radius=4`，在每次呼吸开头最亮（0.22）到 1.7 s 完全消失，同时 1 → 1.9 倍擑开。为了让光晕不跑出占位，两个点外面各包一层同尺寸 `Grid`（10×10 / 8×8），**占位尺寸一字未变**。
- XAML：`UpdateStateDot`、`StatusPillDot` 各包一层 `Grid`（内层光晕 + 圆点，都带具名 `ScaleTransform`），删掉原先写在 XAML 里的 `AutoReverse` 动画。
- C#（`MainWindow.xaml.cs`）：构造函数里启动更新点的呼吸（整个会话常驻）；新增 `SetPillBreathing(bool live)`，在 `Render()` 里按 `state.State == BridgeState.Running` 开/停，用 `BeginAnimation(prop, null)` 清掉动画回到基础值（点满亮、光晕不可见），带幂等守卫不会每次 Render 重启动画。
- 更新点依旧三态（绿/红/橙）都呼吸；胶囊点只有绿色呼吸，灰/黄/红全静止。

**验证**：
- `dotnet build -c Release`：**0 警告 0 错误**。
- 更新点 12 帧逐帧采样（间隔 250 ms）：核心色 `#1D9A5B` (29,154,91) → (115,192,153) → 回升，且**中间 3 帧停在同一个值**，证实底部停留那段节拍生效。
- 胶囊点同时段 12 帧：色值恒为 (174,174,178)，**完全静止**（灰态不呼吸）。
- 布局未动：状态文字左缘像素位置修前修后都是 x=564。
- 截图：`artifacts/screenshots/dot-breath-refined-strip.png`（一呼一吸帧条）、局部放大 `dot-peak.png` / `dot-dim.png`（可见光晕只出现在峰值）。
- **未验证**：胶囊点绿色（正在监听）的呼吸未实机看过——需要真实微信连接才能进那个状态；它与已验证的更新点用的是同一个 `BreathingLight.Create`，且具名 Transform 由编译期保证存在。

**注**：本轮构建时旧进程占着 exe，先关掉重建再启动；`artifacts/screenshots/dot-breath-green-strip.png`、`dot-breath-red-strip.png` 是上一版（对称淡入淡出）的产物，已被 `dot-breath-refined-strip.png` 取代。

---

## 1.5.1 · 2026-11-10（续）：更新状态点改成呼吸灯（纯 XAML，三色通用）

> 状态：代码完成、构建通过、逐帧像素验证通过。**未打包、未上线、未改版本号**。

**用户要求**：这个点不管绿/橙/红，都希望有呼吸灯的质感；只改这个点，不碰其他功能。

**做法**（WPF 里呼吸灯的标准写法，参考通用的 Storyboard + DoubleAnimation + SineEase 控件样本，不引入任何依赖）：
- 只改 `MainWindow.xaml` 里的 `UpdateStateDot`：给它加 `RenderTransformOrigin="0.5,0.5"` + 一个 `ScaleTransform`，在 `Ellipse.Triggers` 的 `FrameworkElement.Loaded` 上起一条 `Storyboard RepeatBehavior="Forever" AutoReverse="True"`。
- 两条动画各 1.6 s（一次完整吸气/呼气 3.2 s）：`Opacity` 1 → **0.35**、`ScaleX/ScaleY` 1 → **0.88**，都带 `SineEase EaseInOut`，所以是缓入缓出、不会闪。
- **颜色不参与动画**：`Fill` 仍由 `RenderUpdate()` 按三态赋值，呼吸只改透明度/缩放，因此绿、红、橙三种状态自动都一样。
- `RenderTransform` 不影响布局，所以 10 px 占位、文字位置、卡片高度、按钮居中全部不变；未动任何 C# 代码。

**验证**：
- `dotnet build -c Release`：**0 警告 0 错误**；`Verify-Source.ps1` PASS。
- 逐帧采样（`PrintWindow` 连续抓帧 + 像素采样，绿态 12 帧 / 间隔 120 ms）：圆点核心色从 `#1D9A5B` (29,154,91) 平滑淡到 (161,213,187) 再回升——按白底混合反推透明度约 1.0 ↔ 0.39，与设计的 1 → 0.35 一致。
- 红态 8 帧：从 `#D33B3B` (211,60,60) 淡到 (240,187,187)，证明动画确实与颜色无关（橙态同一条代码路径）。
- 帧条拼图：`artifacts/screenshots/dot-breath-green-strip.png`、`dot-breath-red-strip.png`。
- 开销：动画运行 6 秒内进程 CPU 共 **31 ms**（约 0.5% 单核），可忽略。
- 收尾：假 pending 包已删、`updates` 目录为空、应用已退出、`settings.json` 指针未变。

---

## 1.5.1 · 2026-11-10（续）：更新说明标题与体积的**基线对齐**

> 状态：代码完成、构建通过、实机像素测量验证。**未打包、未上线、未改版本号**。

**用户要求**：截图里「v1.5.2」与「0.0 MB」的下限没在同一条水平线上，要求修复。

**根因**：两段文字是 **两个平级的 TextBlock**，只能用 `VerticalAlignment="Bottom"` 做底边对齐。字号不同（13 vs 11）时下降部深度不同，底边对齐并不能让基线对齐，实测「0.0 MB」的基线比「v1.5.2」**高 3 物理像素（1.5 DIP，DPI 2.0）**。

**改法**：把两段文字改成 **同一个 TextBlock 的两个内联 Run**（`UpdateNoteTitle` / `UpdateNoteSize` 由 TextBlock 改为 Run，名字不变所以 code-behind 的 `.Text` 赋值不用改），WPF 内联 Run 天然共用一条基线；间距由体积那一 Run 的前导两个空格表达（内联 Run 没有 Margin）。顺带删掉不再用的 `Footnote` 样式引用与 `Margin="8,0,0,2"`。

**验证**：
- `dotnet build -c Release`：**0 警告 0 错误**。
- 像素测量（`PrintWindow` 截图像素级扫描）：标题墨迹行 359–378、「0.0 MB」墨迹行 362–378——**下缘完全相同（378）**；修前为 359–378 vs 359–375（差 3 px）。
- 三个状态重拍：`font-state-uptodate(-crop).png`（绿点、无标题行）、`font-state-red(-crop).png`（红点）、`font-state-orange.png` + `font-state-orange-crop.png`（橙点）；局部放大 `font-baseline-zoom.png` / `font-baseline-zoom-red.png`。
- 收尾：假 pending 包已删、`updates` 目录为空、应用已退出、未点安装、`settings.json` 指针未变。

---

## 1.5.1 · 2026-11-10（续）：软件更新卡片字号层级（当前版本最大）+ 圆点微调到 10px

> 状态：代码完成、构建通过、三态（无更新 / 有更新待下载 / 有更新待安装）实机截图验证通过。**未打包、未上线、未改版本号**。

**用户要求**：截图上「当前版本 v1.5.1」看着比下面新版本标题「v1.5.2」还小；要求当前版本更大、下面新版本收小（至少要比当前版本小、但不显突兀），小圆点大小要与当前版本字体匹配，三个状态都要考虑。

**改动**（仅 `src/VoiceBridge.App/MainWindow.xaml` 的更新卡片）：
- 「当前版本」行：`UpdateVersion` 15 → **17 SemiBold**（字号阶梯的主级）。
- 新版本标题：`UpdateNoteTitle` 从 `SectionTitle`（16 SemiBold）改为显式 **13 SemiBold / Label 色**；体积小字 `UpdateNoteSize` 12 → **11**（仍为 Secondary 灰）。这样「17 主级 > 13 次标题 > 11 元数据」三级清楚，且新版本标题仍比下方说明正文（13）不显眼。
- 状态圆点：`UpdateStateDot` 9px → **10px**，与 17px 文字视觉重量匹配（两列 Grid 与按钮整块居中的结构未动，`RowSpan` 仍禁用）。
- 按钮、文案、下载/安装逻辑、其它页面一律未动。

**验证**（真实应用 PrintWindow 截图 + 裁切，`artifacts/screenshots/`，已 gitignore）：
- `dotnet build -c Release`：**0 警告 0 错误**；`Verify-Source.ps1` PASS。
- 无更新（绿点，卡片只有一行）：`font-state-uptodate(-crop).png` —— 17px 主行在 52 DIP 卡片内仍居中，卡片高度未变。
- 有更新待下载（红点 + 标题行 + 说明框）：`font-state-red(-crop).png`。
- 有更新待安装（橙点 + 绿实心按钮）：`font-state-orange(-crop).png`。
- 三态下「当前版本」均明显大于「v1.5.2」标题，圆点与文字视觉匹配，按钮仍在「卡片上沿→说明框上沿」区域内居中。
- 收尾：假 pending 包已删、`updates` 目录回到空、未点安装、线上清单未动；`settings.json` 的 `FishKeyName` 仍与 provider `CredentialId` 一致。

**坑（工具）**：`tools/UiSmokeTest shot` 是 `CopyFromScreen`，窗口被其他窗口遮住时只能拍到桌面；本轮改用 `%TEMP%\wvb-shot.ps1`（PrintWindow + `PW_RENDERFULLCONTENT` + `SetProcessDPIAware`）直接渲染窗口内容，输出 1880×1560 物理像素，可靠且不被遮挡。该脚本在临时目录，不入仓库。

**未做 / 待拍板**：版本号仍为 1.5.1（等于线上）；`tools/UpdatePanelPreview` 仍是旧字体/旧三色数组，下次用它量测前需同步。

---

## 1.5.1 · 2026-11-10（续）：软件更新状态点改色为绿 / 红 / 橙（未打包）

> 状态：代码完成、构建通过、三态实机截图验证通过。**未打包、未上线、未改版本号**，等用户决定是否随下一版发布。

**用户要求**：「软件更新」那个小圆点现在的语义是 灰（最新）→ 橙（有更新）→ 绿（下载完待装）→ 灰（重启安装后），要求改成 **绿（最新常态）→ 红（发现更新）→ 橙（下载完待装）**，即只把这三个颜色互换，其它代码一律不动。

**改动**（仅一处映射）：
- `src/VoiceBridge.App/MainWindow.Updates.cs` 的 `RenderUpdate()`：`UpdateStateDot.Fill = manifest is null ? IdleDot : verifiedUpdate is not null ? LiveDot : WarnDot;` → `manifest is null ? LiveDot : verifiedUpdate is not null ? WarnDot : ErrorDot;`（绿 `#1D9A5B` / 橙 `#B2760A` / 红 `#D33B3B`）。
- 三个画刷是现成的静态字段，直接换顺序即可；**没有重新定义画刷**，因此左侧状态胶囊「未开始（灰）/ 正在监听（绿）/ 正在准备（橙）/ 已停止（红）」的配色完全没变。
- 按钮配色、文案、布局、下载与安装逻辑一律未动。
- `src/VoiceBridge.App/MainWindow.xaml` 里描述该圆点的那行注释由「grey / amber / green」改为「green / red / orange」。

**验证**（真实应用 + 真机截图，见 `artifacts/screenshots/`，该目录已被 gitignore）：
- `dotnet build -c Release`：**0 警告 0 错误**（本次本机 `%LOCALAPPDATA%\Temp\wvb-sdk` 曾被清理，重新下载 dotnet-sdk-8.0.427 解压到 `%LOCALAPPDATA%\Temp\wvb-sdk\sdk-tmp` 使用）。
- 绿色（常态，`latest.json` = 1.5.1，本地 1.5.1）→ `update-dot-green.png`：圆点为绿。
- 红色（发现新版）→ `update-dot-red-available.png`：在 `%LOCALAPPDATA%\WeChatVoiceBridge\updates` 临时放一份 6 字段假 pending 清单（v1.5.2，zip 存在但 sha 不匹配，于是「可更新但未下载」）→ 圆点为红、按钮「更新到 v1.5.2」、状态行「发现新版本。」。
- 橙色（下载完待装）→ `update-dot-orange-ready.png`：把假 zip 的 sha256 与清单对齐（4096 字节假包）→ 圆点为橙、按钮「重启安装更新」、状态行「更新包已下载并通过校验，可直接重启安装。」。
- 收尾：三份假文件全部删除（`updates` 目录回到空）、应用进程已关闭、**未点击任何安装/重启**，线上清单未动。
- 顺带体检：`KeyStoreInspector` 显示钥匙库 1 条被引用条目 `speech-5f1c37e3…`、len=51、fp=68BD1117（与文档记录的真钥匙指纹一致），`settings.json` 的 `FishKeyName` 与 provider `CredentialId` 一致——没有被 1.5.0 修过的「指针覆写」问题复现。

**未做 / 待拍板**：
- `tools/UpdatePanelPreview`（开发期预览与像素量测工具，不入发布物）里还写着旧的 IdleDot/WarnDot/LiveDot 三色数组，本次按「不动其他代码」要求没改；下次用它量测前需同步成绿/红/橙。
- 版本号仍是 1.5.1（与线上相同），所以**这次改动还不能直接推更新**：要发布须先把 `Directory.Build.props` 升到 1.5.2 并补 `ReleaseHistory` 条目，再走「打包 → Muse → 公网复核」流程。等用户明确发话。

---

## 1.5.1 · 2026-11-10（更新页三处打磨 + 高级页布局 + ⋮ 菜单比例，已上线）

> 状态：**已发布上线**。源码 commit `dc2a9fd`；Muse 发布单 `codex_20261110_141758_3cb004`，回执 `status=ok`。

### 用户要求（按提出顺序）

1. **更新页版本说明要更像标题**：`v1.5.0 · 90.8 MB` 那行小字改成标题样式，安装包体积作为次要信息跟在右侧。
2. **状态圆点与文字纵向对齐**：不只是灰点，黄点、绿点三种状态都要在同一中线上。
3. **删掉第二行「已是最新版本」**：格子里只留圆点和一行字，上下居中。
4. **更新按钮居中范围重定义**：先说「往下一点点」，随后澄清——上是卡片上沿，下是「更新说明框的上沿」，因为按钮与说明之间没有横线，两者是一个整体。
5. **无说明文字时也要居中**：有更新说明时设计没问题，没有说明时「当前版本」行没居中好。
6. **「移除」按钮悬停提示多余**：鼠标停上去会弹文字，去掉。
7. **高级页三处**：语音服务标题与第一个服务分组挨太近；试听区要给「音色」「文本」各加小标题；文本框下面那条横线多余。
8. **⋮ 菜单位置**：先要求从按钮向右下展开、宽度收窄不出框，再要求参照参考图改成接近正方形的比例，「更像正常软件的直觉位置」。

### 改动

- **更新页（`MainWindow.xaml` / `MainWindow.Updates.cs`）**：
  - 删除 `UpdateSubtext`（第二行说明）及其全部赋值；状态行改为 `StackPanel Orientation=Horizontal` + 圆点与文字双 `VerticalAlignment=Center`，三态只换 `Fill`。
  - 新增 `UpdateNoteHeader` 标题行：`UpdateNoteTitle` = `v{版本}`（`SectionTitle` 16 SemiBold），`UpdateNoteSize` = `{大小} MB`（12 灰、`VerticalAlignment=Bottom`、`Margin 8,0,0,2`），与更新状态联动显隐。
  - 卡片改为**两列 Grid**：左列是「状态行 + 说明标题行」的 `StackPanel`，右列按钮 `VerticalAlignment=Center`，从而按「卡片上沿→说明框上沿」整块居中。
- **「移除」按钮**：`ContactRemoveAction` 样式删掉 `ToolTip` Setter（两处使用点共用此样式），悬停只留配色。
- **高级页试听区**：改用 `FieldLabel`（12 灰）在控件上方标「音色」「文本」，块内边距 16/14、块间距 16；删掉文本行下方的 `Hairline` 与按钮行里的空占位，按钮在卡片内右对齐（`Row` + `Margin 0,2,0,6`）。
- **语音服务标题**：`ProvidersList` 加 `Margin="0,12,0,0"`，不再贴绿色分组头。
- **⋮ 菜单**：定位改为 `HorizontalOffset = 79.5 - menu.ActualWidth`（**右缘固定距窗口 16 DIP，加宽部分向左吃，再宽也不出框**），`VerticalOffset = -4`（与按钮留约 6px，不压阴影）；`RowMenu` 改 `MinWidth 0 + Width 112`、圆角 12、菜单项 `Padding 16`，实测 112×102 DIP（宽高比 ≈1.1）。
- `Directory.Build.props` → 1.5.1；`ReleaseHistory` 补 8 条。

### 坑

- **`RowSpan=2` 会把自动行撑高**：最初用 `Grid.RowSpan="2"` 让按钮跨两行居中，结果跨行子元素被 WPF 分摊高度，空行被撑到 18px，卡片 52→70、文字看着偏高 9 DIP。改两列 Grid 后 `probe` 实测偏差 -0.5 DIP。**不要改回 RowSpan**。
- **DPI 是 2.0，不是 1.5**：窗口 1880×1560 物理 = 940×780 DIP。早期几轮按 1.5 换算出的 106.7 / 23 / 127 DIP 全部作废，文档已按 2.0 重写。
- 菜单宽度与「不出框」本来是耦合的（⋮ 距窗口右约 95.5 DIP）。改成「右缘固定」后解耦，以后加菜单项不用重算。

### 验证与发布

- `dotnet build` 0/0；`Test.ps1` **.NET + Python 全 PASS**；`Verify-Source` PASS；58 项等价性 PASS（`tests/wx-multi/test_audited_wechat_keys.py` 用内置 Python 直跑，内置环境无 pytest）。
- `tools/UpdatePanelPreview`（同构渲染 + 像素测量）三态偏差 -0.5/-0.5/0.0 DIP；`tools/UiSmokeTest` 新增 `shot`/`probe`/`nohover`/`menu`/`menuclick` 模式，真机截图与真鼠标点击验证。
- 打包：`Publish.ps1` → `artifacts\win-x64-v1.5.1\` + ZIP 90,815,896 字节 / `6c43b41dddd0764b7084bb0ebc567818578d2cfae97e48f8e0ab01b0b5e65b1b`；`Build-Installer.ps1 -PayloadDirectory`（**不要传 -Output**）→ Setup 66,213,170 字节 / `cfd0482045045267b5bef2b3526a08c377f7e45517157529a8f3368f66fc4a3b`。Clean-payload PASS（1733 文件）。
- Muse：先 `status=info`（流水线接手），约 3 分钟后 `status=ok`（`publishedAt=2026-11-10T13:21:18Z`），称哈希链三方一致、旧清单备份 `latest.json.bak-1.5.0`。
- **独立公网复核**：`latest.json` 200、六字段齐全、version `1.5.1`、size 与 sha256 一致、notes 294 字中文正常；ZIP 与 Setup HEAD 200 且长度一致；**完整下载 ZIP 重算 SHA-256 一致**；本地 Setup 重算与回执一致。

---

## 1.5.0 · 2026-11-10（API 配置简化 + 音色失效语义 + 凭据指针修复 + 凭据库自动清扫）

> 状态：**全部完成并通过自验，待用户检视后决定是否打包推新**。不打包、不推线上、不更新桌面快捷方式。应用位置：`src\VoiceBridge.App\bin\Release\net8.0-windows\WeChatVoiceBridge.exe`。

### 用户要求（四条）

1. **API 配置界面优化**：同厂商多 Key 取消「未启用/待用/已启用/使用中」括号小字；右侧「启用/编辑/删除」多按钮合并为最右一个白色「启用」按钮（启用后绿色「使用中」+ 行首小绿点，未启用灰点）；「编辑/删除」并入「⋮」竖排三点菜单；全面删除面向开发者的说明性文字（「已启用 Fish Audio 2」「其他 Key 保持待用」「每款软件不预置任何服务密钥」等）。
2. **音色消失 bug**：新增同厂商 API 后首页联系人音色消失。规则：API 改动不影响所选音色时（增/切换/删非关键 Key）一律不消失、不换样式、不弹任何提示；仅当音色所属厂商的 Key **全部**删除时，音色变灰色斜体、点「开始」提示「音色已失效，请重新选择」、下拉框里名字仍可见但不可点选，仅有效音色可选。
3. **凭据指针覆写事故修复**：当日新构建启动后重写 settings.json 与 fish-keys.dpapi，Fish 服务 `CredentialId` 指向 32 位空壳凭据（`speech-2466c4b1…`）→ 401 试听失败。修复要求：迁移/启动加载**原样保留**「服务→凭据」映射，禁止占位值覆写；指针恢复到真钥匙条目；自验 = 重启后指针不变 + 实际试听成功。
4. **凭据库自动清理规矩**：`fish-keys.dpapi` 只保留仍被引用条目（引用 = 任何服务 `CredentialId` + 顶层 `FishKeyName` + 联系人/试听凭据覆盖）；两个时机：①启动加载并完成全部配置迁移**之后**清扫（顺序不能反，防误删迁移目标）；②软件里删除服务时连带清掉因此无引用的条目；本次会话新建、未挂到服务的条目豁免当次清扫、下次启动再判；只删无引用条目，删几条记数到日志。

### 改动清单

- **UI（要求 1）**：`MainWindow.Library.cs` 的 `ProviderRow` 简化为 `Id/Name/Kind/IsActive`；行模板 = 左侧 9px 圆点（`#AEAEB2` 灰 / `#1D9A5B` 绿，`IsActive` 触发）+ 名称 + 白色「启用」按钮（`App.xaml` 新 `ProviderEnableButton`，ControlTemplate 内 Veil 覆盖层实现 hover，绿色态 hover 不褪色）+「⋮」按钮（`ProviderMoreClick` 动态 ContextMenu = 编辑/删除）；`ActivateProviderClick` 去掉 `NeedsActivation` 门槛与提示文案；删除「我的音色」「语音服务」脚注与 `VoiceHint` 块。
- **音色语义（要求 2）**：`VoiceLibraryMigration.ActiveKeyFor` 加同厂商 fallback（厂商还有任意 Key 即可用；全部删光才失效）+ `IsUsable`；`MainWindow.Library.cs` `RefreshLibrary` 重写为**就地同步** `SyncVoiceChoice`（不再 `voiceChoices.Clear()` —— 清空重建导致 WPF `Selector` 选中态重置，是音色消失的根因）；`ContactCard.cs` `VoiceChoice` 由 record 改为可变类 + `INotifyPropertyChanged`；`MainWindow.xaml` 新 `VoiceChoiceItemTemplate`（失效灰色斜体）与 `VoiceChoiceItemStyle`（`IsEnabled` 绑 `Usable`，名字保留在下拉但不可点选）；三处音色 ComboBox 切换到该模板。错误文案统一为「音色已失效，请重新选择。」；UserGuide 两处同步改写。
- **凭据指针（要求 3，根因两处）**：① `ProfileEditors.Save` 旧逻辑每次输入新密钥都铸 `"speech-" + Result.Id` 新名、把用户真钥匙名顶掉 → 改为仅在无既有名时铸名，否则保留 `Result.CredentialId`；② `KeyStore.Load` 在 `fish-keys.dpapi` 不可读时曾落入 legacy 导入、整库覆写 → 改为**已存在就绝不写**（读不出返回空、文件原样）。`KeyStore.Replace` 新增为唯一受护栏的写入口（空名/空密钥永不落盘）。`EditProvider` 改走 `KeyStore.Replace`。
- **凭据清扫（要求 4）**：新 `src/VoiceBridge.Core/CredentialSweep.cs` 纯函数（`ReferencedNames` 收集三类引用源；`UnreferencedEntries` 出删除名单，引用与会话新建永不入选）；`LocalSettings.KeyStore.Load(out createdThisLoad)` 上报 legacy 导入新建名；`MainWindow.xaml.cs` 新 `sessionNewKeys` 会话豁免集（`AddKeyClick`/`EditProvider` 新建名登记，`DeleteKeyClick` 注销）+ `SweepCredentials(when, providers, keyName, contacts)`（删无引用条目 → `KeyStore.Save` → `AppLog.Write` 记条数）；启动时在 `ReadStoredSettings()`（全部迁移）与 `KeyStore.Load()` **之后**清扫一次（`"启动"`）；`DeleteProviderById` 在摘除服务后清扫一次（`"删除服务"`）。新 `AppLog.cs` → `%LOCALAPPDATA%\WeChatVoiceBridge\app.log`（只记条数与事件名，128KB/200 行封顶，写失败不阻塞）。`DeleteKeyClick` 守卫从「被启用的服务使用」收紧为「被任何服务引用」。
- **版本**：`Directory.Build.props` → 1.5.0；`ReleaseHistory.cs` 新 1.5.0 条目（四项：界面精简、音色不无故消失、凭据指针修复、凭据库自动清理）。
- **工具**（`tools/`，不入发布物）：`KeyStoreInspector`（只读列条目名/长度/指纹）、`CredentialRepair`（`probe`/`repair <name>`/`verify`，本轮加 `inject <name>` 塞测试孤儿条目）、`UiSmokeTest`（UIA 驱动真实窗口：高级→试听这段文字→首页→读「试听完成。」）。

### 事故记录（凭据指针）

当日早间构建启动后把 `settings.json`/`fish-keys.dpapi` 重写、服务指针被占位值顶掉。用指纹分组 + 真实 Fish 调用辨认真钥匙：51 位 `fp=68BD1117` 组（`默认` 等 5 份拷贝）HTTP 200 为真钥匙；`fp=240975F2` 与 32 位空壳 401。`CredentialRepair repair speech-d1ee2ca3106c44d1af5b91b674944451` 恢复指针（备份 `settings.json.bak-20261110-112909`），并按上文修掉两处根因。教训：修复指针必须在应用**未运行**时做——旧构建退出时会把内存里的坏指针写回。

### 验证（全部通过）

- `dotnet build -c Release` 0 警告 0 错误；产物 `WeChatVoiceBridge.exe` 版本 **1.5.0.0**。
- `scripts/Test.ps1` PASS（.NET + Python）；直接 `dotnet test`（含 WVB_PYTHON）**211/211**（205 旧 + 6 新清扫测试）；`Verify-Source.ps1` PASS；58 项等价性自检 PASS。
- **清扫自验（用户指定）**：`CredentialRepair inject orphan-sweep-c/d` 塞两条无引用条目 → 重启应用 → 钥匙库 16 条只剩被引用的 `speech-d1ee2ca3…` 一条，两条孤儿被清；`app.log` 记「凭据清扫（启动）：删除 2 条无引用条目。」（首轮全库清扫记「删除 15 条」）。
- **试听自验**：`UiSmokeTest` 驱动真实窗口（高级→试听这段文字）→ 提示「**试听完成。**」exit 0；试听后 `settings.json` 指针不变（`CredentialId`/`FishKeyName` 均 `speech-d1ee2ca3106c44d1af5b91b674944451`）。
- 清扫删除服务路径由 `CredentialSweepTests.DeletingAServiceSweepsOnlyTheEntriesItOrphaned` 等 6 例覆盖（共享指针只清孤儿、待用 Key 不清、会话新建豁免、空白指针安全）。

### 待用户裁决

- 用户检视 `src\VoiceBridge.App\bin\Release\net8.0-windows\WeChatVoiceBridge.exe`；合格后再谈打包推新（届时走 Muse）。

---

## 1.4.9 · 2026-11-10（wx-multi 多版本适配合并 + 更新页 UI 优化）

> 本段是**断点续做的活文档**：每完成一部分就在这里追加，接手者先看这里与 `.workbuddy/memory/2026-11-10.md`。用户本轮指令原文要点见 `PROJECT-BRIEF.md` §7。

### 用户本轮要求（逐条）

1. 先完善文档型内容，保证没读过聊天记录的新模型仅凭文档+代码就能熟悉项目与要求；随时维护交接文档与工作执行记录，每完成一部分就及时留下文案记录。
2. 先查看 Git 是否上传好，没问题再继续。
3. 把 `wx-multi-handoff\package-v1\` 合并进 `WeChatVoiceBridge.sln`：先通读包内 `README.md`；**合并清单以 `docs/review-crouton.md` §4 为准绳**；修复说明看包内 `docs/fix-round1.md`。
   - 只支持微信 **4.1.11–4.2.0.13**，范围外必须走包内已实现的 `unsupported_version` 通道，不许静默失败或乱读数据。
   - 密钥掩码与原切片逐字节一致，不许改动原切片行为；**58 项等价性自检合并后仍要成立**。
   - 不许回退 1.4.7 / 1.4.8 已上线改动：无出厂默认 API、无内置音色、1.4.8 安装引导（装完不自动弹新版，点「完成」或关闭引导才打开）。
4. 更新进度条页面删掉「点确定才会进入更新应用」这类啰嗦解释文案；下载后绿色安装按钮上下不够居中、版本更新文字与按钮区域不够美观，继续优化布局；**做好保留截图**。
5. 合并完先 `dotnet build` 必须编译通过无报错；**先不用打包**，用户觉得合格后打包、再推新；告诉用户新版软件位置；**桌面快捷方式先不用更新**。

### 已完成

- **Git 核对**：`master` = `origin/master` = `git ls-remote` = `c917c6c26b7f9aa6d2198fa58d05de1d7ce073a5`，0 ahead / 0 behind，工作区干净。`c917c6c` 是 Muse 代提交的 release-state snapshot，把整个 `wx-multi-handoff/package-v1/` 纳入了仓库。
- **文档基线**：新建 `docs/PROJECT-BRIEF.md`（自包含项目总览：项目结构、环境命令、发布纪律、版本状态、三条不可回退红线、技术约定、当前任务准绳、接手检查清单）；`HANDOVER-CURRENT.md` 顶部加「§0 当前正在做的任务」并指向总览；建立当日 `.workbuddy/memory/2026-11-10.md`。
- **通读成果包**：`README.md`、`merge-notes-for-workbuddy.md`、`docs/review-crouton.md`（含 §2.2 合并配方与 §4 清单）、`docs/fix-round1.md`（R1–R8 修复对照）、`docs/impl-summary.md`（实现总结与 58 项构成）、自检脚本全文、项目与包内 `WeChatSession.cs`、`worker/audited_wechat_keys.py`。
- **差异核对（关键结论）**：包内 `impl/` 的 4 个文件相对当前项目**仅多出 version 透传与错误码映射**（`wechat_adapter.py` 11 行、`main.py` 9 行、`PythonFixtureSource.cs` 2 行、`WeChatDirectory.cs` 2 行），直接替换不会回退 1.4.x 改动；`worker/audited_wechat_keys.py` 当前是 8.7KB 的「原切片」，包内是 35KB 的多版本化重写。自检脚本硬编码 `ROOT/slice/worker/...` 与 `ROOT/out/impl/worker/...`，成果包里没有这两个目录，合并后必须把自检改造成指向项目真实文件的形式才能长期复跑。

### 合并主体已完成（本段第一小节）

- **落地文件**：`worker/audited_wechat_keys.py`（35KB 多版本化重写，替换原 8.7KB 切片）、`worker/wechat_adapter.py`（`WeChatStore(..., version=...)` → `WeChatDB(version=version)`）、`worker/main.py`（start 包透传 version + 显式捕获 `UnsupportedVersionError` → `code="unsupported_version"` 并保留中文 message）、`src/VoiceBridge.WeChat/PythonFixtureSource.cs` 与 `WeChatDirectory.cs`（start 包 `version` 字段 + code switch 新增中文「不支持此版本」文案）。
- **原切片冻结**：`tests/wx-multi/slice/audited_wechat_keys.py`（8,704 字节，逐字节复制自合并前的 `worker/audited_wechat_keys.py`，**禁止编辑**，作为 58 项等价性基线）。
- **自检落位**：`tests/wx-multi/test_audited_wechat_keys.py` 由包内自检改造，路径改为指向**真实生产文件**：`IMPL=worker/audited_wechat_keys.py`、`MAIN=worker/main.py`、`SLICE=tests/wx-multi/slice/...`，这样 58 项门禁长期守护实际 shipped 代码，而不是包内副本。
- **`WeChatSession.cs` 按 review §2.2 合并**：基底 impl 版（`MinSupportedVersion=4.1.11`、`MaxSupportedVersion=4.2.0.13`、`IsSupportedVersion`、`Version` 字段、`Bind` 区间判定与中文拒绝文案），并入 discovery 版 `RootAnchorName`/`ScanMaxDepth=6`/`ScanBudget=20s` 与 `DiscoverRootByScan`/`EnumerateFixedDrives`/`ScanDirectory`，`DiscoverRoot()` 末尾改 `return DiscoverRootByScan();`。**未使用 discovery 的单版本 `SupportedVersion` 锁**。另新增只读 `DetectClientVersion()`（不碰数据库/内存/账号目录）供宿主版本门控使用。
- **R5 编译期修复（包内未编译验证的两处 CS8602）**：`module` 可能为 null、`version.ToString()` 可能为 null。改为 `if (module is null || !IsSupportedVersion(parsed)) throw ...`，把「读不到版本」也归入明确拒绝；通过后用 `parsed!.ToString()`。行为不变，绝不静默乱读。
- **R6 破坏点同步**：`tests/VoiceBridge.Tests/ContactIndexCacheTests.cs` 两处 5 参数构造（含一处 target-typed `new(...)`）补 `"4.2.0.13"` 版本参数；全仓 grep 确认无其它 `new WeChatSession(` / `SupportedVersion` 引用。
- **复验结果**：
  - `tests/wx-multi/test_audited_wechat_keys.py` **58/58 PASS**（`ALL SELF-CHECKS PASSED (58 checks)`），含 R1 错误映射、R2 掩码单值、R3 空版本、R4 严格解析、49 项 slice 等价性（常量、HMAC 200 组、合成内存场景 A/B/C、拒绝版本零句柄）。
  - ⚠️ **必须用项目内置 Python 3.12.10 跑**：`artifacts/win-x64/runtime/python/python.exe tests/wx-multi/test_audited_wechat_keys.py`。系统 Python 3.13 会被 `main.py` 的 `sys.version_info[:2] != (3, 12)` 检查提前挡下，导致 R1 误报失败（第一次跑就是踩了这个坑，非代码问题）。
  - `python -m py_compile` 三个 worker 文件全过；`dotnet build WeChatVoiceBridge.sln -c Release` **0 警告 0 错误**。

### 版本门控弹窗与目录兜底（已落实）

- **范围外版本弹窗**：新增 `src/VoiceBridge.App/VersionGate.cs`（`ShowUnsupportedVersion(detected, min, max)`），用既有 `DialogShell` 风格弹窗，显示「检测到的微信版本：X」+ 支持区间 `4.1.11`–`4.2.0.13` + 两按钮「更新微信」（打开官网下载页）/「退出应用」。`App.xaml.cs` 在 `ContentRendered` 时若宿主不在更新交接模式下就先调 `WeChatSession.DetectClientVersion()`，范围外则弹窗，点「退出应用」设 `ShutdownMode` 并 `Environment.Exit(0)`。
- **目录兜底（已满足）**：MainWindow 数据目录行本就有「浏览…」+ `OpenFolderDialog` + 自动保存，`WeChatSession.DiscoverRoot()` 扫描失败时回退到手动选择，无需新增代码。

### 更新页 UI 优化（已落实 + 截图）

- **删啰嗦文案（只删文案、1.4.8 行为不动）**：`UpdateInstallationWindow.cs` 两处解释（进度中「安装完成后，点『完成』或关闭本窗口才会打开新版本」、完成态「点『完成』或关闭本窗口后，才会打开新版本」）改为中性短句；`UpdateProcess.cs` 失败回执与 100% 进度文案去掉「等待确认」「暂未确认启动成功」等重复说明。
- **绿色按钮 + 版本文字重新布局**：`MainWindow.xaml`「软件更新」行用 `DockPanel` 把状态点 + 两行文字（`UpdateVersion` 主 + `UpdateSubtext` 副）固定在左侧、按钮固定 `MinHeight=36` 钉在右侧，整行 `VerticalAlignment=Center`；三种状态（灰/薄荷描边/实心绿）共用同一套 36 高度、`Padding=20,0,20,0`、`FontSize=14`，文字垂直居中。修复了一处历史隐患：`App.xaml` 默认按钮模板的 `ContentPresenter` 显式绑定 `VerticalContentAlignment`（否则隐式 TextBlock 样式会把字压成深色、破坏白字）。
- **「版本说明」区块重设计（用户第二轮反馈：按钮下面那段文字不美观）**：原先 `UpdatePackage` 行就是「`v1.4.9 · 89.5 MB` + 一大段密排正文」直接堆在按钮下方，无分段、无层次。改为：
  - 尺寸行右侧加灰色小标签「更新内容」，让这块读起来是一个带标题的小节，而不是状态行的附属段落；
  - 说明放进浅色圆角内嵌面板（`#F6F9F7` 底 / `#E7EEE9` 边 / 圆角 10 / 内边距 12,10）；
  - 每条更新一行、左侧 5px 绿色圆点 + 12px 正文 + `LineHeight=19`，由 `MainWindow.Updates.cs` 新增的 `FillNotes()` 按 `\n` 拆分生成，长说明可扫读；
  - 去掉内层 `MaxHeight=132` 滚动框：整页本来就在 `ContentScroller` 里，硬切只会把最后一行截成半截。说明为空时连面板一起隐藏（`UpdateNotesBox`）。
  - 原 `UpdateNotes`（TextBlock）已由 `UpdateNotesPanel`（StackPanel）+ `FillNotes()` 取代，全仓无其它引用。
- **第三轮微调（用户看图后）**：① 删掉 `v1.4.9 · 89.5 MB` + 「更新内容」标签整行（`UpdateSize` 控件移除，体积信息改挂 `UpdatePackage.ToolTip`，不再占版面）；说明面板现在是该区块唯一内容、去掉上边距。② **「状态点全变绿」是预览工具的 bug，不是应用回归**——真机三色一直是对的：`MainWindow.xaml.cs` 里 `IdleDot=#AEAEB2`（灰，无更新）/ `WarnDot=#B2760A`（琥珀，发现新版）/ `LiveDot=#1D9A5B`（绿，可安装），`RenderUpdate()` 按 `manifest/verifiedUpdate` 三态赋值。之前截图三态都填了 `Positive` 绿色，已改为按真实色值渲染，并加画「已是最新」灰点态做对照。
- **截图（已生成，供用户检视）**：
  - `artifacts/screenshots/update-ui-states.png` —— 三态并排（已是最新 / 发现新版本 / 下载完成绿色按钮）。
  - `artifacts/screenshots/update-install-green.png` —— 绿色按钮特写：白字、垂直居中、行内点+两行文字对齐。
  - `artifacts/screenshots/update-panel-with-notes.png` —— **按钮 + 下方版本说明整块**，三态上下对照（灰点已是最新 / 琥珀点发现新版 / 绿点可安装），按真实 660 内容宽渲染，用于判断「按钮与下面文字」是否协调。
  - 预览工具：`tools/UpdatePanelPreview/`（复用 `App.xaml` 真实样式资源，按 660 宽离屏渲染；`tools/UpdateNotesPreview/` 是更早那版「更新区 + 版本介绍对话框」合成图工具）。截图属本地产物、被 `.gitignore` 忽略，不入库；工具源码入库。

### 收尾：版本推进到 1.4.9（未发布）

- `Directory.Build.props` 版本 `1.4.8` → `1.4.9`（版本唯一来源）；`ReleaseHistory.cs` 顶部新增 `1.4.9` 条目（多版本适配 + 更新页优化，并显式声明不回退 1.4.7/1.4.8 三条改动）。复跑 `dotnet build -c Release`：**0 警告 0 错误**，DLL 内嵌版本字符串已为 `1.4.9`。
- **新版软件位置（仅供检视，尚未打包/上线）**：`src/VoiceBridge.App/bin/Release/net8.0-windows/WeChatVoiceBridge.exe`（同目录含 `WeChatVoiceBridge.dll` 与全部依赖）。桌面快捷方式**保持 1.4.7 r3 不动**。

### 全量回归（合并 + UI 全部完成后）

- `scripts/Test.ps1`：构建 0 警告 0 错误；**C# 197/197 PASS**；Python **54 通过**（41 个需 Windows 内置 Python+SQLCipher 的 live 测试按约定跳过，属正常）。
- `scripts/Verify-Source.ps1`：**PASS**（静态边界 + 明显密钥扫描）。
- `tests/wx-multi/test_audited_wechat_keys.py`（用 `artifacts/win-x64/runtime/python/python.exe`，即内置 Python 3.12.10）：**58/58 PASS**，含 R1 错误映射、R2 掩码单值、R3 空版本、R4 严格解析、49 项 slice 等价性。
- 结论：**未回退 1.4.7/1.4.8** 任一改动（无出厂默认 API、无内置音色、1.4.8 安装引导需用户确认才显示新版）。

### 用户裁决后下一步（尚未做）

- [ ] 用户检视构建产物 + 截图，认为合格后 → 打包：`scripts/Build-Installer.ps1` / `Publish.ps1` 生成 1.4.9 的 ZIP + Setup，并跑出厂门禁 `Verify-Source.ps1`（PASS + 1619 files 无 per-user 数据/无 live API key）。
- [ ] 打包后联系 Muse 走发布单推新（`-Version 1.4.9` + 新 SHA256 + 中文 notes），不重发旧包、不自行上传。
- [ ] 用户实机点更新，目视确认 1.4.8 安装引导行为 + 1.4.9 更新页三态布局。

---

## 1.4.8 · 2026-11-09（更新完成后由用户决定何时打开新版）

用户先解除上线闸门：「检查一遍没有问题之后，推到线上去就好。仔细测试一遍，没有问题之后，推到线上去就好了。要去联系 muse推新版本哈」。随后补报一个更新引导窗口的 bug：**「更新有那个安装引导进度条然后到百分百就可以点确定，要改成就是如果点确定或者叉，才会开始弹出新更新的应用页面然后这个安装引导的界面消失……不点确定，不会自己弹出来新更新的应用界面」**，并要求「你看现在是不是这样 不是的话优化成这个样」；沟通由我代劳：「你跟 muse 沟通就好」。

**核对结论：原行为确实不符合要求**。`UpdateInstaller.Apply()` 在文件替换完成、进度 95% 时无条件调用 `restart(exe)`；`UpdateProcess.Restart()` 启动新版进程并以 `MainWindowHandle != IntPtr.Zero` 作为「启动确认成功」的判据，于是新版主窗口在用户点「完成」之前就自己弹出；窗口文案还写着「完成后会自动打开新版」「新版本已启动，可以关闭此窗口」。

### 设计取舍

不能直接把 `restart(exe)` 删掉：它同时承担「新版程序确实加载成功才提交更新，加载失败恢复旧版本并保留备份」的保护。因此把**启动确认**与**显示窗口**解耦：新版进程照常启动、但只写 `ready` 信号并保持主窗口隐藏；安装引导窗口在用户点「完成」或右上角叉触发 `OnClosing` 时写 `go`；新版进程轮询到 `go`（或 helper 进程异常消失的兜底）才显示主窗口并清理信号。回滚路径仍用不带 handshake 的旧启动方式，老客户端 5 参数 `--update-result` 行为不变。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.Core/UpdateHandoff.cs`（**新建**） | 跨进程握手协议：`--await-handoff <ticket>`、`LaunchArguments`、ticket 只允许 32 位 Guid `N` 格式（不能逃逸 updates 目录）、`SignalReady()` / `SignalGo()` / `IsReady()` / `IsGo()` / `ShouldShowMainWindow() => IsGo()` / `Cleanup()`；信号用 `.tmp` + `File.Move(overwrite)` 原子写，路径固定在 `%LOCALAPPDATA%\WeChatVoiceBridge\updates`，helper 与新版进程共用 |
| `src/VoiceBridge.App/UpdateProcess.cs` | `Apply(string[] args, UpdateHandoff handoff, ...)` 增加握手参数；`Restart(..., UpdateHandoff? handoff = null)` 成功安装时附加 `--await-handoff <ticket>`，改以 `handoff.IsReady()` 判定启动成功，失败回滚仍用 `MainWindowHandle` 老判据；`ReadRestartNotice` 兼容 5 与 7 参数；新增 `HelperStillRunning(args)` 供 helper 消失兜底；100% 文案改「更新完成，等待确认后打开新版本」，未确认路径不再 `SignalGo()` |
| `src/VoiceBridge.App/App.xaml.cs` | 主窗口创建后若发现 handshake 则走 `AwaitInstallationConfirmation()`：`ShutdownMode.OnExplicitShutdown`、`SignalReady()`、120ms `DispatcherTimer` 轮询 `go` 或 helper 退出，再 `RestoreAndShow()` 恢复 ShutdownMode / 清理信号 / `Show()` + `Activate()`；`SingleInstance` 二次启动激活回调在等待期直接 return，防止绕过隐藏；握手写失败等异常一律兜底显示，不让应用永久不可见 |
| `src/VoiceBridge.App/UpdateInstallationWindow.cs` | 新增 `handoff = UpdateHandoff.Create(UpdateProcess.DirectoryPath)`；`OnClosing` 在安装忙时照旧阻止关闭，完成后（点「完成」或叉）写 `go`；初始与成功文案改为「点「完成」或关闭本窗口后才会打开新版本」，成功时按钮显示「完成」；`InstallAsync` 把 handshake 传入 `UpdateProcess.Apply` |
| `tests/VoiceBridge.Tests/UpdateHandoffTests.cs`（**新建**） | 18 例：初始无 `ready`/`go`、`ready` 后仍 `ShouldShowMainWindow()==false`、`go` 后才允许显示、`LaunchArguments` round-trip、老 5 参数无握手、非法 ticket 不创建、ticket 唯一且不能逃逸、`Cleanup()` 只清自己的信号、信号幂等不留 `.tmp`、空目录抛 `ArgumentException` |
| `Directory.Build.props` | `1.4.7` → `1.4.8`（Muse 建议旧包不再复用、另起版本号，已采纳） |
| `src/VoiceBridge.App/ReleaseHistory.cs` | 新增 1.4.8 条目（4 条），说明 100% 后不自动弹出、必须由用户确认、保留启动确认与失败回滚 |

### 验证（版本改动后重跑）

- Release 全量构建 **0 警告 0 错误**；`Test.ps1` **197/197 C# PASS** + **54/54 Python PASS**（41 个需 SQLCipher 运行时的跳过属既有基线）。
- `Verify-Source.ps1` **PASS**；`Publish.ps1` 与 `Build-Installer.ps1` 出厂门禁各一次，均 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`。
- 产物内容：ZIP 1621 项，`WeChatVoiceBridge.dll` / `VoiceBridge.Core.dll` / `WeChatVoiceBridge.exe` 与发布目录逐字节一致；EXE `ProductVersion=1.4.8+<commit>`、`FileVersion=1.4.8.0`、Setup `ProductVersion=1.4.8`；出厂 DLL 含 `--await-handoff` / `UpdateHandoff` / `ShouldShowMainWindow` / `SignalGo` / 1.4.8 版本说明，旧的「新版本已启动，可以关闭此窗口」文案已消失，`VoicePresets` / `SyncDefaultFish` 等禁用符号仍未回流。
- 静态行为链复核：握手目录固定（helper 与新版进程一致）、7 参数命令行可 round-trip、`FindProcess` 用 PID + 启动时间 ticks 校验、安装窗口中「完成」按钮 `Click => Close()` 与叉都汇入 `OnClosing`。⚠️ **跨进程 WPF 实机窗口隐藏未做目视验证**：需要真实跑一次升级，等 1.4.8 上线后由用户点更新实测（桌面快捷方式刻意保持 1.4.7，使线上版本严格大于桌面版本）。

### 产物（本地实算，供 1.4.8 发布单）

| 文件 | 字节 | SHA-256 |
| --- | --- | --- |
| `artifacts\WeChat-Voice-Bridge-V1.4.8-win-x64.zip` | 89,464,301 | `A4CA75B3C4F9EECB2D4BADB291161165FE6A7B9107F5343F678736D3E6CCB1C0` |
| `artifacts\WeChat-Voice-Bridge-V1.4.8-Setup.exe` | 65,302,086 | `A9FB702F06EC212C17E80B5F86FBE042734345769D92B6B68F11AB786FA3128C` |

发布目录 `artifacts\win-x64-v1.4.8`，r3 的 1.4.7 产物保留不删。

### 发布状态与 Muse 沟通（工程记录）

- 1.4.7 发布单 `codex_20261109_230425_438f4b` 正式回执为 `error`：`publish-voicebridge 1.4.7 REFUSED at public_verify`，公网清单 `GET` 连续 3 次 403（Caddy），流水线把清单回滚到 1.4.6 并声明未发布任何文件。不盲目重发。
- 暂缓通知 `codex_20261109_231021_676940` 最终 `ok`：旧包不再上传、不再翻清单；同时揭示 A1 服务器上 22:13:01 UTC 有一次**流水线之外的手工翻版**，服务器 `latest.json` 当时是 1.4.7 旧包。已明确回复 Muse：手工翻版不是我做的，我没有 A1 登录或 sudo 能力，请他以 A1 侧日志与文件时间为准继续查。
- 策略消息 `codex_20261109_233510_8cda41` 回执 `info`：Muse 确认①查手工翻版来源 ②清单保持现状等 1.4.8 覆盖 ③采纳 1.4.8、桌面保持 1.4.7 ④正在修 Caddy 403 ⑤**修好并公网验证通过后通知我，再发全新 1.4.8 发布单**。
- 22:4x UTC 我本地实测 `GET/HEAD latest.json` 已恢复 **200**，但内容仍是那份手工翻版 1.4.7（version=1.4.7 / publishedAt=2026-11-09T22:20:00Z / sha256=f2f58514… / size=89462225）。已再发消息 `wvb-muse-148-ready.txt` 告知 403 恢复、附上 1.4.8 两个产物的实测哈希与字节数，请 Muse 放行；**未自行上传、未绕过 Muse 通道、未重发 1.4.7**。
- 桌面快捷方式保持 1.4.7 r3 不动，目的就是让线上 1.4.8 严格大于桌面版本，用户能亲自点一次更新、目视确认新的引导窗口行为。

### 上线结果（2026-11-09 22:59 UTC）

- 策略消息 `codex_20261109_234635_779950` 的**正式 `ok` 回执**：Muse 独立两次实测 `GET/HEAD latest.json` 均 200（22:48:14、22:51:00 UTC，760 字节内容一致），确认 403 已恢复，放行发全新 1.4.8 发布单；清单保持现状不动、不来回翻，旧 1.4.7 包不上。
- 发布单 **`codex_20261109_235238_857190`**（`-Type publish-voicebridge -Version 1.4.8`）：`package_path` 指向 1.4.8 ZIP、`sha256=a4ca75b3…`、`package_size=89464301`（脚本自动实算）、中文 `notes` 无乱码。先回 `info`（常驻流水线接手：哈希/版本核验 → 分块上传 → 拼装验哈希 → 翻清单 → 公网复核），随后**正式 `ok`**：1.4.8 已发布完成，ZIP 哈希链三方一致（申报=PC 实测=A1 落地），Setup.exe 一并发布，`latest.json` 更新为 1.4.8，旧版备份 `latest.json.bak-1.4.7`。
- **我方独立公网友核（不只信回执）**：`GET latest.json` → 200 / 1093 字节，六字段 `version, publishedAt, url, sha256, size, notes` 齐全；`version=1.4.8`、`publishedAt=2026-11-09T22:59:27Z`、`url=…/WeChat-Voice-Bridge-V1.4.8-win-x64.zip`、`sha256=a4ca75b3c4f9eecb2d4badb291161165fe6a7b9107f5343f678736d3e6ccb1c0`、`size=89464301`，与本地实算完全一致；中文 notes 正确无乱码。ZIP 与 Setup 均 `HEAD 200`，`Content-Length` 分别为 89,464,301 / 65,302,086。
- **下载级哈希终验**：把线上 ZIP 与 Setup 完整下载后逐字节计算，ZIP `89,464,301` 字节 / `a4ca75b3c4f9eecb2d4badb291161165fe6a7b9107f5343f678736d3e6ccb1c0`、Setup `65,302,086` 字节 / `a9fb702f06ec212c17e80b5f86fbe042734345769d92b6b68f11ab786fa3128c`，**size 与 hash 均与本地产物一致**（临时副本已删除）。
- **手工翻版来源（Muse 最终结论，工程记录）**：A1 `auth.log` 显示 22:13:01 UTC 有人执行 `sudo cp` 清单 → `latest.json.bak-1.4.6`，随后 `sudo mv /tmp/latest-147.json` → `latest.json`；该文件名不是流水线用的 `/tmp/latest-new.json`，流水线日志里也没有这一笔。我的否认（无 A1 登录/sudo 能力）已记录，现有日志无法指认操作者，Muse 不臆测并已报给用户。
- 至此**线上正式版为 1.4.8**。桌面仍是 1.4.7 r3，客户端应出现更新按钮；请用户点一次更新实测：进度到 100% 后新版主窗口不自动出现，点「完成」或叉后引导界面消失、新版窗口打开。这是本轮唯一未做实机目视验证的部分。

---

## 1.4.7 r3 · 2026-11-09（删 API 后保留厂商名换样式 + 同厂商多 Key 自选启用）

> 仍在同一未发布版本号 1.4.7 上迭代；**线上仍是 1.4.6，本轮未上线、未联系 Muse**。上一节的 r2 产物哈希作废，以本节为准。本轮按用户明确指令**更新了桌面快捷方式**（指向 r3），此前「留在 1.4.5 体验自升级」的约定就此结束。

用户指令三点：① 删除 API 后该厂商音色仍保留厂商名、但换成缺失样式，编辑与删除按钮照常可用；② 同一厂商有多个 Key 时多一个「启用」按钮选择用哪个，不同厂商各可启用一个，只有同厂商 ≥2 个 Key 才出现该按钮；③ 添加第二个/第三个 Key 只添加、不自动应用，原来在用的保持不变。并补充事实前提：**Fish 的音色 ID 是平台公共的，任何有效 Key 都能跑**。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.Core/SpeechProfiles.cs` | `VoiceProfile` 新增可空 `ProviderKind`（缓存厂商）。删掉连接后音色仍知道自己属于哪个厂商 |
| `src/VoiceBridge.Core/VoiceLibraryMigration.cs` | 新增 `VendorOf()` / `CacheVendor()` / `UseActiveKey()`；`BindOrphanVoices()` 增加厂商保护（已知厂商与目标不同绝不改绑）并在绑定时写入 `ProviderKind` |
| `src/VoiceBridge.Core/ProviderActivation.cs`（新增） | 三条规则下沉到数据层便于单测：`StaysOnStandby()`（同厂商已有启用 Key 时新 Key 待用）、`NeedsActivation()`（同厂商 ≥2 个 Key 才显示按钮）、`Activate()`（只切换同厂商 Enabled，其它厂商原样返回） |
| `src/VoiceBridge.App/LocalSettings.cs` | `Load()` 在 provider/音色迁移后调用 `CacheVendor()`，趁连接还在把厂商写进老音色，发生变化时 `Save()` 一次；DPAPI 凭据库仍不读不写不删 |
| `MainWindow.Library.cs` | `LibraryRow` 加 `ProviderMissing` / `ProviderHint`，`ProviderRow` 加 `ShowActivate` / `CanActivate` / `ActivateLabel`；`RefreshLibrary()` 按「厂商名 + 是否可用」生成分组与状态；新增 `VendorName()` / `ActiveProviderFor()`；`ChooseContactVoice()`、`SpeechFor()`、`RefreshContactSpeech()` 改为按厂商回退到启用的 Key（`SpeechFor` 用 `voice with { ProviderId = provider.Id }` 满足 `Validate` 的归属校验） |
| `MainWindow.Library.cs` `EditProvider()` | 新增服务若 `StaysOnStandby()` 为真则 `Enabled=false`（添加不应用）；孤儿音色只绑到该厂商**实际启用**的 Key |
| `MainWindow.Library.cs` `DeleteProviderClick()` / `ActivateProviderClick()`（新增） | 删除前把厂商缓存到音色；同厂商若还有 Key，音色立即跟随仍可用/自动启用的那个；点「启用」用 `ProviderActivation.Activate()` 只切同厂商，并 `UseActiveKey()` 让该厂商音色跟随 |
| `ProfileEditors.cs` | `ProviderEditor.Save()` 不再强制 `Enabled=true`（编辑待用 Key 不会偷偷启用）；`VoiceEditor` 在原服务已删时插入「（原服务已删除）」占位选项，编辑弹窗可正常打开、保存并保留原绑定；若有其它服务，把服务选择框移到详情页实现真正改绑 |
| `MainWindow.xaml` | 「我的音色」行用 `DataTemplate.Triggers` + `DataTrigger ProviderMissing`：名称灰化斜体、显示警示状态标签，编辑/删除按钮不变；「语音服务」行新增「启用」按钮（`ShowActivate` + `BoolToVis`，已启用显示「使用中」并禁用）；两处说明文案同步 |

### 三条规则的确切语义

- **同厂商互斥、跨厂商独立**：`Activate()` 先按 Id 找到目标，只改写 `Kind == target.Kind` 的条目；其它厂商的对象原样返回（`Assert.Same` 单测锁定）。只有一个 Key 的厂商没有按钮、也不需要选择。
- **添加不应用**：第一个 Key 默认启用；此后任何新增 Key 在同厂商已有启用 Key 时一律 `Enabled=false`，包括用户手动切到第二个后再加第三个。
- **任意 Key 都能跑音色**：音色只存 Voice ID（合成时是 `reference_id`），鉴权用启用 Key。切换启用 / 删除某 Key 后，只要同厂商还有启用的 Key，`SpeechFor()` 就自动回退过去，无需手工改绑；只有该厂商一个 Key 都不剩时才显示缺失样式。

### 验证与产物

- Release 构建 **0 警告 0 错误**；`Test.ps1` **179/179 C# PASS**（新增 `ProviderActivationTests` 12 个）+ **54/54 Python PASS**；`Verify-Source.ps1` **PASS**。
- 新增单测覆盖：第一个 Key 启用、加第二/第三个不切换、跨厂商各启用一个、激活幂等、未知 Id 无副作用、删除连接后厂商名仍在、老 JSON 无厂商字段时回填并可反序列化、同厂商音色跟随、跨厂商音色零损伤、孤儿音色不跨厂商误绑。
- 静态结构核查：从真实 `MainWindow.xaml` 抽取两个 `DataTemplate`，核对所有 `{Binding ...}` 路径都能在 `LibraryRow` / `ProviderRow` 字段中解析，`DataTemplate.Triggers` 的 `TargetName` 与 `x:Name`（`VoiceName` / `VoiceHint`）一一对应，启用按钮的事件与 `BoolToVis` 转换器齐全。⚠️ 本轮**未做运行时隔离 WPF 渲染验证**：验证脚本需要临时编译 .NET 代码，被环境安全策略拦下，未绕过；样式行为请在桌面端目视确认。
- ZIP / Setup 打包门禁各一次 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`。
- 发布物（**以这一组为准**）：`WeChat-Voice-Bridge-V1.4.7-win-x64.zip` **89,462,225 字节** / SHA-256 `F2F58514A93E63148166845E5CFA982F8056D9EE6ECC29BFDA51784DE408635C`，1621 项；`WeChat-Voice-Bridge-V1.4.7-Setup.exe` **65,299,128 字节** / SHA-256 `2139EDB3521D6C8A89D4284C891FEEC974A371BBB2C12747AF219A857A21B51E`。出厂 DLL 含 `ProviderActivation` / `StaysOnStandby` / `UseActiveKey` / `ActivateProviderClick` / `ProviderMissing` 等符号，且含本轮新增的版本日志文案（发布 DLL 与构建 DLL 哈希一致）；无个人配置与密钥残留。
- 旧 r2 产物改名留档为 `...-r2-win-x64.zip` / `...-r2-Setup.exe`；r1 的 Setup 仍在。`ReleaseHistory.cs` 在首轮打包后才补上本轮条目，因此首轮 r3 包改名留档为 `...-r3-prelog-win-x64.zip` / `...-r3-prelog-Setup.exe`，标准名包已按最终源码重出。打包时用户正运行 r2 的 exe（PID 11884），**未强杀进程、未覆盖其目录**，改发布到 `artifacts\win-x64-v1.4.7-r3`。
- **桌面快捷方式已按用户指令更新**：`D:\OneDrive\桌面\微信语音桥.lnk` → `artifacts\win-x64-v1.4.7-r3\WeChatVoiceBridge.exe`（已核对 .lnk 二进制只含 r3 路径、不含 1.4.5）。

---

## 1.4.7 修订 · 2026-11-09（清空内置音色 + 添加 API 后音色自动可用）

> 1.4.7 **从未上线**，本轮在同一版本号上迭代。下面旧的 1.4.7 段是首轮记录，其中「预置音色本次不动」的要求与该段产物哈希均已作废；**以本节的产物哈希和新包为准**。线上仍是 1.4.6，桌面快捷方式未动。

用户新指令：内置音色也清空、无内置音色、只保留用户新添加的；并明确「api 添加进来，对应的音色就不要显示无 api 了，而是变成可用的状态，请修复」。

### 问题定位：「无 API」从哪来

- 首轮 1.4.7 摘掉了出厂 `fish-default` **服务**，但按当时要求保留了 4 个预置音色（聂聂 / 里奥 / 熊二 / 派大星）+「此前使用的音色」的播种，这些音色的 `ProviderId` 仍指向已不存在的 `fish-default`。
- 于是 `RefreshLibrary()` 把它们归到「服务已删除 / 服务不可用」分组，`ChooseContactVoice()` 提示「尚未配置的 API」——即用户说的「无 API」。用户删掉自己添加的服务后留下的孤儿音色同样会卡在这个状态。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.App/MainWindow.Library.cs` | 删掉 `InitializeLibrary()` 里 `if (library.Count == 0)` 的整段预置音色播种；删掉 `LegacyPresetProviderId` 常量与两处按它查找音色的逻辑（联系人默认音色改为只按 `VoiceId` / 第一条可用音色）；加载 `VoiceLibrary` 时再按 `IsFactoryVoice()` 过滤一次做双保险 |
| `src/VoiceBridge.App/MainWindow.xaml.cs` | 删掉 `VoicePreset` 记录与 `VoicePresets` 四个内置音色；`RefreshVoices()` 只列 `userVoices`，空列表时 `SelectedIndex = -1` 防越界；`VoiceIdOf()` 只查用户命名音色 |
| `src/VoiceBridge.Core/VoiceLibraryMigration.cs`（新增） | `IsFactoryVoice()` 用**双条件**精确识别出厂音色（`ProviderId` 精确等于出厂 Id **且** `Id` 以 `fish-` 开头）——用户把内置音色改绑到自己服务后不再算出厂音色，会保留；`RemoveFactoryVoices()` 返回用户音色 + 被删 Id 并清理空/重复 Id；`CleanContactReferences()` / `CleanTestVoiceReference()` 清理悬空引用；`BindOrphanVoices()` 把没有有效服务的音色关联到指定服务 |
| `src/VoiceBridge.App/LocalSettings.cs` | `Load()` 在 provider 迁移之外再跑音色迁移，同步清理 `ContactProfiles[].VoiceProfileId` 与 `TestVoiceProfileId`，任一迁移发生变化就 `Save()` 一次；DPAPI 凭据库仍不读不写不删 |
| `MainWindow.Library.cs` `EditProvider()` | 新增服务保存后调用 `BindOrphanVoices(library, providers, updated, out bound)`，把孤儿音色自动关联到新服务并提示数量；已绑定现存服务的音色一律不动，编辑已有服务时不触发 |
| 文案 | `RefreshLibrary()` 的「服务已删除 / 服务不可用」→「未关联服务」；`ChooseContactVoice()` 的「尚未配置的 API」→「尚未关联可用的 API」并指引改绑；`MainWindow.xaml` 的「我的音色」补空态说明、语音服务说明补「不预置音色」 |
| `scripts/Verify-Source.ps1` | 新增防复发断言：禁止 `VoicePresets` / `LegacyPresetProviderId` 播种、`LocalSettings.Load()` 必须调用音色迁移、`MainWindow.Library.cs` 必须保留孤儿绑定；顺手修了 PowerShell 5.1 读 UTF-8 无 BOM 脚本的语法坑（补 BOM）与 `git diff --check` 的 stderr 噪声 |

### 验证与产物（本轮重做）

- Release 构建 **0 警告 0 错误**；`Test.ps1` **167/167 C# + 54/54 Python PASS**（新增 `VoiceLibraryMigrationTests.cs` 14 个用例：出厂音色全删、用户音色逐字段保留、改绑过的内置 Id 保留、相似 Id 不误删、幂等、联系人/试听引用清理、旧 JSON 反序列化、孤儿绑定与幂等）；`Verify-Source.ps1` **PASS**。
- **打包环境插曲**：用户当时正在运行 `artifacts\win-x64-v1.4.7\WeChatVoiceBridge.exe`（PID 36940），DLL 被锁导致覆盖发布失败。**没有强杀进程、没有动那个目录**；改为发布到并行目录 `artifacts\win-x64-v1.4.7-r2`，ZIP / Setup 仍用标准文件名输出新内容。旧 Setup 改名保留为 `WeChat-Voice-Bridge-V1.4.7-r1-Setup.exe`（未删除）；旧 ZIP 在覆盖尝试中已被安全删除机制移入回收站。
- 出厂门禁：ZIP 与 Setup 打包前各一次 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`。
- `WeChat-Voice-Bridge-V1.4.7-win-x64.zip`：**89,457,459 字节**，SHA-256 `116BB5C052852FBA961E4B2219EB9C73C56C62FC4CED81D7F347ACC00278C1E0`；1621 项、exe/dll `ProductVersion=1.4.7`、worker 与两个 wheel 齐全、无 `settings.json` / `.dpapi` / `.key` / 数据库残留。
- `WeChat-Voice-Bridge-V1.4.7-Setup.exe`：**65,298,145 字节**，SHA-256 `5D711DC6E9F043AFEE37A160CCE14ED4CFBA3D6C0D9B39C9546D034FF6C5B7AD`；Inno Setup `Successful compile`。
- 出厂二进制符号核查：`VoiceBridge.Core.dll` 含 `VoiceLibraryMigration` / `RemoveFactoryVoices` / `BindOrphanVoices`；`WeChatVoiceBridge.dll` 已**不含** `VoicePresets` / `LegacyPresetProviderId` /「此前使用的音色」，含「未关联服务」与 `BindOrphanVoices`。
- **未上线、未联系 Muse、桌面快捷方式未动**。上线闸门：必须等用户实测通过并明确发话。

---

## 1.4.7 · 2026-11-09（取消出厂自带「默认 API」）

用户要求：全新安装时 API 服务列表为空、一条不种；任何服务条目都可删，不留内置/不可删条目；老用户升级时自己添加过的服务与钥匙原样保留、所有人的内置默认 API 自动删掉；出厂包里不得再含任何真实 API 钥匙；**预置音色本次不动**。并要求交付说明「种子逻辑原来在哪、怎么摘的；升级迁移怎么处理的老配置」。

### 根因排查：「真实 API 钥匙」到底在哪

- 源码、git 全历史、1.4.6 的 ZIP 与 Setup.exe 中**没有明文硬编码的真实 API Key**；ZIP 里命中的 `sk-` 是 .NET 语言标签（`sk-SK`）和 SPDX 许可证 ID，不是密钥。
- 真正的链路是「默认服务 + 本机凭据库」：`MainWindow.Library.cs` 每次启动补种一条固定 `Id="fish-default"` 的连接，`SyncDefaultFish()` 把本机当前选中的凭据名（`CredentialId`）持续写回它，`DeleteProviderClick()` 又对固定 Id 做了不可删特判。开发机 `%LOCALAPPDATA%\WeChatVoiceBridge\fish-keys.dpapi` / `fish-key.dpapi` 用 DPAPI 存着开发者本人的真实钥匙，本机 `settings.json` 实测就是 `fish-default → CredentialId=speech-fish-default`。所以这条出厂连接在本机会直接读到开发者钥匙且删不掉；出厂包虽未带 `.dpapi`/`settings.json`，产品语义上依然不成立。

### 种子逻辑原来在哪、怎么摘的

| 位置（改动前） | 作用 | 本轮处理 |
| --- | --- | --- |
| `MainWindow.Library.cs` `InitializeLibrary()` | `if (!providers.Any(item => item.Id == DefaultFishId)) providers.Insert(0, new() { Id = "fish-default", ... CredentialId = selectedKeyName });` 每次启动补种 | 删除整段播种；改为只加载 `settings.Providers`，过滤固定出厂 Id、空 Id 与重复 Id |
| `MainWindow.Library.cs` `DeleteProviderClick()` | `row.Id == DefaultFishId` 直接拦下，提示「默认 Fish 连接请通过编辑修改」 | 删除特判；任何服务都可删，绑定音色保留并提示可改绑 |
| `MainWindow.Library.cs` `SyncDefaultFish()` | 把 `selectedKeyName` / `fishModel` / 延迟写回默认服务 | 整个方法删除，`PublishRouting()` 不再调用 |
| `MainWindow.Library.cs` `RefreshLibrary()` | `Name == "Fish Audio"` 时显示「默认连接」 | 改为空名时按服务类型显示标签，不再有「默认」概念 |
| `MainWindow.ConnectionSettings.cs` `StoreProvider()` | 编辑默认服务时联动隐藏控件与 `selectedKeyName` | 删除 `applyingProvider` 与整套联动，只做普通 upsert |
| `MainWindow.xaml.cs` | `applyingProvider` 抑制隐藏控件事件 | 删除该字段引用 |
| `MainWindow.xaml` | 隐藏区「默认 Fish 服务」「默认 Fish 密钥」文案 | 改为中性文案；「语音服务」说明补「软件不预置任何服务或密钥，首次使用请添加自己的 API」 |

预置音色按用户要求**本次不动**：播种逻辑保留，只把常量改名 `LegacyPresetProviderId = ProviderMigration.FactoryDefaultId`，明确它仅是旧音色条目的归属标记、不再对应任何会被创建的服务。删除服务后音色仍保留、可在「编辑音色」改绑，与放开删除保持一致。

### 升级迁移怎么处理的老配置

新增 `src/VoiceBridge.Core/ProviderMigration.cs`（放 Core 便于测试项目直接引用）：`FactoryDefaultId = "fish-default"`，`IsFactoryDefault()` 用 `StringComparison.Ordinal` 全等匹配，`RemoveFactoryDefault(providers, out removed)` 返回用户自己的服务列表。

`LocalSettings.Load()` 读 `settings.json` 后**无条件**调用它：

- 全新安装 / 空配置 → 返回空列表，什么都不种；
- 老配置 → 只精确摘掉 `Id == "fish-default"`，用户自建服务（GUID 等生成 Id）连同 `CredentialId`、`Endpoint`、`Model`、`Latency`、`TimeoutSeconds`、`AudioFormat` 原样保留、顺序不变；
- 顺手清理空 Id 与重复 Id；相似 Id（`fish-default-1`、`FISH-DEFAULT`、`fish_default`、带空格的）绝不误删；
- 确实摘掉过出厂条目时立刻 `Save()` 一次，把 `settings.json` 本身也写干净；写盘失败只影响落盘、内存结果已干净，下次启动重试；
- 迁移**不读、不写、不删** DPAPI 凭据库（`KeyStore`），用户自己的钥匙一条不动。

`MainWindow.Library.cs` 的 `InitializeLibrary()` 再做一次同样的 Id 过滤作为双保险，避免其它来源的配置把出厂服务带回列表。

### 出厂包不得含真实 API 钥匙

新增 `scripts/Assert-CleanPayload.ps1`；`Publish.ps1` 在写 ZIP 前、`Build-Installer.ps1` 在调 Inno Setup 前都强制调用：

1. **文件名门禁**：`settings.json` / `*.dpapi` / `*.key` / `.env*` / 数据库与 WAL/SHM/journal，命中即拒绝打包（沿用并收窄了旧检查；CPython 运行时自带的 `cacert.pem` 是合法文件，不在黑名单）。
2. **明文门禁**：对发布目录每一个文件（含 DLL/EXE）按 1 MB 分块扫描明显密钥形态，兼顾跨块边界。
3. **精确门禁**：读取本机 DPAPI 凭据库（只在内存里，绝不打印或写盘），把其中每个真实 Key 按明文与 UTF-16 两种形式作为模式去比对，出厂包里只要出现同一串就拒绝打包；干净 CI 机器没有凭据库时跳过该项并给出警告。

`Verify-Source.ps1` 扫描范围扩大到所有会随包发布的文本文件，并新增防复发断言：不允许再出现 `providers.Insert(` 播种 / `SyncDefaultFish` / `DefaultFishId`；`LocalSettings.Load()` 必须调用迁移；`fish-default` 字面量只允许存在于 `ProviderMigration.cs`。

### 验证与产物

**环境说明**：本轮开始时 `C:\Program Files\dotnet` 只剩运行时、SDK 目录已不存在，`dotnet build` 报 `No .NET SDKs were found`。从微软官方下载 `dotnet-sdk-8.0.427-win-x64.zip` 解压到 `%LOCALAPPDATA%\Temp\wvb-sdk\sdk`（不改系统 PATH、不装系统组件），用它完成全部构建与测试。该临时目录若被清理，按 HANDOVER 第 5 节重下即可。

- Release 全量构建：**0 警告 0 错误**。
- `Test.ps1`：**153/153 C# PASS**（141 原有 + `ProviderMigrationTests` 12 个新增）+ **54/54 Python PASS**（41 个因本机无 SQLCipher 环境按既有基线跳过）。新增用例覆盖：全新安装空列表、老配置只剩出厂条目时升级后为空、用户自建服务与 `CredentialId` 逐字段原样保留、幂等迁移、5 组相似 Id 不误删、空/重复 Id 清理、真实旧 JSON 反序列化后只丢出厂连接。
- `Verify-Source.ps1`：**PASS**（含本轮新增的三条防复发断言）。扫描范围扩大到所有会随包发布的文本文件，并排除了规则文件自身（它引用了被禁词字面量）。
- **打包门禁实测**：`Publish.ps1` 与 `Build-Installer.ps1` 各跑一次 `Assert-CleanPayload`，均 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`——即把开发机 DPAPI 凭据库里的 2 个真实 Key 按明文与 UTF-16 两种形式在全部出厂文件中比对，零命中。另对旧 `artifacts/win-x64`（1.4.6 发布目录）试跑也 `PASS (1593 files ...)`，说明旧包本来就没带明文钥匙，问题确实在默认服务的引用语义。
- **产物**（均在 `artifacts/`，未动桌面快捷方式）：
  - `WeChat-Voice-Bridge-V1.4.7-win-x64.zip`：89,456,803 字节，SHA-256 `908AC12B7767BC11930787F96C73886BB3C009EA0C992C92E246FFE05035F66A`；1621 项、exe/dll `ProductVersion=1.4.7`、Python 运行时与两个 wheel 齐全、无 `settings.json`/`.dpapi`/`.key`/`.db`/WAL/SHM 残留。
  - `WeChat-Voice-Bridge-V1.4.7-Setup.exe`：65,297,495 字节，SHA-256 `3F0927711AC2FE53F2174F7D7C63F905C1B23E1015FD7B1094ECE5F41DD76263`；Inno Setup `Successful compile`。
- **出厂二进制符号核查**：`VoiceBridge.Core.dll` 含 `ProviderMigration` / `RemoveFactoryDefault`；`WeChatVoiceBridge.dll` 不含 `SyncDefaultFish` / `DefaultFishId`。后者仍能搜到 `fish-default` 字符串，是 `LegacyPresetProviderId = ProviderMigration.FactoryDefaultId` 这个 const 被内联的结果，只用于给旧预置音色打归属标记，**不会创建任何服务**（全仓 `providers.Add/Insert` 只剩用户经 `ProviderEditor` 新建的 GUID 条目与从 settings 加载两处）。
- **未做**：实机全新安装后的界面目视确认、真实跨机器升级（把一份含 `fish-default` 的旧 `settings.json` 放到另一台 Windows 上跑）。这两项需要用户在桌面确认。
- **本轮未上线、未联系 Muse**：用户只要求改机制与交付报告；线上仍是 1.4.6。桌面快捷方式保持指向 `artifacts/win-x64-v1.4.5`。

---

## 1.4.6 · 2026-11-09（更新按钮三态配色 + 上线发布）

用户要求「软件更新」区有升级时的按钮做得更居中协调、且三种状态颜色各不相同，并明确**不要更新桌面快捷方式**（要在 Muse 推上线后自己从桌面端点更新升级）。

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.App/App.xaml` | 新增 `UpdateReadyText` / `UpdateReadyFill` / `UpdateReadyBorder` / `UpdateInstallFill` / `UpdateInstallBorder` / `WhiteText` 色板；新增 `UpdateReadyButton`（浅绿描边）与 `UpdateInstallButton`（实心绿）两个样式，二者与 `SecondaryButton` 共用 36 DIP 高度、`MinWidth 120`、`Padding 18,7`、圆角 9、内容居中 |
| `src/VoiceBridge.App/MainWindow.xaml` | `UpdateAction` 加本地 `MinHeight=36` / `MinWidth=120` / `Padding=18,7` / 双向居中对齐——本地值优先于样式，切换样式不再撑高整行 |
| `src/VoiceBridge.App/MainWindow.Updates.cs` | `RenderUpdate` 按三态选样式与字色：无更新＝灰色 `SecondaryButton`、发现新版＝浅绿描边 `UpdateReadyButton`、已校验可安装＝实心绿 `UpdateInstallButton`；按钮文字用 `TextBlock` 禁换行、居中 |
| `Directory.Build.props` / `ReleaseHistory.cs` | 版本 1.4.6，新增 1.4.6 更新说明 |

**发布前复验**：`Test.ps1` 全量 141/141 C# + 54/54 Python PASS、Release 构建 0 警告 0 错误；`Verify-Source.ps1` PASS；逐项静态核查前几轮全部修复点仍在；隔离 WPF 工程 11 个场景（取消/点叉/添加 × 3 种初始提示词 + 选普通提示词、直接点「编辑自定义…」、删除预设、旧版自定义数据）全部通过且 `shows 自定义=False`；三态按钮截图确认三种颜色不同、尺寸一致、上下居中。

**产物（并排在 artifacts/）**：`WeChat-Voice-Bridge-V1.4.6-win-x64.zip`（89,456,214 字节，实算 SHA-256 `047F899051D63016E309812AC893342D89A20C7AB80D7C461F8EF970FEE1A37B`）与 `WeChat-Voice-Bridge-V1.4.6-Setup.exe`（65,298,533 字节）。ZIP 含 1621 项、版本 1.4.6、Python 运行时与 wheel 齐全、无数据库/WAL/配置/密钥残留、程序集含本轮全部修复符号。

**按用户要求**：桌面快捷方式保持指向 `artifacts/win-x64-v1.4.5`，未调用 `Install-DesktopShortcut.ps1`，以便用户亲自体验「桌面端检测到线上 1.4.6 → 下载 → 重启安装」的完整升级流程。线上发布走 Muse 通道，本代理不自行上传。

**Muse 发布结果**：发布单 `codex_20261109_023128_ac2ff2` 先收到一条 `error`（`Remote end closed connection without response`，声称「未发布或已回滚」）。本代理未盲目重发，而是先独立实测：`latest.json` 已是 1.4.6、线上 ZIP HTTP 200 / 89,456,214 字节 / SHA-256 与本地完全一致、Setup.exe HEAD 200 / Content-Length 65,298,533；随后用允许范围内的简短问答向 Muse 核实，并取回同一发布单的第二封正式回执 **status = ok**：「publish-voicebridge 1.4.6 已发布完成（更正：上一条 error 回复是误报，请以本条为准，不要重发）」。原因：流水线 stage 8 公网复核首次抓清单遇到瞬时连接中断被判异常，但文件与清单当时已全部落位、异常分支未执行回滚。Muse 复核 ZIP 三方一致、Setup.exe sha256 `8f7251a5aa9f2083abac982b8260ff75b79b584acb3f371c7db4606c976b0c5d`（本地实算一致），旧清单备份为 `latest.json.bak-1.4.3`。**结论：1.4.6 已上线，不要重发本单。**


## 1.4.5 · 2026-11-09（自定义提示词选中状态根治）

用户第三次反馈同一个问题：点「自定义」弹出窗口后，取消或点叉应恢复打开前的提示词，确定并添加后应直接选中新内容，但收起框一直显示「自定义」三个字。

**为什么 1.4.3 的修复没生效（如实记录）**：当时的隔离验证工程用的是**一个直接放在 StackPanel 里的裸 ComboBox**，并模拟 `combo.SelectedValue = "custom"`；真实主窗口里 ComboBox 在 `DataTemplate` 内、由 `ContentControl`/`ListBox` 生成，且用户点击会先设置 `SelectedItem`。两者行为不同：真实场景下 `SelectedValue` 的 TwoWay 绑定会把值**立即改回源值**，但 `SelectedItem`（以及收起框的 `SelectionBoxItem`）仍停在「自定义」项上。因此只靠事后 `Notify` / `UpdateTarget()` 永远拉不回显示。另外 1.4.3 的 `finally` 用了 `DispatcherPriority.ContextIdle`，会被主窗口的重复定时器饿死。

**本轮改法（让「自定义」永远不成为选中值）**

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.App/ContactCard.cs` | `AnnouncementChoiceId` setter 遇到 `custom` 直接 return（不再触发 `CustomRequested`）；删除因此不再使用的 `CustomRequested` 事件、`customRequestPending` 字段、`FinishCustomRequest()` |
| `src/VoiceBridge.App/MainWindow.Library.cs` | `CardFor` 移除该事件订阅；新增 `AnnouncementSelectionChanged`（用 `e.AddedItems` 判断动作项，而不是看已被绑定改回的 `SelectedValue`）；新增 `RestoreAnnouncementItem`，用 `SetCurrentValue` 同时把 `SelectedItem` 与 `SelectedValue` 改回真实选项；对话框改为 `Dispatcher.BeginInvoke` 默认 Normal 优先级；`SyncAnnouncementSelection` 改为按 `ItemsSource` 匹配并同样恢复两个属性 |
| `src/VoiceBridge.App/MainWindow.xaml` | 两个提示词 ComboBox 加 `SelectionChanged="AnnouncementSelectionChanged"` |

**验证（这次用贴近真实的工程）**：`D:\USERNAME\CodexScratch\Temp\wvb-announcement-rootfix-20261109` 链接真实 `ContactCard.cs` / `AnnouncementDialog.cs` / `DialogShell.cs`，用 `ContentControl` + `DataTemplate`（含 `x:Name="AnnouncementBox"` 与 TwoWay `SelectedValue`）渲染，逐字复制产品的两个处理器；模拟真实点选是设置 `SelectedItem`。跑了 7 组场景（取消/点叉/添加 × 初始无提示词/昵称说/消息+留言），日志全部通过：

- 点「自定义」的同时收起框已是打开前的选项（`无提示词` / `“小林”说+消息` / `消息+“小林”留言`）；
- 取消、点叉后 `shows 自定义=False`，`card` 与 `combo` 都保持原值；
- 添加并使用后 `combo` 与 `card` 都等于新 preset id，显示 `✧₍^˶- ˕ -˵^₎✧说+消息`；
- 输入框每次空白，对话框正常关闭。

`Test.ps1` 全量构建 0 警告 0 错误、141/141 C# + 54/54 Python PASS；`Verify-Source.ps1` PASS。发布到 `artifacts/win-x64-v1.4.5` 并更新桌面快捷方式。

---

## 1.4.4 · 2026-11-09（头像恢复、通话稳定性、任务栏合并、词库光标）

用户一次提出：旧版本升级后已选联系人头像不自动显示；微信通话时微信被关闭；更新时任务栏多一个图标；词库编辑不自动出现光标；并要求把工作日志与交接文档写清楚。

**代码改动**

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.WeChat/ContactSearchCache.cs` | `Fingerprint` 只用 `contact.db` 主库，不再含 `-wal`；抽出 `TryLoad`；新增 `TryFind(session, peerId, ...)`，按会话 ID 精确查、不受 `TrySearch` 的 60 条上限影响 |
| `src/VoiceBridge.App/MainWindow.ContactIndexing.cs` | watcher `Filter` 由 `contact.db*` 改为 `contact.db`；重建间隔 15 秒 → 5 分钟；索引就绪后 `Dispatcher.InvokeAsync(RestoreMissingAvatars)` |
| `src/VoiceBridge.App/MainWindow.Library.cs` | 新增 `contactIndexCache` 字段与 `RestoreMissingAvatars`，对 `AvatarUrl` 为空且无磁盘缓存的卡片补 URL 并触发 `FetchAvatarAsync` |
| `src/VoiceBridge.App/ContactCard.cs` | 新增 `UpdateAvatar`，只更新 URL 不改姓名，复用 `changed` 落盘 |
| `src/VoiceBridge.WeChat/PythonFixtureSource.cs` | 新增 `CheckWeChatOccasionally()`，`QueryAsync` 里的微信进程存活检查由每轮（约 1 秒）降到最多每 10 秒一次 |
| `src/VoiceBridge.App/App.xaml.cs` | 窗口创建前 `SetCurrentProcessExplicitAppUserModelID("WeChatVoiceBridge")`，主程序 / 更新助手 / 重启后的新版共用一个任务栏按钮 |
| `src/VoiceBridge.App/MainWindow.xaml.cs` | `FocusReading` 改为在容器可视化树按名字找输入框、`Keyboard.Focus` + `IsVisibleChanged` 等待可见、最多重试 3 次；编辑光标仍在「读作」末尾 |
| `Directory.Build.props` | `1.4.3` → `1.4.4` |
| `src/VoiceBridge.App/ReleaseHistory.cs` | 新增 1.4.4 条目（4 条） |
| `tests/VoiceBridge.Tests/ContactIndexCacheTests.cs` | 新增用例：WAL 变动不使索引失效、第 80 行仍可 `TryFind` 找回头像 URL、主库变更仍失效 |
| `docs/user-guide.md`、`src/VoiceBridge.App/UserGuide.xaml.cs` | 头像自动恢复、词库编辑光标、更新窗口任务栏、微信通话冲突说明 |
| `docs/security-review.md` | 新增「2026-11-09 补充：微信语音/视频通话时微信自行退出」：静态排查、排除项、缓解、实机验证步骤 |
| `docs/HANDOVER-CURRENT.md`、`docs/WORKLOG.md` | 新建（本文件与交接文档） |

**验证**：`Test.ps1` 全量 Release 构建 0 警告 0 错误、141/141 C# + 54/54 Python PASS；`Verify-Source.ps1` PASS；发布物 `1.4.4.0`、Python 运行时与两个 wheel 齐全、无 `.db`/WAL/SHM/`settings.json`/密钥残留、程序集含本轮新增符号；桌面快捷方式回读确认指向 `artifacts/win-x64-v1.4.4`，旧目标消失。

**未验证 / 需用户确认**：微信通话冲突只有静态证据，未实机复现；头像恢复、词库光标、任务栏分组需实际桌面确认。

---

## 1.4.3 · 2026-11-08（两轮）

### 第二轮：提示词、下拉菜单、热移除、词库覆盖提示词

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.App/AnnouncementDialog.cs` | 输入框每次空白 |
| `src/VoiceBridge.App/ContactCard.cs` | getter 不再返回 `custom` 哨兵；旧数据用 `AnnouncementChoice.CurrentId` 生成真实选项；预设 label 改为 `提示词+消息` / `消息+提示词`；新增 `IsAddNew`；`RefreshAnnouncements` 按 ID 增删而非只截尾部 |
| `src/VoiceBridge.App/MainWindow.Library.cs` | `EditCustomAnnouncement` 同步 `FinishCustomRequest` + 新增 `SyncAnnouncementSelection`（`BindingOperations...UpdateTarget()` 强制回读，修复下拉框卡在"自定义"）；`RemoveContactClick` 放开监听中限制；`RoutedSpeech.Resolve` 对提示词应用词库规则 |
| `src/VoiceBridge.App/MainWindow.xaml` | 两个提示词 ComboBox 加 `x:Name="AnnouncementBox"`；`AnnouncementMenuTemplate` 给「自定义」加绿色 ＋ / 加粗 / 分隔线 |
| `src/VoiceBridge.App/MainWindow.Interaction.cs` | `RemovePeerButton` 不再被监听态只读限制 |
| `src/VoiceBridge.Core/SessionController.cs` | `Start` 新增可选 `admitFilter`，`Admit` 中应用；`Session.AdmitFilter` |
| `src/VoiceBridge.Core/ContactPreferences.cs` | `Prepare` 加可选 `Func<string,string>? renderPrefix` |
| `src/VoiceBridge.Core/ReadingText.cs` | **新建**：复刻 worker `_apply_rules` 的 both/inside/standalone、数字护栏、长词优先；`ignoreScope` 供提示词使用 |
| `src/VoiceBridge.App/MainWindow.xaml.cs` | `StartClick` 传按路由快照判断的准入过滤器；`PreviewOperationAsync` 对正文与提示词同样应用词库规则 |
| `tests/VoiceBridge.Tests/ReadingTextTests.cs` | **新建**，7 项（后增至 8 项） |
| `docs/user-guide.md`、`UserGuide.xaml.cs`、`ReleaseHistory.cs` | 同步说明与 1.4.3 条目 |

验证：构建 0/0；`Test.ps1` 139 C# + 54 Python PASS；`Verify-Source.ps1` PASS；隔离 WPF 工程 `D:\USERNAME\CodexScratch\Temp\wvb-announcement-fix-20261108` 链接真实 `ContactCard.cs`/`AnnouncementDialog.cs`，日志确认输入框为空、取消回到 `“小林”说+消息`、添加后显示 `✧...✧说+消息`、旧数据返回 `current`；下拉菜单截图确认绿色 ＋ 与分隔线。

### 第一轮：更新弹窗浅绿色外框

`DialogShell.cs`、`ReleaseNotesDialog.xaml(.cs)` 改为透明窗口（`AllowsTransparency=True`、`Background=Transparent`），外层 16 DIP 透明，卡片 `#FFFEFB` + `#DCE3DF` 细边框 + 中性阴影；移除 WindowChrome，拖动改 `DragMove()`，补 Alt+F4。`docs/guide-ui-validation.md` 追加后续变更说明。

验证：构建 0/0；隔离工程 `D:\USERNAME\CodexScratch\Temp\wvb-dialog-clean-20261108` 截屏对比（含 `before_after.png`），拖动实测位移 80/45 DIP。

### 发布与快捷方式（同日）

- `scripts/Publish.ps1`：修复 `Expand-Archive -Force` 清空已缓存 Python 目录触发宿主 safe-delete 拦截；`worker/deps` 不再整目录删除。现在可重复发布到同一目录。
- 发布 `artifacts/win-x64-v1.4.3`（旧快捷方式原指向 `preview-1.4.2-ui2`，导致用户测到的仍是旧行为），用 `Install-DesktopShortcut.ps1` 更新桌面快捷方式。
- 顺手加固：词典「单独出现时」/「句中出现时」规则在拼出来的提示词短语里会漏替换，`ReadingText.Apply` 新增 `ignoreScope`，提示词一律包含匹配，正文保留原三档语义。

---

## 版本与发布状态对照

| 版本 | 内容 | 状态 |
| --- | --- | --- |
| 1.3.3 | 线上正式版 | 已上线（早于本次会话） |
| 1.4.0–1.4.2 | 历史开发快照，见 `NEXT-AGENT-HANDOVER-1.4.md` | 本地 |
| 1.4.3 | 弹窗外观 + 提示词/词库/热移除 + 快捷方式 | 本地试用，`artifacts/win-x64-v1.4.3` |
| 1.4.4 | 头像恢复、通话缓解、任务栏合并、词库光标、文档 | 本地试用，`artifacts/win-x64-v1.4.4` |
| 1.4.5 | 自定义提示词选中状态根治 | 本地试用，`artifacts/win-x64-v1.4.5`；**桌面快捷方式仍指向此处（用户要求保留，用于体验自升级）** |
| 1.4.6 | 更新按钮三态配色与居中 | 已上线（2026-11-09） |
| 1.4.7 | 取消出厂默认 API、删 API 保留厂商名、同厂商多 Key 自选 | 已上线（后被 1.4.8 覆盖） |
| 1.4.8 | 更新完成后由用户决定何时打开新版 | **线上正式版**（`codex_20261109_235238_857190`，2026-11-09 22:59 UTC） |
| 1.4.9 | wx-multi 多版本适配 + 更新页精简 | 本地，未打包 |
| **1.5.0** | **API 配置简化（含「⋮」菜单重做）+ 音色失效语义 + 凭据指针修复 + 凭据库自动清扫** | **线上正式版**（发布单 `codex_20261110_130657_f72a7b`，2026-11-10 12:09:47 UTC） |
| **1.5.1** | 更新页打磨（标题化/圆点对齐/按钮整块居中）+ 高级页布局 + ⋮ 菜单比例 + 「移除」去悬停提示 | **线上正式版**（发布单 `codex_20261110_141758_3cb004`，2026-11-10 13:21:18 UTC） |
| 1.5.1 工作区（未升版本号） | 软件更新状态点改色：绿（最新）/ 红（发现更新）/ 橙（下载完待装） | 已构建 + 三态实机截图验证，**未打包、未上线** |

**发布状态更正（1.4.6 已上线）**：本会话确实生成了 `WeChat-Voice-Bridge-V1.4.6-win-x64.zip` 与 `...-Setup.exe`，并经 Muse 通道把 1.4.6 推到线上（详见 1.4.6 段）。此处的旧表述是 1.4.5 及更早留下的、已作废。线上正式版自 2026-11-09 起为 **1.4.6**（此前为 1.4.3）。

---

## 2026-11-10 · v1.5.0 追加：「⋮」更多菜单重做

用户复查截图指出「点完这三个点，编辑和删除完全不是一个画风的产物」。原因是 `ProviderMoreClick` 用 `new ContextMenu()` + `new MenuItem()`，走的是 WPF 默认模板：灰渐变边框、方角、紧凑行高、无圆角面板。

**改法**（`App.xaml` 新增三套样式，`MainWindow.Library.cs` 接线）：
- `RowMenu`（TargetType=ContextMenu）：外层 10px 透明留白给阴影，内层白底 `CornerRadius=10` + `#E9EEEC` 1px 边 + `DropShadowEffect`（Blur 18 / Depth 3 / Opacity 0.16 / `#2B3A35`），`MinWidth=116`、内容 4px 内边距。
- `RowMenuItem`：字号 13、高 34、左右 12 内边距、`Margin 0,1`（两行悬停底色不相接）、圆角 7；悬停底色由 `Background` 承载（模板里 `IsHighlighted` 时 `TemplateBinding` 取它）。
- `RowMenuItemDanger`：`BasedOn` 上者，文字 `Danger`（#D33B3B），悬停底色 `#FCEDED`。
- 定位：`HorizontalOffset = -(ActualWidth - button.ActualWidth - 10)`，让**白面板**右缘而不是阴影圈对齐「⋮」；`VerticalOffset = -6`，扣掉阴影留白后仍是 4px 间隙。

**坑**：ControlTemplate 里用 `DataTrigger Binding=Tag` 区分危险项不生效（悬停仍是浅灰），改为「底色走 `Background`」后正常。

**验证**：新建 `tools/MenuPreview`（加载 `App.xaml` 资源、反射置 `IsHighlighted`）渲染静态/编辑悬停/删除悬停三态；`tools/UiSmokeTest` 增 `menu`（抓真实弹窗截图）与 `menuclick`（`user32.mouse_event` 真点，UIA `InvokePattern` 对 MenuItem 无效）两种模式，并刻意点击行右侧留白证明整行可命中 → 「编辑语音服务」正常弹出；点后未做删除，避免动用户配置。`dotnet build` 0/0，`dotnet test` **211/211**。

---

## 2026-11-10 · v1.5.0 上线（Muse 发布单 `codex_20261110_130657_f72a7b`）

用户授权「推到线上最新版本，去联系 muse」。

**打包**：
- `scripts/Publish.ps1` → `artifacts\win-x64-v1.5.0\` + `artifacts\WeChat-Voice-Bridge-V1.5.0-win-x64.zip`
  ZIP = 90,814,448 字节 / `48e2addaf9a6ffb7af19cc2dfe1943a2176b037f42f7d95397a892be6ee6cc33`
- `scripts/Build-Installer.ps1 -PayloadDirectory artifacts\win-x64-v1.5.0` → `artifacts\WeChat-Voice-Bridge-V1.5.0-Setup.exe`
  Setup = 66,213,216 字节 / `462980bde3133cf37c11edd926707d95b5ecaddc44eb1e17148a818f38b11734`
- 门禁：Clean-payload PASS（1733 文件，无 settings.json / dpapi / 明文 Key）；`Test.ps1` PASS；`Verify-Source.ps1` PASS；58 项等价性 PASS；payload 版本 1.5.0 > 线上 1.4.8。

**Muse 通道**：`muse-call -Type publish-voicebridge -Version 1.5.0 -PackagePath <zip> -Sha256 <hex> -Notes <中文>`。
第一次回执是 `status=info`（流水线接手，预计 10 分钟），要求不退出等待；最终 `status=ok`（`publishedAt=2026-11-10T12:09:47Z`），回执称哈希链三方一致（申报 = PC 实测 = A1 落地），并备份了 `latest.json.bak-1.4.8`。

**独立公网友核**（不只信回执）：
- `latest.json` HTTP 200，version `1.5.0`、publishedAt `2026-11-10T12:09:47Z`、size 90,814,448、sha256 与本地一致；notes 1307 字，按 UTF-8 解码是正常中文（命令行长串中文经 `.cmd` 传参未损坏）。
- ZIP HEAD 200 / 90,814,448；Setup HEAD 200 / 66,213,216。
- **完整下载 ZIP（90,814,448 字节）重算 SHA-256 = `48e2adda…cc33`，与清单一致**。

**坑**：`Build-Installer.ps1` 的 `-Output` 是「额外再复制一份到某处」，不是产物路径；第一次传成与脚本默认相同的 `artifacts\WeChat-Voice-Bridge-V1.5.0-Setup.exe`，编译完复制自己给自己触发 `IOException`（文件被占用）导致脚本非 0 退出，重跑只传 `-PayloadDirectory` 即正常。

**未动**：桌面快捷方式仍指向 `artifacts\win-x64-v1.4.7-r3`（用户要求保留，让线上版本严格大于桌面版，便于亲自点一次更新实测）。

---

## 2026-11-10 — 项目纪律：增量记录铁律写入 AGENTS.md（Muse 按用户指示）

用户定规：本项目以后「做一部分，就记录一部分」——Git、交接文档、工作记录、日志都随做随记、随记随存，以便任何接手者（包括限流中断后换上的新模型）只靠仓库就能续跑。

**动作**：在项目根目录 `AGENTS.md` 顶部新增《项目工作指令》，把上述要求落成可执行条目：一节的定义、每节必做的三件事（记 WORKLOG、更新 HANDOVER-CURRENT、单独 git 提交）、推送节奏与远端 HEAD 核对、中断自救（落盘优先于新活）、禁止事项、发版前四项自查与发布后回执确认（含 commit 号）。原有的 WeChat database probes 条目原样保留在文件后部。

**验证**：本次只改 `AGENTS.md` 与本条 WORKLOG 记录，单独成一个提交并推送，远端 HEAD 核对一致后才算完成（结果见本提交）。当时工作区另有 5 个文件的未提交改动（小版本细节优化进行中），不属于本节，未混入本提交——该节完成后须按新规矩自行记 WORKLOG 并单独提交。

---

## 2026-11-10 · v1.5.1：更新页标题化 + 状态点对齐 + 删掉第二行说明

用户复查 1.5.0 上线后的两处界面问题（截图对照）：
1. 版本说明前的「v1.5.0 · 90.8 MB」是一行小字，希望「更像标题一些」；
2. 「软件更新」一行的灰点与「当前版本」不在一条线上，要求三种状态（灰/琥珀/绿）都纵向居中与文字对齐；
3. 追加指令：删掉第二行的「已是最新版本」，格子里只保留点和一行字、上下居中。

**改动**（`MainWindow.xaml` / `MainWindow.Updates.cs`）：
- 状态行结构改为 `StackPanel Orientation=Horizontal`：9px 圆点 + 版本号，两者 `VerticalAlignment=Center`，整行只有一行字，因此圆点必然落在文字中心线上；三种状态只换 `Fill`，几何完全一致。删掉 `UpdateSubtext` 文本块与其三处赋值（状态由卡片下方的状态行与按钮文案承载）。
- 更新说明上方新增标题行：`UpdateNoteTitle`（`SectionTitle`，16 Semibold）+ `UpdateNoteSize`（`Footnote`，12 灰、底线对齐），内容为 `v{版本}` 与 `{大小} MB`；原来那行小字彻底不做了。
- `RenderUpdate` 同步填标题与体积；保留 `UpdatePackage.ToolTip`。
- **按钮改为整块居中**：说明与状态行之间没有分隔线、视觉上是一个整体，所以按钮的居中范围取「卡片上沿 → 说明框上沿」而不是状态行。结构是两列 Grid：左列 `StackPanel`（状态行 + 说明标题行），右列只有按钮、`VerticalAlignment=Center`，因此按钮天然在左列全高居中。
  - **坑（记下来）**：最初用「两行 Grid + 按钮 `RowSpan=2`」实现，WPF 会把跨行子元素的高度分摊到各行，空的第二行被撑成 18px，卡片因此从 52 长到 70，状态文字看着偏高（实测文字中心 27、卡片中心 36，差 9 DIP）。换成两列结构后中间层高度回到 52，文字中心 = 卡片中心 = 27。

**验证**：
- 预览工具 `tools/UpdatePanelPreview` 改为与真实布局同构，并新增**像素级测量**：按圆点自身几何范围找实心圆、按暗度找标题墨迹，比较两者中心；三态偏差 **-0.5 / -0.5 / 0.0 DIP（判定 OK）**，静态图 `artifacts/screenshots/update-panel-with-notes.png`。
- 真实应用截图（`tools/UiSmokeTest` 新增 `shot <pid> <png> [navId]` 模式）：`artifacts/update-live-uptodate.png`（灰点、单行、已是最新）、`artifacts/update-live-newer.png`（绿点、`v1.5.2  0.0 MB` 标题 + 说明面板）。后者用**本地暂存的 pending 更新包**触发（4096 字节假包 + 6 字段 state.json），截图后已删除，线上清单未动、未点击安装。
- 真实应用几何量测（`tools/UiSmokeTest` 新增 `probe <pid>`）：用 UIA 取「当前版本」文本与按钮的边界矩形比中心，无说明时为 **470.5 vs 471.0（差 -0.5 DIP，OK）**——修前会差约 9 DIP。
- `dotnet build` 0 警告 0 错误；`dotnet test` **211/211**；`Test.ps1` PASS；`Verify-Source` PASS；58 项等价性 PASS。
- 版本 **1.5.1**，`ReleaseHistory` 新增对应条目（含「按钮按整块居中」一条）。

**尚未做**：打包与 Muse 推送（等用户看过效果点头）。桌面快捷方式未动。

---

## 2026-11-10 · v1.5.1（续）：联系人「移除」按钮不再弹悬停提示

用户在实际界面悬停「移除」时看到浮动提示文字（「移出朗读名单，不会删除微信联系人」），判定没必要，要求去掉。

**改动**：`src/VoiceBridge.App/MainWindow.xaml` 的 `ContactRemoveAction` 样式删掉 `ToolTip` Setter。两处使用点（`SingleContactTemplate` / `ContactCardTemplate` 的「移除」按钮）都只靠这个样式，按钮本身没写 ToolTip，因此一处删除即全清；悬停配色不变。

**验证**：
- `tools/UiSmokeTest` 新增 `nohover <pid> <button> <png> [navId]` 模式：先 `ShowWindow(SW_RESTORE)` + 前置窗口，把光标两跳移到按钮中心，再用 `AutomationElement.FromPoint` **确认光标命中的就是这个按钮**（否则「没有提示」可能只是鼠标停在空处），随后 4 秒内轮询本进程所有顶层窗口里有没有 `ControlType.ToolTip` 元素。
- 实测：命中 `[移除]`、4 秒无 tooltip、**PASS**，截图 `artifacts/screenshots/contact-remove-nohover.png`。
- `dotnet build` 0/0；`Test.ps1` **.NET tests: PASS + Python tests: PASS**（注意：直接跑 `dotnet test` 会有 7 项因缺 `WVB_PYTHON` 而失败，必须走 `Test.ps1` 才有环境）。
- `ReleaseHistory` 1.5.1 增补该条。

**仍未做**：1.5.1 打包与 Muse 推送，等用户确认视觉后一起做（本次改动并入 1.5.1，不单独发版）。

---

## 2026-11-10 · v1.5.1（续）：语音服务间距 / 试听区加小标题 / ⋮ 菜单改向右展开

用户对着截图提了四条，全部并入 1.5.1。

**改动**：
1. **语音服务标题与首个分组太近**：`ProvidersList` 加 `Margin="0,12,0,0"`。分组头是绿色 `LibraryGroupHeader`，原来几乎贴着标题。
2. **试听卡片加字段标题**：原来两个输入框直接在白卡里，没有名字。现在每块用 `StackPanel`（`Margin=16,14,16,0` / `16,16,16,0`）+ `FieldLabel`（12 灰，与对话框字段一致）包住：「音色」在音色下拉上方、「文本」在文本框上方；块间距 16，标签到控件 6。
3. **删掉文本行下的横线**：`<Border Style="{StaticResource Hairline}"/>` 去掉；顺带删掉按钮行里那个空的占位 `TextBlock`，按钮改 `HorizontalAlignment=Right` 直接右对齐。
4. **「⋮」菜单方向**：原来 `HorizontalOffset = -(ActualWidth - buttonWidth - 10)` 是「菜单右缘对齐按钮右缘」→ 向左展开，用户觉得「往左歪」。改为从按钮**向右下**展开：`Placement=Bottom` 本身已左对齐，把 offset 改成「右缘固定、宽度变化时向左吃」的形式即可。宽度从 `MinWidth 116` 改为 `MinWidth 0` + 显式 `Width`。
5. **菜单与按钮「粘在一起」**：`VerticalOffset` 从 -6 改到 -4，让白面板顶部与按钮底之间留出约 6px。原来白面板顶几乎压着按钮底边，阴影盖在按钮上，看着脏。
6. **按参考图调比例**（用户给了一张「新建任务」的下拉截图，说那个比例最合适、可以再宽一些、接近正方形更像样）：`Width` → 112，`RowMenu` 圆角 10 → 12，菜单项 `Padding` 12 → 16。定位改成 `HorizontalOffset = 79.5 - menu.ActualWidth`——79.5 = 按钮左缘到窗口右缘 95.5 减去右边距 16，于是**宽度怎么变，右缘都稳定在距窗口 16 DIP**，加宽的部分全部向左吃，不会出框。**这个 95.5 是布局常数**（卡片右边距 + 行内边距），改窗口外边距或列表内边距时要跟着改。

**验证**（用户已说明测试他自己看，所以只做最低限度）：
- `dotnet build` 0/0。
- 真实截图：`artifacts/screenshots/advanced-test-bottom.png`（试听卡片：音色/文本标题、无横线、按钮右下）、`artifacts/screenshots/menu-square3.png`（菜单最终比例）。
- `UiSmokeTest menu` 现在会打印 ⋮ 与菜单的矩形、以及菜单距窗口右/下边界的余量。最终实测：菜单 popup **224×204 物理像素 = 112×102 DIP**（宽高比 1.1，接近方形），右缘距窗口右 **32 物理 = 16 DIP**，与按钮垂直间隙约 6 DIP。
- **DPI 是 2.0**（窗口 1880×1560 物理 = 940×780 DIP，⋮ 按钮 43×60 物理 = 21.5×30 DIP）。早期几条记录里按 1.5 换算出的「106.7 DIP 宽 / 右缘 23 DIP / ⋮ 距窗口右 127 DIP」全部作废，以 2.0 的数字为准。
- `tools/UiSmokeTest` 顺带增强：`shot` 支持 `NavId#bottom`（自动把页面滚到底再截），`shot`/`menu`/`nohover` 截图或测量前都 `ShowWindow(SW_RESTORE)`——窗口最小化时 `BoundingRectangle` 会变成 -32000，菜单会量出 0,0。

**按钮下方留白**：用户先说「太近」，调到 `Margin="0,2,0,6"` 后回看又说「现在合适了，不用改」，保持现状。

## 2026-11-12 · 文档纠错（续）：删除关于语音服务验证状态的表述

应用户要求，删除 providers.md 与 tts-integration.md 中关于验证状态的整节内容，以及 HANDOVER-CURRENT.md、WORKLOG.md 中的相关表述与遗留实测项。这类表述容易被误读，故不予保留。

## 2026-11-14 · v1.8.2 发布回执（Muse）

- **发布方式**：DS1017 完成适配与打包（本文件 1.8.2 节 1-5），Muse 独立复核改动与真机复验后直接发布；用户 2026-11-14 授权「测试没问题就直接推」。
- **Muse 独立复核**：`worker/audited_wechat_keys.py` 新旧 diff 逐行核对，只有两处 `max_version`、`profile_for` 上界与文案变化，`CONFIG_XOR_MASK` 逐字节一致，全部偏移与 SQLCipher/PBKDF2 参数未动；C# `MaxSupportedVersion` 已为 4.2.0.52。
- **真机复验**（Legion，微信 4.2.0.52 运行且已登录）：1.8.2 产物自带 worker 以 `version=4.2.0.52` 启动，返回 capabilities + ready，联系人头像 1 个；`lastmessage` 取到聂聂会话的真实最后一条消息；`query` 返回 heartbeat；`stop` 干净退出、无 stderr 报错。GUI 实测：启动无「不支持此版本」弹窗、更新说明正常显示；点「开始」进入「正在监听」并保持；点联系人「试听」，Fish 合成播放后界面显示「试听完成」。
- **上传与校验**：经 `pc-to-a1-chunked-upload.ps1` 32 分块传至 A1 staging，拼装后 sha256 与打包台账逐字一致：ZIP `a0d1aea0630d6a1e0ca41b75dd0aff8716bfe160613694e3de218c1b9d547ad6`（92,189,654 字节）；Setup `e9ec6ea486652499ca920aca4670e13daeb0af29056bcd203215f2fead96d6fb`（67,121,939 字节）。
- **清单切换**：`/var/www/aoliao/voice-bridge/latest.json` 已切到 1.8.2（`publishedAt 2026-11-14T06:51:23Z`；旧清单备份 `latest.json.bak-1.8.1`）。公网复核：`https://iceland.de5.net/voice-bridge/latest.json` 返回 1.8.2；ZIP 与 Setup 均 HTTP 200，Content-Length 与台账一致。
- **README**：版本徽章与两个下载链接已更新到 1.8.2（本提交）。

## 2026-11-14 · v1.8.3 发布回执（Muse）

- **发布方式**：DS1017 完成门禁自救改造与打包（本文件 1.8.3 节 1-5），Muse 复核与真机复验后直接发布；用户 2026-11-14 拍板门禁方案（先查自己再拦人、点叉即退出、启动不阻塞、允许启动后再弹）并授权测试通过即直发。
- **Muse 复核**：`git diff 5da1b1a..HEAD -- worker/ src/VoiceBridge.WeChat/` 为空，提取参数与微信支持范围（4.1.11–4.2.0.52）一字未动；门禁关闭逻辑回读源码确认：`OnClosing` 经纯函数 `VersionGatePolicy.OnDialogClosed(remediationChosen, installStarted)` 裁定是否退出。
- **真机复验**（Legion，出货 payload `artifacts\win-x64-v1.8.3`）：①`WVB_TEST_WECHAT_VERSION=9.9.9.9` 启动，主窗口先渲染、门禁随后弹；框内文案为「语音桥已是最新（v1.8.3），暂时还不支持微信 Windows 9.9.9.9」，按钮只有「更新语音桥」「退出应用」（过新情形无「更新微信」）；②点「更新语音桥」，框内真实走线上清单检查并显示「暂时没有可安装的更新」；③选过自救动作后再点叉，应用继续运行；④重新启动后不选动作直接点叉，应用整体退出；⑤真实微信 4.2.0.52 启动无门禁、更新说明正常、开始监听进入「正在监听」并可正常结束。
- **未实测分支**：门禁框内「下载→交接安装」需线上有更高版本才触发，与设置页共用同一套 UpdateProcess 代码，本版仅代码复核；下一次发版时顺带实测（DS 节 4 保留点 1）。
- **上传与校验**：分块上传 32 块 ALLDONE，A1 拼装后 sha256 与打包台账逐字一致：ZIP `3e5a7d3cdcd9f722aa4e85e1b87e9fdfc7483ec0034f776eeded6ddcc9f5e180`（92,199,780 字节）；Setup `93ee5890509208e022d45310a1d44e615e1cfe616a92fd4fcc0adb7bd3128194`（67,127,439 字节）。
- **清单切换**：`latest.json` 已切到 1.8.3（`publishedAt 2026-11-14T07:57:41Z`；旧清单备份 `latest.json.bak-1.8.2`），并首次带上可选字段 `wechatSupport: "4.1.11-4.2.0.52"` 供门禁判断。公网复核：清单返回 1.8.3；ZIP 与 Setup 均 HTTP 200，Content-Length 与台账一致。
- **README**：版本徽章与两个下载链接已更新到 1.8.3（本提交）。
