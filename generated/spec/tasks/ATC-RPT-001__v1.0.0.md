<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-RPT-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-RPT-001：实施 DEV-022 报告签发门禁

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-REPORT` |
| Feature | `FEAT-RPT-GATE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 技术负责人, 质量负责人, 授权签字人, QA负责人 |
| 影响模块 | report, release-gate, traceability, accreditation, qc, result, batch, instrument, scope, receiving, audit, outbox, authorization, automated-test |
| 来源 | PRD-MAIN#RPT-TRACE-001, PRD-MAIN#RPT-GATE-001, PRD-MAIN#RPT-GATE-002, PRD-MAIN#RPT-CLAIM-001, PRD-MAIN#RPT-SCOPE-001, PRD-MAIN#LAB-ACC-001, PRD-MAIN#AC-RPT-001, PRD-MAIN#AC-ACC-001, PRD-MAIN#AC-TRACE-001 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, OD-001@1.0.0, OD-011@1.0.0, OD-029@1.0.0, OD-030@1.0.0, BUS-RPT-001@1.0.0, BUS-RPT-002@1.0.0, BUS-RPT-003@1.0.0, AC-RPT-001@1.0.0, AC-ACC-001@1.0.0, AC-TRACE-001@1.0.0, ATC-RESULT-001@1.0.0, ATC-QC-001@1.0.0, ATC-INST-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `86ba2b2323216eb56e7180564cbefd0e984f9947aa28b3a966e3f92ac2597b7c` |

## 业务结果

报告首次成为受治理对象：每一行都能追溯到当前有效采用与完整贡献链，认可资格逐行按六维计算而非机构级布尔，签发前的每一个阻断都指名对象、规则版本与下一步——这正是 GOAL-008（超认可/超授权签发事件为零）与 RISK-019（认可简化为机构标记）的结构性防线。

## 主要参与者

报告装配者与授权签字人（report.manage 能力）

## 触发条件

报告装配者创建报告、追加报告行并请求签发门禁评估

## 前置条件

- OD-011/022/029 已决定
- 结果采用、QC、批次、分配、收样、范围、仪器模块的公开端口可用
- 受控认可范围引用由调用方以稳定 ID + 版本 + 内容哈希声明（OD-030 口径）

## 正常路径

- POST 报告：固定对象范围（法人/实验室/客户/服务单/产品类别）与规则集版本 → DRAFT
- POST 报告行：每行声明结果组+期望版本、范围行、范围分区（五类之一）、认可范围引用+版本+哈希、可选分包披露引用；ResultAdoptionPort 以 ALLOWED 固定采用目标与组版本，贡献链引用一并落为不可变事实
- POST 门禁评估：扇出结果采用、QC 可报告性（涉及该目标的每个运行）、收样放行、范围生产资格、分配状态、批次状态、仪器导入状态，并执行六维行级认可校验与签字授权校验
- 全部来源 ALLOWED 且认可校验通过 → 门禁 ALLOWED，报告可推进至 PENDING_APPROVAL
- GET 门禁端口：按 expectedVersion + ruleSetVersion 固定返回 ALLOWED/BLOCKED/UNKNOWN 与逐项阻断明细

## 失败路径

- 任一来源端口 BLOCKED → 该项计入阻断明细，报告不进入待批准
- 任一来源端口 UNKNOWN 或异常 → 同样阻断（失败关闭），原因码指明来源
- 行的六维认可校验失败（不在范围/版本不匹配/已过有效期/引用缺失/签字人无资格）→ RPT.ACCREDITATION_BLOCKED 逐行阻断
- 对非认可行使用认可声明 → RPT.VALIDATION_FAILED
- EVALUATED 范围分区的行 → RPT.CONFORMITY_DECISION_UNAVAILABLE 阻断（ConformityDecision 依赖未决 OD-034，不得默认放行）
- 同一范围行+采用目标重复成行 → RPT.DUPLICATE_ATTRIBUTION
- 贡献链必需引用缺失 → RPT.TRACE_INCOMPLETE 并指明缺失环节
- UPDATE/DELETE 任何 report 事实 → 数据库 55000（RPT.REPORT_APPEND_ONLY）
- 行为人缺失/组织不匹配/能力拒绝 → RPT.NOT_AUTHORIZED，仅 audit_attempt 留痕
- 平台审计或发件箱写入失败 → 整体回滚，业务事实不产生

## 领域不变量

- 每行恰好一个当前有效采用，经端口以精确组版本固定（RULE-005、RPT-TRACE-001）
- 范围分区恰为五类；EVALUATED 因 OD-034 未决而一律阻断
- 认可按六维逐行计算，禁止报告级/机构级布尔（OD-029@1.0.0、RPT-CLAIM-001）
- 阻断项逐条返回 {对象, 规则集版本, 原因码, 允许的下一步}，不聚合（RPT-GATE-002）
- QC 可报告性须询问涉及该目标的每个运行，任一 BLOCKED 即阻断（端口按运行作用域）
- 全部跨模块端口调用在本模块事务之外完成（gate-then-commit），决策与版本原样固定
- 全部事实追加式、DB 触发器强制；乐观并发 expectedCurrentVersion + advisory lock
- 事实、平台审计意图与发件箱同事务；模块 audit_attempt 独立于回滚存活
- 状态止于 PENDING_APPROVAL——本卡不签发、不生成报告文件、不实现版本链

## 数据契约

```json
{
  "accreditationVerdict": [
    "lineId",
    "status(ACCREDITED/NOT_ACCREDITED/UNKNOWN)",
    "failedDimensions[]"
  ],
  "gateEvaluation": [
    "evaluationId",
    "reportId",
    "reportVersion",
    "decision(ALLOWED/BLOCKED/UNKNOWN)",
    "blockers[{objectRef, objectType, ruleSetVersion, reasonCode, allowedNextSteps[]}]",
    "evaluatedBy",
    "evaluatedAt"
  ],
  "gateStatusResult": [
    "decision",
    "reasonCodes[]",
    "reportId",
    "currentVersion?",
    "blockers[]",
    "ruleSetVersion"
  ],
  "report": [
    "reportId",
    "ruleSetVersion(RPT-ISSUANCE@1.0.0)",
    "objectScope{legalEntityId, laboratoryId, customerId, serviceOrderId, productCategory}",
    "reportNumber",
    "state(DRAFT/PENDING_APPROVAL)",
    "version"
  ],
  "reportLine": [
    "lineId",
    "reportId",
    "lineNumber",
    "resultGroupId",
    "expectedGroupVersion",
    "adoptionTargetId",
    "adoptionRuleSetVersion",
    "scopeLineId",
    "scopePartition(ACTUAL_TESTED/APPROVED_COVERAGE/NOT_EVALUATED/CUSTOMER_DECLARED/LABORATORY_CONCLUSION)",
    "traceRefs{batchId, allocationId, receivedItemId, requirementSnapshotRef}",
    "accreditationRef{id, version, sha256}",
    "accreditationDimensions{siteId, methodRef+version, productMatrix, parameterRange, validUntil, signatoryId}",
    "subcontractingDisclosureRef?"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "RPT.VALIDATION_FAILED",
    "RPT.ELIGIBILITY_BLOCKED",
    "RPT.APPLICABILITY_UNKNOWN",
    "RPT.ACCREDITATION_BLOCKED",
    "RPT.CONFORMITY_DECISION_UNAVAILABLE",
    "RPT.DUPLICATE_ATTRIBUTION",
    "RPT.TRACE_INCOMPLETE",
    "RPT.EXPECTED_VERSION_CONFLICT",
    "RPT.NOT_AUTHORIZED",
    "RPT.OBJECT_NOT_ACCESSIBLE",
    "RPT.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/reports → 201 创建报告草稿",
    "POST /api/v1/reports/{id}/lines → 201 追加报告行（采用门禁固定）",
    "POST /api/v1/reports/{id}/gate-evaluation → 201 执行签发门禁评估",
    "POST /api/v1/reports/{id}/submit-for-approval → 201 门禁 ALLOWED 时推进至待批准",
    "GET /api/v1/reports/{id} → 200 报告、行、贡献链与门禁明细",
    "GET /api/v1/reports/{id}/issuance-gate → 200 门禁决策与逐项阻断"
  ],
  "publicPort": "IReportIssuanceGatePort.EvaluateAsync(ReportIssuanceGateRequest) → ALLOWED/BLOCKED/UNKNOWN + blockers[]，版本与规则集固定，供 DEV-023 签署卡与计费链消费"
}
```

## 状态转换

- 报告：DRAFT →（门禁 ALLOWED）PENDING_APPROVAL；不可逆。已签发/已交付与版本链属 DEV-023

## 权限与职责分离

- 新增能力 report.manage（装配/行/门禁/推进/读取共用，操作差异由状态约束）；HttpClaims 精确 claim 检查
- 消费的各模块端口自行强制其能力，本卡不放宽也不复制

## 审计要求

- 每个命令写平台 audit_intent（同事务）+ outbox 事件（Report.Created/LineAdded/GateEvaluated/SubmittedForApproval）
- 失败尝试写 report.audit_attempt（SHA-256 目标哈希，独立于回滚）
- 读取写 READ_REPORT 审计

## UX 状态

- 本卡不新增前端页面
- 签发工作台与验证页属后续卡

## 可观测性

- 计数器：报告数、行数、门禁决策分布、阻断原因码分布、认可判定分布
- 结构化日志固定 correlationId 与错误码

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-RPT-001-01 | positive | 采用门禁 ALLOWED 的结果组 | 创建报告并追加行 | 采用目标与组版本原样固定；贡献链引用齐备；审计+发件箱同事务 |
| TC-RPT-001-02 | negative | 一行收样身份冲突、一行 QC 阻断、一行签字人无资格 | 请求门禁评估 | 签发被阻止；三个阻断项各含对象/规则版本/原因码/下一步；不聚合为单一布尔 |
| TC-RPT-001-03 | boundary | 一行六维全部在范围内、另一行方法不在范围内；机构级已认可标记存在 | 查询行级认可状态 | 逐行独立返回 ACCREDITED / NOT_ACCREDITED；非认可行使用认可声明被拒；机构标记不改变判定；过期/版本不匹配/缺失引用判为不在范围 |
| TC-RPT-001-04 | regression | 聚合自三个平行试样的采用 | 重建贡献链并逐环节移除必需引用 | 完整链可重建；缺任一环节阻断并指明缺失；重复归属被拒 |
| TC-RPT-001-05 | negative | 任一来源端口返回 UNKNOWN 或抛异常 | 门禁评估 | 阻断且原因码指明来源；报告不进入待批准；audit_attempt 留痕 |
| TC-RPT-001-06 | negative | 范围分区为 EVALUATED 的行 | 门禁评估 | RPT.CONFORMITY_DECISION_UNAVAILABLE；不得默认放行（OD-034 未决） |
| TC-RPT-001-07 | negative | 已有报告事实 | UPDATE/DELETE 及并发同版本提交 | 55000 拒绝；恰一个成功，另一方 EXPECTED_VERSION_CONFLICT |
| TC-RPT-001-08 | negative | 审计或发件箱注入失败 | 创建报告 | 业务事实回滚为零；audit_attempt 恰一次 |
| TC-RPT-001-09 | boundary | 已评估的报告 | 正确/过期版本与未知规则集查询 | ALLOWED/BLOCKED / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN] |

## 明确非目标

- 不实现电子签名、内容哈希绑定与签发（DEV-023）
- 不实现版本链、撤回/作废/替代与验证页（DEV-023，OD-022 语义）
- 不生成报告文件或 PDF 渲染
- 不实现 ConformityDecision 与全面合规引用（OD-034 未决）
- 不实现分包对象与分包方回传（OD-013 未决）
- 不实现认可证书内容管理或认可机构接口
- 不新增前端页面
- 不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/decisions/OD-011__v1.0.0.json`
- `spec/decisions/OD-022__v1.0.0.json`
- `spec/decisions/OD-029__v1.0.0.json`
- `spec/requirements/BUS-RPT-001__v1.0.0.json`
- `spec/requirements/BUS-RPT-002__v1.0.0.json`
- `spec/requirements/BUS-RPT-003__v1.0.0.json`
- `spec/acceptance/AC-RPT-001__v1.0.0.json`
- `spec/acceptance/AC-ACC-001__v1.0.0.json`
- `spec/acceptance/AC-TRACE-001__v1.0.0.json`
- `spec/stories/ATC-RPT-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-27-dev-022-report-issuance-gate/**`
- `OpenLIMS.slnx`
- `contracts/report/**`
- `src/modules/report/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/unit/report/**`
- `tests/contract/report/**`
- `tests/integration/report/**`
- `tests/architecture/**`
- `tests/contract/labeling/OpenLIMS.Labeling.ContractTests/packages.lock.json`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/contract/receiving/OpenLIMS.Receiving.ContractTests/packages.lock.json`
- `tests/contract/scope/OpenLIMS.Scope.ContractTests/packages.lock.json`
- `tests/contract/quantity/OpenLIMS.Quantity.ContractTests/packages.lock.json`
- `tests/contract/allocation/OpenLIMS.Allocation.ContractTests/packages.lock.json`
- `tests/contract/batch/OpenLIMS.Batch.ContractTests/packages.lock.json`
- `tests/contract/result/OpenLIMS.Result.ContractTests/packages.lock.json`
- `tests/contract/billing/OpenLIMS.Billing.ContractTests/packages.lock.json`
- `tests/contract/instrument/OpenLIMS.Instrument.ContractTests/packages.lock.json`
- `tests/contract/qc/OpenLIMS.Qc.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/report/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-RPT-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module report`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `python -m tools.specgen check`

## 完成定义

- 装配、贡献链、五类分区、六维认可、七端口扇出门禁与逐项阻断全部落地且追加式 DB 强制
- AC-RPT-001/AC-ACC-001/AC-TRACE-001 三条验收各有对应回归测试
- 全部既有测试项目保持绿色
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
