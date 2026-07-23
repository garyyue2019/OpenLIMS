<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-005@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-005：处理收样异常并执行授权决定

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@0.1.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-EXCEPTION` |
| 开发就绪度 | `blocked` |
| 变更级别 | `major` |
| 负责人角色 | 质量产品负责人, 实验室负责人, EHS负责人, QA负责人 |
| 影响模块 | exception, receiving, identity, scope-change, authorization, audit, automated-test |
| 来源 | PRD-MAIN#OD-002, PRD-MAIN#OPS-EXC-001, PRD-MAIN#OPS-EXC-002, PRD-MAIN#OD-005 |
| 固定依赖 | ATC-PLT-000@0.1.0, ATC-REC-004@0.1.0, OD-002@1.0.0, OPS-EXC-001@0.1.0, OPS-EXC-002@0.1.0, OD-005@0.1.0, SEC-AUTH-001@0.1.0, SEC-AUD-001@0.1.0 |
| 规格指纹 | `0cbee64dcc5595dfe1f7ea0405f09d696489b7c60b74079d30e55b0ed0cdba00` |

## 业务结果

收样异常有明确责任方、证据、影响和授权决定；系统不会为了推进订单而静默缩小范围、延长有效期或默认接受风险。

## 主要参与者

收样员发起，质量、技术、EHS或客户合同授权人按 OD-005 矩阵处理

## 触发条件

收样或身份评估发现数量不足、超温、破损、污染、标签冲突、身份错配或超时

## 前置条件

- OD-005 已批准逐场景审批矩阵
- 异常分类、严重度、SLA和客户等待计时规则已批准
- 关联实物、委托、范围和证据版本已固定

## 正常路径

- 创建唯一 Exception 并固定发现事实和证据
- 根据分类和风险计算责任方、可选决定和必需批准
- 授权人选择待客户指令、条件接收、拒收、安全封存、退回或发起范围/补样变更
- 决定引用 OD-005 版本、限制条件、有效期、证据和批准人
- 任何范围变化创建独立 ScopeChange，不回写冻结范围
- 决定通过事务发件箱通知受影响模块

## 失败路径

- 不在授权矩阵内的用户不能提交决定
- 缺少温度记录、照片、客户确认等必需证据时保持开放
- 条件接收限制为空或适用性 UNKNOWN 时阻断
- 尝试直接降低范围、延长期限或关闭异常而无决定时拒绝
- 并发决定只允许一个基于期望版本成功
- 通知失败不回滚已批准决定，但进入事务发件箱重试和差异队列

## 领域不变量

- 异常事实与决定分离且历史不可变
- 异常关闭不删除原始证据
- 条件接收不能隐含允许全部下游动作
- 范围变化只通过版本化变更单
- UNKNOWN 适用性默认阻断

## 数据契约

```json
{
  "decision": [
    "decisionType",
    "matrixVersion",
    "constraints",
    "validUntil",
    "requiredApprovals",
    "evidenceRefs",
    "rationale"
  ],
  "decisionTypes": [
    "AWAIT_CUSTOMER",
    "CONDITIONAL_ACCEPT",
    "REJECT",
    "SAFETY_HOLD",
    "RETURN",
    "REQUEST_RESAMPLE",
    "REQUEST_SCOPE_CHANGE"
  ],
  "exception": [
    "type",
    "severity",
    "objectRefs",
    "observedAt",
    "evidenceRefs",
    "impactCandidate",
    "status"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "DECISION_NOT_AUTHORIZED",
    "DECISION_EVIDENCE_INCOMPLETE",
    "APPLICABILITY_UNKNOWN",
    "SCOPE_CHANGE_REQUIRED",
    "EXPECTED_VERSION_CONFLICT"
  ],
  "operations": [
    "POST /api/v1/exceptions",
    "POST /api/v1/exceptions/{id}/decisions",
    "POST /api/v1/exceptions/{id}/close"
  ]
}
```

## 状态转换

- Exception: OPEN -> UNDER_REVIEW -> DECIDED -> CLOSED
- 决定可使 ReceivedItem 进入 AWAITING_CUSTOMER、CONDITIONAL_ACCEPTED、REJECTED、SAFETY_HOLD 或 RETURN_PENDING
- 决定不得直接伪造 ACCEPTED

## 权限与职责分离

- 按异常类型、严重度、实验室、客户和有效期校验授权
- 发起人与最终批准人的职责分离按 OD-005 执行
- EHS安全封存不能被普通质量角色解除
- 客户确认只代表客户意图，不替代实验室技术批准

## 审计要求

- 记录异常事实、每次决定候选、批准、拒绝、撤回和关闭
- 保留规则矩阵版本、批准链、证据哈希和影响对象
- 接口通知与重放单独审计

## UX 状态

- 异常工作台按风险和等待责任方分组
- 决定选项由矩阵过滤但服务端再次校验
- 缺少证据显示逐项阻断
- 条件接收显示限制和到期时间
- 范围变更以独立链接展示而非直接编辑

## 可观测性

- 指标 receiving_exception_open_total 和 decision_lead_time_seconds
- 客户等待与内部等待分别计时
- 高风险异常超 SLA 告警
- 事务发件箱失败进入差异队列

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-005-01 | positive | 实物数量低于批准样品需求 | 收样员创建异常 | 异常保持开放；不得自动缩减范围；提供补样或范围变更受控路径 |
| TC-REC-005-02 | negative | 超温异常缺少温度记录和技术影响评估 | 尝试条件接收 | 返回DECISION_EVIDENCE_INCOMPLETE；保持隔离 |
| TC-REC-005-03 | boundary | 异常类型未配置批准矩阵 | 提交决定 | 返回APPLICABILITY_UNKNOWN；默认阻断并告警 |
| TC-REC-005-04 | security | 用户不在该严重度审批范围 | 批准条件接收 | 服务端拒绝；状态不变；记录越权尝试 |
| TC-REC-005-05 | concurrency | 两名批准人读取同一版本 | 分别提交冲突决定 | 最多一笔成功；另一笔版本冲突 |
| TC-REC-005-06 | recovery | 决定已提交但下游通知暂时失败 | 发件箱重试 | 决定不重复；下游只生效一次；差异队列关闭 |

## 明确非目标

- 不决定 OD-005 的矩阵内容
- 不实现完整 CAPA/QMS
- 不允许直接编辑冻结 TestScopeMatrix
- 不以客户同意替代技术和质量批准

## 允许修改路径

- `src/modules/exception/**`
- `src/modules/receiving/public-contracts/**`
- `src/modules/test-scope/public-contracts/**`
- `contracts/exception/**`
- `apps/web/exceptions/**`
- `tests/exception/**`

## 验证命令

- `python -m tools.specgen check`
- `TECH_STACK_TEST_COMMAND_REQUIRED_BY_ED-001`
- `DECISION_MATRIX_TEST_REQUIRED_BY_OD-005`
- `OUTBOX_IDEMPOTENCY_TEST_REQUIRED`

## 完成定义

- 异常分类和决定均版本化
- 自动缩减范围和默认条件接收的反向测试通过
- 权限、职责分离、并发、恢复和审计测试通过
- UNKNOWN 默认阻断
- 下游通知幂等
- 追踪矩阵完整

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
