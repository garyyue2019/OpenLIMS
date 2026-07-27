<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-TOY-003@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-TOY-003：实施 DEV-026 玩具 LabelReview 版本失效与重审

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-QUALITY` |
| Feature | `FEAT-TOY-LABEL-REVIEW` |
| 开发就绪度 | `blocked` |
| 变更级别 | `major` |
| 负责人角色 | 玩具行业包负责人, 法规负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | toy, label-artifact, packaging, instruction, marketing-age-claim, label-review, impact-invalidation, photo-evidence, authorization, audit, automated-test |
| 来源 | PRD-MAIN#OPS-TOY-002, PRD-MAIN#OPS-TOY-007 |
| 固定依赖 | ED-001@2.0.0, OD-001@1.0.0, OD-002@1.0.0, BUS-TOY-001@1.0.0, BUS-TOY-002@1.0.0, BUS-TOY-005@0.1.0, AC-TOY-004@0.1.0, ATC-TOY-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `300d1a403de387d7f7b8c7b0880cb0939b6622ed83df168f5d19275797c9c786` |

## 业务结果

实验室能证明某一市场和语言的包装/标签/说明书/营销年龄声明究竟审过哪个不可变版本；年龄决定或产品事实变化后，受影响旧审查不会继续悄悄放行，新审查与变更原因完整串联。

## 主要参与者

提交工件版本的玩具技术人员、执行 LabelReview 的法规/技术审查人，以及查询有效审查状态的下游服务

## 触发条件

提交新工件版本、批准/拒绝审查，或产品/年龄判定变化需要评估既有审查是否失效

## 前置条件

- ATC-TOY-001 已提供版本化 AgeGradeDecision、AccessibilityAssessment 与产品聚合版本
- 图片证据使用平台不可变对象引用和哈希，不把二进制正文写入 toy 数据库
- 拟议 requirement、acceptance、权限和影响匹配语义经人工批准为精确后继版本

## 正常路径

- 创建 artifactId 并追加工件版本，固定类型、语言、市场、内容哈希和图片证据引用
- 创建 LabelReview 草案，固定工件版本、产品版本、AgeGradeDecision 版本、审查范围和规则集
- 获准审查人批准或拒绝，形成不可变 reviewVersion
- 产品或 AgeGradeDecision 新版本形成时，以固定 impactRuleRef@version 比对 changeScopeRefs 与 reviewScopeRefs
- 命中范围的 APPROVED 审查追加 INVALIDATED 事实并进入 RE_REVIEW_REQUIRED；不命中的审查保留状态且记录影响评估
- 提交新工件/审查版本，引用触发变更、前一 reviewVersion 与失效原因
- 版本固定公共端口返回 VALID/RE_REVIEW_REQUIRED/REJECTED/UNKNOWN，UNKNOWN 视为拒绝

## 失败路径

- 缺工件类型、语言、市场、内容哈希或图片证据 → TOY.LABEL_ARTIFACT_INVALID
- 审查未固定工件/产品/年龄决定/规则版本 → TOY.LABEL_REVIEW_INVALID
- 影响规则缺失、版本未知或无法判定范围重叠 → TOY.LABEL_IMPACT_UNKNOWN，旧审查按 UNKNOWN 阻断
- 尝试使用 INVALIDATED/REJECTED/UNKNOWN 审查 → TOY.LABEL_REVIEW_NOT_VALID
- 修改/删除已保存工件、审查或失效事实 → 数据库 55000（TOY.TOY_APPEND_ONLY）
- expectedCurrentVersion 不匹配或并发审查 → TOY.EXPECTED_VERSION_CONFLICT
- 缺拟议 toy.label.manage 或 toy.label.review 能力 → TOY.NOT_AUTHORIZED
- 对象证据、审计、发件箱或持久化失败 → 整体回滚

## 领域不变量

- 工件、图片引用、审查与失效事实全部追加式且精确版本固定
- 受影响 APPROVED 审查必须失效；UNKNOWN 不能沿用旧批准
- 不受影响的语言/市场/范围不做全局失效，但影响评估证据必须可重建
- 新审查引用触发变更和旧审查，历史内容与原因永不覆盖
- 产品合规 LabelReview 归 toy 模块所有，不访问 labeling 模块的 print_job/scan 私表
- 本卡不实现通用产品主数据编辑、报告 ConformityDecision 或多 TestUnit 汇总结论

## 数据契约

```json
{
  "labelArtifact": [
    "artifactId",
    "productId",
    "artifactType(PACKAGING/LABEL/INSTRUCTION/MARKETING_AGE_CLAIM)",
    "versionNumber",
    "language",
    "market",
    "contentHash",
    "imageEvidenceRefs[{objectRef, hash}]",
    "createdBy",
    "createdAt"
  ],
  "labelReview": [
    "reviewId",
    "artifactId/artifactVersion",
    "reviewVersion",
    "productVersion",
    "ageGradeDecisionVersion",
    "reviewScopeRefs[]",
    "impactRuleRef@version",
    "ruleSetVersion",
    "state(DRAFT/APPROVED/REJECTED/INVALIDATED)",
    "reviewedBy?",
    "reviewedAt?",
    "decisionReason?"
  ],
  "reviewInvalidation": [
    "reviewId/reviewVersion",
    "changeType(PRODUCT_VERSION/AGE_GRADE_DECISION)",
    "changeRef/version",
    "matchedScopeRefs[]",
    "impactRuleRef@version",
    "reason",
    "invalidatedAt"
  ],
  "reviewStatus": [
    "decision(VALID/RE_REVIEW_REQUIRED/REJECTED/UNKNOWN)",
    "reasonCodes",
    "artifactVersion",
    "reviewVersion",
    "productVersion",
    "ageGradeDecisionVersion",
    "ruleSetVersion"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "TOY.LABEL_ARTIFACT_INVALID",
    "TOY.LABEL_REVIEW_INVALID",
    "TOY.LABEL_IMPACT_UNKNOWN",
    "TOY.LABEL_REVIEW_NOT_VALID",
    "TOY.EXPECTED_VERSION_CONFLICT",
    "TOY.NOT_AUTHORIZED",
    "TOY.OBJECT_NOT_ACCESSIBLE",
    "TOY.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/toy/products/{id}/label-artifacts → 201 创建工件首版",
    "POST /api/v1/toy/products/{id}/label-artifacts/{artifactId}/versions → 201 追加工件版本",
    "POST /api/v1/toy/products/{id}/label-artifacts/{artifactId}/reviews → 201 创建审查草案",
    "POST /api/v1/toy/products/{id}/label-reviews/{reviewId}/decision → 200 批准或拒绝",
    "GET /api/v1/toy/products/{id}/label-reviews/status → 200 按市场/语言/工件类型返回版本固定状态"
  ],
  "publicPort": "IToyLabelReviewStatusPort@v1：固定 productVersion、ageGradeDecisionVersion、market、language、artifactType、ruleSetVersion，返回 VALID/BLOCKED/UNKNOWN；UNKNOWN 视为拒绝"
}
```

## 状态转换

- 工件：ARTIFACT@Vn → ARTIFACT@Vn+1，仅追加
- 审查：DRAFT → APPROVED 或 REJECTED；APPROVED → INVALIDATED 由追加失效事实派生；终态不可逆
- 状态：VALID → RE_REVIEW_REQUIRED 在命中变更后；新批准 reviewVersion 才恢复 VALID；影响 UNKNOWN 时保持 UNKNOWN

## 权限与职责分离

- 拟议：创建工件/草案要求 toy.label.manage 与对象范围
- 拟议：批准/拒绝要求 toy.label.review；起草人与批准人是否允许同人需人工评审，本草案不默认
- 系统失效动作来自受信产品/年龄版本事件，不接受客户端伪造 approvedBy、invalidatedAt 或 changeRef
- 图片对象读取仍经平台对象授权；客户端不得提交 OrganizationGroup

## 审计要求

- 记录 CREATE_LABEL_ARTIFACT_VERSION、CREATE_LABEL_REVIEW、DECIDE_LABEL_REVIEW、INVALIDATE_LABEL_REVIEW 与精确 before/after version
- 失效审计包含 changeRef/version、匹配范围、影响规则版本和 correlationId
- 业务事实、audit_intent、outbox 同事务；失败/拒绝/UNKNOWN 独立追加 audit_attempt

## UX 状态

- 本卡不新增前端页面；未来 UI 必须区分工件版本、审查版本、失效原因与重审状态

## 可观测性

- 各类型/市场/语言工件与审查计数
- 失效、重审待办、UNKNOWN 影响和拒绝原因计数
- 结构化日志不含工件正文、图片二进制或客户敏感内容

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-TOY-003-01 | positive | 两种语言和市场的四类工件 | 分别创建和追加版本 | 类型/语言/市场/图片证据与内容哈希固定；旧版本原样可取回 |
| TC-TOY-003-02 | positive | 中文与英文审查均批准 | 年龄 V2 只命中中文审查范围 | 中文 INVALIDATED/RE_REVIEW_REQUIRED；英文保持 VALID 且影响证据存在 |
| TC-TOY-003-03 | negative | 影响规则版本未知 | 年龄决定变化并查询旧审查 | TOY.LABEL_IMPACT_UNKNOWN 或 UNKNOWN；旧审查不能继续放行 |
| TC-TOY-003-04 | positive | 旧审查已失效 | 新工件和新审查批准 | 新审查引用旧版本与触发变更；旧历史未改写 |
| TC-TOY-003-05 | boundary | 缺语言、市场、哈希、图片或非法工件类型 | 提交版本 | 逐项 TOY.LABEL_ARTIFACT_INVALID；业务事实为零 |
| TC-TOY-003-06 | permission | 有工件管理但无审查能力 | 批准审查 | TOY.NOT_AUTHORIZED；失败尝试留痕 |
| TC-TOY-003-07 | concurrency | 两个请求决定同一 DRAFT reviewVersion | 并发批准/拒绝 | 恰一个成功；另一方 TOY.EXPECTED_VERSION_CONFLICT |
| TC-TOY-003-08 | audit | 图片对象确认、审计或 Outbox 注入失败 | 创建工件、决定或失效 | 对应事实整体回滚；失败证据保留 |
| TC-TOY-003-09 | database-boundary | 工件、审查和失效已保存 | UPDATE/DELETE 任一行 | 数据库 55000 拒绝；历史仍完整 |

## 明确非目标

- 不修改既有 Labeling 收样标签打印/扫描语义或访问其私表
- 不实现产品主数据通用编辑器、外部法规内容库或图像 OCR/AI 判断
- 不实现 OPS-TOY-005 汇总结论、报告签发或认证状态
- 不定义影响范围、权限或职责分离的未批准默认值
- 不新增前端页面、Seal、tag、GitHub Release、部署或生产迁移执行

## 允许修改路径

- `spec/requirements/BUS-TOY-005__v0.1.0.json`
- `spec/acceptance/AC-TOY-004__v0.1.0.json`
- `spec/stories/ATC-TOY-003__v0.1.0.json`
- `generated/spec/**`
- `.planning/2026-07-27-dev-026-toy-label-review/**`
- `OpenLIMS.slnx`
- `contracts/toy/**`
- `src/modules/toy/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/unit/toy/**`
- `tests/contract/toy/**`
- `tests/integration/toy/**`
- `tests/architecture/**`
- `tests/test_repository_contract.py`
- `docs/domain/toy/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`
- `contracts/**/packages.lock.json`
- `src/modules/**/packages.lock.json`
- `src/host/**/packages.lock.json`
- `tests/**/packages.lock.json`

## 验证命令

- `python -m tools.specgen ready --story ATC-TOY-003@0.1.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module toy`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `python -m tools.specgen check`

## 完成定义

- 人工批准后继 BUS/AC/Story 精确版本且 ready 为 READY 后才编码
- 四类工件、语言、市场、图片证据与内容哈希版本化且数据库不可变
- 产品/年龄变化的命中、局部失效、UNKNOWN 和重审链有完整自动测试
- toy 与 labeling 模块私表边界通过架构和数据库测试
- 权限、并发、恢复、审计/Outbox 回滚和失败证据测试齐全
- 全仓门禁通过、二次 generate written=0、所有改动位于 approved Story allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
