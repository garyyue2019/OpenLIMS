<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-QC-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-QC-001：实施 DEV-021 QC 影响传播

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-QUALITY` |
| Feature | `FEAT-QC-IMPACT` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室负责人, 质量负责人, 技术负责人, QA负责人 |
| 影响模块 | qc, impact-propagation, reportability, release-gate, batch, result, audit, outbox, authorization, automated-test |
| 来源 | PRD-MAIN#LAB-QC-001, PRD-MAIN#LAB-QC-002, PRD-MAIN#LAB-QC-003, PRD-MAIN#RULE-010, PRD-MAIN#RULE-022, PRD-MAIN#AC-QC-001 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, OD-001@1.0.0, OD-030@1.0.0, BUS-QC-001@1.0.0, BUS-QC-002@1.0.0, BUS-QC-003@1.0.0, AC-QC-001@1.0.0, ATC-BATCH-001@1.0.0, ATC-RESULT-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `17119ce811f55636ddb354a0ea4ba4bfb627bafe6bbd3860f6966cf8dfdbe94f` |

## 业务结果

QC 从批次层的'冻结原因声明'升级为可执行的质量关口：规则按方法版本执行留下可追事实，失败影响一次性覆盖全批而非单条结果，解除阻断必须五关口齐备且偏差获批无法走捷径——这正是 RISK-006（QC 形式审批后错误放行）的结构性防线。

## 主要参与者

QC 执行者与质量放行审核者（qc.manage 能力）

## 触发条件

QC 执行者为某分析批开启 QC 运行并提交规则结果；失败时质量审核者推进五关口

## 前置条件

- 批次已存在且未冻结（经 IBatchStatusPort 门禁固定）
- OD-001 已决定试点切片（玩具×物理机械）
- OD-030 已决定最小执行记录与外部引用口径

## 正常路径

- POST QC 运行：固定批次引用+版本（批次门禁 ALLOWED 原样固定）、方法引用+版本、QC 规则集版本 → OPEN
- POST QC 结果：逐条规则落 QCResult（规则引用+版本、控制类型、观测值、判定、判定依据），追加式不可变
- POST 判定：全部 PASS → PASSED，运行结束；任一 FAIL → FAILED 并要求登记影响集
- POST 影响集：登记该运行覆盖的全部受影响目标（结果组/任务，含目标版本），空集被拒绝
- GET 可报告性端口：受影响目标返回 BLOCKED 并列明未满足关口
- POST 关口：INVESTIGATION / IMPACT_SCOPE / VALIDITY_DECISION / ADOPTION_RULE / TECHNICAL_REVIEW 逐项满足（各带引用+版本、满足人、时间）
- POST 解除：五关口齐备时解除阻断，可报告性转为 ALLOWED

## 失败路径

- 批次门禁 BLOCKED（含已冻结）或 UNKNOWN → QC.ELIGIBILITY_BLOCKED / QC.APPLICABILITY_UNKNOWN，运行不产生
- 空影响集或重复登记同一目标 → QC.VALIDATION_FAILED
- 五关口未齐备时解除 → QC.RELEASE_GATE_INCOMPLETE，阻断保持，无解除事实
- 仅有偏差获批时解除 → QC.RELEASE_GATE_INCOMPLETE（偏差获批不属五关口，RULE-010）
- 同一关口重复满足或对已解除运行再次解除 → QC.VALIDATION_FAILED
- UPDATE/DELETE 任何 qc 事实 → 数据库 55000（QC.QC_APPEND_ONLY）
- 行为人缺失/组织不匹配/能力拒绝 → QC.NOT_AUTHORIZED，仅 audit_attempt 留痕
- 平台审计或发件箱写入失败 → 整体回滚，业务事实不产生

## 领域不变量

- 方法版本与 QC 规则集版本在运行事实上原样固定，不得漂移（LAB-QC-001）
- 影响集覆盖全批受影响目标，不得只处理发现异常的单条结果（RULE-022）
- 受影响目标在解除前一律 BLOCKED（LAB-QC-002、AC-QC-001）
- 解除关口恰为五项且偏差获批不在其中（LAB-QC-003、RULE-010）
- 全部事实追加式、DB 触发器强制；乐观并发 expectedCurrentVersion + advisory lock
- 事实、平台审计意图与发件箱同一事务提交；模块 audit_attempt 独立于回滚存活
- 批次冻结语义由 batch 模块拥有——本卡经端口消费，不复制也不放宽
- 本卡不修改结果模块事实：可报告性经 IQcReportabilityPort 表达，消费方自行担责

## 数据契约

```json
{
  "deviationApproval": [
    "deviationId",
    "qcRunId",
    "approvalRef{id, version}",
    "approvedBy",
    "approvedAt",
    "注：不属五关口"
  ],
  "impactEntry": [
    "impactId",
    "qcRunId",
    "targetType(RESULT_GROUP/TASK)",
    "targetId",
    "targetVersion",
    "recordedBy",
    "recordedAt"
  ],
  "qcResult": [
    "qcResultId",
    "qcRunId",
    "ruleRef{id, version}",
    "controlType(BLANK/SPIKE/DUPLICATE/REFERENCE_MATERIAL/CALIBRATION_CHECK)",
    "observedValue",
    "verdict(PASS/FAIL)",
    "verdictBasis"
  ],
  "qcRun": [
    "qcRunId",
    "ruleSetVersion(QC-IMPACT@1.0.0)",
    "objectScope{legalEntityId, laboratoryId}",
    "batchId",
    "expectedBatchVersion",
    "batchGateDecision",
    "batchGateRuleSetVersion",
    "methodRef{id, version}",
    "qcRuleSetRef{id, version}",
    "state(OPEN/PASSED/FAILED/RELEASED)",
    "version"
  ],
  "releaseGate": [
    "gateId",
    "qcRunId",
    "kind(INVESTIGATION/IMPACT_SCOPE/VALIDITY_DECISION/ADOPTION_RULE/TECHNICAL_REVIEW)",
    "evidenceRef{id, version}",
    "satisfiedBy",
    "satisfiedAt"
  ],
  "reportabilityResult": [
    "decision(ALLOWED/BLOCKED/UNKNOWN)",
    "reasonCodes[]",
    "qcRunId?",
    "targetId",
    "outstandingGates[]",
    "ruleSetVersion"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "QC.VALIDATION_FAILED",
    "QC.ELIGIBILITY_BLOCKED",
    "QC.APPLICABILITY_UNKNOWN",
    "QC.RELEASE_GATE_INCOMPLETE",
    "QC.EXPECTED_VERSION_CONFLICT",
    "QC.NOT_AUTHORIZED",
    "QC.OBJECT_NOT_ACCESSIBLE",
    "QC.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/qc-runs → 201 开启 QC 运行（批次门禁固定）",
    "POST /api/v1/qc-runs/{id}/results → 201 追加 QC 规则结果",
    "POST /api/v1/qc-runs/{id}/verdict → 201 判定运行 PASSED/FAILED",
    "POST /api/v1/qc-runs/{id}/impact → 201 登记影响集",
    "POST /api/v1/qc-runs/{id}/deviation-approval → 201 记录偏差获批（不解除阻断）",
    "POST /api/v1/qc-runs/{id}/gates → 201 满足单个解除关口",
    "POST /api/v1/qc-runs/{id}/release → 201 五关口齐备后解除阻断",
    "GET /api/v1/qc-runs/{id} → 200 运行、结果、影响集与关口明细",
    "GET /api/v1/qc-runs/{id}/reportability → 200 可报告性决策"
  ],
  "publicPort": "IQcReportabilityPort.EvaluateAsync(QcReportabilityRequest) → ALLOWED/BLOCKED/UNKNOWN，按目标与版本+规则集固定，供报告链后续卡消费"
}
```

## 状态转换

- QC 运行：OPEN →（全 PASS）PASSED；OPEN →（任一 FAIL）FAILED →（五关口齐备）RELEASED；均不可逆
- 关口：未满足 → 已满足（单向一次）

## 权限与职责分离

- 新增能力 qc.manage（运行/结果/判定/影响/关口/解除/读取共用，操作差异由状态约束）；HttpClaims 精确 claim 检查
- 消费的批次状态端口自行强制其能力，本卡不放宽也不复制

## 审计要求

- 每个命令写平台 audit_intent（同事务）+ outbox 事件（Qc.RunOpened/ResultRecorded/VerdictRecorded/ImpactRecorded/GateSatisfied/Released）
- 失败尝试写 qc.audit_attempt（SHA-256 目标哈希，独立于回滚）
- 读取写 READ_QC_RUN 审计

## UX 状态

- 本卡不新增前端页面
- QC 队列与放行界面属后续卡

## 可观测性

- 计数器：运行数、结果判定分布、影响目标数、关口满足分布、解除数、可报告性决策分布
- 结构化日志固定 correlationId 与错误码

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-QC-001-01 | positive | 未冻结批次与固定方法/规则集版本 | 开启运行并逐条落 QCResult | 版本原样固定；全 PASS → PASSED；审计+发件箱同事务 |
| TC-QC-001-02 | positive | 一条规则 FAIL | 判定并登记覆盖全批的影响集 | 运行 FAILED；全部目标登记；空影响集被拒绝；重复目标被拒绝 |
| TC-QC-001-03 | negative | QC 失败且已记录偏差获批 | 影响范围与有效性决定未记录时查询可报告性并尝试解除 | BLOCKED 并列明未满足关口；解除被拒 QC.RELEASE_GATE_INCOMPLETE；无解除事实 |
| TC-QC-001-04 | boundary | 五关口中任缺一项 | 解除 | 逐项拒绝并列明缺失关口；齐备后解除成功且可报告性 ALLOWED |
| TC-QC-001-05 | negative | 批次状态端口 BLOCKED 或异常 | 开启运行 | QC.ELIGIBILITY_BLOCKED / QC.APPLICABILITY_UNKNOWN；运行事实为零；audit_attempt 恰一次 |
| TC-QC-001-06 | negative | 已有 QC 事实 | UPDATE/DELETE 及并发同版本提交 | 55000 拒绝；恰一个提交成功，另一方 EXPECTED_VERSION_CONFLICT |
| TC-QC-001-07 | negative | 审计或发件箱注入失败 | 开启运行 | 业务事实回滚为零；audit_attempt 恰一次 |
| TC-QC-001-08 | boundary | 已解除与未解除运行 | 正确/过期版本与未知规则集查询 | ALLOWED / BLOCKED / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN] |

## 明确非目标

- 不实现环境监控采集或校准状态权威来源（OD-012 未决）
- 不实现分包方 QC 回传（OD-013 未决）
- 不修改批次冻结语义或结果模块事实（经端口消费）
- 不实现报告签发闸门（报告链待 OD-011/022/029）
- 不实现 QC 限值/控制图统计或趋势判定（规则判定由调用方声明依据）
- 不新增前端页面
- 不触碰未决 OD，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-QC-001__v1.0.0.json`
- `spec/requirements/BUS-QC-002__v1.0.0.json`
- `spec/requirements/BUS-QC-003__v1.0.0.json`
- `spec/acceptance/AC-QC-001__v1.0.0.json`
- `spec/stories/ATC-QC-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-27-dev-021-qc-impact-propagation/**`
- `OpenLIMS.slnx`
- `contracts/qc/**`
- `src/modules/qc/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/unit/qc/**`
- `tests/contract/qc/**`
- `tests/integration/qc/**`
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
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/qc/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-QC-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module qc`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `python -m tools.specgen check`

## 完成定义

- 规则执行、影响传播、五关口解除与可报告性端口全部落地且追加式 DB 强制
- AC-QC-001（偏差获批不解除）与五关口逐项缺失均有回归测试
- 全部既有测试项目保持绿色
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
