# DEV-007 受控放行

## 目标

以最轻治理实现版本固定的正常放行与受限放行，并将放行结果通过公共资格端口提供给下游；不自动创建下游任务，不实现完整撤销工作流。

## 阶段

1. [completed] 核对旧任务卡阻断原因、来源状态、依赖和现有实现边界。
2. [completed] 追加批准的 DEV-007 后继规格并使 `ATC-REC-006@2.0.0` 返回 READY。
3. [completed] 在任务卡 `allowed_paths` 内实现放行领域逻辑、持久化、API、审计、Outbox 和资格端口 v2。
4. [completed] 补齐正向、反向、边界、权限、并发、恢复和回归测试。
5. [in_progress] 执行全部完成门禁，确认生成幂等，提交并推送分支。

## 决策约束

- 只有 `MATCHED` 身份结论可放行。
- 无异常时为 `RELEASED`；全部异常均为有效条件接收时为 `RELEASED_WITH_CONSTRAINTS`。
- 允许动作取交集，禁止动作取并集；限制过期、UNKNOWN 或最终允许集为空时阻断。
- `OPEN`、`AWAITING_CUSTOMER`、`REJECTED`、`SAFETY_HOLD` 均阻断。
- 只使用 `receiving.release.approve` 单一质量能力，不引入多级签署。
- ReleaseDecision 不可变并固定所有输入版本；旧资格端口 v1 语义保持不变。

## 问题

- 暂无。
