# Progress Log: DEV-006 收样异常与授权决定

## Session: 2026-07-25

- 推送 `5cff28b`，确认 PR #5 的规格、主验证和 Windows 模块验证全部成功。
- squash 合并 PR #5，合并提交为 `bc6da83`。
- 从合并后的 `origin/main` 创建 `codex/dev-006-receiving-exceptions`。
- 运行前置门禁：82 个规格版本有效、来源 CURRENT、impact 为空。
- 旧 `ATC-REC-005@1.0.0` 为 BLOCKED，确认必须追加新 SemVer 规格而非原地改写。
- 已确定最轻审批模型，准备创建精简批准规格与后继任务卡。
- 追加 `OD-005@1.0.0`、`OPS-EXC-001@1.0.0`、`OPS-EXC-002@1.0.0` 和 `ATC-REC-005@2.0.0`；未改写旧版本。
- 首次 validate 发现两个 activation 枚举和 Story 可观测性字段错误，已按 Schema 修正，并将实现路径收敛到现有 Receiving 模块。
- `validate` 通过（86 个版本），来源 CURRENT；impact 只包含四个新增 Major 版本；`ATC-REC-005@2.0.0` 返回 READY。
- 生成器创建 DEV-006 任务卡、Feature 和追踪派生物；未手工编辑 `generated/spec/`。
- 在现有 Receiving 模块实现异常公共契约、确定性分类、质量/EHS 最小审批、追加迁移、事务服务、三条 API、低基数指标和运行说明。
- 异常事实和决定不可变；ReceivedItem 与标签投影版本条件递增，状态始终保持 `QUARANTINED`。
- 新增最小 Web 工作台，可建档、显示严重度/状态并提交显式条件限制或拒收/封存决定；服务端保持矩阵和权限权威。
- Receiving 单元 32/32、契约 25/25、真实 PostgreSQL 集成 17/17、前端 43/43 通过；全解决方案 155/155 .NET 测试通过。
- 正式 Receiving task profile、Architecture 8/8 和 Contracts profile 均通过；Python 仓库契约 40/40 通过。
- 最终严格规格门禁全部通过，连续两次 `generate` 均为 `written=0 unchanged=59 removed=0`；任务卡仍为 READY，impact 为空。
- 最终 allowed_paths 审计覆盖 37 个状态条目且 0 违规；`git diff --check` 通过。
- 格式工具只修正本任务 C# 文件；未修改路径外既有平台测试文件。
