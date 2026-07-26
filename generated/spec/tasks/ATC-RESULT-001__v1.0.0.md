<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-RESULT-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-RESULT-001：实施 DEV-014 结果来源与采用

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-QUALITY` |
| Feature | `FEAT-RESULT-PROVENANCE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室技术负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | result, batch, raw-data, provenance, adoption, retest, authorization, audit, outbox, automated-test |
| 来源 | PRD-MAIN#LAB-RAW-001, PRD-MAIN#LAB-RAW-002, PRD-MAIN#LAB-PROV-001, PRD-MAIN#LAB-PROV-002, PRD-MAIN#LAB-RES-001, PRD-MAIN#LAB-RES-002, PRD-MAIN#LAB-RES-003, PRD-MAIN#LAB-RES-004, PRD-MAIN#AC-RETEST-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ED-001@2.0.0, OD-002@1.0.0, OD-030@1.0.0, ATC-BATCH-001@1.0.0, BUS-RES-001@1.0.0, BUS-RES-002@1.0.0, BUS-RES-003@1.0.0, AC-RETEST-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `89ae098800fcc9e3929738cf6a9978e0a79567d6cda65f661882c816343c0124` |

## 业务结果

实验室的每个报告结果都可追溯到不可变原始观测与外部证据；复测不能被用来挑选有利结果，采用决定在看到复测数据前就被规则锁定，下游报告只消费唯一有效采用结果。

## 主要参与者

具有 result.record 及法人、实验室、客户、委托和产品类别对象范围的检测执行/复核人

## 触发条件

执行人创建结果组、提交观测/派生、记录采用规则或提交采用

## 前置条件

- DEV-013 已交付 BatchStatusPort
- 部署绑定唯一 OrganizationGroup
- 调用身份由服务端建立
- 外部证据由调用方提交稳定 ID、版本、SHA-256 和解析器版本

## 正常路径

- 校验 actor capability 和对象范围
- 创建结果组：在独立事务评估 BatchStatusPort，ALLOWED 后固定批次决定与版本（gate-then-commit）
- 提交观测：类型六分，非 INITIAL 附触发原因与批准引用，RETEST 要求组内已有采用规则
- 提交派生：输入必须组内已存在且不重复，排除输入附理由，固定聚合规则版本
- 记录采用规则（策略 RETEST_REPLACES_ORIGINAL 或 TECHNICAL_REVIEW_SELECTS）
- 提交采用：引用规则版本并按策略校验目标，采用版本递增且最新有效
- 公共 ResultAdoptionPort 对当前组版本返回 ALLOWED 与有效采用

## 失败路径

- 批次门禁 BLOCKED/UNKNOWN 或端口异常返回 RES.ELIGIBILITY_BLOCKED / RES.APPLICABILITY_UNKNOWN
- 未知观测类型、缺失证据哈希或解析器版本返回 RES.VALIDATION_FAILED
- 无预先规则的 RETEST 观测或采用返回 RES.ADOPTION_RULE_REQUIRED
- 违反策略的采用返回 RES.ADOPTION_STRATEGY_VIOLATION
- 悬空输入、重复计入或无规则聚合返回 RES.VALIDATION_FAILED
- 无能力或跨范围请求返回 RES.NOT_AUTHORIZED
- 旧 expectedCurrentVersion 返回 RES.EXPECTED_VERSION_CONFLICT
- 持久化、审计或 Outbox 失败整体回滚

## 领域不变量

- 观测、派生、规则与采用创建后不可修改、覆盖或删除（LAB-RAW-002）
- 来源图按构造无环：输入必须已存在
- 排除输入与纳入输入同等保留
- 每组只有一个有效采用结果（最新采用版本）
- 采用必须引用预先记录的规则版本
- UNKNOWN 等同阻断
- 不读取 batch/allocation 等私有表，仅消费版本化公共端口
- 不做 QC 规则执行、限值判定或报告签发

## 数据契约

```json
{
  "adoption": [
    "adoptionVersion",
    "targetId",
    "ruleVersion",
    "reviewApprovalRef?/version",
    "adoptedBy",
    "adoptedAt"
  ],
  "adoptionRule": [
    "ruleVersion",
    "strategy(RETEST_REPLACES_ORIGINAL/TECHNICAL_REVIEW_SELECTS)",
    "ruleRef/version",
    "recordedBy",
    "recordedAt"
  ],
  "derivation": [
    "derivationId",
    "aggregationRuleRef/version",
    "value",
    "unit",
    "inputs[{targetId, included, rationale?}]"
  ],
  "group": [
    "resultGroupId",
    "batchId/batchVersion",
    "memberId",
    "testItemRef/version",
    "scopeLineId",
    "batchGate{decision,ruleSetVersion}",
    "version",
    "state(ACTIVE)",
    "createdBy",
    "createdAt"
  ],
  "observation": [
    "observationId",
    "kind(INITIAL/DUPLICATE/RETEST/SUPPLEMENT/RE_PREPARATION/RE_SAMPLING)",
    "value",
    "unit",
    "evidence{sourceSystem,externalRef/version,sha256,parserVersion}",
    "triggerReason?",
    "approvalRef?/version",
    "recordedBy",
    "recordedAt"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "RES.VALIDATION_FAILED",
    "RES.ELIGIBILITY_BLOCKED",
    "RES.APPLICABILITY_UNKNOWN",
    "RES.ADOPTION_RULE_REQUIRED",
    "RES.ADOPTION_STRATEGY_VIOLATION",
    "RES.NOT_AUTHORIZED",
    "RES.OBJECT_NOT_ACCESSIBLE",
    "RES.EXPECTED_VERSION_CONFLICT",
    "RES.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/result-groups",
    "POST /api/v1/result-groups/{id}/observations",
    "POST /api/v1/result-groups/{id}/derivations",
    "POST /api/v1/result-groups/{id}/adoption-rule",
    "POST /api/v1/result-groups/{id}/adoptions",
    "GET /api/v1/result-groups/{id}",
    "GET /api/v1/result-groups/{id}/adoption-status"
  ],
  "publicPort": "ResultAdoptionPort@v1",
  "success": [
    "201 ResultGroupResult",
    "201 ResultObservationResult",
    "201 ResultDerivationResult",
    "201 AdoptionRuleResult",
    "201 ResultAdoptionResult",
    "200 ResultGroupResult",
    "200 ResultAdoptionStatusResult"
  ]
}
```

## 状态转换

- NONE -> ACTIVE@v1 by 建组（批次门禁 ALLOWED）
- ACTIVE@vN -> ACTIVE@vN+1 by 追加观测/派生/规则/采用
- 任何失败不产生事实也不推进版本

## 权限与职责分离

- Result 模块只新增并校验 result.record 单一能力和既有五维对象范围
- 被消费的 BatchStatusPort 按其已发布契约校验 batch.manage，本卡不放宽也不复制
- 不新增草稿编辑或多级签署
- 客户端不能提交 OrganizationGroup

## 审计要求

- 记录命令类型、groupId/version、观测/采用摘要、actor、correlationId 和结果
- 失败、越权、版本冲突与事务回滚通过独立追加路径记录
- Outbox eventId 与结果事实一一对应
- 敏感正文不写日志或指标

## UX 状态

- 本卡不新增前端页面
- HTTP 响应返回服务端计算的组版本、来源图与有效采用
- 客户端不得自行推断采用、绕过批次门禁或把 UNKNOWN 当作允许

## 可观测性

- result_observation_total 按 kind 聚合
- result_adoption_total 按 strategy 聚合
- result_gate_total 与 result_rejected_total 按决定/原因聚合
- UNKNOWN、事务回滚和 Outbox 积压写结构化告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-RES-001-01 | positive | 批次门禁 ALLOWED | 建组并提交 INITIAL 观测与证据 | 组 v1→v2，证据哈希与解析器版本固定；审计与 Outbox 同事务提交 |
| TC-RES-001-02 | boundary | 组内两条观测 | 提交含排除输入的派生、重复计入与悬空输入 | 合法派生成功且排除理由保留；重复计入与悬空输入被拒绝 |
| TC-RES-001-03 | negative | 组内已有 INITIAL 且无采用规则 | 直接提交 RETEST 观测 | RES.ADOPTION_RULE_REQUIRED；无副作用 |
| TC-RES-001-04 | negative | RETEST_REPLACES_ORIGINAL 规则与更有利的 INITIAL | 尝试采用 INITIAL | RES.ADOPTION_STRATEGY_VIOLATION；采用最新 RETEST 成功 |
| TC-RES-001-05 | permission | 缺少 capability 或对象范围 | 任一操作 | 统一拒绝；追加脱敏失败审计 |
| TC-RES-001-06 | concurrency | 两个调用使用相同 expectedCurrentVersion | 并发提交观测 | 最多一笔成功；另一笔版本冲突 |
| TC-RES-001-07 | recovery | 审计或 Outbox 失败 | 提交并重试 | 首笔全部回滚；重试只产生一个逻辑事实 |
| TC-RES-001-08 | regression | 两次合规采用 | 查询采用状态并尝试改写历史 | 最新采用版本有效且历史保留；数据库拒绝 UPDATE/DELETE；旧组版本状态查询 UNKNOWN |

## 明确非目标

- 不实现 QC 规则执行或解除阻断（LAB-QC-001/003 待 OD-001）
- 不实现限值判定、符合性或报告签发
- 不实现单位换算或确定性计算引擎（FEAT-CALC）
- 不实现分包回传（LAB-SUB/OD-013）
- 不实现仪器导入（待 OD-001）
- 不新增前端工作台
- 不修改 Release baseline，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-RES-001__v1.0.0.json`
- `spec/requirements/BUS-RES-002__v1.0.0.json`
- `spec/requirements/BUS-RES-003__v1.0.0.json`
- `spec/acceptance/AC-RETEST-001__v1.0.0.json`
- `spec/stories/ATC-RESULT-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-014-result-provenance-adoption/**`
- `OpenLIMS.slnx`
- `contracts/result/**`
- `src/modules/result/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/architecture/**`
- `tests/unit/result/**`
- `tests/contract/result/**`
- `tests/integration/result/**`
- `tests/e2e/result/**`
- `tests/contract/labeling/OpenLIMS.Labeling.ContractTests/packages.lock.json`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/contract/receiving/OpenLIMS.Receiving.ContractTests/packages.lock.json`
- `tests/contract/scope/OpenLIMS.Scope.ContractTests/packages.lock.json`
- `tests/contract/quantity/OpenLIMS.Quantity.ContractTests/packages.lock.json`
- `tests/contract/allocation/OpenLIMS.Allocation.ContractTests/packages.lock.json`
- `tests/contract/batch/OpenLIMS.Batch.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/result/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-RESULT-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module result`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 追加迁移不改写既有模块历史
- 观测、派生、规则与采用完整、不可变且版本固定
- 预先规则、策略校验、来源图约束和 UNKNOWN 始终失败关闭
- 权限、并发、事务、恢复、审计和 Outbox 测试通过
- 公共采用状态端口只依据精确组版本
- 无跨模块私表访问
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
