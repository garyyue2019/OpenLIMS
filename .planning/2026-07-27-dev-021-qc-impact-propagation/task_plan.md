# DEV-021 QC 影响传播（ATC-QC-001）

## 目标

OD-001 解锁的第三张（最后一张）卡：交付 qc 模块——按方法版本执行 QC 规则并落 QCResult 事实（LAB-QC-001）；QC 失败计算影响范围并把受影响结果保持为不可报告（LAB-QC-002、RULE-022 传播到全部关联任务与结果，不只发现异常的那一条）；解除阻断必须调查+影响范围+有效性决定+采用规则+技术复核五项齐备（LAB-QC-003），偏差获批本身绝不解除（RULE-010、AC-QC-001）。公开 QC 可报告性端口供报告链后续卡消费。

## 阶段

1. [completed] 侦察：LAB-QC-001/002/003、RULE-010/022、AC-QC-001、OPS-BATCH-003 全在基线；无既有 QC 规格；批次模块已有 QC_SAMPLE 成员+QC_FAILURE 冻结原因+IBatchStatusPort，结果模块已消费批次门禁 → 本卡建在其上，不重复冻结语义。
2. [completed] 规格 BUS-QC-001/002/003 + AC-QC-001 + ATC-QC-001 并 READY；契约测试计数更新。
3. [completed] contracts/qc + src/modules/qc 八件套 + 宿主/slnx/verify/OpenAPI/架构测试接线。
4. [completed] 单元/契约/集成测试（专用 openlims_qc_test）。
5. [in_progress] 完整门禁 + 对抗式评审工作流，CI 全绿后按授权交付。

## 约束

- 与 batch 冻结的边界：batch 冻结由授权人声明原因（已交付），本卡负责 QC 规则执行、QCResult 事实、影响集计算与解除阻断五关口；通过 IBatchStatusPort 消费冻结状态，不复制也不放宽。
- 不触碰 OD-012（设备状态/方法权威来源）与 OD-013（分包回传），二者未决且不在本卡范围。
- 模块模板：私有 schema、追加式 55000、advisory lock + expectedCurrentVersion、独立 audit_attempt、平台 audit_intent+outbox 同事务、单一新能力 qc.manage、状态端口 UNKNOWN=阻断、gate-then-commit。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| BatchStatusResult 字段名为 CurrentBatchVersion 而非 CurrentVersion，CS1061 | 1 | 按契约实际字段名修正。 |
| 评审：可报告性端口作用域易被误读为按目标聚合 | 1 | 非缺陷（三名验证者一致），补契约文档与领域文档说明。 |
