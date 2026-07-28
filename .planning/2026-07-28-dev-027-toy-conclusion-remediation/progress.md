# Progress Log: DEV-027 Toy 结论修复

## Session: 2026-07-28 remediation

- 用户授权按建议顺序执行。
- DEV-028 已保存为 `codex/dev-028-textile-runtime` 分支提交，工作树回到干净 main 基线。
- 已创建 `codex/dev-027-toy-conclusion-remediation`。
- 开工门禁通过：validate 189/389、source current、impact empty、`ATC-TOY-004@1.0.0` READY。
- 当前进入 Phase 1：先确认 SemVer 与现有公共端口，再创建后继任务卡；READY 前不改生产代码。
- 初次并行探索确认 schema 允许 patch/minor/major，history 会按 change_class 与行为哈希校验；公共端口侧的组合检索因无匹配退出，未修改生产文件，下一步分开读取实现。
- 已确认 allowed_paths 属于行为哈希，PATCH 不合法；同 ID 再批准会与不可改写 v1 形成 strict 多 approved，治理方向调整为新稳定 remediation Story。平台事务 accessor 可复用；签署验证公共端口不存在，Result adoption 端口需继续核对。
- 已选择未分配的 `ATC-TOY-005@1.0.0` / `DEV-031`，避开已预留的 DEV-029/030。现有 Result adoption 端口不足以提供 recorder 证据，Toy tested-scope 请求也缺签署绑定字段；新卡将显式增加最小公共证据契约并坚持 UNKNOWN 失败关闭。
- 进一步确认 Toy 服务的 Begin/Commit/Rollback 也已与平台 `ExecuteAsync` 不兼容。Result 能在自身模块内重建带 recorder 的版本化证据，remediation 将新增 Result-owned 只读公共端口，而不是跨模块读表。
- 规格/领域对照又发现 coverage decision 被错误放宽为可选、结论授权使用空对象范围、GET 缺显式对象授权。新 Story 将把这些列为现有批准语义的修复，不新增宽松默认值。
- 宿主/项目引用审查发现 Toy 未消费 Result 公共契约，且 Worker 未注册 Toy migration。API 的 Result→Toy 注册顺序可支持公共端口 DI；Worker 接线与锁文件将纳入 ATC-TOY-005 allowed_paths。
- 创建前 `ready --story ATC-TOY-005@1.0.0` 按预期返回 Story 不存在；该 RED 证明尚无编码授权，下一步先添加并严格验证新卡。
- 已创建并由用户授权 `ATC-TOY-005@1.0.0 / DEV-031`。治理门禁通过：strict validate 190/389、source current、READY、history passed；首次 generate 写 10，check 通过，创建不可覆盖 `dev-031-toy-conclusion-remediation-final` snapshot；二次 generate `written=0 unchanged=130`，impact 归零。
- Phase 1 完成，进入 Phase 2 test-first remediation；从现在起生产代码修改受新 Story 的真实 allowed_paths 授权。
- Toy Release/warnaserror 编译 RED 稳定复现且无测试噪声：仅 `ToyConclusionPersistence.cs` 第 18、69 行缺失 `ITransactionToken`（0 warnings/2 errors）；修复 Store 签名后预期会继续暴露 Service 的过时 Begin/Commit/Rollback API。
- 已添加 Toy 结论领域和 Result evidence rules 的先失败单元测试。Toy unit RED 仍被生产 token 缺失阻断；Result unit RED 纯指向缺失 `ResultConclusionEvidenceRequest/Decisions/Reasons` 与 `EvaluateConclusionEvidence`，符合 test-first 预期。
- 新增 Result-owned `IResultConclusionEvidencePort@v1` 契约形状和纯规则：精确 adoptionVersion 解析 observation/derivation RecordedBy 与对象范围，缺 adoption BLOCKED，未知规则/组/target UNKNOWN。Result unit 10/10 通过。
- Toy 契约兼容增加重认证引用、signing intent、signed content hash 与新错误码；领域新增规范哈希、强制 coverage decision、外部证书 informational、UNKNOWN SoD 失败关闭。Toy unit 尚由既有 persistence 编译 RED 阻断。

## Persistence and migration review

- Confirmed the existing Toy persistence and service layers are still wired to obsolete transaction APIs and cannot compile.
- Confirmed Result evidence must be resolved through `IResultConclusionEvidencePort` before object authorization and conclusion evaluation.
- Confirmed remediation must add a new migration version; the published `20260728_002_toy_conclusion` migration will remain byte-for-byte unchanged.
- Selected the next Toy migration version: `20260728_004_toy_conclusion_remediation`.
- Next: add Result-port integration coverage and Toy HTTP/PostgreSQL RED tests, then replace persistence/service wiring and append the remediation migration.

## Test Results

- Environment RED: system `dotnet test` cannot start because only SDK 9.0.305 is on the default PATH while `global.json` requires 10.0.302. The repository SDK/runtime must be used.
- Resolved environment RED: `C:\Users\Administrator\.dotnet\dotnet.exe` provides exactly SDK 10.0.302 and will be used for all .NET restore/build/test gates.
- Confirmed production RED with SDK 10.0.302: Toy unit compilation stops at `ToyConclusionPersistence.cs` lines 18 and 69 because obsolete `ITransactionToken` no longer exists.
- Result evidence integration test attempt 1 reached the real service and failed with `RES.ADOPTION_RULE_REQUIRED`; fixed the fixture by appending the required pinned adoption rule and advancing expected group versions.
- Result conclusion-evidence integration test now passes against PostgreSQL: persisted adoption resolves the exact target, original `RecordedBy`, object scope, and versions; a missing adoption version returns BLOCKED.
- Added Toy HTTP contract coverage for both POST endpoints, detail/history GET endpoints, response content/signature hashes, all conclusion error codes, and OpenAPI operation IDs.
- Added PostgreSQL RED coverage for ITEM/TESTED_SCOPE success, resolved evidence persistence, UNKNOWN/BLOCKED/exception, SoD, mixed scope, audit/outbox rollback, concurrent correlation retry, parent/child SQLSTATE 55000 immutability, mandatory coverage, and read authorization.
- Added explicit Result-contract references to the Toy runtime and integration test projects so Toy consumes only `IResultConclusionEvidencePort`, never Result private persistence.
- Replaced Toy conclusion persistence/service with platform ambient transactions, Result evidence resolution, exact object authorization, real RecordedBy SoD, signing binding, read audit, and correlation idempotency.
- Added append-only migration `20260728_004_toy_conclusion_remediation` and Worker Toy registration; published migration `20260728_002_toy_conclusion` remains unchanged.
- Toy unit suite is GREEN with SDK 10.0.302: 45/45 tests pass.
- Toy contract attempt 1: 31/32 passed. Only the OpenAPI operation-ID assertion failed because the generated document omitted the conclusion operation name; runtime POST/GET contract tests themselves passed. Diagnostic pending.
- OpenAPI diagnosis complete: the host document is a static catalog, not reflected endpoint metadata. Added the four Toy conclusion paths/operation IDs to the approved API-host surface.
- Toy HTTP contract suite is GREEN: 32/32 tests pass, including all four conclusion operations and stable error mappings.
- Toy PostgreSQL integration suite is GREEN: 29/29 tests pass, including rollback, SQLSTATE 55000, read authorization, Result evidence failure, SoD, mixed scope and concurrent idempotent retry.
- Updated repository inventory assertions to 190 specs / 44 tasks / 73 features and added the explicit Worker/API Toy-module architecture assertion.
- Locked restore is RED as expected after adding public project references; NuGet requested a force-evaluated lock refresh before locked-mode verification.
- Refreshed approved lock files with `--force-evaluate`; the subsequent full-solution `--locked-mode` restore is GREEN and stale Textile restore metadata is gone.
- Full `OpenLIMS.slnx` Release build with locked restore, `--no-restore`, and warnings-as-errors is GREEN: 0 warnings / 0 errors.
- Exact architecture verification profile is GREEN: 18/18 architecture tests pass, including explicit Toy registration in both production hosts and module-boundary checks.
- Exact contracts verification profile is GREEN across the solution; Toy contract suite remains 32/32.
- Exact Toy task profile is GREEN after locked restore and full build: unit 45/45, PostgreSQL integration 29/29, HTTP contract 32/32.
- Worker host successfully executed `--apply-module-migration toy` against the dedicated Toy test database, proving catalog discovery and idempotent migration sequencing.
- Phase 2 test-first remediation and Phase 3 implementation are complete; entering Phase 4 full governance/history/repository verification and DEV-028 handoff.
- Final governance validate/source/history and two deterministic generate passes are GREEN (`written=0 unchanged=130`), but Python gate attempt 1 failed two repository-governance assertions. Seal status audit is next; no approved spec will be edited blindly.
- Seal audit completed: ATC-TOY-005 is unsealed. Added the required direct OD-002 dependency, added the five approved Toy delivery refs to the repository inventory assertion, and reserved an append-only governance-correction snapshot path while preserving the existing final snapshot.
- Governance correction revalidated: strict/source/READY pass; impact is limited to ATC-TOY-005. Regeneration wrote 7 expected derivatives, appended `dev-031-toy-conclusion-remediation-governance-correction`, and the second generate is deterministic (`written=0 unchanged=130`).
- Python gate attempt 2: 40/41 pass; only the approval-evidence assertion for legacy `AC-TOY-002@1.0.0` remains. Seal/evidence audit pending.
- Evidence audit found no Release Seals and exactly one missing record. Expanded the repair card to the exact AC-TOY-002 path, backfilled the already-documented user approval without changing status/behavior, and reserved a second append-only correction snapshot.
- AC approval-evidence correction revalidated READY/source/strict; regenerated 7 expected derivatives, appended the second correction snapshot, and achieved deterministic second generate (`written=0 unchanged=130`).
- Python repository gate is GREEN: 41/41 tests pass.
- Result suites are GREEN: unit 10/10 and PostgreSQL integration 9/9, including the new conclusion-evidence port test.
- Change audit found no whitespace errors. All changed paths are inside the expanded ATC-TOY-005 allowlist; lock-file line-ending noise is being normalized before the final diff review.
- Lock normalization audit confirms only 19 lock files have semantic project-graph changes; status entries with empty diffs are mtime/index noise and will disappear after index refresh. No unrelated dependency versions changed.
- Final static audit passes: no obsolete conclusion transaction tokens/manual coordinator calls, no Toy access to Result private SQL, no signature-bypass TODO, published `ToyConclusionMigration.cs` is unchanged, and `git diff --check` is clean.
- Automated allowlist audit passes: all 61 changed/untracked paths match one of the 28 exact ATC-TOY-005 allowed-path patterns.
- Final completion gates are GREEN after the last governance correction: strict validate 190/389, source current, history passed, two generate passes at `written=0 unchanged=130`, spec check passed, and Python 41/41.
- Phase 4 is in progress: stage/review/commit DEV-031, then return to `codex/dev-028-textile-runtime` and run its full task gate with the Toy remediation integrated.

| Command | Result |
|---|---|
| `python -m tools.specgen validate` | PASS: 189 versions / 389 source entries |
| `python -m tools.specgen source-status` | PASS: SOURCE CURRENT |
| `python -m tools.specgen impact` | PASS: empty |
| `python -m tools.specgen ready --story ATC-TOY-004@1.0.0` | PASS: READY |
| `python -m tools.specgen validate --strict-warnings` after ATC-TOY-005 | PASS: 190/389 |
| `python -m tools.specgen ready --story ATC-TOY-005@1.0.0` | PASS: READY |
| governance generate/check/snapshot | PASS: first written=10, snapshot created, second written=0 |
| Toy module build before remediation | EXPECTED RED: CS0246 ITransactionToken at persistence lines 18/69 |
| Toy/Result unit tests before remediation contracts | EXPECTED RED: Toy transaction token; Result evidence contract/rules missing |
| Result unit tests after evidence contract/rules | PASS: 10/10 |
