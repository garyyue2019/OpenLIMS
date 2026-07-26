# DEV-011 纺织样品需求未来适配契约切片

## 交付范围与定位

DEV-011 按 PRD 对非试点行业包的未来适配要求（L1291/L1327），仅交付纺织行业包的**契约层**：

- `OpenLIMS.Contracts.Textile`：样品需求计算模型、互斥裁样/样品不足纯规则（`TextileSampleRequirementRules`）、CuttingPlan 结构与校验；
- `OpenLIMS.Textile.ContractTests`：确定性规则测试与 JSON 序列化形状冻结（`Profile=textile`）。

**本卡不生产化**：不注册模块、schema、迁移、HTTP 端点、能力或宿主接线。`OD-001@0.1.0`（Release 1 试点行业）保持 open——其中记录用户拒绝纺织首发、方向为玩具婴童产品；本卡不改变该方向。BUS-TEX-* 需求以 `enabled_pack/DISABLED` 激活，仅在纺织行业包正式纳入发布并经 OD-001 后继决定批准后启用。

## 样品需求计算（OPS-TEXTILE-001/002）

需求行维度：款号 × 颜色 × 部件 × 材料 × 部位 × 方向（WARP/WEFT/LENGTHWISE/CROSSWISE）× 检测项目，携带平行数、复测预留、留样、预处理引用、破坏性与互斥破坏组。

- 每行所需试样数 = 平行数 + 复测预留 + 留样；需求面积 = 试样数 × 长 × 宽。
- 按（款号、颜色、部件、部位）聚合与可用面积比较；不足返回 `INSUFFICIENT` 及含方向与项目的缺口明细（AC-TEXTILE-001）。
- 规则集版本不匹配返回 `UNKNOWN`（等同阻断）；方向未知、引用缺失、计数非法一律失败关闭。

## 互斥裁样与共享（AC-TEXTILE-001 / RULE-007）

- 破坏性行**永不共享**裁片：共享组内出现任何破坏性行即 `TEX.EXCLUSIVE_SHARE_REJECTED`——不得以同一裁片满足互斥任务。
- 互斥破坏组标识只允许出现在破坏性行上。
- 非破坏性且规格一致（同款色部件材料部位方向尺寸预处理）的行允许共享，共享组按最大需求取试样数（一试样多次非破坏性使用）。

## CuttingPlan（OPS-TEXTILE-003）

结构必录：来源实物/布批引用、取样部位、方向、长宽尺寸、数量、距布边最小距离、模板版本、操作人、生成试样清单；生成试样数量必须与计划数量一致且 ID 唯一。校验为纯函数。

## 验证

```powershell
pwsh -File scripts/verify.ps1 -Profile task -Module textile
```

规则为纯函数（无 IO、无时钟、无随机），契约回归由序列化冻结测试在 CI 固定。后续 ATC-TEX-002（CuttingPlan 工作流）与 ATC-TEX-003（调湿/洗涤及超差）在纺织包获批进入发布前同样只应交付契约层。

## DEV-012 追加：调湿/洗涤及超差（OPS-TEXTILE-004 / AC-TEXTILE-003）

- `TextilePreconditioningRecord`：类型（CONDITIONING/WASHING）+ 计划/实际条件分离（温度、时长；调湿另需湿度，洗涤另需程序、洗涤剂、干燥方式）+ 显式公差 + 来源布批/CuttingPlan/生成试样关联链 + 可选超差批准引用。
- `TextilePreconditioningRules`：逐字段偏差与公差比较；任一超差即 `OUT_OF_TOLERANCE` 并阻断报告（`reportingAllowed=false`）；批准引用只解锁报告许可，超差事实保留；规则集版本未知返回 `UNKNOWN`。
- ATC-TEX-002（CuttingPlan 工作流）按用户决定跳过——CuttingPlan 契约与校验已在 DEV-011 冻结。
