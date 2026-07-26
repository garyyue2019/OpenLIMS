# DEV-016 AI 资料抽取与缺口建议契约切片

## 目标

按用户批准的 AI 降级决定（纺织模式），交付 AI 治理契约层：运行控制封套、事实类别不得提升规则、抽取候选/缺口建议/人工处置契约与纯失败关闭校验、序列化冻结；不运行模型、不处理客户数据、不触碰 OD-006/007。

## 阶段

1. [completed] 侦察：AI-BOM-001~014 治理要求与 AC-AI-002/003 在基线；运行时被 OD-006（数据分类/处理区域/模型许可）与 OD-007（品类/复核阈值/停止条件）阻断（AI-BOM-014）。
2. [completed] 用户明确选择"AI 降级为契约切片"。
3. [completed] 创建后继规格（BUS-AI-001~003 + AC-AI-003@1.0.0 + ATC-AI-001@1.0.0，conditional/DISABLED 激活）并 READY。
4. [completed] 实现 contracts/ai 纯契约 + AiGovernanceRules 纯规则。
5. [completed] tests/contract/ai 契约测试（Profile=ai）：封套校验、类别提升拒绝、失败关闭隔离、处置原值保留、序列化冻结、确定性。
6. [completed] 完整门禁通过；已按授权自动提交、PR #16、CI 全绿并 squash 合并。

## 约束

- AC-AI-002：AI_INFERENCE 永不自动提升为 VERIFIED_FACT（无权威来源+验证方法即拒绝）。
- AC-AI-003/AI-BOM-008：未知字段、非法单位、缺必需来源→隔离（QUARANTINED），不产生下游产物。
- AI-BOM-010：人工修改后保留 AI 原值、人工值、原因、责任人。
- 零运行时：无模块、无 schema、无端点、无能力、无模型调用；OD-006/007 保持 open。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| `ATC-AI-001@1.0.0` 不存在 | 1 | 预期缺口；起草任务卡。 |
