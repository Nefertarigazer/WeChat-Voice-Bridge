# 项目工作指令（AGENTS.md）

本文件是本项目的最高工作纪律。任何接手本项目的代理或模型（WorkBuddy、Codex、DeepAI Code、OpenCode、Muse 或其他）开始工作前必须先读完本文件，并同时读 `docs/PROJECT-BRIEF.md`、`docs/HANDOVER-CURRENT.md`、`docs/WORKLOG.md` 的最近条目，再看 `git log -10` 与 `git status`。个人习惯与本文件冲突时，以本文件为准。

## 增量记录铁律：做一节，记一节，存一节

核心一句：**不要攒活，更不要攒记录。** 每完成一「节」就立刻记录、立刻存档，然后才做下一节。这样即便突然限流、断线、额度耗尽或更换模型，新接手者只靠仓库就能无损续跑，不依赖任何人的聊天记录或记忆。

「一节」的定义：一个可独立描述、可独立验证的最小步——一个功能点、一个 bug 修复、一处 UI 调整、一次测试结论，都算一节。

每完成一节，在开始下一节之前，必须按顺序做完三件事：

1. **记 WORKLOG**：向 `docs/WORKLOG.md` 追加一条，写清：日期、这一节做了什么、为什么、改了哪些文件、怎么验证的（构建/测试结果；没验证就明写「未验证」）、还剩什么、下一节是什么、有无阻塞或待拍板事项。
2. **更新交接状态**：只要当前状态有变（版本号、已完成/未完成清单、已知问题、运行与发布方式、续跑点），就同步更新 `docs/HANDOVER-CURRENT.md` 的当前状态部分。它永远反映「现在」，不是历史流水。
3. **存 Git**：把这一节的代码与上面的记录一起单独提交。提交信息说清这一节做了什么；不混入无关改动，不把多节攒成一个大提交。

推送节奏：本地提交必须每节立刻做（防崩溃、防断线）；推送到 GitHub 至少在以下时点必做，并且推送后核对远端 HEAD 与本地一致——每节推送条件方便时随节推；不方便时，最迟不得晚于：打包发版前、工作时段结束时、交接给下一个代理前。

## 中断自救（限流 / 断线 / 换人之前）

一旦察觉额度、上下文或时间即将中断，立刻停止开启新工作，把剩余精力全部用于落盘：补齐当前节的 WORKLOG、更新 HANDOVER-CURRENT 的「续跑点」（下一节第一步做什么、从哪个文件入手、什么还没测）、提交并推送。**落盘优先于多做一节。** 一个只做完但没记录的进度，等于没有进度。

禁止事项：

- 把文档、日志、交接记录攒到最后一次性补写；
- 只改代码不记录，或记录与代码分在相隔很久的两个提交里；
- 未提交的改动长期堆在工作区里过夜或直接拿去发版；
- 用聊天记录、某个代理的私有笔记代替仓库里的 WORKLOG 与 HANDOVER（聊天会丢，仓库不会）。

## 发版衔接

通过 muse-call 向 Muse 发 publish-voicebridge 发布单之前，先自查四项全绿：WORKLOG 已记到最新一节、HANDOVER-CURRENT 是当前状态、工作区干净或 WIP 已单独提交、本地提交已推送且远端 HEAD 一致。

Muse 的发布成功回执里会附一条提醒。收到回执后，开发代理须立即：把发布结果记入 WORKLOG 与 HANDOVER-CURRENT、单独提交并推送，然后通过 muse-call 回 Muse 一句确认，带上该文档提交的 commit 号。

Muse 侧（或任何执行发布的一方）在每次发布回执中必须带上上述提醒，并在发布后复核仓库的记录与推送状态，发现欠账要在回执或随后的联系中明确指出，不得默默替开发代理长期兜底补记。

---

# WeChat database probes

- For live probes, use `tempfile.TemporaryDirectory()` on the configured local scratch drive outside OneDrive. Fix older probes that use bare `mkdtemp()` before rerunning them. Close SQLite connections and stop any child processes before leaving the temporary directory context, including when a probe fails.
- Reuse a single snapshot within each batch of read-only queries. Keep database copies and their WAL and SHM files out of the project, test reports, and published artifacts.
