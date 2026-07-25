# DEV-007 受控放行与版本固定资格

DEV-007 只解决一件事：把 DEV-005 的身份结论和 DEV-006 的异常决定，在一次服务端事务中汇总为不可变放行决定。它不自动创建拆解、制样或检测任务，也不引入多级签署。

## 放行规则

- 最新身份结论必须为 `MATCHED`，并固定其 decision ID、版本和 `REC-ELIGIBILITY@1.0.0` 规则版本。
- 没有异常时形成 `RELEASED`，ReceivedItem 从 `QUARANTINED` 转为 `ACCEPTED`。
- 存在异常时，所有异常必须为未过期的 `CONDITIONALLY_ACCEPTED`，且当前决定矩阵为 `OD-005@1.0.0`。
- 多个条件接收的允许动作取交集，禁止动作取并集；禁止集合从允许集合中移除。
- `OPEN`、`AWAITING_CUSTOMER`、`REJECTED`、`SAFETY_HOLD`、未知规则、过期限制或最终允许集合为空均保持隔离。
- 放行要求 `receiving.release.approve` 和既有法人、实验室、客户、委托、产品类别范围；不要求重复批准已由质量/EHS 决定的异常。

## 固定数据

`receiving.receiving_release_decision` 追加保存：

- 放行决定 ID、版本、ReceivedItem ID 和放行前对象版本；
- 当前 IdentityDecision ID 和版本；
- 每个异常的状态版本、决定 ID、决定版本和矩阵版本；
- `REC-RELEASE@2.0.0`、`OD-005@1.0.0`、结果、动作限制和最早有效期；
- 批准人、批准时间和理由。

放行决定和 ReceivedItem 状态历史均由数据库触发器保护，禁止更新或删除。业务决定、对象状态/版本、标签投影、状态历史、审计意图和 Outbox 使用同一事务；任一写入失败全部回滚，失败尝试另行追加到 `audit_attempt`。

## HTTP API

`POST /api/v1/received-items/{id}/release-decisions`

```json
{
  "expectedItemVersion": 5,
  "ruleSetVersion": "REC-RELEASE@2.0.0",
  "rationale": "身份与异常限制已复核。"
}
```

常见失败码：

- `IDENTITY_NOT_MATCHED`：没有当前 `MATCHED` 决定；
- `BLOCKING_EXCEPTION`：存在开放、待客户、拒收或安全封存异常；
- `RELEASE_APPLICABILITY_UNKNOWN`：规则、矩阵、限制或固定输入无法安全解释；
- `RELEASE_NOT_AUTHORIZED`：缺少质量放行能力或对象范围；
- `EXPECTED_VERSION_CONFLICT`：对象已被其他身份、异常或放行操作推进；
- `REC.PERSISTENCE_UNAVAILABLE`：业务、审计或 Outbox 事务无法完整提交。

## 资格端口

| 端口 | 固定规则 | 行为 |
|---|---|---|
| `IReceivingEligibilityPort` | `REC-ELIGIBILITY@1.0.0` | 保留 DEV-005 失败关闭语义，永不返回 `ALLOWED` |
| `IReceivingEligibilityPortV2` | `REC-ELIGIBILITY@2.0.0` | 根据固定 ReleaseDecision 返回 `ALLOWED`、`BLOCKED` 或 `UNKNOWN` |

v2 对 `ACCEPTED` 的三个已知动作返回 `ALLOWED`。对 `CONDITIONALLY_ACCEPTED`，只有固定允许且未禁止、限制尚未过期的动作返回 `ALLOWED`；其他已知动作返回 `BLOCKED`。规则、动作、对象版本或固定输入不一致时返回 `UNKNOWN`，调用方必须按阻断处理。

## 恢复与排查

- API 返回版本冲突时重新读取身份、异常和 ReceivedItem 当前版本后再提交，不复用旧表单。
- API 返回持久化不可用时先检查数据库、`receiving.audit_pending` 与 `receiving.outbox`；事务已回滚，可使用新 correlation ID 重试。
- `receipt_release_total` 按结果聚合，`receipt_release_blocked_total` 按阻断原因聚合，`lab_execution_gate_total` 按动作和决定聚合；指标不包含客户或对象标识。
- 撤销和既有下游任务影响评估属于后继工作，本切片不通过修改原决定模拟撤销。
