# Findings & Decisions: DEV-005 隔离和身份评估

## Initial findings

- DEV-004 已通过 PR #4 Squash 合并到 `main`，合并提交为 `824a10332851d918ff620e198ef604075214602b`。
- 当前开发分支为 `codex/dev-005-isolation-identity-assessment`。
- 现有规格把主题拆为两张旧卡：`ATC-REC-003`（身份评估前统一隔离门禁）和 `ATC-REC-004`（身份评估事实与结论）。
- `OPS-RECEIPT-003`、`OPS-IDENTITY-001/002/003` 和相关验收规格目前可见的版本仍是 `0.1.0`，需要用门禁确认状态和未决语义。
- 必须保持单集团独立部署、集团多机构；不得引入共享 SaaS 多租户。
- 规格门禁已确认：`ATC-REC-003@1.0.0` 与 `ATC-REC-004@1.0.0` 均为 BLOCKED，当前不能编码。
- 多数依赖已有批准替代版本：`ATC-REC-001@2.0.0`、`ORG-STRUCT-001@1.0.0`、`OD-009@1.0.0`、`SEC-AUTH-001@1.0.0`、`SEC-AUD-001@2.0.0`、`NFR-ARCH-001@2.0.0`；新卡应精确引用这些版本。
- 真正未批准的是 `OPS-RECEIPT-003`、`OPS-IDENTITY-001/002/003`、`AC-REC-001`、`AC-ID-001` 和 `OD-005` 中涉及的状态、证据、结论、异常与放行边界。
- 当前实现只有 Receiving 和 Labeling 模块；不存在拆解、制样或检测分配模块，因此 DEV-005 可以交付版本化统一资格端口和契约门禁，但不能伪称已经接入尚不存在的三个下游命令。
- DEV-006 明确负责异常、条件接收和拒收；DEV-007 明确负责受控解除隔离。因此 DEV-005 的 MATCHED 结论不能直接放行，MISMATCHED/INDETERMINATE 也不应提前实现完整异常审批矩阵。
- `OD-009@1.0.0` 已批准 ReceivedItem 为完整销售玩具或套装，身份评估粒度可以直接绑定 ReceivedItem。

## Questions under review

1. DEV-005 应以一张组合 Story 交付，还是以隔离门禁为前置、身份评估为主切片？
2. 哪些状态、结论、证据、权限和异常联动已有批准语义？
3. 哪些下游操作端口必须在本切片内真正接入门禁，避免只实现一个孤立状态字段？

## Recommended package boundary

- DEV-005 作为一个任务包，内部顺序为：Receiving 统一资格端口 → 身份观察/结论 → 前端工作台和端到端证据。
- 资格端口支持拆解、制样、检测分配三个动作枚举，但在 DEV-007 发布明确 ReleaseDecision 前始终返回 BLOCKED；未知状态返回 UNKNOWN 并按 BLOCKED 处理。
- 身份观察、客户声明快照和身份结论三层独立、追加版本化；任何结果都不覆盖登记数据。
- 身份结论仅为 MATCHED、MISMATCHED、INDETERMINATE；MATCHED 仍保持隔离，后续由 DEV-007 放行。
- MISMATCHED/INDETERMINATE 在本卡记录冲突事实并发出幂等 Outbox 事件，完整异常对象和审批留给 DEV-006。

## Implementation architecture findings

- Receiving 当前是单项目模块，API、领域规则、PostgreSQL 持久化和迁移均位于 `src/modules/receiving/OpenLIMS.Modules.Receiving`；DEV-005 可在该模块追加身份评估文件和迁移，无需创建越界模块。
- `ReceivingModule` 已统一注册 API、Worker 和迁移依赖；新身份评估服务、持久化、公共资格端口应从此处注册，并把新迁移追加在 DEV-003/004 迁移之后。
- 公共 Receiving 契约现有登记和标签身份两个文件；任务卡允许 `contracts/receiving/**`，可新增独立 `IdentityAssessmentContracts.cs`，避免污染既有已发布契约。
- 当前 Web 收样功能集中在 `ReceivingRegistrationView.vue` 及同目录客户端/权限文件；DEV-005 工作台可以在同一 feature 下新增客户端与组件并从现有收样页面进入。
- 当前 Receiving 授权端口按集团、法人、实验室、客户、委托单和 capability 做服务端精确声明校验；身份评估可复用此请求模型，新增 `receiving.identity.evaluate` 与 `receiving.eligibility.evaluate` 能力，避免建立第二套范围算法。
- HTTP 端点统一把领域错误映射为 Problem Details，并用 correlationId 串联；身份评估端点应沿用同一模式，同时对对象不存在/无权统一返回不泄露对象的错误。
- 登记服务用 `ITransactionCoordinator` 包裹领域事实、审计与 Outbox，并在 Npgsql 故障时失败关闭；DEV-005 服务应采用相同事务结构和独立失败尝试审计。
- 当前登记契约版本为 `ReceivingContract.Version = 1.0.0`，新公共资格端口需拥有独立的规则集版本常量，且不得从运行中对象推断“最新版”。
- PostgreSQL 基线已包含 `received_item`、状态历史、`audit_pending`、`audit_attempt` 与 `outbox`；DEV-005 应新增独立追加迁移，复用现有审计/Outbox 表而不改写已发布迁移。
- Receiving 单元、契约和 PostgreSQL 集成测试项目都直接引用模块与公共契约，且模块已向三类测试开放 internal；新增测试不需要改项目边界或引入测试专用生产接口。
- `received_item` 已保存客户声明的型号、批次、颜色等登记字段，可在首次观察事务中固定不可变声明快照；不得让后续登记数据更新覆盖该快照。
- 迁移采用单独 `ApplyAsync`、事务和 `openlims.receiving.migration` advisory lock；新迁移应命名为后续追加版本并写入同一 `migration_history`，同时通过 `create table if not exists` 与唯一约束保证重复执行安全。
- Label identity 已把 ReceivedItem 的组织范围、对象版本和 QUARANTINED 状态投影到公共端口；身份评估读取仍应以主 `received_item` + receipt 范围为权威，并在结论事务内锁定对象版本，避免依赖标签投影的陈旧值。
- `ITransactionCoordinator` 通过 AsyncLocal 暴露同一 Npgsql connection/transaction；Receiving 仓储可安全在同一事务写身份事实、审计和 Outbox，但必须显式要求 active transaction。
- 现有 `ReceivingRegistrationStore.InsertAuditAndOutboxPairAsync` 强耦合登记 `ReceiptPlan`，DEV-005 应实现面向 ReceivedItem 范围快照的独立审计/Outbox 写入，避免伪造登记计划。
- 现有失败尝试审计 writer 把 command type 固定为 `RegisterReceipt`；DEV-005 需要扩展它接收明确 command type，或新增身份评估专用 writer，才能正确区分读取、观察、结论、门禁和拒绝证据。
- 当前 Web 页面仅在刚登记成功后持有 ReceivedItem id/version；将 DEV-005 三栏工作台挂到每个登记结果项，可提供最小可操作闭环，同时不虚构尚不存在的跨批次对象搜索。
- 前端权限采用 profile capability 的纯函数判定；应新增身份评估能力判断并保持无权限时只读/禁用，而服务端继续执行完整范围授权。
- 契约测试通过替换模块 service 测试 HTTP 解析、错误映射和 OpenAPI；DEV-005 可新增独立 service interface 与 stub，在无 PostgreSQL 环境下完整覆盖三个 API 操作。
- 锁定 .NET SDK 位于 `%LOCALAPPDATA%/OpenLIMS/dotnet`（10.0.302）；后续验证应把该目录前置到 PATH，保持 `global.json` 不变。
- 现有 Receiving PostgreSQL 测试通过真实模块 DI、固定组织/actor/clock/authorization 运行，并在每例前按依赖顺序 truncate；DEV-005 集成测试可复用同一 provider，先登记一个 ReceivedItem 再执行观察、结论和资格查询。
- 现有授权单测已覆盖多维声明；需增加产品类别 claim 的允许/拒绝用例，证明 DEV-005 新增范围不会静默放宽权限。
- Vue 测试使用 `@vue/test-utils`、hoisted Vitest mock 和轻量 Ant Design stub；身份面板测试应 mock API 客户端与 auth-store，直接验证三栏、差异高亮、版本提交和“仍在隔离”文案。
- 前端客户端测试惯例要求校验 Bearer token、精确路径、稳定服务端错误码以及请求体不包含 `organizationGroupId`；DEV-005 客户端沿用此门禁。
- 当前变更全部落在 `ATC-REC-003@2.0.0` 的允许范围：Receiving 公共契约/实现/API Host、Receiving 三类测试、Web、批准规格及生成输出、仓库契约测试和任务计划；未修改 Labeling 私有实现或已发布迁移。
- 架构测试会扫描 Receiving 源码中的 SQL schema 引用并要求全部为 `receiving`；DEV-005 新持久化只访问该私有 schema，跨模块消费者仅能通过 `IReceivingEligibilityPort` 公共契约调用。
