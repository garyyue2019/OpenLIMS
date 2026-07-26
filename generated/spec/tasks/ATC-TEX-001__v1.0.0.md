<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-TEX-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-TEX-001：实施 DEV-011 纺织样品需求未来适配契约切片

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-TEXTILE-SAMPLE-REQUIREMENT` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室技术负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | textile, sample-requirement, cutting-plan, contracts, serialization, automated-test |
| 来源 | PRD-MAIN#OPS-TEXTILE-001, PRD-MAIN#OPS-TEXTILE-002, PRD-MAIN#OPS-TEXTILE-003, PRD-MAIN#AC-TEXTILE-001 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, OD-027@1.0.0, BUS-TEX-001@1.0.0, BUS-TEX-002@1.0.0, BUS-TEX-003@1.0.0, AC-TEXTILE-001@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `2174f31c3221485eb9866f1a3a268288193e8116e21ae591ba57e36d322f0750` |

## 业务结果

纺织行业包获得可版本化、可测试的契约基线：样品需求计算维度、互斥裁样与不足阻断规则、CuttingPlan 结构在纺织包正式纳入发布前即被冻结，未来生产化不需要破坏性契约变更。

## 主要参与者

平台与行业包契约维护者（无运行时操作者——本卡不交付任何运行时能力）

## 触发条件

契约维护者为纺织行业包定义或演进样品需求与 CuttingPlan 契约

## 前置条件

- OD-001 试点行业决定保持 open，本卡不改变其状态
- OD-027 已决定 ScopeLine 以 (Id, Version) 钉住 SampleRequirement
- 架构测试已允许 contracts/<module> 公共契约根

## 正常路径

- 定义版本固定的需求行维度模型（款号、颜色、部件、材料、部位、方向、项目、平行数、预处理、互斥破坏组、复测预留、留样）
- 以纯函数计算每行所需试样数并按款色部件部位聚合需求面积
- 校验共享声明：跨互斥组或含破坏性行的共享被拒绝
- 可用面积充足返回 SUFFICIENT 及试样计划，不足返回 INSUFFICIENT 及按维度聚合的缺口
- CuttingPlan 结构校验通过后可序列化冻结
- 契约测试固定 JSON 字段与形状

## 失败路径

- 规则集版本未知返回 UNKNOWN 决定
- 方向未知或引用缺失即校验失败
- 跨互斥破坏组的裁片共享声明被拒绝
- 尺寸或数量非正、生成试样数与计划不一致的 CuttingPlan 校验失败
- 本卡无运行时失败路径——无 HTTP、无持久化、无审计面

## 领域不变量

- 所需试样数恒等于平行数加复测预留加留样
- 不得以同一裁片满足互斥破坏任务
- 非破坏性同规格行允许共享（一试样多次非破坏性使用）
- 缺口必须按款号、颜色、部件、部位聚合并列明方向与项目
- UNKNOWN 等同阻断
- 规则为纯函数且确定性
- 本卡不注册模块、schema、HTTP 端点、运行时端口或宿主接线
- 不修改 OD-001/OD-025 及任何既有规格

## 数据契约

```json
{
  "availableFabric": [
    "style/version",
    "colorway/version",
    "component/version",
    "position",
    "availableAreaSquareMm"
  ],
  "calculationResult": [
    "decision(SUFFICIENT/INSUFFICIENT/UNKNOWN)",
    "reasonCodes",
    "specimenPlans(requiredSpecimenCount, areaSquareMm)",
    "gaps(style, colorway, component, position, requiredAreaSquareMm, availableAreaSquareMm, gapAreaSquareMm, contributingItems(direction, testItemRef))",
    "ruleSetVersion"
  ],
  "cuttingPlan": [
    "cuttingPlanId",
    "sourceItemRef/version",
    "samplingPosition",
    "direction",
    "lengthMm",
    "widthMm",
    "plannedCount",
    "minDistanceFromSelvedgeMm",
    "templateVersion",
    "operatorId",
    "generatedSpecimenIds"
  ],
  "demandLine": [
    "style/version",
    "colorway/version",
    "component/version",
    "material/version",
    "position",
    "direction",
    "testItemRef/version",
    "parallelCount",
    "retestReserveCount",
    "retentionReserveCount",
    "preconditioningRef/version?",
    "exclusiveDestructiveGroupId?",
    "destructive",
    "specimenLengthMm",
    "specimenWidthMm",
    "shareGroupId?"
  ],
  "directions": [
    "WARP",
    "WEFT",
    "LENGTHWISE",
    "CROSSWISE"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "TEX.VALIDATION_FAILED",
    "TEX.DIRECTION_UNKNOWN",
    "TEX.EXCLUSIVE_SHARE_REJECTED",
    "TEX.APPLICABILITY_UNKNOWN"
  ],
  "operations": [],
  "publicPort": "无——本卡只交付 OpenLIMS.Contracts.Textile 纯契约程序集与 ITextileSampleRequirementCalculator 纯规则接口，不注册任何 HTTP 端点或运行时端口"
}
```

## 状态转换

- 无运行时状态机——计算为纯函数，输入到结果单向且确定

## 权限与职责分离

- 本卡不新增能力、claim 或授权面
- 契约程序集无外部依赖且不接触部署配置

## 审计要求

- 本卡无运行时审计面；契约演进由规格版本与生成锁追溯

## UX 状态

- 本卡不新增前端页面
- 无任何客户端交互面——契约消费者为未来的纺织行业包模块

## 可观测性

- 本卡无运行时指标；契约回归由 Profile=textile 测试在 CI 固定

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-TEX-001-01 | positive | 三平行加复测预留加留样的需求行 | 纯规则计算 | 所需试样数恒等于三者之和；需求面积等于试样数乘长乘宽 |
| TC-TEX-001-02 | boundary | 同一面料两个互斥破坏项目；可用面积不足 | 计算充足性 | INSUFFICIENT；缺口按款色部件部位聚合并列明方向与项目 |
| TC-TEX-001-03 | negative | 跨互斥破坏组的共享声明 | 计算 | TEX.EXCLUSIVE_SHARE_REJECTED；不产生试样计划 |
| TC-TEX-001-04 | positive | 两条非破坏性同规格行声明共享 | 计算 | 共享组按最大需求取试样数；不重复累加面积 |
| TC-TEX-001-05 | negative | 未知规则集版本或未知方向 | 计算 | UNKNOWN 或校验失败；无部分结果 |
| TC-TEX-001-06 | boundary | 完整与缺失字段的 CuttingPlan | 结构校验 | 完整计划通过；尺寸非正、方向未知或试样数不一致失败 |
| TC-TEX-001-07 | regression | 全部契约记录的样例载荷 | JSON 往返并比对形状 | 字段名与结构与冻结样例一致；反序列化等值 |
| TC-TEX-001-08 | regression | 同一输入重复计算 | 多次执行 | 结果逐字段一致；无时钟或随机依赖 |

## 明确非目标

- 不生产化纺织行业包（无模块、无 schema、无迁移、无 HTTP、无宿主接线）
- 不实现调湿/洗涤执行（OPS-TEXTILE-004 属 ATC-TEX-003）
- 不实现 CoverageDecision（OPS-TEXTILE-005/OD-028）
- 不改变或决定 OD-001/OD-025 试点方向
- 不实现范围/补样变更工作流
- 不新增能力或权限语义
- 不修改 Release baseline，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-TEX-001__v1.0.0.json`
- `spec/requirements/BUS-TEX-002__v1.0.0.json`
- `spec/requirements/BUS-TEX-003__v1.0.0.json`
- `spec/acceptance/AC-TEXTILE-001__v1.0.0.json`
- `spec/stories/ATC-TEX-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-011-textile-sample-requirement/**`
- `OpenLIMS.slnx`
- `contracts/textile/**`
- `tests/contract/textile/**`
- `tests/architecture/**`
- `tests/test_repository_contract.py`
- `docs/domain/textile/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-TEX-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module textile`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 契约模型覆盖 OPS-TEXTILE-001/002/003 全部维度
- 互斥共享拒绝、不足缺口聚合和 UNKNOWN 失败关闭通过确定性测试
- 序列化字段与形状被契约测试冻结
- 无任何运行时注册（模块、schema、端点、能力）
- 架构边界测试通过且 contracts/textile 纳入公共契约扫描
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
