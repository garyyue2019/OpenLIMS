<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-006@2.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-006：实施 DEV-007 受控放行与版本固定资格

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `2.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-RELEASE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 收样产品负责人, 质量负责人, 实验室负责人, QA负责人 |
| 影响模块 | receiving, identity, exception, authorization, audit, outbox, lab-execution-gate, automated-test |
| 来源 | PRD-MAIN#OD-002, PRD-MAIN#OPS-RECEIPT-003, PRD-MAIN#OPS-IDENTITY-003, PRD-MAIN#RULE-026, PRD-MAIN#AC-REC-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-REC-005@2.0.0, ED-001@2.0.0, OD-002@1.0.0, OD-005@1.0.0, OD-035@1.0.0, OPS-RECEIPT-003@1.0.0, OPS-IDENTITY-003@1.0.0, OPS-EXC-001@1.0.0, OPS-EXC-002@1.0.0, AC-REC-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `fdf0bc2308e1e56cdd289ce94001b8e0971678fa3f00ca7329024cdfa1685087` |

## 业务结果

质量人员可以用一次受控操作释放身份匹配且风险已处理的实物；下游只依据固定放行决定获得正常或受限动作资格。

## 主要参与者

具有 receiving.release.approve 及对象法人、实验室、客户、委托和产品类别范围的质量批准人

## 触发条件

授权质量人员在 ReceivedItem 详情提交受控放行

## 前置条件

- DEV-005 已交付身份结论和资格端口 v1
- DEV-006 已交付异常事实及质量/EHS 决定
- ReceivedItem 仍处于 QUARANTINED
- 部署绑定唯一 OrganizationGroup

## 正常路径

- 锁定并重新读取 ReceivedItem、最新身份结论和全部异常状态
- 校验身份为 MATCHED 且异常为空或全部为未过期条件接收
- 无异常计算 RELEASED；条件接收按允许交集、禁止并集计算 RELEASED_WITH_CONSTRAINTS
- 追加不可变 ReleaseDecision 并固定所有输入版本
- 原子更新 ReceivedItem 状态和版本并追加状态历史、审计与 Outbox
- ReceivingEligibilityPort@v2 按固定结果和动作限制返回资格

## 失败路径

- 身份未匹配或没有当前决定时返回 IDENTITY_NOT_MATCHED
- 存在 OPEN、AWAITING_CUSTOMER、REJECTED 或 SAFETY_HOLD 时返回 BLOCKING_EXCEPTION
- 限制过期、最终允许集为空或规则适用性未知时返回 RELEASE_APPLICABILITY_UNKNOWN
- 无能力或跨范围请求统一拒绝并审计
- 对象版本变化时返回 EXPECTED_VERSION_CONFLICT
- 业务、审计或 Outbox 写入失败时整体回滚

## 领域不变量

- ReleaseDecision 不可变并精确引用所有输入版本
- 一个 ReceivedItem 只能从 QUARANTINED 成功放行一次
- 规则升级不自动重算既有决定
- 旧 ReceivingEligibilityPort@v1 保持失败关闭
- UNKNOWN 等同阻断
- 不读取其他模块私表且不自动创建下游任务
- 撤销和完整影响评估只保留后继契约边界，不在本卡实现

## 数据契约

```json
{
  "outcomeEnum": [
    "RELEASED",
    "RELEASED_WITH_CONSTRAINTS"
  ],
  "releaseDecision": [
    "releaseDecisionId",
    "version",
    "receivedItemId",
    "itemVersion",
    "identityDecisionId",
    "identityDecisionVersion",
    "exceptionDecisionVersions",
    "releaseRuleVersion",
    "exceptionMatrixVersion",
    "outcome",
    "allowedActions",
    "prohibitedActions",
    "constraintsValidUntil",
    "approvedBy",
    "approvedAt"
  ],
  "request": [
    "expectedItemVersion",
    "ruleSetVersion",
    "rationale"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "IDENTITY_NOT_MATCHED",
    "BLOCKING_EXCEPTION",
    "RELEASE_APPLICABILITY_UNKNOWN",
    "RELEASE_NOT_AUTHORIZED",
    "EXPECTED_VERSION_CONFLICT",
    "PERSISTENCE_UNAVAILABLE"
  ],
  "operation": "POST /api/v1/received-items/{id}/release-decisions",
  "publicPorts": [
    "ReceivingEligibilityPort@v1",
    "ReceivingEligibilityPort@v2"
  ],
  "success": "201 ReleaseDecisionResult"
}
```

## 状态转换

- QUARANTINED -> ACCEPTED when RELEASED
- QUARANTINED -> CONDITIONALLY_ACCEPTED when RELEASED_WITH_CONSTRAINTS
- 任何失败保持 QUARANTINED

## 权限与职责分离

- 放行只要求 receiving.release.approve 单一质量能力和既有对象范围
- 不新增发起/复核双人链或按异常重复审批
- 资格查询继续要求 receiving.eligibility.evaluate
- 客户端不能提交 OrganizationGroup

## 审计要求

- 记录放行尝试、全部输入版本、规则矩阵、限制、结果、actor 和 correlationId
- 失败、越权、版本冲突与事务回滚通过独立追加路径记录
- Outbox eventId 和 ReleaseDecision 一一对应且可追踪
- 敏感正文不写日志或指标

## UX 状态

- 放行面板显示身份结论、异常状态和派生限制
- 阻断原因由服务端返回，前端不自行推断
- 受限放行逐项显示允许、禁止动作和最早有效期
- 历史放行决定只读展示

## 可观测性

- receipt_release_total 按 outcome 聚合
- receipt_release_blocked_total 按原因聚合
- lab_execution_gate_total 按动作和决定聚合
- UNKNOWN、事务回滚和 Outbox 积压写结构化告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-006-01 | positive | 身份 MATCHED；无异常；授权有效 | 提交放行 | 形成 RELEASED；状态为 ACCEPTED；v2 三动作 ALLOWED |
| TC-REC-006-02 | boundary | 全部异常有效条件接收 | 提交放行 | 允许取交集、禁止取并集；状态为 CONDITIONALLY_ACCEPTED；v2 按动作限制 |
| TC-REC-006-03 | negative | 存在开放、待客户、拒收或安全封存异常 | 提交放行 | 保持隔离；不创建决定或成功事件 |
| TC-REC-006-04 | boundary | 条件已过期或允许交集为空 | 提交放行 | UNKNOWN 失败关闭；保持隔离 |
| TC-REC-006-05 | permission | 调用人缺少能力或对象范围 | 提交放行 | 统一拒绝；追加脱敏失败审计 |
| TC-REC-006-06 | concurrency | 预览后对象或异常改变 | 使用旧版本放行 | 版本冲突或锁内重新评估阻断；不发布成功事件 |
| TC-REC-006-07 | recovery | 审计或 Outbox 失败 | 提交并重试 | 首笔全部回滚；重试只产生一个逻辑决定 |
| TC-REC-006-08 | regression | 已有 ReleaseDecision | 分别查询 v1 和 v2 | v1 仍失败关闭；v2 使用固定决定 |

## 明确非目标

- 不新增多级或双人签署
- 不自动批准条件接收
- 不自动创建拆解、制样或检测任务
- 不实现完整撤销、QMS 偏差、CAPA 或影响评估工作流
- 不把新规则应用于既有决定
- 不实现共享 SaaS 多租户

## 允许修改路径

- `spec/stories/ATC-REC-006__v2.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-007-controlled-release/**`
- `contracts/receiving/**`
- `src/modules/receiving/**`
- `src/host/api/**`
- `apps/web/src/**`
- `tests/architecture/**`
- `tests/unit/receiving/**`
- `tests/contract/receiving/**`
- `tests/integration/receiving/**`
- `tests/e2e/receiving/**`
- `tests/test_repository_contract.py`
- `docs/domain/receiving/**`

## 验证命令

- `python -m tools.specgen ready --story ATC-REC-006@2.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module receiving`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `corepack pnpm@10.34.5 --dir apps/web lint`
- `corepack pnpm@10.34.5 --dir apps/web typecheck`
- `corepack pnpm@10.34.5 --dir apps/web test:unit`
- `python -m tools.specgen check`

## 完成定义

- 追加迁移不改写 DEV-003 至 DEV-006 历史
- 正常和受限放行决定固定全部输入版本
- 权限、反向、边界、并发、事务、恢复、审计和 Outbox 测试通过
- 资格端口 v1 行为不变且 v2 严格执行限制
- UNKNOWN 始终失败关闭且无下游副作用
- 无跨模块私表访问
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
