<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-BILL-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-BILL-001：实施 DEV-015 唯一计费事实

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-BILLING-INTEGRATION` |
| Feature | `FEAT-BILLING-EVIDENCE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 财务负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | billing, result, authorization, audit, outbox, automated-test |
| 来源 | PRD-MAIN#FIN-BILL-001, PRD-MAIN#FIN-BILL-002, PRD-MAIN#FIN-BILL-003, PRD-MAIN#FIN-BILL-004, PRD-MAIN#FIN-BILL-005, PRD-MAIN#AC-BILL-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ED-001@2.0.0, OD-002@1.0.0, ATC-RESULT-001@1.0.0, BUS-BILL-001@1.0.0, BUS-BILL-002@1.0.0, BUS-BILL-003@1.0.0, AC-BILL-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `b6802a9d2521ecbc38a2b2c7adf6cf51b0b026252ae9cfc40864e0f0d870bb58` |

## 业务结果

每个合同约定的服务完成事实产生且仅产生一条有效计费证据；报告重发、接口重试或并发触发不会造成重复计费，免费项与更正全程可审计。

## 主要参与者

具有 billing.record 及法人、实验室、客户、委托和产品类别对象范围的计费操作人

## 触发条件

计费操作人为已采用的结果组提交计费证据或调整证据

## 前置条件

- DEV-014 已交付 ResultAdoptionPort
- 部署绑定唯一 OrganizationGroup
- 调用身份由服务端建立
- 合同基线、收费维度与货币由调用方提交精确稳定 ID 和版本

## 正常路径

- 校验 actor capability 和对象范围
- 在独立事务评估 ResultAdoptionPort，ALLOWED 后固定采用决定与目标（gate-then-commit）
- 校验四元组唯一键（服务事实×合同基线×收费维度×规则版本）
- 零金额必附原因，非零不得附零金额原因
- 原子保存不可变计费证据、审计和 Outbox
- 更正时追加引用原证据的正负调整证据
- 公共 BillingEvidencePort 对当前证据返回 ALLOWED

## 失败路径

- 采用门禁 BLOCKED/UNKNOWN 或端口异常返回 BIL.ELIGIBILITY_BLOCKED / BIL.APPLICABILITY_UNKNOWN
- 相同四元组重复提交返回 BIL.DUPLICATE_BILLING
- 零金额缺原因或非零带原因返回 BIL.VALIDATION_FAILED
- 调整引用不存在证据或金额为零返回 BIL.VALIDATION_FAILED
- 无能力或跨范围请求返回 BIL.NOT_AUTHORIZED
- 持久化、审计或 Outbox 失败整体回滚

## 领域不变量

- 相同四元组只存在一条有效计费证据（数据库唯一约束+领域校验双重保证）
- 证据与调整创建后不可修改或删除
- 调整可正可负但不得为零且必附原因
- 阶段只区分 SERVICE_COMPLETED 与 BILLABLE_CANDIDATE，不实现开票/应收/收入确认
- 货币为声明引用，不做换算
- UNKNOWN 等同阻断
- 不读取 result 私有表，仅消费版本化公共端口

## 数据契约

```json
{
  "adjustment": [
    "adjustmentId",
    "billingEvidenceId",
    "amount(非零，可负)",
    "reason",
    "recordedBy",
    "recordedAt"
  ],
  "evidence": [
    "billingEvidenceId",
    "resultGroupId/groupVersion",
    "adoptionTargetId（端口固定）",
    "contractBaselineRef/version",
    "chargeDimension",
    "billingRuleVersion",
    "amount",
    "currencyRef/version",
    "zeroAmountReason?",
    "stage(SERVICE_COMPLETED/BILLABLE_CANDIDATE)",
    "recordedBy",
    "recordedAt"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "BIL.VALIDATION_FAILED",
    "BIL.DUPLICATE_BILLING",
    "BIL.ELIGIBILITY_BLOCKED",
    "BIL.APPLICABILITY_UNKNOWN",
    "BIL.NOT_AUTHORIZED",
    "BIL.OBJECT_NOT_ACCESSIBLE",
    "BIL.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/billing-evidence",
    "POST /api/v1/billing-evidence/{id}/adjustments",
    "GET /api/v1/billing-evidence/{id}",
    "GET /api/v1/billing-evidence/{id}/status"
  ],
  "publicPort": "BillingEvidencePort@v1",
  "success": [
    "201 BillingEvidenceResult",
    "201 BillingAdjustmentResult",
    "200 BillingEvidenceResult",
    "200 BillingEvidenceStatusResult"
  ]
}
```

## 状态转换

- NONE -> BILLABLE_CANDIDATE by 采用门禁 ALLOWED 的原子创建
- 证据 -> 证据+调整链 by 追加调整
- 任何失败不产生事实

## 权限与职责分离

- Billing 模块只新增并校验 billing.record 单一能力和既有五维对象范围
- 被消费的 ResultAdoptionPort 按其已发布契约校验 result.record，本卡不放宽也不复制
- 客户端不能提交 OrganizationGroup

## 审计要求

- 记录命令类型、evidenceId、四元组摘要、金额、actor、correlationId 和结果
- 失败与越权通过独立追加路径记录
- Outbox eventId 与证据一一对应
- 敏感正文不写日志或指标

## UX 状态

- 本卡不新增前端页面
- HTTP 响应返回服务端计算的唯一键、调整链与净额输入
- 客户端不得自行推断计费资格或把 UNKNOWN 当作允许

## 可观测性

- billing_evidence_total 按 stage 与零金额聚合
- billing_adjustment_total 按正负聚合
- billing_gate_total 与 billing_rejected_total 按决定/原因聚合
- UNKNOWN、重复计费尝试和 Outbox 积压写结构化告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-BILL-001-01 | positive | 结果组有效采用且端口 ALLOWED | 提交计费证据 | 证据创建且固定采用目标；审计与 Outbox 同事务提交 |
| TC-BILL-001-02 | negative | 相同四元组已有证据 | 重复提交 | BIL.DUPLICATE_BILLING；只存在一条有效证据 |
| TC-BILL-001-03 | concurrency | 两个调用相同四元组 | 并发提交 | 最多一笔成功；唯一约束兜底 |
| TC-BILL-001-04 | boundary | 免费项 | 零金额带原因与不带原因提交；非零带零金额原因提交 | 带原因成功；缺原因与错配拒绝 |
| TC-BILL-001-05 | permission | 缺少 capability 或对象范围 | 任一操作 | 统一拒绝；追加脱敏失败审计 |
| TC-BILL-001-06 | negative | 采用端口 BLOCKED/UNKNOWN | 提交证据 | 失败关闭且无事实 |
| TC-BILL-001-07 | recovery | 审计或 Outbox 失败 | 提交并重试 | 首笔全部回滚；重试只产生一条证据 |
| TC-BILL-001-08 | regression | 已有证据 | 追加正负调整、尝试零额调整和改写历史 | 调整链保留且引用原证据；零额调整拒绝；数据库拒绝 UPDATE/DELETE |

## 明确非目标

- 不实现开票申请、法定发票或可开票余额（FIN-INV-*，条件接口）
- 不实现应收、收款、核销或对账（BusinessOps）
- 不实现收入确认或税务（OD-015/016）
- 不实现价格计算或折扣引擎（金额由调用方按合同基线声明）
- 不做货币换算（OD-017 open）
- 不新增前端工作台
- 不修改 Release baseline，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-BILL-001__v1.0.0.json`
- `spec/requirements/BUS-BILL-002__v1.0.0.json`
- `spec/requirements/BUS-BILL-003__v1.0.0.json`
- `spec/acceptance/AC-BILL-001__v1.0.0.json`
- `spec/stories/ATC-BILL-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-015-billing-evidence/**`
- `OpenLIMS.slnx`
- `contracts/billing/**`
- `src/modules/billing/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/architecture/**`
- `tests/unit/billing/**`
- `tests/contract/billing/**`
- `tests/integration/billing/**`
- `tests/e2e/billing/**`
- `tests/contract/labeling/OpenLIMS.Labeling.ContractTests/packages.lock.json`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/contract/receiving/OpenLIMS.Receiving.ContractTests/packages.lock.json`
- `tests/contract/scope/OpenLIMS.Scope.ContractTests/packages.lock.json`
- `tests/contract/quantity/OpenLIMS.Quantity.ContractTests/packages.lock.json`
- `tests/contract/allocation/OpenLIMS.Allocation.ContractTests/packages.lock.json`
- `tests/contract/batch/OpenLIMS.Batch.ContractTests/packages.lock.json`
- `tests/contract/result/OpenLIMS.Result.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/billing/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-BILL-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module billing`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 追加迁移不改写既有模块历史
- 四元组唯一键在领域与数据库双层强制
- 零金额、调整链、门禁与 UNKNOWN 始终失败关闭
- 权限、并发、事务、恢复、审计和 Outbox 测试通过
- 无跨模块私表访问
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
