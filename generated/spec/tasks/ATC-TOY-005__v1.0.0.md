<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-TOY-005@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-TOY-005：修复并验收 DEV-027 Toy 结论运行时

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-TOY-CONCLUSION-RUNTIME-REMEDIATION` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 玩具行业包负责人, 实验室技术负责人, 质量负责人, 授权签字人, 平台架构负责人, QA负责人 |
| 影响模块 | toy, result, conclusion, separation-of-duty, reauthentication, transaction, audit, outbox, worker-migration, automated-test |
| 来源 | PRD-MAIN#OPS-TOY-005, PRD-MAIN#AC-TOY-002, PRD-MAIN#SEC-SIGN-001 |
| 固定依赖 | OD-001@1.0.0, OD-002@1.0.0, OD-034@1.0.0, BUS-TOY-006@1.0.0, AC-TOY-002@1.0.0, ATC-TOY-004@1.0.0, ATC-RESULT-001@1.0.0, ATC-PLT-002@1.0.0, ATC-PLT-003@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `778312d5b7161d5fade4cf88c4dcc59da95bf67532bf7c137cf130337cde2556` |

## 业务结果

Toy ITEM_CONFORMITY 与 TESTED_SCOPE_CONFORMITY 结论能够在真实组织和对象范围内使用固定版本结果证据、真实录入人 SoD 与受控重认证引用安全创建；缺证据、UNKNOWN、无权限、签署绑定失败或平台证据写入失败不会产生半完成或越权结论，并恢复整个 OpenLIMS 解决方案的可构建状态。

## 主要参与者

请求单项符合结论的技术负责人、请求已测范围符合结论的授权签字人、查询历史结论的授权人员，以及提供结果证据的 Result 公共端口

## 触发条件

技术负责人或授权签字人创建 Toy 结论，或授权人员查询既有结论；Worker 对 Toy 模块执行追加迁移

## 前置条件

- ATC-TOY-004@1.0.0 已批准两级结论、固定措辞、未覆盖项和 SoD 语义，但其已合入实现尚未完成验收
- ATC-RESULT-001@1.0.0 已交付不可变结果、采用版本与 RecordedBy/AdoptedBy 事实
- 平台已交付可信请求上下文、对象级授权、ambient PostgreSQL 事务、audit_intent 与 outbox
- 所有结果、需求、计划、覆盖决定和重认证证据均使用稳定 ID 与精确正版本

## 正常路径

- Result-owned 公共端口按 organizationGroupId、resultGroupId、adoptionVersion 和规则集返回 ALLOWED、对象范围、采用 target 与其 RecordedBy
- Toy 服务确认所有采用结果证据完整且位于同一 legal entity/laboratory 对象范围，再校验对应结论 capability
- ITEM_CONFORMITY 校验批准人与采用 target 录入人不同，使用固定模板生成单项结论
- TESTED_SCOPE_CONFORMITY 还固定 reauthenticationRef@version、显式 signingIntent 与规范内容哈希，验证签署哈希与系统重算一致
- Toy 结论事实、结果证据引用、签署绑定、audit_intent 与 outbox 在同一平台事务追加提交
- Worker catalog 能发现 Toy 模块并按顺序执行只追加 remediation migration
- 查询按可信组织、对象范围和 capability 返回不可变历史结论

## 失败路径

- 结果组、采用版本、采用 target、RecordedBy 或对象范围缺失/UNKNOWN → TOY.CONCLUSION_EVIDENCE_UNKNOWN，且不保存结论
- Result 公共端口不可用或规则版本不匹配 → TOY.CONCLUSION_EVIDENCE_UNKNOWN，UNKNOWN 等同阻断
- coverageDecisionRef@version 缺失或版本无效 → TOY.CONCLUSION_EVIDENCE_INCOMPLETE
- 批准人等于任一采用 target 的 RecordedBy → TOY.CONCLUSION_SOD_VIOLATION
- TESTED_SCOPE 缺 reauthenticationRef、signingIntent、signedContentHash 或哈希不匹配 → TOY.CONCLUSION_SIGNATURE_INVALID
- 行为人缺 toy.conclusion.approve-item / toy.conclusion.approve-scope，或对象范围不匹配 → TOY.NOT_AUTHORIZED / TOY.OBJECT_NOT_ACCESSIBLE
- 整件全面合规、自选措辞、空未覆盖项或外部证书参与判定 → 保持 ATC-TOY-004 的稳定拒绝码
- 结论、审计或 Outbox 任一步写入失败 → 整体回滚并追加独立 audit_attempt；失败尝试不可写则 TOY.PERSISTENCE_UNAVAILABLE
- 直接 UPDATE/DELETE 既有或新结论事实 → PostgreSQL SQLSTATE 55000

## 领域不变量

- 不修改 ATC-TOY-004@1.0.0、既有已发布迁移或历史结论事实；修复使用新 Story、新契约增加和新追加迁移
- Toy 模块不得访问 result、report、scope 或其他模块私表；结果证据只能由 Result-owned 版本化公共端口返回
- Result 证据端口必须按可信组织与对象授权，返回 ALLOWED/BLOCKED/UNKNOWN；UNKNOWN、异常和缺失均不得降级为 ALLOWED
- adoptedResultRef 解释为 ResultGroup 稳定 ID，adoptedResultVersion 解释为不可变 adoptionVersion；端口必须解析其 target 并返回该 observation/derivation 的 RecordedBy，而不是 AdoptedBy
- ITEM 与 TESTED_SCOPE 均执行 approver != RecordedBy；空 recorder 清单不得代表安全通过
- TESTED_SCOPE 签署至少绑定精确 reauthenticationRef@version、非空 signingIntent 与系统规范内容哈希；本卡不声称验证外部身份提供商真实性
- 每个 TestUnit 都必须提供 coverageDecisionRef@version，不再把批准覆盖决定默认为可选
- 业务事实、audit_intent 与 outbox 同事务；失败尝试在主事务外追加且不得包含敏感正文
- 所有结论和签署绑定为追加式不可变，查询旧版本永远返回其原始证据与措辞
- 现有两级结论、固定措辞、强制未覆盖项和禁止整件全面合规语义不得放宽

## 数据契约

```json
{
  "itemConclusion": [
    "adoptedResultRef/adoptedResultVersion",
    "requirementRef/requirementVersion/ruleSetVersion",
    "resolved objectScope/recordedBy",
    "canonical contentHash",
    "approvedBy/approvedAt/correlationId"
  ],
  "resultConclusionEvidencePortV1": [
    "organizationGroupId from trusted context",
    "resultGroupId/adoptionVersion/ruleSetVersion",
    "decision(ALLOWED/BLOCKED/UNKNOWN) and reasonCodes",
    "targetId/targetKind/recordedBy/objectScope/currentGroupVersion"
  ],
  "testedScopeConclusion": [
    "productRef/productVersion/testUnitPlanRef/testUnitPlanVersion",
    "testUnits[] with physical object, hazard domain, adopted result, provenance graph, coverage decision and requirement versions",
    "coveredHazardDomains[]/uncoveredScopes[]/externalReferences[]",
    "reauthenticationRef/version/signingIntent/signedContentHash",
    "resolved objectScope/recordedBy set",
    "approvedBy/approvedAt/correlationId"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "TOY.CONCLUSION_EVIDENCE_INCOMPLETE",
    "TOY.CONCLUSION_EVIDENCE_UNKNOWN",
    "TOY.CONCLUSION_SIGNATURE_INVALID",
    "TOY.CONCLUSION_POLICY_UNKNOWN",
    "TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION",
    "TOY.CONCLUSION_SOD_VIOLATION",
    "TOY.NOT_AUTHORIZED",
    "TOY.OBJECT_NOT_ACCESSIBLE",
    "TOY.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/toy/conclusions/item-conformity → 201 immutable ITEM_CONFORMITY",
    "POST /api/v1/toy/conclusions/tested-scope-conformity → 201 immutable signed TESTED_SCOPE_CONFORMITY",
    "GET /api/v1/toy/conclusions/{id} → 200 authorized immutable conclusion",
    "GET /api/v1/toy/conclusions?productRef=...&version=... → 200 authorized conclusion history"
  ],
  "publicPort": "IResultConclusionEvidencePort@v1：按可信组织、ResultGroup ID、adoptionVersion 与规则集返回 ALLOWED/BLOCKED/UNKNOWN、对象范围、采用 target 与 RecordedBy；UNKNOWN 失败关闭"
}
```

## 状态转换

- Result conclusion evidence：请求 → ALLOWED/BLOCKED/UNKNOWN，不保存 Toy 状态
- Conclusion：不存在 → 追加 APPROVED immutable fact；失败不产生中间状态
- 既有结论无原地更新；语义变化创建新 conclusionId/version 并保留旧证据
- 同一 correlationId 安全重试至多产生一个业务事实和一条业务 Outbox

## 权限与职责分离

- ITEM 创建要求 toy.conclusion.approve-item、可信组织和 Result 对象范围可访问
- TESTED_SCOPE 创建要求 toy.conclusion.approve-scope、可信组织和全部 Result 对象范围一致且可访问
- Result evidence port 复用现有 result.record 对象授权，缺少该能力时更严格地失败关闭；本卡不新增只读权限默认值
- GET 要求相应 Toy 结论 capability 和结论固定的 legal entity/laboratory 范围
- 客户端不得提交 organizationGroupId、approvedBy、RecordedBy 或授权决定

## 审计要求

- 记录 CREATE_TOY_ITEM_CONCLUSION、CREATE_TOY_TESTED_SCOPE_CONCLUSION、READ_TOY_CONCLUSION 及对象 ID/版本、规则集、内容哈希和 correlationId
- 成功创建写 ToyConclusionCreated.v1 Outbox；业务事实、audit_intent 与 outbox 同事务
- 未授权、UNKNOWN、SoD、签署哈希、并发和持久化失败走独立追加 audit_attempt
- 日志和审计不得记录 signingIntent 正文、外部证书正文、Secret 或不必要个人信息

## UX 状态

- 本卡不新增 UI；API 必须返回固定措辞、未覆盖项、对象版本、内容哈希和签署证据引用，供后续 UI 明确展示

## 可观测性

- 结论按 ITEM_CONFORMITY/TESTED_SCOPE_CONFORMITY 成功计数
- Result 证据端口按 ALLOWED/BLOCKED/UNKNOWN 计数
- 拒绝按稳定错误码计数；结构化日志只含 correlationId、稳定对象 ID/版本和规则集

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-TOY-005-01 | positive | ALLOWED Result adoption evidence、不同 recorder 与批准人 | 创建 ITEM_CONFORMITY | 201；固定措辞；事务内事实/审计/Outbox |
| TC-TOY-005-02 | positive | 多个同范围 ALLOWED evidence、完整覆盖决定、未覆盖项、重认证引用与正确内容哈希 | 创建 TESTED_SCOPE_CONFORMITY | 201；签署绑定与结论不可变保存 |
| TC-TOY-005-03 | negative | 未知 result group/adoption/version 或端口异常 | 创建任一结论 | TOY.CONCLUSION_EVIDENCE_UNKNOWN；无业务事实 |
| TC-TOY-005-04 | permission | 批准人等于任一采用 target RecordedBy | 创建结论 | TOY.CONCLUSION_SOD_VIOLATION；失败尝试留痕 |
| TC-TOY-005-05 | permission | 跨组织/跨实验室或缺 capability | 创建或查询 | TOY.NOT_AUTHORIZED 或 TOY.OBJECT_NOT_ACCESSIBLE |
| TC-TOY-005-06 | negative | TESTED_SCOPE 缺精确重认证引用、intent 或正确哈希 | 创建结论 | TOY.CONCLUSION_SIGNATURE_INVALID；无事实 |
| TC-TOY-005-07 | boundary | 缺 coverageDecisionRef@version 或 uncoveredScopes | 创建 TESTED_SCOPE | TOY.CONCLUSION_EVIDENCE_INCOMPLETE |
| TC-TOY-005-08 | regression | 整件全面合规、自选措辞或参与判定的外部证书 | 创建结论 | 保持 ATC-TOY-004 稳定拒绝码 |
| TC-TOY-005-09 | audit | 注入 audit_intent/outbox 失败 | 创建结论 | 事实与同事务证据全部回滚；独立失败尝试一条 |
| TC-TOY-005-10 | recovery | 已创建结论或首次提交前失败 | UPDATE/DELETE 或以同 correlationId 重试 | SQLSTATE 55000；至多一个事实/Outbox；失败证据保留 |
| TC-TOY-005-11 | architecture | Worker 模块目录 | 应用 toy migration | 发现 ToyModule；旧迁移不改写；新迁移单调追加 |

## 明确非目标

- 不验证外部身份提供商或重认证系统的真实性；只绑定批准的版本化证据引用、意图与内容哈希
- 不新增 result 只读 capability 或放宽现有 result.record 授权
- 不实现客户交付渠道、通知、证书管理、前端页面或整件全面合规结论
- 不修改 ATC-TOY-004@1.0.0、既有迁移、快照、Seal 或历史结论
- 不创建 Release、tag、部署或执行生产迁移

## 允许修改路径

- `spec/stories/ATC-TOY-005__v1.0.0.json`
- `spec/acceptance/AC-TOY-002__v1.0.0.json`
- `spec/baselines/dev-031-toy-conclusion-remediation-final.lock.json`
- `spec/baselines/dev-031-toy-conclusion-remediation-governance-correction.lock.json`
- `spec/baselines/dev-031-toy-conclusion-remediation-approval-evidence-correction.lock.json`
- `generated/spec/**`
- `.planning/2026-07-28-dev-027-toy-conclusion-remediation/**`
- `.planning/.active_plan`
- `OpenLIMS.slnx`
- `contracts/toy/**`
- `contracts/result/**`
- `src/modules/toy/**`
- `src/modules/result/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/unit/toy/**`
- `tests/contract/toy/**`
- `tests/integration/toy/**`
- `tests/unit/result/**`
- `tests/contract/result/**`
- `tests/integration/result/**`
- `tests/architecture/**`
- `tests/test_repository_contract.py`
- `docs/domain/toy/**`
- `contracts/**/packages.lock.json`
- `src/modules/**/packages.lock.json`
- `src/host/**/packages.lock.json`
- `tests/**/packages.lock.json`

## 验证命令

- `python -m tools.specgen ready --story ATC-TOY-005@1.0.0`
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1 -Profile task -Module toy`
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1 -Profile architecture`
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- ATC-TOY-005@1.0.0 approved 且 ready 返回 READY 后才修改生产代码
- full solution 在锁定还原后以 Release/warnaserror 成功构建
- TC-TOY-005-01～11 与 ATC-TOY-004 的 TC-TOY-004-01～08 回归全部通过
- Result evidence port 真实返回 adopted target RecordedBy 与对象范围，UNKNOWN/异常失败关闭
- TESTED_SCOPE 固定重认证引用、signing intent 与规范内容哈希，缺失或不匹配不产生事实
- Toy 结论只使用平台 ambient transaction/audit/outbox 和 Result 公共端口，不访问其他私表
- Worker 可发现 Toy migration；既有迁移不修改，新迁移只追加
- 追加最终不可覆盖的 dev-031-toy-conclusion-remediation-final snapshot
- strict/source/impact/ready/history、双 generate written=0、check、Python、架构、契约和任务门禁全部通过
- 所有改动命中本 Story allowed_paths，DEV-028 分支随后完成 full-solution gate

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
