# DEV-008 ScopeLine 生产可用门禁

## 交付范围

DEV-008 提供最小的 `TestScopeMatrix`/`ScopeLine` 批准与生产资格判断能力。一个范围行固定绑定：

- 一个送检对象或特征；
- 一个目标市场和要求条款；
- 一个检测项目；
- 一个方法版本及方法选项；
- 一个样品要求、工作中心和报告位置；
- 一个 `EvaluationMode` 及其条件引用。

本任务不实现报价、合同、生产任务、`TestObjectAllocation`、`CoverageDecision` 或前端工作台，也不从其他模块私有表推导范围。

## 批准与版本

唯一业务能力是 `scope.approve`。调用者还必须同时具备矩阵对象范围内的法人、实验室、客户、委托和产品类别访问权。客户端不能提交或覆盖 `OrganizationGroup`。

初始批准创建 `APPROVED@v1`；后继批准必须提交精确 `expectedCurrentVersion`，并追加 `APPROVED@vN+1`。已批准的矩阵版本和范围行在领域层与 PostgreSQL 触发器两层禁止更新或删除。

没有草稿编辑、双人复核或多级签署流程。

## EvaluationMode

| 模式 | 条件 |
|---|---|
| `EVALUATED` | 必须固定限值规则和判定规则；不得携带免除或不评价理由。 |
| `MEASURED_ONLY` | 只报告测量结果；不得携带符合性判定字段。 |
| `NOT_EVALUATED` | 必须记录不评价理由；不得携带限值、判定或免除引用。 |
| `WAIVED` | 必须固定免除批准引用；不得携带限值或判定规则。 |

未知模式、未知规则版本、缺失引用和冲突字段全部失败关闭。

## API 与公共端口

- `POST /api/v1/scope-matrices`
- `POST /api/v1/scope-matrices/{id}/versions`
- `GET /api/v1/scope-matrices/{id}/versions/{version}`
- `GET /api/v1/scope-matrices/{id}/production-eligibility`
- `IScopeProductionEligibilityPort`（合同版本 `1.0.0`）

公共资格端口只接受精确矩阵版本和 `SCOPE-LINE-GATE@1.0.0`。当前完整批准版本返回 `ALLOWED`；矩阵不存在返回 `BLOCKED`；旧版本、未知规则或不完整数据返回 `UNKNOWN`。`UNKNOWN` 不得被下游当作允许。

## 审计、Outbox 与失败恢复

矩阵事实、平台审计意图和 `ScopeMatrixApproved.v1` Outbox 事件在同一 PostgreSQL 事务中提交。Audit 或 Outbox 写入失败时，矩阵和范围行全部回滚。

校验失败、越权、版本冲突和事务回滚通过 `scope.audit_attempt` 独立追加。目标只保存 SHA-256 哈希，不保存矩阵 ID 或业务正文；若失败审计本身不可写，操作继续失败关闭。

## 迁移与验证

部署时先应用平台迁移，再通过 Worker 应用 Scope 迁移：

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration scope
```

本任务不创建 Seal、tag、GitHub Release 或部署。PostgreSQL 集成测试要求 `OPENLIMS_TEST_POSTGRES_CONNECTION` 指向隔离的合成测试数据库：

```powershell
dotnet test tests/integration/scope/OpenLIMS.Scope.IntegrationTests/OpenLIMS.Scope.IntegrationTests.csproj -c Release
```
