# DEV-012 纺织调湿/洗涤及超差契约切片

## 目标

按用户预先批准的纺织降级决定（DEV-011 同款），交付 OPS-TEXTILE-004 调湿/洗涤计划与实际记录、超差评估和报告阻断的纯契约层；不生产化、不触碰 OD-001。

## 阶段

1. [completed] 语义与来源核对（复用 DEV-011 侦察：OPS-TEXTILE-004、AC-TEXTILE-003 均在基线）。
2. [completed] 基线依授权收敛：TEX-002 跳过（CuttingPlan 契约已在 DEV-011 冻结），TEX-003 按契约切片交付。
3. [completed] 创建后继规格与任务卡，生成派生物并 READY。
4. [completed] 在 contracts/textile 内扩展预处理契约与纯规则。
5. [completed] 契约测试（计划/实际、超差评估、报告阻断、序列化冻结）。
6. [completed] 完整门禁通过；已按授权自动提交、PR #12、CI 全绿并 squash 合并为 `main@fc17aea`。

## 约束

- 与 DEV-011 相同：enabled_pack/DISABLED 激活，无模块/schema/端点/能力/宿主接线；OD-001/OD-025 保持 open。
- PRD 只读；generated/spec 只经生成器写入；未知语义失败关闭。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| `ATC-TEX-003@1.0.0` 不存在，ready 返回错误 | 1 | 预期缺口；起草任务卡。 |
