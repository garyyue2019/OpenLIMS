<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-003@2.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-003：实施隔离门禁和 ReceivedItem 身份评估

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `2.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-QUARANTINE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 收样产品负责人, 身份评估负责人, 质量负责人, QA负责人 |
| 影响模块 | receiving, identity-assessment, lab-execution-gate, authorization, audit, automated-test |
| 来源 | PRD-MAIN#OPS-RECEIPT-003, PRD-MAIN#OPS-IDENTITY-001, PRD-MAIN#OPS-IDENTITY-002, PRD-MAIN#OPS-IDENTITY-003, PRD-MAIN#AC-REC-001, PRD-MAIN#AC-ID-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-REC-001@2.0.0, ATC-REC-002@2.0.0, ED-001@2.0.0, OD-002@1.0.0, OD-009@1.0.0, OD-035@1.0.0, ORG-STRUCT-001@1.0.0, OPS-RECEIPT-001@1.0.0, OPS-RECEIPT-003@1.0.0, OPS-IDENTITY-001@1.0.0, OPS-IDENTITY-002@1.0.0, OPS-IDENTITY-003@1.0.0, AC-REC-001@1.0.0, AC-ID-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `8f80f420f8587b216df5a594319f9eb27b9c76774a0eb96bc66b0050ef515326` |

## 业务结果

身份评估人员可以在集团多机构授权边界内证明收到的实物是什么以及为何一致、错配或待定；任何身份结论都不能绕过隔离进入实验室执行。

## 主要参与者

具有 receiving.identity.evaluate 及对象产品类别、法人、实验室、客户和委托范围的身份评估员；调用统一资格端口的受控服务身份

## 触发条件

评估员打开一个隔离 ReceivedItem 追加观察或结论，或下游服务查询其拆解、制样或检测分配资格

## 前置条件

- DEV-003 和 DEV-004 已交付 ReceivedItem、QUARANTINED 初态、稳定标签身份和对象版本
- 部署绑定唯一 OrganizationGroup
- 对象登记声明可读取

## 正常路径

- 首次观察固定客户声明和对象版本快照，并把评估状态从 NOT_STARTED 变为 IN_PROGRESS
- 评估员追加标签、型号、批次、外观及附件哈希观察
- 服务端比较关键字段，校验必需证据、理由、权限和期望版本
- 提交 MATCHED、MISMATCHED 或 INDETERMINATE 的追加式人工结论
- 结论、审计和必要 Outbox 在同一事务提交，ReceivedItem 仍为 QUARANTINED
- ReceivingEligibilityPort@v1 对三个动作返回固定规则版本、对象版本和失败关闭决定

## 失败路径

- 缺少型号、批次、外观、标签、附件或理由时拒绝提交
- 关键字段存在差异时拒绝 MATCHED，多义未消歧时要求 INDETERMINATE
- 无能力、产品类别或对象组织范围权限时统一拒绝且不泄露对象
- 期望版本过旧时返回 EXPECTED_VERSION_CONFLICT，不覆盖新版本
- 数据库、审计或 Outbox 失败时整体回滚
- 资格端口遇到未知状态、规则、对象或依赖不可用时返回 UNKNOWN 并按阻断处理

## 领域不变量

- ReceivedItem.state 在 DEV-005 始终保持 QUARANTINED，身份评估状态单独保存
- 声明、观察和结论分别追加版本化，历史不可删除或覆盖
- 结论只描述实际收到的 ReceivedItem，不表示代表性、范围覆盖或检测任务使用
- MATCHED 不解除隔离；MISMATCHED 和 INDETERMINATE 不自动条件接收或拒收
- DEV-007 交付前统一资格端口不会返回 ALLOWED，UNKNOWN 等同 BLOCKED
- 客户端不能提交 organizationGroupId，送检客户不是租户，不提供共享 SaaS 多租户数据平面

## 数据契约

```json
{
  "decision": [
    "decisionId",
    "version",
    "observationVersion",
    "declarationSnapshotVersion",
    "outcome",
    "reasonCode",
    "rationale",
    "ruleSetVersion"
  ],
  "declarationSnapshot": [
    "receivedItemId",
    "snapshotVersion",
    "itemVersion",
    "declaredDescription",
    "model",
    "batch",
    "serialNumber",
    "color",
    "capturedAt"
  ],
  "eligibilityInput": [
    "laboratoryId",
    "receivedItemId",
    "requestedAction",
    "expectedItemVersion",
    "ruleSetVersion"
  ],
  "eligibilityOutput": [
    "decision",
    "currentState",
    "assessmentState",
    "identityDecisionId",
    "reasonCodes",
    "itemVersion",
    "decisionVersion"
  ],
  "observation": [
    "observationId",
    "version",
    "expectedItemVersion",
    "observedLabels",
    "observedModel",
    "observedBatch",
    "appearance",
    "attachmentRefs",
    "attachmentHashes",
    "observedAt"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "IDENTITY_EVIDENCE_INCOMPLETE",
    "IDENTITY_CONFLICT",
    "IDENTITY_AMBIGUOUS",
    "AUTHORIZATION_DENIED",
    "OBJECT_NOT_ACCESSIBLE",
    "EXPECTED_VERSION_CONFLICT",
    "RECEIVING_PORT_UNAVAILABLE"
  ],
  "operations": [
    "GET /api/v1/received-items/{id}/identity-assessment",
    "POST /api/v1/received-items/{id}/identity-observations",
    "POST /api/v1/received-items/{id}/identity-decisions"
  ],
  "publicPort": "ReceivingEligibilityPort@v1"
}
```

## 状态转换

- Assessment: NOT_STARTED -> IN_PROGRESS -> MATCHED|MISMATCHED|INDETERMINATE
- 后续观察或更正追加新版本并可形成后续结论，不改写旧结论
- ReceivedItem: QUARANTINED -> QUARANTINED

## 权限与职责分离

- 读写身份评估要求 receiving.identity.evaluate
- 资格查询要求 receiving.eligibility.evaluate 和明确调用用途
- 法人、实验室、客户、委托和产品类别范围在服务端校验
- 审计只读身份不能提交结论或获得执行资格

## 审计要求

- 记录评估读取、观察创建、结论提交、阻断、权限拒绝和版本冲突
- 审计包含集团、法人、实验室、客户、委托、对象、对象版本、事实版本、规则、actor 和 correlationId
- 无效或未授权请求只保存脱敏目标哈希
- 附件下载不在本切片实现，未来必须单独审计

## UX 状态

- 页面三栏区分客户声明、实验室观察和最终结论
- 差异逐项高亮但不自动选择结论
- 必需证据和权限缺失显示明确阻断
- 任何结论都显示仍在隔离并等待受控放行
- 历史版本只读展示

## 可观测性

- identity_assessment_total 按结论聚合且不包含客户或对象标识
- lab_execution_gate_total 按动作、决定和状态聚合
- UNKNOWN、事务回滚和持续权限拒绝告警
- 结构化日志用 correlationId 关联 API、审计和 Outbox

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-003-01 | positive | 声明和观察关键字段一致 | 授权评估员提交 MATCHED | 三层事实保存；对象仍隔离；资格仍 BLOCKED |
| TC-REC-003-02 | negative | 声明型号 A、观察型号 C | 提交 MATCHED 后再提交 MISMATCHED | MATCHED 被拒绝；MISMATCHED 原子保存并发布冲突事实 |
| TC-REC-003-03 | boundary | 存在多种可能身份 | 提交 INDETERMINATE | 保存待定结论；保持隔离 |
| TC-REC-003-04 | permission | 缺少任一能力或组织范围 | 读取或提交评估 | 统一拒绝；不泄露对象；脱敏审计 |
| TC-REC-003-05 | concurrency | 两个评估员读取同一版本 | 第二人用旧期望版本提交 | 返回版本冲突；不覆盖首个结果 |
| TC-REC-003-06 | transaction | 模拟审计或 Outbox 失败 | 提交错配结论 | 事实、结论和事件全部回滚；重试只产生一个逻辑结论 |
| TC-REC-003-07 | contract | 任一身份结论 | 查询三个动作 | 均返回 BLOCKED；规则和对象版本一致 |
| TC-REC-003-08 | recovery | 未知规则或持久化不可用 | 查询资格或提交评估 | 资格 UNKNOWN 并阻断或 API 503；不使用过期允许缓存 |
| TC-REC-003-09 | deployment-isolation | 部署绑定集团甲 | 客户端尝试提交集团乙 | 请求失败关闭；不访问集团乙数据 |

## 明确非目标

- 不实现完整异常聚合或审批
- 不实现条件接收、待客户指令、拒收或安全封存
- 不解除隔离
- 不实现拆解、制样或检测分配模块
- 不由 AI 提交最终结论
- 不实现共享 SaaS 多租户

## 允许修改路径

- `spec/decisions/OD-035__v1.0.0.json`
- `spec/requirements/OPS-RECEIPT-003__v1.0.0.json`
- `spec/requirements/OPS-IDENTITY-001__v1.0.0.json`
- `spec/requirements/OPS-IDENTITY-002__v1.0.0.json`
- `spec/requirements/OPS-IDENTITY-003__v1.0.0.json`
- `spec/acceptance/AC-REC-001__v1.0.0.json`
- `spec/acceptance/AC-ID-001__v1.0.0.json`
- `spec/stories/ATC-REC-003__v2.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-25-dev-005-isolation-identity-assessment/**`
- `OpenLIMS.slnx`
- `contracts/receiving/**`
- `src/modules/receiving/**`
- `src/host/api/**`
- `src/host/worker/**`
- `apps/web/src/**`
- `tests/architecture/**`
- `tests/unit/receiving/**`
- `tests/contract/receiving/**`
- `tests/integration/receiving/**`
- `tests/e2e/receiving/**`
- `tests/test_repository_contract.py`
- `docs/domain/receiving/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`
- `.github/workflows/application-ci.yml`

## 验证命令

- `python -m tools.specgen ready --story ATC-REC-003@2.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module receiving`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `corepack pnpm@10.34.5 --dir apps/web lint`
- `corepack pnpm@10.34.5 --dir apps/web typecheck`
- `corepack pnpm@10.34.5 --dir apps/web test:unit`
- `python -m tools.specgen check`

## 完成定义

- 追加迁移保留 DEV-003/004 已发布历史
- 三层身份事实、人工结论和隔离状态分离
- 权限、证据、并发、事务、恢复、审计和 Outbox 测试通过
- 三个动作共享版本化资格端口且 DEV-007 前全部失败关闭
- Web 页面可完成观察、结论和历史查看并明确显示仍隔离
- 无跨模块私表访问，禁止共享 SaaS 多租户入口
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
