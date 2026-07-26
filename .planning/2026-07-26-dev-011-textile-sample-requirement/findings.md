# DEV-011 发现

## 前置门禁

- 当前基线为 `main@34bf411`，DEV-010（PR #10）已合并；validate 104 规格版本，SOURCE CURRENT。
- `ATC-TEX-001@1.0.0` 尚不存在；spec/ 中无任何 TEXTILE 结构化规格。

## 关键治理冲突（阻断性）

- **`OD-001@0.1.0`（Release 1 唯一试点行业）为 proposed/open，decision=null，但其 direction_basis 明确记录："用户拒绝纺织首发，并说明最近3至6个月玩具检测订单最多且方法最集中"**；release1_industry_direction = 玩具婴童产品；Release-1 玩具切片资格规则明确排除"毛绒或纺织主体"。
- `OD-025@0.1.0`（行业包边界）同样 open，方向为玩具行业包 + 分析化学技术包。
- PRD L672：OPS-TEXTILE-* 全部为"行业包 Must"——只有纺织包纳入发布才是上线必需。
- PRD L1291/L1327：非试点行业包首期"只需提供领域模型、序列化样例和契约测试，不要求生产 UI、工作流或仪器接口"，"不构成其他四行业的生产页面、模板、工作流或仪器集成交付"。
- PRD L1482：未决 OD 未批准前相应功能不得以默认假设进入生产配置。
- **结论：按 backlog 建议顺序直接生产化实现 ATC-TEX-001 与用户已记录的试点方向（玩具）和未决 OD-001/OD-025 冲突，属于授权例外中"需要实质业务决策"的情形，必须停下询问用户。**

## 来源语义（已收集备用）

- OPS-TEXTILE-001~005（L776-780，行业包 Must）：款/色/码/批次/部位覆盖与预处理记录；样品需求按款号×颜色×部件×材料×部位×方向×平行数×预处理×互斥破坏关系+复测预留+留样计算，不足则计划批准前阻断；CuttingPlan 必录字段；调湿/洗涤计划与实际及超差批准；CoverageDecision 维度与"最深色"禁令。
- AC-TEXTILE-001~004（L1163-1173）：样品不足与互斥裁样、代表色依据失效、裁样方向与预处理超差、实测与覆盖披露。基线中验收 ID 为 AC-TEXTILE-*（不是 AC-TEX-*）。
- SampleRequirement 定义（L236）从属于范围行；ScopeLine 已按 (Id, Version) 钉住 sampleRequirementRef，无任何模块拥有 SampleRequirement 实体本身。
- 激活模式：validator 允许 {core, enabled_pack, conditional, business_ops, release}；现有规格只用过 core/release。行业包需求应使用 `enabled_pack`。
- 相关未决 OD：OD-001（试点行业）、OD-010 已决但面积/尺寸维度含 LENGTH/AREA 可用、OD-027 已决、OD-028（代表性覆盖规则，open，对应 OPS-TEXTILE-005/AC-TEXTILE-002）、OD-030（批次边界，open）。

## 候选出路（待用户决策）

1. 跳过纺织三卡（TEX-001/002/003），DEV-011 改做建议清单中其他无未决 OD 阻断的卡。
2. 按 PRD L1291/L1327 把 ATC-TEX-001 收敛为"未来适配契约切片"：仅领域模型 + 序列化样例 + 契约测试（enabled_pack/DISABLED 激活），不生产化。
3. 先做 ATC-GOV-001 冻结 R1 适用性基线（把 OD-001/OD-025 的既有玩具方向正式版本化）——但这本身就是把未决 OD 转为已决，需要用户确认。
