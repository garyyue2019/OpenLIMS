# DEV-010 任务分配资格

## 交付范围

DEV-010 提供最小的 `TestObjectAllocation` 分配事实与资格门禁能力。每条分配固定绑定：

- 一个物理对象引用（RECEIVED_ITEM / TEST_SPECIMEN / TEST_PORTION，锚定 OD-009 粒度）；
- 一个身份映射版本（SampleIdentityAssignment，只引用不改写，OPS-ALLOC-004）；
- 一个范围行（scopeMatrixId + 矩阵版本 + scopeLineId）与一个计划/序列步骤引用；
- 用途、顺序号、破坏性标志（调用方声明，不决定 OD-004 产品/方法级主数据）；
- 请求数量/维度/单位、可选数量预留引用、保存条件引用和有效期。

本任务不实现 UsageEvent、领用/实际消耗/归还、CoverageDecision、计划/任务/批次生成、资源排程或前端工作台。

## 三端口资格门禁（gate-then-commit）

平台禁止嵌套事务（`PLT.NESTED_TRANSACTION_NOT_SUPPORTED`），因此资格评估在分配事务**之外**完成，评估结果以版本固定快照写入分配事实：

| 端口 | 校验内容 | 动作/规则版本 |
|---|---|---|
| `IReceivingEligibilityPortV2` | 身份、隔离与放行状态 | `TEST_ASSIGNMENT`（语义即规格 TEST_OBJECT_ALLOCATION）/ `REC-ELIGIBILITY@2.0.0` |
| `IScopeProductionEligibilityPort` | 方法适用性 / 范围资格 | `SCOPE-LINE-GATE@1.0.0` |
| `IQuantityAvailabilityPort` | 可用量 ≥ 请求量 | `SAMPLE-QUANTITY@1.0.0` |

三个端口全部 `ALLOWED` 才允许提交；任一 `BLOCKED` 返回 `ALC.ELIGIBILITY_BLOCKED`，任一 `UNKNOWN` 或端口异常返回 `ALC.APPLICABILITY_UNKNOWN`，均不产生事实。端口决定、对象版本和规则版本原样固定进分配行。

跨模块只引用 `contracts/receiving`、`contracts/scope`、`contracts/quantity` 公共契约，不读取任何私有表。被消费端口按各自已发布契约校验既有能力（Receiving 放行批准、`scope.approve`、`quantity.post`）；Allocation 模块只新增 `allocation.assign` 单一能力。

## 并发与破坏性互斥

- 同一物理对象（组织 × 类型 × 稳定 ID）的分配序列以 advisory lock + `expectedCurrentVersion` 串行化，并发提交最多一笔成功。
- 存在活跃破坏性分配时，同对象一切新分配返回 `ALC.DESTRUCTIVE_CONFLICT`（AC-ELEC-003 / 10.7-8）；非破坏性分配可并存（AC-LIN-001）。
- 释放通过追加式 `RELEASED` 记录完成，每条分配至多一次；释放后对象恢复可分配。

## API 与公共端口

- `POST /api/v1/test-object-allocations`
- `POST /api/v1/test-object-allocations/{id}/release`
- `GET /api/v1/test-object-allocations/{id}`
- `GET /api/v1/test-object-allocations/{id}/status`
- `IAllocationStatusPort`（合同版本 `1.0.0`）

状态端口只接受精确对象分配版本和 `TASK-ALLOCATION@1.0.0`：当前活跃且未过期返回 `ALLOWED`；分配不存在、已释放或已过期返回 `BLOCKED`；旧版本或未知规则版本返回 `UNKNOWN`。`UNKNOWN` 不得被下游当作允许。

## 审计、Outbox 与失败恢复

分配/释放事实、平台审计意图和 `TestObjectAllocationAssigned.v1` / `TestObjectAllocationReleased.v1` Outbox 事件在同一 PostgreSQL 事务中提交；Audit 或 Outbox 失败时业务事实整体回滚。

校验失败、门禁阻断、越权、版本冲突和事务回滚通过 `allocation.audit_attempt` 独立追加，目标只保存 SHA-256 哈希。已过账行在领域层与数据库触发器两层禁止 UPDATE/DELETE。

## 迁移与验证

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration allocation
```

本任务不创建 Seal、tag、GitHub Release 或部署。PostgreSQL 集成测试要求 `OPENLIMS_TEST_POSTGRES_CONNECTION`，并自动使用专用数据库 `openlims_allocation_test` 与其他模块测试隔离：

```powershell
dotnet test tests/integration/allocation/OpenLIMS.Allocation.IntegrationTests/OpenLIMS.Allocation.IntegrationTests.csproj -c Release
```
