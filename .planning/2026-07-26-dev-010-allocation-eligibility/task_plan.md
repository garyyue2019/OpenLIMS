# DEV-010 任务分配资格

## 目标

在不推进发布、Seal 或部署的前提下，从 PRD 收敛 `ATC-ALLOC-001` 的最小业务切片；只有任务卡经用户明确批准且 `ready` 返回 READY 后，才实现 TestObjectAllocation 的版本固定分配事实、跨模块资格门禁（Scope 资格端口 + Quantity 可用量端口）和失败关闭边界。

## 阶段

1. [completed] 完成规格、来源、影响、分配语义、跨模块组合约束和实现边界核对。
2. [completed] 确认分配最小口径并取得用户对精简 DEV-010 业务基线的明确批准。
3. [completed] 创建批准的后继规格与任务卡，生成派生物并使精确版本任务卡返回 READY。
4. [completed] 在 allowed_paths 内实现领域逻辑、公共契约、持久化/API、审计和跨模块资格校验。
5. [completed] 补齐正向、反向、边界、权限、并发、恢复、审计和回归测试。
6. [in_progress] 执行完整门禁，确认二次生成 written=0，并等待提交/推送指令。

## 约束

- 本任务不修改 Release baseline，不创建 Seal、tag、GitHub Release 或部署。
- PRD 只读；`generated/spec/` 只允许由生成器写入。
- 未知适用性、规则、版本或权限默认阻断。
- OPS-ALLOC-004：SampleIdentityAssignment、TestObjectAllocation、CoverageDecision 是独立关系；分配不得改写实物实际身份。
- 只消费其他模块的版本化公共端口，不读取 quantity/scope/receiving 私有表。
- 不在任务卡 READY 前开始业务编码。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| `ATC-ALLOC-001@1.0.0` 不存在，ready 返回错误 | 1 | 作为预期缺口记录；先依据现有来源起草任务卡，等待用户明确批准。 |
