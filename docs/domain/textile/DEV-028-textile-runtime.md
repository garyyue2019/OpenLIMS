# DEV-028 纺织样品需求与 CuttingPlan 运行时

## 治理边界

- 实施任务：`ATC-TEX-004@1.0.0`。
- 运行时启用决定：`OD-036@1.0.0`。
- 运行时要求：`BUS-TEX-006/007/008@1.0.0`。
- 运行时验收：`AC-TEXTILE-004@1.0.0`。
- `OD-001@1.0.0` 的玩具 + 物理机械唯一生产试点保持不变；DEV-028 只批准实现、自动化测试和受控验证，不构成生产部署、真实灯塔或正式 UAT 证据。

既有 `BUS-TEX-001/002/003@1.0.0` 和 `AC-TEXTILE-001@1.0.0` 保持原字节、原版本和原 DISABLED 契约基线。运行时通过新稳定 ID 追加，不改写冻结快照。

## 运行时契约

| 操作 | 路径 | 成功状态 |
|---|---|---:|
| 计算并保存样品需求 | `POST /api/v1/textile/sample-requirements` | 201 |
| 创建 CuttingPlan 草案 | `POST /api/v1/textile/cutting-plans` | 201 |
| 批准并冻结计划版本 | `POST /api/v1/textile/cutting-plans/{id}/versions/{version}/approval` | 200 |
| 查询固定计划版本 | `GET /api/v1/textile/cutting-plans/{id}/versions/{version}` | 200 |

创建/查询要求 `textile.sample-requirement.manage`；批准额外要求 `textile.cutting-plan.approve`。组织集团、行为人、法人和实验室范围来自可信请求上下文与 claims，客户端不能覆盖。

模块不默认允许或禁止创建人自批。若组织要求职责分离，必须由授权策略显式配置，Textile 不补业务默认值。

## 失败关闭规则

- 所有引用和规则集使用精确版本；不从运行对象解析最新版。
- 未知规则集返回 `UNKNOWN`，不得批准。
- 互斥破坏或破坏性共享以 `TEX.EXCLUSIVE_SHARE_REJECTED` 拒绝。
- 面积不足保存 `INSUFFICIENT`、完整缺口及 `TextileSampleShortageDetected.v1` Outbox 事件；CuttingPlan 可以保留草案证据，但批准以 `TEX.SAMPLE_REQUIREMENT_NOT_APPROVABLE` 拒绝。
- `expectedCurrentVersion` 冲突以 `TEX.EXPECTED_VERSION_CONFLICT` 拒绝。
- 业务事实、audit intent 或 Outbox 任一写入失败均整体回滚；主事务外追加 `textile.audit_attempt`。

## 持久化与边界

首个单调迁移为 `20260728_001_textile_runtime`，创建：

- `textile.sample_requirement`
- `textile.cutting_plan`
- `textile.cutting_plan_approval`
- `textile.audit_attempt`

需求计算、计划和批准均为追加式事实，数据库触发器拒绝 UPDATE/DELETE（SQLSTATE `55000`）。业务 Store 只使用平台 `IPostgresTransactionAccessor` 和 audit/outbox 公共端口，不访问 Receiving、Scope、Quantity、Allocation、Toy 或 Report 私表。

迁移由 Worker 执行：

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration textile
```

## 验证

聚焦自动化覆盖：

- 领域：确定性哈希、SUFFICIENT/INSUFFICIENT/UNKNOWN、固定需求引用、批准门禁和状态端口。
- HTTP：四路由、四个 operationId、关联 ID 和九类稳定 problem 状态。
- PostgreSQL：原子提交、缺口事件、权限、并发、恢复、audit/outbox 回滚、追加式约束和状态端口。
- 架构：Textile SQL 只访问 `textile.*`；API 与 Worker 显式注册模块。

PostgreSQL 集成测试使用独立数据库 `openlims_textile_test`，要求 `OPENLIMS_TEST_POSTGRES_CONNECTION` 指向隔离的合成测试实例。
