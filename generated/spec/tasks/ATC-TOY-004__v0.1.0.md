<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-TOY-004@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-TOY-004：阻断 DEV-027 多 TestUnit 危险域覆盖结论

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-QUALITY` |
| Feature | `FEAT-TOY-CONCLUSION-COVERAGE` |
| 开发就绪度 | `blocked` |
| 变更级别 | `major` |
| 负责人角色 | 法规负责人, 实验室技术负责人, 质量负责人, 法务负责人, 授权签字人, QA负责人 |
| 影响模块 | toy, test-unit, hazard-coverage, result-provenance, conformity, reporting, authorization, disclosure, automated-test |
| 来源 | PRD-MAIN#OPS-TOY-005, PRD-MAIN#AC-TOY-002, PRD-MAIN#OD-034 |
| 固定依赖 | ED-001@2.0.0, OD-001@1.0.0, OD-002@1.0.0, OD-034@0.1.0, BUS-TOY-006@0.1.0, AC-TOY-002@0.1.0, ATC-TOY-002@0.1.0, ATC-RESULT-001@1.0.0, ATC-RPT-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `962b89343dbf84ffed312730c1b66e632966505b81c681927e8e64b77c670259` |

## 业务结果

在开放决策被正式解决前，仓库对多 TestUnit 产品结论保持可见且可验证的阻断，防止代理把多个实物的局部结果拼成一件并不存在的全面通过；决定后可以从已冻结证据边界创建新的 MAJOR 任务版本。

## 主要参与者

负责 OD-034 的法规/技术/质量/法务/授权签字人，以及检查任务 readiness 的工程代理

## 触发条件

当前仅由规格门禁触发阻断；OD-034 完成后才允许触发实现规划

## 前置条件

- OD-034 发布后继 approved/decided 版本并满足行业措辞、权限、报告样例和反向场景 exit criteria
- BUS-TOY-006 与 AC-TOY-002 依据该决定发布后继 approved MAJOR 版本
- ATC-TOY-002 的 TestUnit/危险域证据边界已批准并交付
- 本卡按决定结果创建后继 MAJOR 版本，重新指定精确依赖和 allowed_paths

## 正常路径

- 当前版本运行 ready 时明确列出 proposed/open 决策和未批准依赖并返回 BLOCKED
- 任何实现代理在 BLOCKED 状态停止编码，不创建结论接口、数据库迁移或报告放行
- 决策完成后由人工评审创建 BUS/AC/Story 的后继 MAJOR 版本，填入批准的语义和权限
- 后继版本才可定义逐 TestUnit 输入到产品/型号结论的状态机、接口、措辞和验收

## 失败路径

- 运行时尝试使用当前 proposed/open OD-034 → TOY.CONCLUSION_POLICY_UNKNOWN
- 缺 TestUnit、结果、危险域覆盖、来源图或未覆盖项披露 → TOY.CONCLUSION_EVIDENCE_INCOMPLETE
- 尝试把多个 TestUnit 描述为同一整件全部通过 → TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION
- 报告链尝试绕过既有 ConformityDecision 阻断 → 保持 RPT.CONFORMITY_DECISION_UNAVAILABLE
- 代理试图自行选择结论措辞、批准角色或外部证书语义 → 规格评审拒绝，不进入实现

## 领域不变量

- 当前版本不提供可执行结论语义、默认批准角色或全面合规措辞
- 逐 TestUnit 证据必须保留实物身份、危险域、结果版本、覆盖依据和未覆盖项，不能合成虚构实物
- OD-034 未决定时 UNKNOWN 等同阻断，报告模块现有阻断不得放宽
- 任何后继实现必须使用新 MAJOR 规格，不能原地修改本 0.1.0 历史

## 数据契约

```json
{
  "evidenceEnvelopeOnly": [
    "productRef/version",
    "testUnitPlanRef/version",
    "testUnits[{testUnitId, physicalObjectRef/version, hazardDomainRefs@version, adoptedResultRefs@version, coverageDecisionRef@version?}]",
    "untestedOrUnknownScopes[]",
    "resultProvenanceGraphRef/version",
    "ruleSetVersion"
  ],
  "runtimeDecision": "BLOCKED/UNKNOWN until an approved successor specification exists",
  "undecidedByOD034": [
    "conclusionLevel",
    "conclusionStatement",
    "approvalRole",
    "signatureRequirement",
    "externalComprehensiveAssessmentRef",
    "certificationStatusRef",
    "mandatoryDisclosureText"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "TOY.CONCLUSION_POLICY_UNKNOWN",
    "TOY.CONCLUSION_EVIDENCE_INCOMPLETE",
    "TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION",
    "RPT.CONFORMITY_DECISION_UNAVAILABLE"
  ],
  "operations": [],
  "publicPort": "NONE in this blocked version; no ConformityDecision endpoint or port may be registered"
}
```

## 状态转换

- 无运行时状态机；当前仅允许规格状态 BLOCKED，直到 approved successor 替代

## 权限与职责分离

- 不新增能力或批准角色；OD-034 必须决定谁可批准何种结论
- 现有管理员、技术人员或授权签字人身份均不得被代理推断为默认结论批准权

## 审计要求

- 当前无业务写入；任何阻断探测仅记录稳定错误码和 correlationId，不伪造 ConformityDecision 审计

## UX 状态

- 不新增 UI；现有 EVALUATED/ConformityDecision 入口保持不可用并展示明确阻断原因

## 可观测性

- ready/运行时阻断计数按 OD-034 与证据缺失原因区分
- 不得记录未批准的结论正文或把日志冒充审批证据

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-TOY-004-01 | governance | OD-034@0.1.0 proposed/open | 运行 ready | BLOCKED；列出 open decision 与 proposed dependencies |
| TC-TOY-004-02 | scope-boundary | 当前 blocked 规格 | 扫描路由、公共端口、迁移和报告门禁 | 不存在 toy ConformityDecision 写接口；RPT.CONFORMITY_DECISION_UNAVAILABLE 保持 |
| TC-TOY-004-03 | negative | 多个 TestUnit 分别覆盖不同危险域 | 请求描述为同一整件全部通过 | TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION；不产生业务事实 |
| TC-TOY-004-04 | negative | 调用方提供自选结论措辞或角色 | 请求结论 | TOY.CONCLUSION_POLICY_UNKNOWN；不得采用调用方默认值 |

## 明确非目标

- 不决定或批准 OD-034
- 不实现任何 ConformityDecision、结论措辞、批准流、报告放行、外部认证或证书引用
- 不放宽现有报告 EVALUATED 分区阻断
- 不创建代码、迁移、前端、Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/decisions/OD-034__v0.1.0.json`
- `spec/requirements/BUS-TOY-006__v0.1.0.json`
- `spec/acceptance/AC-TOY-002__v0.1.0.json`
- `spec/stories/ATC-TOY-004__v0.1.0.json`
- `generated/spec/**`
- `.planning/2026-07-27-dev-027-toy-conclusion-blocked/**`
- `tests/test_repository_contract.py`
- `docs/domain/toy/**`

## 验证命令

- `python -m tools.specgen validate --strict-warnings`
- `python -m tools.specgen source-status`
- `python -m tools.specgen impact`
- `python -m tools.specgen ready --story ATC-TOY-004@0.1.0`
- `python -m tools.specgen check`

## 完成定义

- ready 稳定返回 BLOCKED 并列出 OD-034 open/proposed 与未批准依赖
- 仓库不存在当前版本授权的结论实现、接口、迁移或报告放行
- 完整 AC-TOY-002 不因拆分可实施切片而丢失汇总结论阻断
- OD-034 决定后使用后继 MAJOR 规格，不修改本版本历史

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
