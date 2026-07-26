<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-AI-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-AI-001：实施 DEV-016 AI 资料抽取与缺口建议契约切片

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-AI-GOVERNANCE` |
| Feature | `FEAT-AI-DOC-EXTRACTION` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 技术负责人, AI负责人, 质量负责人, QA负责人 |
| 影响模块 | ai, run-control, fact-class, extraction, gap-suggestion, human-review, contracts, serialization, automated-test |
| 来源 | PRD-MAIN#AI-BOM-002, PRD-MAIN#AI-BOM-004, PRD-MAIN#AI-BOM-007, PRD-MAIN#AI-BOM-008, PRD-MAIN#AI-BOM-009, PRD-MAIN#AI-BOM-010, PRD-MAIN#AC-AI-003 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, BUS-AI-001@1.0.0, BUS-AI-002@1.0.0, BUS-AI-003@1.0.0, AC-AI-003@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `ba2fc8779dceef1736e9ab0f9e0921a9fb41427daf28e2adbef266e17787ce6a` |

## 业务结果

AI 旁路在获得法务/隐私批准前即拥有被冻结的治理契约：运行封套、事实类别税则、失败关闭校验和人工处置结构可被未来生产化直接复用，且任何消费方都无法绕过'近似、非约束、未验证'的治理边界。

## 主要参与者

平台与 AI 治理契约维护者（无运行时操作者——本卡不交付任何运行时能力）

## 触发条件

契约维护者为 AI 旁路定义或演进治理契约

## 前置条件

- OD-006/OD-007 保持 open，本卡不改变其状态
- 架构测试已允许 contracts/<module> 公共契约根

## 正常路径

- 定义运行控制封套（模型/路由/提示模板/输出模式/输入版本全固定）
- 以纯规则校验模型结构化输出：合法输出产出候选（含来源定位与事实类别）与缺口/澄清建议
- 未知字段、非法单位或缺必需来源→整体 QUARANTINED 并列明验证错误
- 校验 VERIFIED_FACT 必须同时携带权威来源与验证方法，AI_INFERENCE 提升被拒绝
- 人工处置（ACCEPT/MODIFY/SPLIT/MERGE/REJECT）保留 AI 原值、人工值、原因、责任人且类别不变
- 契约测试固定 JSON 字段与形状

## 失败路径

- 封套缺任一固定引用即校验失败
- 未知事实类别或处置类型即校验失败
- 无权威来源+验证方法的 VERIFIED_FACT 或类别提升返回 AIX.FACT_CLASS_PROMOTION_REJECTED
- 非法输出隔离并返回 AIX.OUTPUT_QUARANTINED 明细
- MODIFY 缺人工值/原因/责任人即校验失败
- 本卡无运行时失败路径——无 HTTP、无持久化、无模型调用

## 领域不变量

- 事实类别与处置类型为显式最小枚举
- AI_INFERENCE 永不自动成为 VERIFIED_FACT（AC-AI-002）
- 隔离输出不产生任何下游产物（AC-AI-003）
- 处置保留 AI 原值且不改类别
- 规则为纯函数且确定性
- 本卡不注册模块、schema、HTTP 端点、能力或模型调用
- 不修改 OD-006/OD-007 及任何既有规格

## 数据契约

```json
{
  "candidate": [
    "candidateId",
    "targetField",
    "value",
    "unit?",
    "factClass(OBSERVATION/ASSUMPTION/AI_INFERENCE/VERIFIED_FACT)",
    "confidence(0..1)",
    "sourceLocation{documentRef/version, page?, region?}",
    "authoritySourceRef?/version",
    "verificationMethodRef?/version"
  ],
  "disposition": [
    "dispositionId",
    "candidateId",
    "kind(ACCEPT/MODIFY/SPLIT/MERGE/REJECT)",
    "aiOriginalValue",
    "humanValue?",
    "reason",
    "responsibleActor"
  ],
  "gapSuggestion": [
    "gapId",
    "targetField",
    "kind(MISSING_INFORMATION/CLARIFICATION)",
    "question"
  ],
  "runEnvelope": [
    "modelRef/version",
    "gatewayRoute",
    "promptTemplateRef/version",
    "outputSchemaRef/version",
    "inputRefs[](ref/version)"
  ],
  "validationResult": [
    "decision(ACCEPTED/QUARANTINED)",
    "errors(field, code, detail)",
    "candidates[]",
    "gaps[]"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "AIX.VALIDATION_FAILED",
    "AIX.OUTPUT_QUARANTINED",
    "AIX.FACT_CLASS_PROMOTION_REJECTED"
  ],
  "operations": [],
  "publicPort": "无——本卡只交付 OpenLIMS.Contracts.Ai 纯契约程序集与 IAiOutputValidator 纯规则接口，不注册任何 HTTP 端点或运行时端口"
}
```

## 状态转换

- 无运行时状态机——校验为纯函数，输入到结果单向且确定

## 权限与职责分离

- 本卡不新增能力、claim 或授权面
- 契约程序集无外部依赖且不接触部署配置或模型凭据

## 审计要求

- 本卡无运行时审计面；契约演进由规格版本与生成锁追溯

## UX 状态

- 本卡不新增前端页面
- 无任何客户端交互面——契约消费者为未来的 AI 旁路模块

## 可观测性

- 本卡无运行时指标；契约回归由 Profile=ai 测试在 CI 固定

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-AI-001-01 | positive | 全固定引用的封套 | 校验 | 通过；缺任一引用即失败 |
| TC-AI-001-02 | negative | 含未知字段、非法单位或缺来源的输出 | 校验 | 整体 QUARANTINED；错误明细列出字段与代码；无下游产物 |
| TC-AI-001-03 | negative | AI_INFERENCE 候选无权威来源或验证方法 | 声明 VERIFIED_FACT 或提升 | AIX.FACT_CLASS_PROMOTION_REJECTED |
| TC-AI-001-04 | boundary | 同一字段多候选分支与弃权 | 校验 | 分支与弃权合法；伪装单一确定答案的重复字段拒绝 |
| TC-AI-001-05 | regression | MODIFY 处置 | 校验 | AI 原值、人工值、原因、责任人齐备；缺任一即失败；类别不变 |
| TC-AI-001-06 | positive | 缺失信息与澄清问题 | 校验 | 建议独立于候选表达；不写入受控对象 |
| TC-AI-001-07 | regression | 全部契约记录样例载荷 | JSON 往返并比对形状 | 字段与结构与冻结样例一致 |
| TC-AI-001-08 | regression | 同一输入重复校验 | 多次执行 | 结果逐字段一致；无时钟或随机依赖 |

## 明确非目标

- 不运行任何模型或处理客户数据（OD-006/007 未决，AI-BOM-014 前置不满足）
- 不实现图片近似 BOM（R1 排除产品化）
- 不实现注入防御运行时、降级开关或评估集执行（后续卡）
- 不新增能力或权限语义
- 不修改 Release baseline，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-AI-001__v1.0.0.json`
- `spec/requirements/BUS-AI-002__v1.0.0.json`
- `spec/requirements/BUS-AI-003__v1.0.0.json`
- `spec/acceptance/AC-AI-003__v1.0.0.json`
- `spec/stories/ATC-AI-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-016-ai-extraction/**`
- `OpenLIMS.slnx`
- `contracts/ai/**`
- `tests/contract/ai/**`
- `tests/architecture/**`
- `tests/test_repository_contract.py`
- `docs/domain/ai/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-AI-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module ai`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 契约覆盖运行封套、事实类别、候选/缺口/处置全部维度
- 失败关闭隔离、类别提升拒绝与原值保留通过确定性测试
- 序列化字段与形状被契约测试冻结
- 无任何运行时注册
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
