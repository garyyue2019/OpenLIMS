<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-TEX-003@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-TEX-003：实施 DEV-012 纺织调湿/洗涤及超差契约切片

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-TEXTILE-PRECONDITION` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室技术负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | textile, preconditioning, out-of-tolerance, contracts, serialization, automated-test |
| 来源 | PRD-MAIN#OPS-TEXTILE-004, PRD-MAIN#AC-TEXTILE-003 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, BUS-TEX-001@1.0.0, BUS-TEX-003@1.0.0, BUS-TEX-004@1.0.0, BUS-TEX-005@1.0.0, AC-TEXTILE-003@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `d39d389de0fb5854bbb44b6fae3156de00e5acd25949b63d67812c748ca1fa89` |

## 业务结果

纺织行业包的预处理（调湿/洗涤）与超差语义在纳入发布前即被契约冻结：计划/实际分离、逐字段公差评估、影响未批准前不得报告的阻断规则可被未来生产化直接复用，无破坏性契约变更。

## 主要参与者

平台与行业包契约维护者（无运行时操作者——本卡不交付任何运行时能力）

## 触发条件

契约维护者为纺织行业包定义预处理与超差契约

## 前置条件

- DEV-011 已交付 contracts/textile 与 CuttingPlan 契约
- OD-001 试点行业决定保持 open，本卡不改变其状态

## 正常路径

- 按类型（CONDITIONING/WASHING）定义计划与实际条件模型及显式公差
- 记录关联来源布批引用、可选 CuttingPlan 与生成试样清单
- 纯规则逐字段比较计划与实际并聚合判定
- 全部在公差内为 WITHIN_TOLERANCE 且 reportingAllowed=true
- 超差列明字段、计划值、实际值与偏差；补充批准引用后 reportingAllowed=true 且事实保留
- 契约测试固定 JSON 字段与形状

## 失败路径

- 规则集版本未知返回 UNKNOWN 且 reportingAllowed=false
- 类型未知、方向未知或必录字段缺失即校验失败
- 公差为负或非法即校验失败
- OUT_OF_TOLERANCE 且无批准引用时 reportingAllowed=false（未批准前不得报告）
- 本卡无运行时失败路径——无 HTTP、无持久化、无审计面

## 领域不变量

- 调湿必录温度、湿度、时长；洗涤必录温度、时长、程序、洗涤剂、干燥方式
- 计划与实际分离保存且互不覆盖
- 任一字段超差即整体 OUT_OF_TOLERANCE
- 批准引用只解锁报告许可，不修改或抹除超差事实
- UNKNOWN 等同阻断
- 规则为纯函数且确定性
- 本卡不注册模块、schema、HTTP 端点、运行时端口或宿主接线
- 不修改 OD-001/OD-025 及任何既有规格

## 数据契约

```json
{
  "assessment": [
    "decision(WITHIN_TOLERANCE/OUT_OF_TOLERANCE/UNKNOWN)",
    "reasonCodes",
    "deviations(field, plannedValue, actualValue, deviation, toleranceValue)",
    "reportingAllowed",
    "ruleSetVersion"
  ],
  "preconditioningRecord": [
    "recordId",
    "type(CONDITIONING/WASHING)",
    "sourceItemRef/version",
    "cuttingPlanId?",
    "specimenIds[]",
    "planned{temperatureC, humidityPercent?, durationMinutes, program?, detergent?, dryingMethod?}",
    "actual{同计划字段}",
    "tolerances{temperatureC, humidityPercent?, durationMinutes}",
    "outOfToleranceApproval?/version",
    "operatorId"
  ],
  "types": [
    "CONDITIONING",
    "WASHING"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "TEX.VALIDATION_FAILED",
    "TEX.PRECONDITIONING_TYPE_UNKNOWN",
    "TEX.APPLICABILITY_UNKNOWN"
  ],
  "operations": [],
  "publicPort": "无——本卡只扩展 OpenLIMS.Contracts.Textile 纯契约程序集与 TextilePreconditioningRules 纯规则，不注册任何 HTTP 端点或运行时端口"
}
```

## 状态转换

- 无运行时状态机——评估为纯函数，输入到结果单向且确定

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
| TC-TEX-003-01 | positive | 实际条件全部在显式公差内 | 纯规则评估 | WITHIN_TOLERANCE；reportingAllowed=true；无偏差项 |
| TC-TEX-003-02 | boundary | 温度超差而其余在公差内 | 评估 | OUT_OF_TOLERANCE；偏差项列明字段、计划值、实际值、偏差与公差；reportingAllowed=false |
| TC-TEX-003-03 | positive | 超差记录补充批准引用 | 重新评估 | 决定仍为 OUT_OF_TOLERANCE；reportingAllowed=true；偏差事实保留 |
| TC-TEX-003-04 | boundary | 调湿缺湿度或洗涤缺程序/洗涤剂/干燥方式 | 校验 | 校验失败；无部分结果 |
| TC-TEX-003-05 | negative | 未知规则集版本或未知类型 | 评估 | UNKNOWN 且 reportingAllowed=false 或校验失败 |
| TC-TEX-003-06 | regression | 记录关联来源布批、CuttingPlan 与生成试样 | 序列化并评估 | 关联链字段完整往返；超差评估携带全部关联 |
| TC-TEX-003-07 | regression | 记录与评估结果样例载荷 | JSON 往返并比对形状 | 字段名与结构与冻结样例一致 |
| TC-TEX-003-08 | regression | 同一输入重复评估 | 多次执行 | 结果逐字段一致；无时钟或随机依赖 |

## 明确非目标

- 不生产化纺织行业包（无模块、无 schema、无迁移、无 HTTP、无宿主接线）
- 不实现超差审批工作流（批准仅为版本固定引用）
- 不实现 CuttingPlan 执行工作流（ATC-TEX-002 已按用户决定跳过，契约已在 DEV-011 冻结）
- 不改变或决定 OD-001/OD-025 试点方向
- 不新增能力或权限语义
- 不修改 Release baseline，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-TEX-004__v1.0.0.json`
- `spec/requirements/BUS-TEX-005__v1.0.0.json`
- `spec/acceptance/AC-TEXTILE-003__v1.0.0.json`
- `spec/stories/ATC-TEX-003__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-012-textile-preconditioning/**`
- `contracts/textile/**`
- `tests/contract/textile/**`
- `tests/test_repository_contract.py`
- `docs/domain/textile/**`

## 验证命令

- `python -m tools.specgen ready --story ATC-TEX-003@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module textile`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 契约模型覆盖 OPS-TEXTILE-004 全部维度并表达 AC-TEXTILE-003 关联链
- 超差逐字段评估、报告阻断与批准解锁通过确定性测试
- 序列化字段与形状被契约测试冻结
- 无任何运行时注册（模块、schema、端点、能力）
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
