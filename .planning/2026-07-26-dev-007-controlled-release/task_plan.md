# DEV-007 受控放行

## 目标

以最轻治理实现版本固定的正常放行与受限放行，并将放行结果通过公共资格端口提供给下游；不自动创建下游任务，不实现完整撤销工作流。

## 阶段

1. [completed] 核对旧任务卡阻断原因、来源状态、依赖和现有实现边界。
2. [completed] 追加批准的 DEV-007 后继规格并使 `ATC-REC-006@2.0.0` 返回 READY。
3. [completed] 在任务卡 `allowed_paths` 内实现放行领域逻辑、持久化、API、审计、Outbox 和资格端口 v2。
4. [completed] 补齐正向、反向、边界、权限、并发、恢复和回归测试。
5. [completed] 执行全部完成门禁，确认生成幂等，提交并推送分支。
6. [in_progress] 修复 PR #7 的 PostgreSQL 集成测试断言，重跑门禁与 CI，并在全绿后 Squash 合并。
7. [pending] 核验仓库发布入口与发布审批条件；仅在存在明确、已批准的发布目标时执行发布。

## 决策约束

- 只有 `MATCHED` 身份结论可放行。
- 无异常时为 `RELEASED`；全部异常均为有效条件接收时为 `RELEASED_WITH_CONSTRAINTS`。
- 允许动作取交集，禁止动作取并集；限制过期、UNKNOWN 或最终允许集为空时阻断。
- `OPEN`、`AWAITING_CUSTOMER`、`REJECTED`、`SAFETY_HOLD` 均阻断。
- 只使用 `receiving.release.approve` 单一质量能力，不引入多级签署。
- ReleaseDecision 不可变并固定所有输入版本；旧资格端口 v1 语义保持不变。

## 问题

- 仓库尚无明确 deploy/publish 工作流，Release 1 基线仍为 proposed；合并不等于生产发布。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| PR #7 `Application CI / verify` 失败：状态历史断言期望 2、实际 3 | 1 | 确认注册产生 RECEIVED、QUARANTINED，放行追加 ACCEPTED；将测试断言校正为 3。 |
| GitHub annotations 按钮点击超时 | 1 | 刷新 DOM 后直接读取已展开的测试日志，不重复同一失败操作。 |
| 本机未安装 `pwsh`，任务卡验证命令无法按字面启动 | 1 | 改用已存在的 Windows PowerShell 运行同一 `scripts/verify.ps1` 与相同参数。 |
| Windows PowerShell 能启动验证脚本，但本机仅有 .NET SDK 9.0.305，仓库要求 10.0.302 | 2 | 不修改 `global.json` 或降低 SDK 门禁；由已配置 .NET 10 与 PostgreSQL 的 PR CI 执行，先完成其余本地门禁。 |
