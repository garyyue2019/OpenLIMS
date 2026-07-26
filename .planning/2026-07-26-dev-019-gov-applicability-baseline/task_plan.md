# DEV-019 冻结 R1 适用性基线（ATC-GOV-001）

## 目标

依据用户 2026-07-26 对 OD-001 的正式决定（"定 OD-001：玩具 + 机械物理方法族"）：把 OD-001 落为 decided v1.0.0（技术包主选从 7-23 未批准的分析化学意向翻转回物理机械原推荐），新增 BUS-GOV-001 适用性基线不变量与 ATC-GOV-001 任务卡，并用 specgen snapshot 生成不可覆盖的 spec/baselines/r1-applicability-baseline.lock.json 完成"冻结"；仓库契约测试新增基线断言（textile=enabled_pack/DISABLED、ai=conditional/DISABLED、GOV=core/ENABLED、快照存在且含 OD-001@1.0.0）。

## 阶段

1. [completed] 侦察（工作流 5 路并行）：OD-001 v0.1.0 结构与 7-23 意向背景；decided 模式（新版本文件、status=approved、decision_state=decided、approval_evidence 含"用户"）；GOV 无既有规格、PRD 锚点为 MoSCoW 适用性制度 L669-676/RULE-026/L1482/OD-001 表行 L1447；specgen activation 语义与 snapshot 冻结机制；OD-025（平台/包边界）可避开保持 open。
2. [completed] OD-001__v1.0.0.json + BUS-GOV-001@1.0.0 + ATC-GOV-001@1.0.0，READY；契约测试 138→141、特性 48→49。
3. [completed] snapshot --name r1-applicability-baseline 生成冻结工件；契约测试新增适用性基线断言。
4. [in_progress] 完整门禁，CI 全绿后按授权提交/PR/合并。

## 约束

- 不触碰 OD-025/OD-012/OD-031 等其余未决 OD；不修改 REL 发布基线文件；PRD 只读。
- 决策文本必须忠实记录：市场=中国内销首发、产品资格=3 岁+硬质塑胶非电动玩具（沿用已确认方向）、技术包=物理机械（用户 7-26 决定，取代 7-23 分析化学意向）。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
