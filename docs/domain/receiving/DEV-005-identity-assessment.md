# DEV-005 隔离与身份评估运行说明

## 交付边界

DEV-005 为每个 `ReceivedItem` 建立三层独立事实：客户登记声明快照、实验室观察和人工身份结论。结论仅允许 `MATCHED`、`MISMATCHED`、`INDETERMINATE`，任何结论都不会解除 `QUARANTINED`。

拆解、制样和检测分配通过公共 `IReceivingEligibilityPort` 查询同一门禁。调用方必须绑定 `REC-ELIGIBILITY@1.0.0`、ReceivedItem 对象版本和下列精确动作之一：

- `DISASSEMBLY`
- `SAMPLE_PREPARATION`
- `TEST_ASSIGNMENT`

在 DEV-007 提供有效受控放行决定前，已知状态始终返回 `BLOCKED`；未知规则、动作、对象版本或 Receiving 依赖返回 `UNKNOWN`。调用方必须把 `UNKNOWN` 当作阻断，不能使用缓存的旧允许结果。

## API 与权限

- `GET /api/v1/received-items/{id}/identity-assessment`
- `POST /api/v1/received-items/{id}/identity-observations`
- `POST /api/v1/received-items/{id}/identity-decisions`

身份评估读写要求 `receiving.identity.evaluate`。公共资格查询要求 `receiving.eligibility.evaluate` 和明确调用用途。服务端同时校验部署集团、法人、实验室、客户、委托单、当前可收样证据和产品类别范围；客户端不能提交集团标识。

系统管理员身份不会自动获得上述业务能力。无权或不可访问对象的失败证据只保存目标哈希，不回显对象资料。

## 事实、版本与事务

追加迁移为 `20260725_003_identity_assessment`。它只新增下列表，不改写 DEV-003/004 已发布迁移：

- `receiving.identity_declaration_snapshot`
- `receiving.identity_observation`
- `receiving.identity_decision`
- `receiving.identity_assessment`

三个历史事实表通过数据库触发器拒绝 `UPDATE` 和 `DELETE`。后续观察或更正必须追加新版本；新观察会把当前评估重新置为 `IN_PROGRESS`，但不会删除旧结论。

每次观察或结论使用 `expectedItemVersion` 做行锁和乐观并发校验。事实、ReceivedItem 新版本、标签对象版本、当前评估投影、审计和 Outbox 在同一事务提交。审计或 Outbox 失败时整体回滚，失败尝试另写哈希安全审计。

## 证据要求

观察必须同时包含标签、型号、批次、外观，以及至少一个附件引用和对应 SHA-256。`MATCHED` 不能掩盖型号或批次差异；`MISMATCHED` 必须有实际关键字段差异；`INDETERMINATE` 使用 `IDENTITY_AMBIGUOUS` 原因码并填写人工理由。

页面把客户声明、实验室观察和人工结论分为三栏，逐项高亮型号/批次差异，历史版本只读。页面持续显示“仍在隔离”，不会提供解除隔离操作。

## 可观测性与恢复

- `identity_assessment_total` 只按结论聚合。
- `lab_execution_gate_total` 只按动作、决定和评估状态聚合。
- 指标标签不包含客户、委托单或对象标识。
- `UNKNOWN`、事务回滚和权限拒绝写结构化警告；日志使用 correlationId 关联 API、审计与 Outbox。

持久化不可用时 API 返回 503；资格端口返回 `UNKNOWN`。恢复数据库后，调用方必须用当前对象版本和固定规则版本重新查询，禁止把失败前的结果当作允许证据。
