# DEV-009 不可变数量流水与并发预留

## 交付范围

DEV-009 提供最小的数量账户与不可变流水能力。一个数量账户固定绑定：

- 一个对象引用（类型、稳定 ID、版本，锚定 OD-009 收到实物粒度）；
- 一个计量维度（`COUNT`、`MASS`、`LENGTH`、`AREA`、`VOLUME`）；
- 一个单位、一个精度（小数位）和一个守恒公差。

本任务不实现 `TestObjectAllocation` 任务分配、物理谱系、保管责任链、复合样投入比例、制样守恒完成门禁（AC-QTY-002 全量语义）、跨维度/跨单位换算或前端工作台。

## 计量口径（OD-010）

- 本切片禁止一切跨维度和跨单位换算（OPS-QTY-003 最严格实现）。
- `COUNT` 维度精度固定为 0 位小数；其余维度精度为 0~6 位。
- 对象声明为不可合理计量时拒绝建账（BP-018），返回 `QTY.NOT_QUANTIFIABLE`，不制造伪精确数量。
- 守恒公差在建账时固定，本切片仅保存配置，制样守恒门禁属后继任务。

## 过账与版本

唯一业务能力是 `quantity.post`。调用者还必须同时具备账户对象范围内的法人、实验室、客户、委托和产品类别访问权。客户端不能提交或覆盖 `OrganizationGroup`。

建账创建 `ACCOUNT@v1`；每笔过账必须提交精确 `expectedCurrentVersion`，并把账户版本推进到 `vN+1`。已过账条目在领域层与 PostgreSQL 触发器两层禁止更新或删除。

条目类型：`RECEIPT`、`OUTPUT`、`RETURN`（增加余额）；`RESERVE`、`RESERVE_RELEASE`（预留生命周期）；`ALLOCATE`、`CONSUME`、`LOSS`、`DISPOSE`(减少余额)；`REVERSAL`、`RESTATE`（更正链）。

## 预留与守恒

- 余额 = 已过账合计；可用量 = 余额 − 活跃预留；两者均不得为负。
- `RESERVE` 建立整额持留；一个持留只能被一笔 `RESERVE_RELEASE` 或一笔携带 `reservationId` 的消耗性条目按原额关闭。
- 减少性过账在事务内校验可用量；负余额和超分配返回 `QTY.INSUFFICIENT_BALANCE`。
- 并发过账依赖账户级 advisory lock 加 `expectedCurrentVersion`；同版本并发提交最多一笔成功（AC-QTY-001）。

## 冲销与重记（OPS-QTY-004）

- `REVERSAL` 必须引用未被冲销、无预留关联的业务条目，金额与原条目一致，效果为精确逆向；开放的 `RESERVE` 可被冲销关闭。
- `RESTATE` 必须引用一笔 `REVERSAL`，按原条目方向以更正金额重记；一笔冲销只能重记一次。
- 更正不修改任何历史行，全部通过追加完成。

## API 与公共端口

- `POST /api/v1/quantity-accounts`
- `POST /api/v1/quantity-accounts/{id}/entries`
- `GET /api/v1/quantity-accounts/{id}`
- `GET /api/v1/quantity-accounts/{id}/availability`
- `IQuantityAvailabilityPort`（合同版本 `1.0.0`）

公共可用量端口只接受精确账户版本和 `SAMPLE-QUANTITY@1.0.0`。当前版本且可用量充足返回 `ALLOWED`；账户不存在、请求非法或可用量不足返回 `BLOCKED`；旧版本或未知规则版本返回 `UNKNOWN`。`UNKNOWN` 不得被下游当作允许。

## 审计、Outbox 与失败恢复

账户/流水事实、平台审计意图和 `QuantityAccountCreated.v1` / `QuantityEntryPosted.v1` Outbox 事件在同一 PostgreSQL 事务中提交。Audit 或 Outbox 写入失败时，业务事实全部回滚。

校验失败、越权、版本冲突和事务回滚通过 `quantity.audit_attempt` 独立追加。目标只保存 SHA-256 哈希，不保存账户 ID 或业务正文；若失败审计本身不可写，操作继续失败关闭。

## 迁移与验证

部署时先应用平台迁移，再通过 Worker 应用 Quantity 迁移：

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration quantity
```

本任务不创建 Seal、tag、GitHub Release 或部署。PostgreSQL 集成测试要求 `OPENLIMS_TEST_POSTGRES_CONNECTION` 指向隔离的合成测试数据库：

```powershell
dotnet test tests/integration/quantity/OpenLIMS.Quantity.IntegrationTests/OpenLIMS.Quantity.IntegrationTests.csproj -c Release
```
