<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-RPT-002@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-RPT-002：实施 DEV-023 报告签名与不可变版本链

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-REPORT` |
| Feature | `FEAT-RPT-VERSION-CHAIN` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 授权签字人, 质量负责人, 技术负责人, QA负责人 |
| 影响模块 | report, electronic-signature, content-hash, version-chain, correction, withdrawal, supersession, verification-page, audit, outbox, authorization, automated-test |
| 来源 | PRD-MAIN#RPT-SIGN-001, PRD-MAIN#RPT-VERS-001, PRD-MAIN#RPT-VERS-002, PRD-MAIN#RPT-VERS-003, PRD-MAIN#RPT-VERS-004, PRD-MAIN#SEC-SIGN-001, PRD-MAIN#SEC-SIGN-002, PRD-MAIN#AC-RPT-002 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, OD-001@1.0.0, OD-011@1.0.0, OD-022@1.0.0, BUS-RPT-004@1.0.0, BUS-RPT-005@1.0.0, AC-RPT-002@1.0.0, ATC-RPT-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `be1df834b2701d4d88f9277375391711e55fd283441f32e6e04873cba3ff1082` |

## 业务结果

报告获得可验证的不可变历史：每个已签发版本都有自己的快照、哈希与签名，改一个字就换一个哈希从而使旧签名对不上；旧引用永远取回它当初对应的那一版，更正只能以新版本表达。这正是 RPT-VERS-002/004 与 SEC-SIGN-002 要防的三件事——覆盖历史、静默换件、改内容不重签。

## 主要参与者

授权签字人（report.manage 能力 + 重新认证证据）与质量放行审核者

## 触发条件

签字人对通过门禁的报告执行签发；此后质量人员按 SOP 执行五种受控动作之一

## 前置条件

- DEV-022 的报告装配与签发门禁已交付
- 报告处于待批准且最新门禁评估 ALLOWED 并覆盖全部行
- OD-011/OD-022 已决定

## 正常路径

- GET 版本快照预览：服务端按规范化规则算出当前内容哈希供签字人核对
- POST 签发：携带重新认证证据引用、签署意图文本与期望内容哈希；服务端重算一致则落快照+哈希+签名，版本进入 ISSUED
- POST 更正/补充：携带影响评估引用产生序号加一的新版本（回到 DRAFT 语义，需重新门禁与重新签发）
- POST 撤回：标记指定版本停止被依赖，记录原因与执行人，不产生新版本
- POST 替代：以新报告号记录取代关系
- POST 作废：终止整条报告链，此后不再接受任何动作
- GET 验证页：返回当前有效版本、全部历史版本状态与取代关系
- GET 指定版本：始终返回该版本自身的快照与哈希

## 失败路径

- 无 ALLOWED 门禁评估、或评估未覆盖当前全部行 → RPT.ISSUANCE_GATE_NOT_SATISFIED
- 缺重新认证证据、签署意图为空或缺期望哈希 → RPT.SIGNATURE_REQUIREMENTS_UNMET
- 期望内容哈希与服务端重算不一致 → RPT.CONTENT_HASH_MISMATCH（被签内容已变，原签名失效）
- 重复签发同一版本 → RPT.VERSION_ALREADY_ISSUED
- 更正或补充缺影响评估引用 → RPT.IMPACT_ASSESSMENT_REQUIRED
- 对未签发版本执行受控动作 → RPT.VERSION_NOT_ISSUED
- 对已作废链执行任何动作、或重复撤回同一版本 → RPT.VERSION_CHAIN_CLOSED / RPT.VALIDATION_FAILED
- UPDATE/DELETE 任何版本快照、签名或受控动作 → 数据库 55000（RPT.REPORT_APPEND_ONLY）
- 行为人缺失/组织不匹配/能力拒绝 → RPT.NOT_AUTHORIZED，仅 audit_attempt 留痕
- 平台审计或发件箱写入失败 → 整体回滚，业务事实不产生

## 领域不变量

- 已签发版本的快照、哈希与签名不可删除或覆盖（RPT-VERS-002，DB 触发器强制）
- 内容哈希对规范化快照计算并绑定签名（RPT-SIGN-001）；内容变化即哈希变化即签名失效（SEC-SIGN-002）
- 签发三要素齐备才允许签发（SEC-SIGN-001）
- 更正/补充只能以新版本表达，不得就地修改（RPT-VERS-001）
- 按版本号取回始终返回该版本自身内容，旧引用永不静默返回不同内容（RPT-VERS-004）
- 撤回与作废不删除已发生事实（RULE-011）
- 全部事实追加式；乐观并发 expectedCurrentVersion + advisory lock；事实、平台审计与发件箱同事务
- 本卡不接外部签章系统（OD-011 明确 Release 1 不接入）、不渲染报告文件、不实现交付渠道

## 数据契约

```json
{
  "controlledAction": [
    "actionId",
    "reportId",
    "versionNumber",
    "kind(CORRECTION/SUPPLEMENT/WITHDRAWAL/VOID/SUPERSESSION)",
    "impactAssessmentRef?",
    "supersedingReportNumber?",
    "reason",
    "performedBy",
    "performedAt"
  ],
  "signature": [
    "signatureId",
    "reportId",
    "versionNumber",
    "contentHash",
    "reauthenticationRef{id, version}",
    "signingIntent",
    "signatoryId",
    "signedAt"
  ],
  "verificationPage": [
    "reportId",
    "reportNumber",
    "currentVersion?",
    "chainState(ACTIVE/VOIDED)",
    "versions[{versionNumber, state, contentHash, signedAt, supersededBy?}]",
    "supersedingReportNumber?",
    "ruleSetVersion"
  ],
  "versionSnapshot": [
    "snapshotId",
    "reportId",
    "versionNumber",
    "contentHash(SHA-256)",
    "canonicalContent",
    "lineCount",
    "createdBy",
    "createdAt"
  ],
  "versionState": [
    "ISSUED",
    "SUPERSEDED",
    "WITHDRAWN",
    "VOIDED"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "RPT.VALIDATION_FAILED",
    "RPT.ISSUANCE_GATE_NOT_SATISFIED",
    "RPT.SIGNATURE_REQUIREMENTS_UNMET",
    "RPT.CONTENT_HASH_MISMATCH",
    "RPT.VERSION_ALREADY_ISSUED",
    "RPT.VERSION_NOT_ISSUED",
    "RPT.IMPACT_ASSESSMENT_REQUIRED",
    "RPT.VERSION_CHAIN_CLOSED",
    "RPT.EXPECTED_VERSION_CONFLICT",
    "RPT.NOT_AUTHORIZED",
    "RPT.OBJECT_NOT_ACCESSIBLE",
    "RPT.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "GET /api/v1/reports/{id}/pending-content-hash → 200 待签内容哈希预览",
    "POST /api/v1/reports/{id}/issuance → 201 受控签发（三要素 + 哈希绑定）",
    "POST /api/v1/reports/{id}/controlled-actions → 201 执行一种受控动作",
    "GET /api/v1/reports/{id}/verification → 200 验证页读模型",
    "GET /api/v1/reports/{id}/versions/{versionNumber} → 200 该版本自身的快照与签名"
  ],
  "publicPort": "IReportVersionChainPort.EvaluateAsync(ReportVersionChainRequest) → 当前有效版本、链状态与指定版本状态，版本+规则集固定，供交付与计费链消费"
}
```

## 状态转换

- 版本：（门禁 ALLOWED + 三要素）→ ISSUED；ISSUED →（更正/补充产生下一版本）SUPERSEDED；ISSUED →（撤回）WITHDRAWN；任一版本 →（作废整链）VOIDED
- 报告链：ACTIVE →（作废）VOIDED，不可逆且此后拒绝一切动作

## 权限与职责分离

- 沿用 report.manage 能力，不新增能力；签发额外要求重新认证证据引用（SEC-SIGN-001），其权威校验属外部身份体系
- 受控动作与签发均经对象级授权端口

## 审计要求

- 每个命令写平台 audit_intent（同事务）+ outbox 事件（Report.Issued/Corrected/Supplemented/Withdrawn/Voided/Superseded）
- 失败尝试写 report.audit_attempt
- 读取验证页与指定版本写 READ_REPORT_VERIFICATION 审计

## UX 状态

- 本卡不新增前端页面
- 验证页为读模型 API——面向公众的页面渲染属后续卡

## 可观测性

- 计数器：签发数、哈希不匹配拒绝数、各受控动作数、验证页查询数
- 结构化日志固定 correlationId 与错误码

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-RPT-002-01 | positive | 门禁 ALLOWED 且覆盖全部行 | 携带重认证证据、签署意图与期望哈希签发 | 版本 ISSUED；快照+哈希+签名落为不可变事实；审计+发件箱同事务 |
| TC-RPT-002-02 | negative | 已取得内容哈希预览 | 报告行发生变化后仍以旧哈希签发 | RPT.CONTENT_HASH_MISMATCH；不产生签名；audit_attempt 留痕 |
| TC-RPT-002-03 | negative | 门禁 ALLOWED | 分别缺重认证证据、缺签署意图、缺期望哈希 | 逐项 RPT.SIGNATURE_REQUIREMENTS_UNMET |
| TC-RPT-002-04 | positive | V1 已签发 | 带影响评估引用更正并重新签发得 V2；分别按 V1/V2 版本号取回 | V2 序号加一且有自己的哈希与签名；V1 快照哈希签名原样保留；按 V1 取回仍返回 V1 内容与历史状态；验证页显示当前版本与取代关系 |
| TC-RPT-002-05 | negative | V1 已签发 | 无影响评估引用地更正 | RPT.IMPACT_ASSESSMENT_REQUIRED；不产生新版本 |
| TC-RPT-002-06 | boundary | V1 已签发 | 撤回 V1 并再次按版本号取回；重复撤回 | V1 状态 WITHDRAWN 但快照与签名保留；取回返回 V1 自身与已撤回状态；重复撤回被拒绝 |
| TC-RPT-002-07 | negative | 已签发版本 | 作废后再尝试任何受控动作或签发 | 链状态 VOIDED；后续动作一律 RPT.VERSION_CHAIN_CLOSED |
| TC-RPT-002-08 | negative | 已有签名与快照 | UPDATE/DELETE 及并发同版本签发 | 55000 拒绝；恰一个成功，另一方冲突 |
| TC-RPT-002-09 | negative | 审计或发件箱注入失败 | 签发 | 签名与快照回滚为零；audit_attempt 恰一次 |
| TC-RPT-002-10 | boundary | 含历史版本的报告 | 正确/过期版本与未知规则集查询 | 返回当前有效版本与链状态 / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN] |

## 明确非目标

- 不接入外部签章系统或 CA 证书（OD-011 明确 Release 1 不接入）
- 不渲染报告 PDF 或任何文件产物
- 不实现客户交付渠道、通知或下载链接托管
- 不实现面向公众的验证页前端
- 不实现 ConformityDecision（OD-034 未决）
- 不实现分包方回传（OD-013 未决）
- 不修改 DEV-022 已交付的装配与门禁语义
- 不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-RPT-004__v1.0.0.json`
- `spec/requirements/BUS-RPT-005__v1.0.0.json`
- `spec/acceptance/AC-RPT-002__v1.0.0.json`
- `spec/stories/ATC-RPT-002__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-27-dev-023-report-signature-version-chain/**`
- `contracts/report/**`
- `src/modules/report/**`
- `src/host/api/**`
- `tests/unit/report/**`
- `tests/contract/report/**`
- `tests/integration/report/**`
- `tests/architecture/**`
- `tests/test_repository_contract.py`
- `docs/domain/report/**`

## 验证命令

- `python -m tools.specgen ready --story ATC-RPT-002@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module report`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `python -m tools.specgen check`

## 完成定义

- 内容哈希、三要素签发、五种受控动作、验证页读模型与版本链端口全部落地且追加式 DB 强制
- AC-RPT-002 的 V1→V2 全程与旧版本号取回各有回归测试
- 全部既有测试项目保持绿色
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
