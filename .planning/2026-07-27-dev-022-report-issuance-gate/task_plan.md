# DEV-022 报告签发门禁（ATC-RPT-001）

## 目标

用户 2026-07-27 定下 OD-011（CNAS/CMA + 电子签名法）、OD-022（版本链取代）、OD-029（完整行级多维范围）与卡切分（门禁优先/签署其次），解锁报告链。本卡交付 report 模块的第一半：报告装配 + 全链追溯重建 + 签发门禁（含行级认可校验），状态止于「待批准」，公开 IReportIssuanceGatePort；电子签名、内容哈希、不可变版本链与验证页属 DEV-023。

## 阶段

1. [completed] 侦察（工作流 5 路）：OD-011/022 无规格对象需从零起草，OD-029 有 v0.1.0；三者语义重叠需去冲突；RPT-* 十条 Must 目前 traceability 零覆盖；10 个既有端口决策形状统一可纯组合。
2. [completed] 三个 OD 决策落盘（已完成）+ BUS-RPT-001/002/003 + AC-RPT-001/AC-TRACE-001/AC-ACC-001 + ATC-RPT-001 并 READY。
3. [completed] contracts/report + src/modules/report 八件套 + 宿主/slnx/verify/OpenAPI/架构测试接线。
4. [completed] 单元/契约/集成测试（专用 openlims_report_test）。
5. [completed] 完整门禁 + 对抗式评审工作流，CI 全绿后按授权交付。

## 关键设计

- **门禁纯组合**：扇出 IResultAdoptionPort / IQcReportabilityPort（须询问涉及该目标的每个运行）/ IReceivingEligibilityPortV2 / IScopeProductionEligibilityPort / IAllocationStatusPort / IBatchStatusPort / IInstrumentImportPort，全部 gate-then-commit（在自身事务外）。
- **阻断项形状**（RPT-GATE-002）：{objectRef, ruleSetVersion, reasonCodes[], allowedNextSteps[]}，逐项返回不聚合。
- **行级认可**（OD-029 六维）：实验室/地点 × 方法版本 × 产品/基质 × 参数/量程 × 有效期 × 签字人；机构级布尔禁止；混合报告合法但非认可行必须显式标注。
- **EVALUATED 模式失败关闭**：AC-TRACE-001 要求 EVALUATED 行追溯 ConformityDecision，而该对象依赖未决 OD-034 → 门禁对 EVALUATED 行返回 CONFORMITY_DECISION_UNAVAILABLE 阻断，不猜测。
- 状态机止于 待批准；已签发/交付属 DEV-023。

## 约束

- 不触碰 OD-034（结论层级）、OD-012（权威来源）、OD-013（分包回传）；分包披露以调用方声明的受控引用表达，不实现分包对象。
- 模块模板：私有 schema、追加式 55000、advisory lock + expectedCurrentVersion、独立 audit_attempt、平台 audit_intent+outbox 同事务、单一新能力 report.manage、端口 UNKNOWN=阻断。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| BatchStatusResult / ScopeEligibilityDecisions 字段常量名与假设不符 | 2 | 按契约实际名称修正。 |
| 评审：门禁缺结果采用端口（高危） | 1 | 补第七来源 + 有效目标漂移检测 + 集成回归。 |
| 评审：QC 只问一个运行 | 1 | 行改为引用一组运行，新增追加式子表，逐个询问。 |
| 评审：端口回放过期 ALLOWED（高危） | 1 | 以认可判定条数与行数比对作新鲜度判别。 |
| 评审：收样端口传入认可 SiteId | 1 | 改传报告实验室 id，夹具令两者显式不同。 |
| 本地测试库残留旧 report schema | 1 | 一次性 schema 重建验证后撤回临时代码。 |
