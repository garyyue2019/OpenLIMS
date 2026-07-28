# Task Plan: DEV-025 玩具 TestUnit 与样品需求批准

## Goal

实现 `ATC-TOY-002@1.0.0`：版本固定的 TestUnit 危险域、平行号和有序序列；互斥破坏任务永久禁止复用同一 TestUnit；样品需求按基础、平行、互斥、化学最低量、复测预留和留样确定性计算，技术批准后才可供下游使用。

## Current Phase

COMPLETE

## Phases

### Phase 1: 开始门禁与上下文

- [ ] 运行 validate、source-status、impact、ready 四项门禁
- [ ] 阅读生成任务卡、批准 BUS/AC、现有 toy/quantity/allocation 公共契约与实现边界
- [ ] 核对当前分支、工作树与 `allowed_paths`
- **Status:** complete

### Phase 2: 失败测试与详细设计

- [ ] 为 TestUnit 计划、需求计算、技术批准、端口状态和 PostgreSQL 追加式强制编写先失败测试
- [ ] 覆盖正向、反向、边界、权限、并发、恢复、审计和跨模块端口版本固定
- [ ] 确认不访问 Quantity/Allocation 私表，不实现 DEV-027 结论或 DEV-026 LabelReview
- **Status:** complete

### Phase 3: 实现

- [ ] 扩展 toy 公共契约、领域、服务、持久化、迁移、API 和遥测
- [ ] 使用版本化 Quantity/Allocation 公共端口或显式编排契约，不复制其余额/分配状态机
- [ ] 同步必要锁文件、Host 接线、验证路由和文档
- **Status:** complete

### Phase 4: 验证

- [ ] 运行 DEV-025 task profile、architecture 和 contracts 适用门禁
- [ ] 运行严格规格、来源、历史、两次 generate、check 和 Python 41 项
- [ ] 运行全量 .NET/前端适用门禁并审计 `allowed_paths`
- **Status:** complete

### Phase 5: 交付

- [ ] 提交、推送、创建 PR，等待 CI 全绿并 Squash merge
- [ ] 同步 main 并记录 DEV-025 合并证据
- [ ] 移交 DEV-026 到其授权 planning 目录
- **Status:** complete

## Constraints

- 只修改 `ATC-TOY-002@1.0.0` 的 `allowed_paths`。
- 不直接编辑 `generated/spec/`，不修改 PRD，不改写已发布规格/迁移/证据。
- 不定义危险域代码、化学最低量或共享规则默认值；未知输入失败关闭。
- 不直接访问 Quantity/Allocation 私表，不实现 OPS-TOY-005/DEV-027 或 LabelReview/DEV-026。

## Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| First planning-file patch used mojibake as a context anchor and did not apply | 1 | Re-applied using stable ASCII headings only; no partial file changes occurred |
| Combined `rg` search had a PowerShell quote terminator error | 1 | Simplified all patterns to single-quoted PowerShell literals |
| Parallel `rg` search still returned exit 1 because one optional pattern had no matches | 2 | Split known-match searches from the optional search and use `Select-String` for the optional scan |
| New DEV-025 unit tests fail with missing contract/domain types and error constants | expected red baseline | Implement the approved contract and domain behavior, then rerun the same test project |
| Exclusive-group unit test hit plan-invalid before the intended conflict | 1 | Removed the inherited non-destructive share rule when converting the step to destructive; the fixture now isolates the exclusive-group invariant |
| Combined progress/contract patch used a progress-table context line from the wrong position | 1 | Split the progress update from the contract-test patch and used stable local anchors; no partial changes occurred |
| Warning-as-error build found nullable Allocation status passed to a non-null snapshot field | 1 | The preceding exact `ACTIVE` check guarantees non-null; added the localized null-forgiving annotation at snapshot construction |
| First PostgreSQL run mapped the truncated generated destructive-history constraint to generic plan-invalid | 1 | Match the stable constraint-name prefix instead of an overlong generated full name |
| Reconstruction test used record equality on `IReadOnlyList` fields | 1 | Compare stable scalar identity/hash/state and ordered nested business fields structurally |
| `psql` is not installed for direct constraint inspection | 1 | Used Npgsql error semantics and PostgreSQL's known identifier truncation behavior; no database mutation was needed |
| Contract routes passed but the OpenAPI operation-id assertion still failed | 1 | The host intentionally serves a static deterministic OpenAPI document; added the four DEV-025 operations in the allowed API host path |
| Whole-solution `dotnet format --verify-no-changes` found Toy integration formatting plus unrelated pre-existing import-order findings | 1 | Formatted only the edited Toy integration file; left out-of-scope Worker/Platform test files untouched and rely on required warning-as-error/architecture gates |
| First optional pre-commit `rg` scan returned exit 1 on the desired no-match result | 1 | Re-ran with explicit no-match handling and bin/obj exclusions |
| Broad fallback placeholder scan entered build binaries and timed out | 1 | Restricted the final scan to source globs and excluded bin/obj; both private-table and placeholder scans passed |
