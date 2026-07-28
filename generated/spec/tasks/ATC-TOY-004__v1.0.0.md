<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-TOY-004@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-TOY-004：DEV-027 多 TestUnit 危险域覆盖结论

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-QUALITY` |
| Feature | `FEAT-TOY-CONCLUSION-COVERAGE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 法规负责人, 实验室技术负责人, 质量负责人, 授权签字人, QA负责人 |
| 影响模块 | toy, test-unit, hazard-coverage, result-provenance, conformity, reporting, authorization, disclosure, automated-test |
| 来源 | PRD-MAIN#OPS-TOY-005, PRD-MAIN#AC-TOY-002, PRD-MAIN#OD-034 |
| 固定依赖 | ED-001@2.0.0, OD-001@1.0.0, OD-002@1.0.0, OD-034@1.0.0, BUS-TOY-002@1.0.0, BUS-TOY-003@1.0.0, BUS-TOY-006@1.0.0, AC-TOY-002@1.0.0, ATC-TOY-002@1.0.0, ATC-RESULT-001@1.0.0, ATC-RPT-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `f30a41219cb744920466ed9d3b638ec00ffb5a7fa6973f4e1c3a3686bcccbd85` |

## 业务结果

多 TestUnit 结果可汇总为已测范围符合结论，逐一展示危险域覆盖依据并强制披露未覆盖项；ITEM_CONFORMITY 由技术负责人批准，TESTED_SCOPE_CONFORMITY 由授权签字人重认证签署；永久拒绝整件产品全面合规表述；外部认证证书仅作不参与判定的信息性旁注。

## 主要参与者

实验室技术负责人、授权签字人、质量管理人员、法规人员

## 触发条件

技术负责人批准单检测项目符合，或授权签字人批准产品版本已测范围符合结论并完成重认证签署

## 前置条件

- OD-034@1.0.0 已批准两级结论层级、措辞模板、批准权限与外部引用边界
- BUS-TOY-006@1.0.0 与 AC-TOY-002@1.0.0 已批准多 TestUnit 证据输入边界与未覆盖项披露不变式
- ATC-TOY-002@1.0.0 的 TestUnit/危险域证据边界已交付
- SEC-AUTH-001 已提供 TOY_CONCLUSION_APPROVE_ITEM 与 TOY_CONCLUSION_APPROVE_SCOPE 能力
- OD-011 与 SEC-SIGN-001 已提供重认证签署与签署意图绑定

## 正常路径

- ITEM_CONFORMITY：技术负责人对单检测项目请求结论，系统固定 adoptedResultRef@version、requirementRef@version 与 ruleSetVersion，校验 SoD（批准人不得是录入人），生成固定措辞结论，无需重认证签署
- TESTED_SCOPE_CONFORMITY：授权签字人对产品版本请求已测范围符合结论，系统固定 productRef@version、testUnitPlanRef@version、逐 TestUnit 引用 physicalObjectRef@version、hazardDomainRef@version、adoptedResultRef@version、coverageDecisionRef@version、resultProvenanceGraphRef@version 与 ruleSetVersion
- 系统逐 TestUnit 展示实际危险域与覆盖依据，生成 coveredHazardDomains[] 与 uncoveredScopes[]（每项标注 NOT_TESTED、UNKNOWN 或 NOT_APPLICABLE 及理由）
- 系统按固定模板渲染结论正文：『所检 N 个 TestUnit 就下列已测危险域符合 <requirementRef 清单>；未覆盖项：<uncoveredScopes 清单>』，未覆盖项段落不可省略
- 授权签字人完成 SEC-SIGN-001 重认证与签署意图绑定内容哈希，系统生成不可变结论记录
- 外部认证引用以 externalReferences[{issuer, reference, statedScope, notPartOfThisConclusion:true}] 记录为信息性旁注，不参与判定
- 审计与发件箱在同一事务写入，事务失败则回滚全部副作用

## 失败路径

- uncoveredScopes 缺失、任一版本引用缺失、覆盖决定未批准、来源图不可重建或危险域适用性为 UNKNOWN → TOY.CONCLUSION_EVIDENCE_INCOMPLETE，失败关闭
- 调用方传入自选措辞、批准角色或结论层级默认值 → TOY.CONCLUSION_POLICY_UNKNOWN
- 试图把多个 TestUnit 的局部结果表述为同一整件全部符合 → TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION，不产生业务事实
- 结论批准人同时是所引用任一 adoptedResult 的录入人 → TOY.CONCLUSION_SOD_VIOLATION
- 尝试就地修改或删除已批准结论 → 数据库层拒绝，审计与发件箱回滚
- 外部证书试图填补未覆盖项或参与判定 → 按 externalReferences 边界拒绝

## 领域不变量

- 结论层级恰好 ITEM_CONFORMITY 与 TESTED_SCOPE_CONFORMITY 两级；不存在整件产品全面合规层级的枚举、接口、措辞模板或批准角色
- 任何汇总输入都必须固定全部版本引用；缺任一版本引用即失败关闭
- 输出必须逐 TestUnit 展示实际执行的危险域与覆盖依据，并以 coveredHazardDomains[] 与 uncoveredScopes[] 双清单形式呈现
- 未覆盖项披露段落不可省略且不可隐藏；uncoveredScopes 未提供时拒绝，不得以空数组默认视为全覆盖
- 结论正文措辞由系统按 OD-034 固定模板确定性渲染；调用方不得传入自选措辞
- 危险域适用性为 UNKNOWN、覆盖决定未批准或来源图不可重建时一律失败关闭，UNKNOWN 等同阻断
- ITEM_CONFORMITY 由技术负责人能力批准；TESTED_SCOPE_CONFORMITY 由授权签字人能力批准并必须完成重认证与签署意图绑定内容哈希
- 结论批准人不得同时是所引用任一 adoptedResult 的录入人，违反以 TOY.CONCLUSION_SOD_VIOLATION 拒绝
- 已批准结论为追加式不可变事实，不得就地修改或删除；变更通过新结论版本表达并保留完整历史
- 外部全面法规评估、认证状态或证书仅以 externalReferences[] 记录且必须携带 notPartOfThisConclusion=true，不参与判定、不减少未覆盖项披露、不得渲染为本实验室符合性意见
- 本要求不放宽报告模块既有阻断；报告行级签发门禁语义继续归 OD-029

## 数据契约

```json
{
  "itemConformity": {
    "immutable": true,
    "input": [
      "adoptedResultRef@version",
      "requirementRef@version",
      "ruleSetVersion"
    ],
    "output": [
      "conclusionId",
      "conclusionLevel:ITEM_CONFORMITY",
      "conclusionStatement (fixed template)",
      "approvedBy (technical director)",
      "approvedAt",
      "version"
    ]
  },
  "prohibitedLevel": "WHOLE_PRODUCT_COMPLIANCE: 永久禁用；不提供枚举、接口、措辞或批准角色",
  "testedScopeConformity": {
    "immutable": true,
    "input": [
      "productRef@version",
      "testUnitPlanRef@version",
      "testUnits[{testUnitId, physicalObjectRef@version, hazardDomainRef@version, adoptedResultRef@version, coverageDecisionRef@version, resultProvenanceGraphRef@version}]",
      "ruleSetVersion"
    ],
    "output": [
      "conclusionId",
      "conclusionLevel:TESTED_SCOPE_CONFORMITY",
      "coveredHazardDomains[]",
      "uncoveredScopes[{scope, reason:NOT_TESTED|UNKNOWN|NOT_APPLICABLE, detail}]",
      "conclusionStatement (fixed template)",
      "approvedBy (authorized signatory)",
      "signatureRef",
      "approvedAt",
      "version",
      "externalReferences[{issuer, reference, statedScope, notPartOfThisConclusion:true}]?"
    ]
  }
}
```

## API / 命令契约

```json
{
  "errors": [
    "TOY.CONCLUSION_EVIDENCE_INCOMPLETE",
    "TOY.CONCLUSION_POLICY_UNKNOWN",
    "TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION",
    "TOY.CONCLUSION_SOD_VIOLATION"
  ],
  "operations": [
    "POST /api/toy/conclusions/item-conformity",
    "POST /api/toy/conclusions/tested-scope-conformity",
    "GET /api/toy/conclusions/{conclusionId}",
    "GET /api/toy/conclusions?productRef=...&version=..."
  ],
  "publicPort": "ToyConformityConclusionPort"
}
```

## 状态转换

- DRAFT → (技术负责人批准) → ITEM_CONFORMITY_APPROVED
- DRAFT → (授权签字人重认证签署) → TESTED_SCOPE_CONFORMITY_APPROVED
- 已批准结论不可变，不得转为其他状态

## 权限与职责分离

- TOY_CONCLUSION_APPROVE_ITEM: 技术负责人；批准 ITEM_CONFORMITY
- TOY_CONCLUSION_APPROVE_SCOPE: 授权签字人；批准 TESTED_SCOPE_CONFORMITY，必须叠加 SEC-SIGN-001 重认证与签署意图
- separation_of_duty: 结论批准人不得同时是该结论所引用的任一 adoptedResult 的录入人

## 审计要求

- 结论起草、批准、重认证签署、版本引用、覆盖依据、未覆盖项披露全部审计
- 审计与发件箱在同一事务写入
- SoD 校验失败不产生业务事实且审计拒绝原因

## UX 状态

- 技术负责人界面：ITEM_CONFORMITY 起草与批准
- 授权签字人界面：TESTED_SCOPE_CONFORMITY 起草、审查、重认证签署
- 结论查看界面：显示固定模板措辞、coveredHazardDomains、uncoveredScopes、外部引用（标注不参与判定）
- 整件全面合规请求：明确拒绝并展示 TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION 错误

## 可观测性

- 结论层级、批准耗时、SoD 拒绝、证据缺失、UNKNOWN 阻断按原因区分计数
- 未覆盖项数量、外部引用数量按结论级别统计
- 重认证签署成功率与失败原因分布

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-TOY-004-01 | happy-path | 单检测项目 adoptedResult@version、requirement@version | 技术负责人请求 ITEM 结论且 SoD 通过 | 生成固定措辞结论；无需重认证签署；不可变保存 |
| TC-TOY-004-02 | happy-path | 产品版本、3 个 TestUnit 各覆盖不同危险域、化学迁移未测 | 授权签字人请求 SCOPE 结论并完成重认证签署 | 逐 TestUnit 显示危险域与覆盖依据；coveredHazardDomains 列 3 个；uncoveredScopes 显式披露化学迁移为 NOT_TESTED；固定模板措辞含未覆盖项段落 |
| TC-TOY-004-03 | negative | 多个 TestUnit 分别覆盖不同危险域 | 请求整件产品全面合规结论 | TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION；不产生业务事实 |
| TC-TOY-004-04 | negative | 调用方提供自选结论措辞或批准角色 | 请求结论 | TOY.CONCLUSION_POLICY_UNKNOWN；不得采用调用方默认值 |
| TC-TOY-004-05 | negative | SCOPE 请求不提供 uncoveredScopes | 请求结论 | TOY.CONCLUSION_EVIDENCE_INCOMPLETE；不得以空数组默认视为全覆盖 |
| TC-TOY-004-06 | boundary | 外部认证引用 | 记录为 informational 旁注 | notPartOfThisConclusion=true；不减少未覆盖项；不渲染为本实验室符合性意见 |
| TC-TOY-004-07 | negative | 结论批准人同时是 adoptedResult 录入人 | 请求批准 | TOY.CONCLUSION_SOD_VIOLATION；不产生业务事实 |
| TC-TOY-004-08 | invariant | 已批准 SCOPE 结论 | 尝试 UPDATE 或 DELETE | 数据库层拒绝；审计与发件箱回滚 |

## 明确非目标

- 不核验外部认证证书真实性与有效期
- 不实现客户交付渠道、通知策略或证书管理
- 不放宽报告模块既有 EVALUATED 分区阻断
- 不创建发布、Seal、tag 或部署

## 允许修改路径

- `src/OpenLIMS.Toy/Domain/Conclusion/**`
- `src/OpenLIMS.Toy/Application/Commands/CreateItemConformityConclusion*`
- `src/OpenLIMS.Toy/Application/Commands/CreateTestedScopeConformityConclusion*`
- `src/OpenLIMS.Toy/Application/Queries/Get*Conclusion*`
- `src/OpenLIMS.Toy/Infrastructure/Persistence/ConclusionRepository*`
- `src/OpenLIMS.Toy/Infrastructure/Persistence/Migrations/*_AddToyConclusion*`
- `src/OpenLIMS.Toy/Ports/ToyConformityConclusionPort*`
- `tests/OpenLIMS.Toy.Tests/Domain/Conclusion/**`
- `tests/OpenLIMS.Toy.Tests/Application/Commands/*Conclusion*`
- `tests/OpenLIMS.Toy.Tests/Integration/*Conclusion*`
- `spec/decisions/OD-034__v1.0.0.json`
- `spec/requirements/BUS-TOY-006__v1.0.0.json`
- `spec/acceptance/AC-TOY-002__v1.0.0.json`
- `spec/stories/ATC-TOY-004__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-28-dev-027-toy-conclusion-spec/**`
- `docs/domain/toy/**`

## 验证命令

- `python -m tools.specgen validate --strict-warnings`
- `python -m tools.specgen source-status`
- `python -m tools.specgen impact`
- `python -m tools.specgen ready --story ATC-TOY-004@1.0.0`
- `python -m tools.specgen check`
- `dotnet test tests/OpenLIMS.Toy.Tests --filter Category=ToyConclusion`
- `dotnet test tests/OpenLIMS.Toy.Tests --filter FullyQualifiedName~TC_TOY_004`

## 完成定义

- OD-034/BUS-TOY-006/AC-TOY-002/ATC-TOY-004 全部 1.0.0 approved 并通过规格验证
- ready 稳定返回 READY 且依赖全部解析到 approved 版本
- 两级结论层级实现且通过 TC-TOY-004-01/02 正向测试
- 整件全面合规拒绝通过 TC-TOY-004-03 反向测试
- 自选措辞拒绝通过 TC-TOY-004-04 反向测试
- 未覆盖项强制披露通过 TC-TOY-004-05 反向测试
- 外部证书 informational 旁注通过 TC-TOY-004-06 边界测试
- SoD 拒绝通过 TC-TOY-004-07 反向测试
- 结论不可变性通过 TC-TOY-004-08 不变式测试
- 审计与发件箱同事务回滚测试通过
- 完整 AC-TOY-002@1.0.0 验收场景全部通过

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
