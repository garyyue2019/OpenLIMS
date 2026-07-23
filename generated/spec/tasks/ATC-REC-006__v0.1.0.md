<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-006@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-006：受控解除隔离并发布执行资格

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@0.1.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-RELEASE` |
| 开发就绪度 | `blocked` |
| 变更级别 | `major` |
| 负责人角色 | 收样产品负责人, 质量负责人, 实验室运营负责人, QA负责人 |
| 影响模块 | receiving, identity, exception, outbox, lab-execution, audit, automated-test |
| 来源 | PRD-MAIN#OD-002, PRD-MAIN#OPS-RECEIPT-003, PRD-MAIN#OPS-IDENTITY-003, PRD-MAIN#RULE-026 |
| 固定依赖 | ATC-PLT-000@0.1.0, ATC-REC-003@0.1.0, OD-002@1.0.0, ATC-REC-004@0.1.0, ATC-REC-005@0.1.0, OPS-RECEIPT-003@0.1.0, OPS-IDENTITY-003@0.1.0, RULE-004@0.1.0, RULE-026@0.1.0, NFR-ARCH-002@0.1.0, OD-005@0.1.0 |
| 规格指纹 | `d385a6cf535d42ad537f39868e4ecf4760a320d5e3d079498b90afe5406c008e` |

## 业务结果

只有身份、异常和限制均满足批准规则的实物才获得明确执行资格；在制下游工作固定引用该资格版本，后续变化通过撤销或新决定传播。

## 主要参与者

具备收样放行能力授权的身份评估员或质量批准人

## 触发条件

授权人员在实物详情提交解除隔离或受限放行

## 前置条件

- ATC-REC-003 至 005 已交付
- OD-005 明确谁可放行及条件接收限制
- 对象身份结论为允许类型
- 所有阻断异常已有允许放行的决定或已关闭
- 发件箱和幂等消费者可用

## 正常路径

- 重新读取并锁定对象、身份结论、异常和限制的最新明确版本
- 计算 RELEASED 或 RELEASED_WITH_CONSTRAINTS
- 创建不可变 ReleaseDecision
- 条件更新 ReceivedItem 状态和版本
- 同一事务写入 outbox 事件
- 下游投影幂等更新执行资格
- 任务创建固定引用 ReleaseDecision 版本

## 失败路径

- 身份待定或错配无授权决定时拒绝
- 存在开放阻断异常时拒绝
- 适用性或限制解释为 UNKNOWN 时拒绝
- 对象版本变化时返回冲突并重新评估
- 发件箱写入失败时整体回滚
- 消费者重复投递不得重复创建下游动作

## 领域不变量

- 放行决定不可变且引用所有输入版本
- 状态变化不删除隔离历史
- 在制任务绑定 releaseDecisionId/version
- 规则更新不自动改变既有决定
- 撤销通过新事件和影响评估而非覆盖

## 数据契约

```json
{
  "event": [
    "eventId",
    "aggregateId",
    "aggregateVersion",
    "releaseDecisionId",
    "outcome",
    "constraintSummary",
    "occurredAt"
  ],
  "outcomeEnum": [
    "RELEASED",
    "RELEASED_WITH_CONSTRAINTS"
  ],
  "releaseDecision": [
    "receivedItemId",
    "itemVersion",
    "identityDecisionVersion",
    "exceptionDecisionVersions",
    "ruleSetVersion",
    "outcome",
    "constraints",
    "approvedBy",
    "approvedAt"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "IDENTITY_NOT_RESOLVED",
    "BLOCKING_EXCEPTION_OPEN",
    "RELEASE_APPLICABILITY_UNKNOWN",
    "RELEASE_NOT_AUTHORIZED",
    "EXPECTED_VERSION_CONFLICT"
  ],
  "operation": "POST /api/v1/received-items/{id}/release-decisions",
  "success": "200 ReleaseDecisionResult"
}
```

## 状态转换

- IDENTITY_ASSESSING或QUARANTINED -> ACCEPTED
- 满足批准限制时 -> CONDITIONALLY_ACCEPTED
- 放行失败保持原状态
- 后续撤销不删除原决定并触发影响评估

## 权限与职责分离

- 按 OD-005 角色、对象、产品、实验室和有效期授权
- 条件接收可能要求质量或EHS追加批准
- 发起人和最终批准人按矩阵职责分离
- 下游服务只消费最小资格事件

## 审计要求

- 记录放行尝试、输入版本、规则矩阵、批准链、结果和限制
- 失败阻断记录原因
- 事件发布和每次重放可追踪
- 撤销或失效引用原决定而不覆盖

## UX 状态

- 放行页展示身份结论、全部开放异常和限制摘要
- 任何 UNKNOWN 明确显示为阻断
- 条件接收显示允许/禁止动作和有效期
- 并发冲突要求刷新
- 历史决定和撤销链只读可查

## 可观测性

- 指标 receipt_release_total 按结果和实验室聚合
- 指标 receipt_release_blocked_total 按规则码聚合
- 发件箱积压和消费者死信告警
- 可通过 correlationId 串联决定、事件和下游资格投影

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-006-01 | positive | 身份匹配；无开放阻断异常；授权有效 | 提交放行 | 创建ReleaseDecision；状态变为ACCEPTED；发布一次幂等事件 |
| TC-REC-006-02 | boundary | OD-005允许条件接收；限制完整且批准链满足 | 提交受限放行 | 状态为CONDITIONALLY_ACCEPTED；事件携带限制摘要；下游按动作再次校验 |
| TC-REC-006-03 | negative | 存在未决定的身份冲突异常 | 尝试放行 | 返回BLOCKING_EXCEPTION_OPEN；状态和事件均不变 |
| TC-REC-006-04 | security | 用户无对应实验室或严重度授权 | 提交放行 | 服务端拒绝；记录审计 |
| TC-REC-006-05 | concurrency | 预览时可放行；提交前新增阻断异常 | 使用旧对象版本提交 | 版本冲突或条件写入失败；不发布事件 |
| TC-REC-006-06 | recovery | 同一outbox事件投递两次 | 下游消费者处理 | 资格投影只生效一次；处理记录可审计 |
| TC-REC-006-07 | regression | 对象已绑定旧ReleaseDecision；新OD-005版本发布 | 读取在制任务资格 | 继续引用旧决定；除非批准影响评估和迁移 |

## 明确非目标

- 不自动批准条件接收
- 不自动创建检测任务
- 不把新规则应用于既有决定
- 不实现完整QMS偏差流程

## 允许修改路径

- `src/modules/receiving/release/**`
- `src/modules/receiving/public-contracts/**`
- `src/modules/lab-execution/eligibility-projection/**`
- `contracts/receiving/release/**`
- `apps/web/receiving/release/**`
- `tests/receiving/release/**`

## 验证命令

- `python -m tools.specgen check`
- `TECH_STACK_TEST_COMMAND_REQUIRED_BY_ED-001`
- `OUTBOX_IDEMPOTENCY_TEST_REQUIRED`
- `PINNED_BASELINE_REGRESSION_TEST_REQUIRED`

## 完成定义

- 放行决定固定所有输入版本
- 正常、受限、阻断、权限、并发、恢复和规则升级测试通过
- 发件箱与消费者幂等
- 在制对象不读取最新版
- 撤销和影响评估边界有契约
- 追踪矩阵完整

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
