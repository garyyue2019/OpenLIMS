<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-TOY-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-TOY-001：实施 DEV-024 玩具年龄分级判定与可触及性评估

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-TOY-AGE-ACCESSIBILITY` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 技术负责人, 玩具行业包负责人, QA负责人 |
| 影响模块 | toy, age-grade, declaration, accessibility, abuse-event, scope-reassessment, audit, outbox, authorization, automated-test |
| 来源 | PRD-MAIN#OPS-TOY-001, PRD-MAIN#OPS-TOY-002, PRD-MAIN#OPS-TOY-003 |
| 固定依赖 | ED-001@2.0.0, OD-001@1.0.0, OD-002@1.0.0, BUS-TOY-001@1.0.0, BUS-TOY-002@1.0.0, AC-TOY-001@1.0.0, ATC-PLT-001@1.0.0, ATC-PLT-002@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `9378943c2fa4dd87ff918c12953d127006619a49d31a41286c1965fcd9cfc70b` |

## 业务结果

年龄分级是玩具检测一切要求的入口——它决定适用条款、样品需求与标签审查。把客户声明和实验室判定分开存，是因为它们经常不一致，而实验室要为自己的判定负责；把判定冻结成不可变版本，是因为改判必须留下'当初是怎么判的'。可触及性同理：滥用试验之后暴露出来的部件会带出新的机械、化学与标签要求，不触发重评就等于漏检。

## 主要参与者

玩具行业包技术人员（toy.manage 能力）与批准年龄判定的技术负责人

## 触发条件

客户提交年龄/用途声明，或实验室做出、改判年龄分级，或记录一次可触及性评估

## 前置条件

- OD-001 已决定玩具/婴童用品为试点行业包
- 平台请求上下文、对象级授权与事务内审计/发件箱已交付（DEV-002/003）

## 正常路径

- POST 客户声明：记录声明年龄下限、用途描述与声明来源，作为独立事实留痕
- POST 年龄判定：携带判定依据、适用标准引用与批准人，落为 DRAFT 版本
- POST 冻结：把该版本置为 EFFECTIVE 并把前一生效版本转为 SUPERSEDED
- POST 可触及性评估：给出阶段与该阶段的完整可触及部件集合；AFTER_ABUSE 另附滥用事件标识
- 服务端比对前序版本部件集合，有新增即为机械/化学/标签各追加一条 PENDING 重评触发
- POST 结清重评：以批准结论把指定触发置为 RESOLVED
- GET 判定/评估：按版本号取回其自身内容；GET 概览返回生效判定、最新评估与未结清触发

## 失败路径

- 判定缺依据、缺标准引用或缺批准人 → TOY.VALIDATION_FAILED
- 对已冻结版本再次修改或冻结 → TOY.DECISION_FROZEN
- AFTER_ABUSE 未给滥用事件标识，或 INITIAL/AFTER_NORMAL_USE 携带了滥用事件标识 → TOY.VALIDATION_FAILED
- 首个评估阶段不是 INITIAL，或部件集合为空 → TOY.VALIDATION_FAILED
- 结清一个不存在或已结清的重评触发 → TOY.REASSESSMENT_NOT_PENDING
- 并发写入同一产品导致 expectedCurrentVersion 不符 → TOY.EXPECTED_VERSION_CONFLICT
- UPDATE/DELETE 任何声明、判定、评估、部件或触发 → 数据库 55000（TOY.TOY_APPEND_ONLY）
- 行为人缺失/组织不匹配/能力拒绝 → TOY.NOT_AUTHORIZED，仅 audit_attempt 留痕
- 平台审计或发件箱写入失败 → 整体回滚，业务事实不产生

## 领域不变量

- 客户声明与实验室判定是两类事实，分表存储，声明变更不改写判定（OPS-TOY-001）
- 判定携带依据、标准引用、批准人与冻结状态；冻结后内容不可变（OPS-TOY-001）
- 改判以追加新版本表达，前一版本转 SUPERSEDED 而非被覆盖（OPS-TOY-002）
- 同一时刻至多一条 EFFECTIVE 判定：生效状态由追加式冻结日志派生，不存可变状态列
- 评估按 INITIAL / AFTER_NORMAL_USE / AFTER_ABUSE 分别版本化，版本 1 必须是 INITIAL（OPS-TOY-003）
- 新暴露部件必须对机械、化学、标签三个范围各触发一次重评；无新增不得触发（OPS-TOY-003）
- 全部事实追加式；乐观并发 expectedCurrentVersion + advisory lock；事实、平台审计与发件箱同事务
- 本卡只交付判定与触发本身，不实施被触发范围的重算逻辑

## 数据契约

```json
{
  "accessibilityAssessment": [
    "assessmentId",
    "productId",
    "versionNumber",
    "stage(INITIAL/AFTER_NORMAL_USE/AFTER_ABUSE)",
    "abuseEventRef?",
    "accessibleParts[]",
    "assessedBy",
    "assessedAt"
  ],
  "ageDeclaration": [
    "declarationId",
    "productId",
    "declaredMinimumAgeMonths",
    "intendedUse",
    "declarationSource",
    "declaredBy",
    "declaredAt"
  ],
  "ageGradeDecision": [
    "decisionId",
    "productId",
    "versionNumber",
    "minimumAgeMonths",
    "rationale",
    "standardRef{id, version}",
    "approvedBy",
    "state(DRAFT/EFFECTIVE/SUPERSEDED)",
    "frozenAt?"
  ],
  "overview": [
    "productId",
    "effectiveDecision?",
    "decisions[{versionNumber, state, minimumAgeMonths, frozenAt}]",
    "latestAssessment?",
    "pendingTriggers[]",
    "accessibilityStatus(SETTLED/REASSESSMENT_PENDING)",
    "ruleSetVersion"
  ],
  "reassessmentTrigger": [
    "triggerId",
    "productId",
    "assessmentVersion",
    "scope(MECHANICAL/CHEMICAL/LABELING)",
    "newlyExposedParts[]",
    "state(PENDING/RESOLVED)",
    "resolutionRef?",
    "resolvedBy?",
    "resolvedAt?"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "TOY.VALIDATION_FAILED",
    "TOY.DECISION_FROZEN",
    "TOY.DECISION_NOT_FOUND",
    "TOY.REASSESSMENT_NOT_PENDING",
    "TOY.EXPECTED_VERSION_CONFLICT",
    "TOY.NOT_AUTHORIZED",
    "TOY.OBJECT_NOT_ACCESSIBLE",
    "TOY.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/toy/products/{id}/age-declarations → 201 记录客户年龄/用途声明",
    "POST /api/v1/toy/products/{id}/age-grade-decisions → 201 追加年龄判定版本",
    "POST /api/v1/toy/products/{id}/age-grade-decisions/{versionNumber}/freeze → 200 冻结为生效",
    "POST /api/v1/toy/products/{id}/accessibility-assessments → 201 记录一次评估并按新暴露部件触发重评",
    "POST /api/v1/toy/products/{id}/reassessment-triggers/{triggerId}/resolution → 200 结清重评触发",
    "GET /api/v1/toy/products/{id}/overview → 200 生效判定、最新评估与未结清触发"
  ],
  "publicPort": "IToyAgeGradeStatusPort.EvaluateAsync(ToyAgeGradeStatusRequest) → ALLOWED/BLOCKED/UNKNOWN 与生效年龄判定版本、可触及性状态，版本+规则集固定，供样品需求与标签审查链消费；UNKNOWN 视为拒绝"
}
```

## 状态转换

- 年龄判定：DRAFT →（冻结）EFFECTIVE；EFFECTIVE →（新版本冻结）SUPERSEDED；SUPERSEDED 为终态
- 重评触发：PENDING →（批准结论）RESOLVED，不可逆
- 可触及性状态：存在 PENDING 触发时 REASSESSMENT_PENDING，全部结清后 SETTLED

## 权限与职责分离

- 新增单一能力 toy.manage；读写均经对象级授权端口精确匹配声明
- 冻结判定额外要求批准人字段，其权威校验属外部身份体系

## 审计要求

- 每个命令写平台 audit_intent（同事务）+ outbox 事件（Toy.AgeDeclared/AgeGradeDecided/AgeGradeFrozen/AccessibilityAssessed/ReassessmentResolved）
- 失败尝试写 toy.audit_attempt（独立连接，回滚后仍存活）
- 读取概览写 READ_TOY_OVERVIEW 审计

## UX 状态

- 本卡不新增前端页面

## 可观测性

- 计数器：声明数、判定数、冻结数、评估数、各范围触发数、结清数
- 结构化日志固定 correlationId 与错误码

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-TOY-001-01 | positive | 产品尚无声明与判定 | 先记录客户声明再做出实验室判定 | 两条事实各自可取回；判定不携带声明内容；审计+发件箱同事务 |
| TC-TOY-001-02 | negative | 产品已有声明 | 分别缺依据、缺标准引用、缺批准人、给非法年龄 | 逐项 TOY.VALIDATION_FAILED；不产生判定 |
| TC-TOY-001-03 | positive | V1 判定已冻结生效 | 客户改口后追加 V2 判定并冻结；分别按 V1/V2 取回 | V2 为唯一 EFFECTIVE，V1 转 SUPERSEDED；按 V1 取回仍返回其自身依据与批准人；V1 内容未被改写 |
| TC-TOY-001-04 | negative | V1 判定已冻结 | 再次冻结 V1，或 UPDATE/DELETE 其行 | TOY.DECISION_FROZEN；数据库 55000 拒绝改写 |
| TC-TOY-001-05 | negative | 产品已有 INITIAL 评估 | AFTER_ABUSE 不给事件标识，或 INITIAL 携带事件标识，或首个评估不是 INITIAL | 逐项 TOY.VALIDATION_FAILED |
| TC-TOY-001-06 | positive | INITIAL 评估不含内部电池仓 | 记录含内部电池仓的 AFTER_ABUSE 评估 | 机械/化学/标签各一条 PENDING 触发；触发携带新暴露部件清单；可触及性状态为 REASSESSMENT_PENDING |
| TC-TOY-001-07 | boundary | INITIAL 评估已记录 | 记录部件集合相同或更少的 AFTER_NORMAL_USE 评估 | 不产生任何触发；可触及性状态为 SETTLED |
| TC-TOY-001-08 | negative | 存在三条 PENDING 触发 | 逐条结清后再次结清同一条 | 全部结清后状态 SETTLED；重复结清 TOY.REASSESSMENT_NOT_PENDING |
| TC-TOY-001-09 | negative | 产品已有判定与评估 | UPDATE/DELETE 任一事实表，及并发追加同一产品的判定 | 55000 拒绝；恰一个成功，另一方 TOY.EXPECTED_VERSION_CONFLICT |
| TC-TOY-001-10 | negative | 审计或发件箱注入失败；另有含历史判定的产品 | 追加判定；以正确/过期版本与未知规则集查询状态端口 | 判定回滚为零且 audit_attempt 恰一次；端口返回 ALLOWED / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN] |

## 明确非目标

- 不实施被触发的机械/化学/标签范围重算本身，只产生并管理触发
- 不实现 TestUnit 危险域分配与互斥破坏性任务（OPS-TOY-004/006 属后续卡）
- 不实现多测试单元汇总结论（OPS-TOY-005 依赖 OD-034，未决）
- 不实现 LabelReview 失效与重审（OPS-TOY-007 属后续卡）
- 不实现玩具样品需求计算与化学最低取样量
- 不接入外部标准库或年龄分级规则引擎
- 不修改既有模块的语义
- 不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-TOY-001__v1.0.0.json`
- `spec/requirements/BUS-TOY-002__v1.0.0.json`
- `spec/acceptance/AC-TOY-001__v1.0.0.json`
- `spec/stories/ATC-TOY-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-27-dev-024-toy-age-grade-accessibility/**`
- `OpenLIMS.slnx`
- `contracts/toy/**`
- `src/modules/toy/**`
- `src/host/api/**`
- `tests/unit/toy/**`
- `tests/contract/toy/**`
- `tests/integration/toy/**`
- `tests/architecture/**`
- `tests/test_repository_contract.py`
- `docs/domain/toy/**`
- `docs/ai-development/06-release1-backlog.md`
- `scripts/verify.ps1`
- `scripts/verify.sh`
- `contracts/allocation/OpenLIMS.Contracts.Allocation/packages.lock.json`
- `contracts/batch/OpenLIMS.Contracts.Batch/packages.lock.json`
- `contracts/billing/OpenLIMS.Contracts.Billing/packages.lock.json`
- `contracts/instrument/OpenLIMS.Contracts.Instrument/packages.lock.json`
- `contracts/labeling/OpenLIMS.Contracts.Labeling/packages.lock.json`
- `contracts/qc/OpenLIMS.Contracts.Qc/packages.lock.json`
- `contracts/quantity/OpenLIMS.Contracts.Quantity/packages.lock.json`
- `contracts/receiving/OpenLIMS.Contracts.Receiving/packages.lock.json`
- `contracts/report/OpenLIMS.Contracts.Report/packages.lock.json`
- `contracts/result/OpenLIMS.Contracts.Result/packages.lock.json`
- `contracts/scope/OpenLIMS.Contracts.Scope/packages.lock.json`
- `src/modules/**/packages.lock.json`
- `tests/**/packages.lock.json`

## 验证命令

- `python -m tools.specgen ready --story ATC-TOY-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module toy`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `python -m tools.specgen check`

## 完成定义

- 声明与判定分表、判定四要素、冻结不可变、改判追加新版本全部落地且追加式 DB 强制
- 三阶段评估版本化与新暴露部件的三范围触发各有回归测试
- AC-TOY-001 全程有端到端集成测试
- toy 模块公开 IToyAgeGradeStatusPort 且 UNKNOWN 视为拒绝
- 全部既有测试项目保持绿色
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
