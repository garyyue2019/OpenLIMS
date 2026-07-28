# Findings: DEV-027 Toy 结论修复

## Baseline

- The machine-wide `dotnet` is SDK 9.0.305, while the user-local executable at `C:\Users\Administrator\.dotnet\dotnet.exe` is the exact repository-pinned SDK 10.0.302.

- `ATC-TOY-004@1.0.0` 为 approved/ready，但其 allowed_paths 仍是旧的 `src/OpenLIMS.Toy/**` 和 `tests/OpenLIMS.Toy.Tests/**`。
- main 中实际实现由提交 `ab38f35` 写入 `src/modules/toy/OpenLIMS.Modules.Toy/**`，且没有提交对应 ToyConclusion unit/contract/integration 测试。
- `ToyConclusionPersistence.cs` 第 18、69 行引用不存在的 `ITransactionToken`，导致 full-solution build 失败。
- `ToyConclusionService` 对 SEC-SIGN-001 仍有 TODO；`GetResultRecordersAsync` 当前返回空列表，使 SoD 证据无法真实闭环。
- DEV-028 已安全保存到 `codex/dev-028-textile-runtime`，当前 Toy 修复分支从干净 `origin/main` 创建。

## Persistence and migration inspection

- `ToyConclusionStore` still depends on `NpgsqlDataSource` and references the removed `ITransactionToken` / `NpgsqlTransactionToken` APIs. Its writes are not using the platform `IAuditIntentWriter` and `IOutboxWriter` inside the ambient transaction.
- `GetResultRecordersAsync` always returns an empty list. Result recorders must instead come from the versioned Result-owned conclusion-evidence port; Toy must not query Result private tables.
- `ToyConclusionService` still calls non-existent `BeginAsync` / `CommitAsync` / `RollbackAsync`, authorizes with an empty object context, bypasses signature validation for tested-scope conclusions, and does not perform explicit object-capability authorization for GET.
- Published migration `20260728_002_toy_conclusion` is immutable. It leaves coverage reference nullable and its immutability trigger reports SQLSTATE `23514`; remediation requires a new append-only migration whose UPDATE/DELETE guards report `55000`.
- Worker does not currently register the Toy module, so Toy migrations cannot be invoked through the worker migration command.
- Existing Toy migration suffixes in order are test-unit-plan/conclusion `_002` and label-review `_003`; use append-only version `20260728_004_toy_conclusion_remediation` for this repair.
- `ToyModule` registers `ToyDataSource`, not a raw `NpgsqlDataSource`; the repaired conclusion store should follow the established Toy stores and use the ambient `IPostgresTransactionAccessor` for transactional reads and writes.

## Decision checkpoints

- ATC-TOY-005 explicitly treats external identity-provider verification as a non-goal. Runtime enforcement for this card is therefore exact `reauthenticationRef@version` persistence plus non-empty signing intent and equality between the submitted signed hash and the system-recomputed canonical content hash.
- The existing `ToyConclusionResult.SignatureRef` remains the compatible public response field; it can expose the pinned reauthentication evidence as `id@version`, while the remediation schema stores ID and version separately with signing intent and content hash.
- Platform transaction callbacks receive only a cancellation token. Module repositories obtain connection/transaction from `IPostgresTransactionAccessor`; audit and outbox writers themselves require that same ambient transaction.
- Toy authorization fails closed on exact organization, capability, legal-entity, and laboratory claims, so Result evidence object scope must be resolved before any create/read authorization decision.
- Result evidence evaluation already runs in the platform transaction, loads the Result group through its store, applies exact `result.record` authorization on the stored object scope, and returns UNKNOWN on PostgreSQL unavailability. Integration coverage should prove the adopted target recorder and object scope are returned from persisted facts.
- Toy contract tests currently stub product/test-unit-plan/label services only; conclusion endpoints are untested and would otherwise reach the unavailable test database. Add a conclusion stub plus POST/GET/OpenAPI/error assertions.
- Toy PostgreSQL tests share one dedicated database and a DI helper. The project will need an explicit Result-contract project reference so a deterministic `IResultConclusionEvidencePort` test double can be injected without crossing private schemas.
- Reuse the existing Toy integration DI helper and serial `toy-postgres` database. Extend its reset list with all conclusion child tables before the parent, and extend `BuildProvider` with evidence decision/scope/recorder inputs.
- Result integration setup already provides real create-observation/adopt flows and a permissive object authorization stub; a focused test can evaluate `IResultConclusionEvidencePort` after adoption and assert target ID/kind, `RecordedBy`, object scope, group/adoption versions, plus UNKNOWN for an absent adoption version.
- The Toy module exposes internals to unit, integration, and contract test assemblies, so integration fixtures can reuse the production canonical-hash function rather than duplicating its algorithm.
- Established Toy stores create one event ID, write the module fact, then call platform `IAuditIntentWriter` and `IOutboxWriter` before the transaction callback returns. Conclusion persistence should use the same pattern and emit `ToyConclusionCreated.v1`.
- Conclusion reads currently omit `content_hash` and use an independent raw data-source connection. The replacement should return an internal persisted scope alongside the public result and perform all reads under the ambient transaction before writing read audit.
- Correlation idempotency needs both a database unique index on `(organization_group_id, correlation_id)` and a transaction-scoped advisory lock before looking up an existing conclusion; this makes concurrent retries return one fact and one outbox rather than merely racing into a duplicate-key error.
- Freeze resolved evidence on the fact: ITEM stores target ID/kind, recorder and Result group version on the parent; TESTED_SCOPE stores the same fields per TestUnit. This preserves the exact SoD evidence even if Result later appends new versions.
- The API host serves a deliberately hand-maintained OpenAPI path catalog rather than reflecting endpoint metadata. The conclusion runtime routes worked, but their four catalog entries were absent; add them explicitly in `src/host/api/OpenLIMS.Api/Program.cs`.
- The apparent missing Textile module warning comes from stale `obj` restore metadata while running with `--no-restore`; the tracked API project has no Textile module reference on this remediation branch. A locked restore should regenerate the graph rather than changing DEV-028 files here.
- Repository contract bootstrap expectations enumerate every generated task/feature and host module explicitly; ATC-TOY-005 and Worker Toy registration must be added to those assertions before Python/architecture gates.
- Current generated inventory is 190 specs, 44 task cards and 73 feature files. The repository test still expected 185/42/70 and omitted the already-generated ATC-TOY-004@1.0.0 plus new ATC-TOY-005@1.0.0 cards; update it to the current deterministic inventory.
- `verify-history` protects `spec/seals/**`; the DEV-031 requirements-lock snapshot is immutable evidence but is not a Release Seal. ATC-TOY-005 is not sealed, so its missing direct OD-002 governance dependency can be corrected without rewriting the existing snapshot. Preserve the first snapshot and append a new governance-correction snapshot after regeneration.
- Of the five newly enumerated approved Toy refs, only unsealed `AC-TOY-002@1.0.0` lacks embedded approval evidence. Its semantic approval is already documented verbatim on OD-034, BUS-TOY-006 and ATC-TOY-004 from the same user decision. Expand ATC-TOY-005 allowed paths and backfill that existing evidence; do not change status or acceptance behavior.
- Force-evaluated restore produced 19 semantic lock changes caused by `Toy -> Result`, `Worker -> Toy`, and API/test transitive graphs. Additional lock files are CRLF-only working-tree noise; normalize all lock JSON to the repository-required LF so only semantic dependency changes remain.

- 后继 Story 必须覆盖真实 modular-monolith 路径和三层测试目录；不能把旧 v1 原地改成正确路径。
- 结果录入人和重认证签署均属于跨模块/平台事实；在发现现有批准公共端口前，不得设计业务默认值。
- 如果仓库没有可复用端口，应在同一后继卡中定义最小版本化公共契约和失败关闭适配器，或先以批准 Spike 明确所有者；不能直接访问私表。

## SemVer and port findings

- `SpecObject.behavior_fingerprint` 排除版本、状态、标题等字段，但不排除 `body.allowed_paths`；修正路径会改变行为哈希，因此不能用 PATCH。
- 同 ID 的 MINOR 后继虽技术上允许行为变化，但旧 `ATC-TOY-004@1.0.0` 是不可改写的 approved 完成证据；再批准同 ID 会触发 strict 多 approved 警告。应优先创建新的稳定 remediation Story，而不是修改或退役 v1。
- 平台已有正确的 `IPostgresTransactionAccessor`，Toy 其他持久化也已采用，结论持久化可直接对齐。
- 仓库没有通用“验证重认证签署”的公共端口；Report 运行时仅绑定版本化 `ReauthenticationRef`、显式 signing intent 与内容哈希，不替外部身份系统验证真实性。
- Result 公共契约已有 `IResultAdoptionPort` 和 `RecordedBy` 字段，但尚未确认该端口是否返回 adopted result 的录入人；需要读取接口与适配器后决定是否兼容扩展。
- `IResultAdoptionPort` 只返回 ALLOWED/BLOCKED/UNKNOWN 与采用版本，不返回 recorder；Result DTO 虽含 `RecordedBy`，没有面向 Toy 的版本化证据查询端口。
- `CreateTestedScopeConformityConclusionRequest` 当前没有 `ReauthenticationRef`、signing intent 或待签内容哈希；服务注释明确“proceed without signature verification”，持久化固定写 NULL。这不是可接受的生产默认值。
- DEV-029/030 已在仓库路线图预留给纺织预处理/结论；`ATC-TOY-005` 与 `DEV-031` 在 spec、planning、docs 均未分配。修复工作采用新稳定 `ATC-TOY-005@1.0.0`、实现任务 `DEV-031`，依赖并补完 DEV-027，而不复用预留 ID。
- 新卡需要兼容扩展 Toy 结论请求：固定版本的重认证引用、显式 signing intent 与内容哈希；Result 侧需要只读公共证据端口，UNKNOWN/缺失必须失败关闭。
- `ITransactionCoordinator` 现行接口只有 `ExecuteAsync(Func<CancellationToken, Task>)`；Toy 结论服务不仅 Store 参数过时，`BeginAsync/CommitAsync/RollbackAsync` 整套事务编排也已失效，需整体对齐当前模块模式。
- Result 模块的 `LoadGroupAsync` 已能在模块内部重建 observations/derivations/adoptions 及 `RecordedBy/AdoptedBy`；可在 Result 所有权内实现只读证据端口，无需 Toy 访问 `result.*` 私表。
- 证据端口必须同时绑定组织、ResultGroup 稳定 ID、期望 group/adoption 版本、采用 target 与规则集；只按自由字符串查询 recorder 会丢失版本/对象范围并产生跨租户风险。
- `BUS-TOY-006@1.0.0` 要求每个 TestUnit 固定且批准 `coverageDecisionRef@version`；当前领域代码错误地把 coverage decision 当可选，必须在修复测试中先形成 RED 再失败关闭。
- 现有结论授权使用空的 `ToyObjectContext("", "")`，无法证明 legal entity/laboratory 对象范围。Result 证据端口应返回其受控对象范围；ITEM/TESTED_SCOPE 必须先核对所有采用结果属于同一可访问范围，再进行 Toy capability 授权。
- Result 当前只有 `result.record` capability。为避免在本修复中发明新的许可语义，证据端口可复用现有对象授权并因此更严格地失败关闭；后续若要分离只读证据能力，必须独立权限规格。
- Toy 的 GET 服务目前仅按 organization 过滤，没有显式 Toy capability/object authorization；remediation 应加入与创建事实一致的可信对象范围和能力校验，并覆盖权限测试。
- Toy 项目当前未引用 `OpenLIMS.Contracts.Result`；实现公共证据端口消费需要新增该 ProjectReference，并机械刷新直接/传递 NuGet lock。
- API catalog 已按 Result → Toy 顺序注册，DI 可由 Result 模块先注册 evidence port、Toy 后消费；应继续使用 `TryAdd` 和公共契约，避免模块实现引用。
- Worker catalog 完全缺少 Toy，尽管 `ToyModule` 实现 `IOpenLimsWorkerModule/IOpenLimsMigrationModule`；当前 `--apply-module-migration toy` 无法从 Worker 执行。修复卡应把 Toy 注册到 Worker 并增加项目引用/锁文件，同时用架构/仓库测试固定。
