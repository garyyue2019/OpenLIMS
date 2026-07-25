# DEV-006 收样异常与授权决定运行说明

## 交付边界

DEV-006 在现有 Receiving 模块内追加收样异常事实和授权决定，不新建重型 QMS 模块。异常类型为数量不足、超温、破损、污染、标签冲突、身份错配和身份待定；污染固定为 `SAFETY_CRITICAL`，其余为 `STANDARD`。未知分类或适用性失败关闭。

任何决定都不会解除 `ReceivedItem` 的 `QUARANTINED` 状态。DEV-007 才负责受控放行和执行资格发布。

## 最小审批矩阵

- 普通异常：`exception.quality.approve` 可提交 `AWAIT_CUSTOMER`、`CONDITIONAL_ACCEPT` 或 `REJECT`。
- 安全关键异常：`exception.ehs.approve` 可提交 `REJECT` 或 `SAFETY_HOLD`。
- 异常发起人不能批准自己的异常。
- 客户确认只作为证据，不替代质量或 EHS 批准。
- 条件接收必须包含证据、技术影响、非空允许动作、非空禁止动作和一年以内有效期；允许与禁止动作不能重叠。

## API 与权限

- `POST /api/v1/exceptions`：要求 `exception.create`。
- `GET /api/v1/exceptions/{id}`：要求 `exception.read`。
- `POST /api/v1/exceptions/{id}/decisions`：按严重度要求质量或 EHS 批准能力。

服务端继续校验部署集团、法人、实验室、客户、委托和产品类别。客户端不能提交集团标识；无权请求仅保留脱敏目标哈希。

## 事实、版本与事务

追加迁移 `20260725_004_receiving_exception` 新增：

- `receiving.receiving_exception`：不可变异常事实；
- `receiving.receiving_exception_decision`：不可变决定历史；
- `receiving.receiving_exception_state`：当前状态和乐观并发版本。

异常创建和决定都会条件递增 `ReceivedItem` 与标签投影的对象版本，但状态保持 `QUARANTINED`。事实、决定、投影、对象版本、审计和 Outbox 在同一事务提交；任何一步失败时整体回滚。并发决定最多一笔基于期望版本成功。

## 可观测性与恢复

- `receiving_exception_total` 仅按类型、严重度和状态聚合。
- `receiving_exception_decision_total` 仅按决定类型和结果聚合。
- 指标不包含客户、委托或对象标识。
- 持久化、审计或 Outbox 不可用时返回稳定错误并保留失败尝试审计；恢复后必须用当前版本重试。
