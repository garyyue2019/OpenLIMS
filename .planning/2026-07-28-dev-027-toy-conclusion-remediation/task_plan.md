# Task Plan: DEV-027 Toy 结论运行时修复与验收

## Goal

在不改写已批准 `ATC-TOY-004@1.0.0`、不访问其他模块私表、不伪造签署或 SoD 证据的前提下，为同一 DEV-027 创建 SemVer 后继任务卡，修复 Toy 结论运行时的编译与事务边界，补齐 TC-TOY-004-01～08、权限、审计/Outbox、恢复和不可变性验证，使主干和 DEV-028 的全解决方案门禁恢复。

## User approval

- 2026-07-28 用户在收到“先隔离 DEV-028，再创建 DEV-027 后继修复卡并完成 Toy 修复”的明确建议后回复：`按你建议的做`。
- 授权仅覆盖受控修复、真实路径和自动化验证；不代表生产部署、外部签名服务已存在，也不允许把 UNKNOWN 签署或结果录入人证据默认为通过。

## Current Phase

Phase 2: test-first remediation

## Phases

### Phase 1: successor governance and READY

- [x] 从干净 main 建立 `codex/dev-027-toy-conclusion-remediation`
- [x] 运行 validate、source-status、impact 和 `ready --story ATC-TOY-004@1.0.0`
- [x] 审查 v1 任务卡路径、行为哈希和现有公共端口，选择新稳定 remediation Story（ATC-TOY-005 / DEV-031）
- [x] 创建经用户授权的 `ATC-TOY-005@1.0.0 / DEV-031`，声明真实 allowed_paths、测试和失败关闭边界
- [x] 通过 strict validate、source-status、impact、ready、history、generate/check 并追加最终 snapshot
- **Status:** complete

### Phase 2: test-first remediation

- RED coverage now exists for Result evidence integration, Toy HTTP contracts, and Toy PostgreSQL runtime/rollback/authorization/recovery boundaries. Result evidence integration is already GREEN; Toy remains RED at the obsolete transaction implementation.

- [ ] 先建立编译、领域、HTTP、权限、SoD、签署、事务、审计/Outbox、恢复和不可变性 RED 测试
- [ ] 确保 RED 只指向生产缺口，不以测试桩掩盖默认运行时行为
- **Status:** complete

### Phase 3: implementation

- [ ] 用平台当前 `IPostgresTransactionAccessor` 替换不存在的事务 token
- [ ] 通过版本化公共端口取得结果录入人和重认证签署决定；UNKNOWN 必须失败关闭
- [ ] 完成结论服务、持久化、端点、迁移和模块接线修复，不访问 Result 或其他模块私表
- **Status:** complete

### Phase 4: verification and DEV-028 unblock

- [ ] 运行 Toy unit/contract/PostgreSQL integration、architecture/contracts 和 full-solution task gate
- [ ] 运行严格规格、来源、影响、ready、历史、双 generate/check、Python 全量测试
- [ ] 审计全部路径均位于后继 Story allowed_paths，并创建修复提交
- [ ] 返回 `codex/dev-028-textile-runtime`，纳入 Toy 修复后完成 DEV-028 全门禁
- **Status:** in_progress

## Constraints

- 不编辑 PRD 来源文档或直接编辑 `generated/spec/**`。
- 不修改 `ATC-TOY-004@1.0.0`、已发布迁移、快照或 Seal；新治理语义必须使用 SemVer 后继文件。
- 不定义不存在的签署默认值；缺失、UNKNOWN 或版本不匹配一律阻断 TESTED_SCOPE_CONFORMITY。
- 不查询 Result 私表；只允许版本化公共端口或显式批准的事件/契约。
- 不为通过测试降低门禁、删除失败证据或绕过追加式审计。

## Errors Encountered

- Attempt 1: system `dotnet` resolves SDK 9.0.305, but `global.json` requires 10.0.302. Resolution: locate and use the repository-configured/bundled .NET 10 SDK; do not weaken or edit `global.json`.
- Planning update attempt 1 used a mojibake table row as patch context and did not match. Resolution: anchor updates on stable ASCII headings.
- Result conclusion-evidence integration attempt 1 failed with `RES.ADOPTION_RULE_REQUIRED`. Resolution: create the required version-pinned adoption rule before adopting the observation and update expected group versions.
- Toy contract suite attempt 1 passed 31/32 but OpenAPI did not contain `createItemConformityConclusion`; diagnose the generated document and endpoint metadata before changing the assertion. The build also surfaced a missing Textile project reference inherited from the isolated DEV-028 work and must be reconciled only within the approved handoff path.
- Direct hidden-process OpenAPI diagnostic attempt was blocked by the execution policy. Resolution: make the contract assertion parse and report the generated operation-ID collection directly, avoiding a background process.
- Locked restore attempt 1 correctly rejected changed project-reference graphs (`Toy -> Result`, `Worker -> Toy`, API consumers). Resolution: run an unlocked `--force-evaluate` restore to refresh approved `packages.lock.json` files, then rerun locked restore.
- Final Python gate attempt 1 has two governance assertion failures: the approved-delivery reference inventory omits OD-034/BUS-TOY-006/AC-TOY-002/ATC-TOY-004/005, and ATC-TOY-005 directly names trusted organization context without a direct OD-002 dependency. Resolution requires a seal audit before changing any approved spec; update stale inventory assertions, and only amend the Story if history proves it is not sealed.
- Python gate attempt 2 reduced to one failure: legacy approved `AC-TOY-002@1.0.0` has no embedded `approval_evidence`, so the newly expanded inventory cannot blindly apply the newer evidence assertion. Audit the five Toy specs and their Seal membership; preserve sealed history and encode an explicit legacy exception only if required.

| Error | Attempt | Resolution |
|---|---:|---|
| 既有 `ATC-TOY-004@1.0.0` allowed_paths 指向不存在的旧目录，而已合入实现位于 `src/modules/toy/**` | 1 | 不改写 v1；先创建 SemVer 后继任务卡授权真实路径，再修改实现 |
| 既有 Toy 结论持久化引用全仓不存在的 `ITransactionToken`/`NpgsqlTransactionToken` | 1 | 作为生产 RED 保留；后继卡 READY 后改用平台现行 ambient transaction accessor |
| 首次并行检索 SemVer 与公共端口时，一个无匹配 `rg` 以 1 退出，使组合调用只返回 SemVer 侧结果 | 1 | 不重复组合失败；分开读取 history/models 实现，并让探索性 `rg` 的无匹配不遮蔽其他输出 |
| 原计划拟用 ATC-TOY-004 SemVer 后继，但 immutable approved v1 与 strict 单-current-version门禁冲突 | 1 | 保留 v1 原字节；DEV-029/030 已在路线图预留，采用尚未分配的 ATC-TOY-005@1.0.0 / DEV-031 作为修复卡 |
| `ready --story ATC-TOY-005@1.0.0` 在创建前返回 Story 不存在 | 1 | 作为预期编码阻断证据；先创建并验证用户授权的新稳定 remediation Story，READY 前不改生产代码 |
| 并行执行 Toy/Result unit RED 时组合调用在 Toy 失败后只回传首个结果 | 1 | Toy RED 已有效；Result 改为独立执行并取得只缺新 evidence contract/rules 的纯 RED，不重复组合调用 |
| 读取 Result rules 时猜测文件名 `ResultRules.cs`，实际规则位于 `ResultDomain.cs` | 1 | 用 `rg --files` 确认真实路径后读取，不重复猜测路径；无文件被修改 |
