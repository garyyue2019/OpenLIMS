# DEV-013 制备/分析批最小切片

## 目标

在用户批准的 OD-030 最小口径（LIMS 最小执行记录+外部引用）下，交付类型化批次事实、跨委托成员与客户隔离、外部证据引用、QC 失败全量冻结和失败关闭批次状态端口。

## 阶段

1. [completed] 语义核对：OPS-BATCH-001~003、AC-BATCH-001、OD-030 草案均在基线；用户已批准最小口径。
2. [completed] 基线依授权收敛：四类型批次（禁通用 ExecutionRun）、成员经 Allocation 状态端口 gate、批准 QC 样成员、外部证据追加引用、批级冻结不可选择性保留、batch.manage 单一能力（法人+实验室维度）。
3. [completed] 创建 OD-030@1.0.0 与后继规格，生成派生物并 READY。
4. [completed] 实现 contracts/batch + src/modules/batch（镜像 allocation 范式，gate-then-commit）。
5. [completed] 单元/契约/集成（专用 openlims_batch_test 库）/架构测试。
6. [in_progress] 完整门禁，CI 全绿后按授权自动提交/PR/合并。

## 约束

- OPS-BATCH-001：制备/预处理/分析/仪器运行分型管理，禁止通用 ExecutionRun。
- OPS-BATCH-002：一个批次可含多委托试样与批准 QC 样，客户隔离与结果归属按成员固定。
- OPS-BATCH-003/AC-BATCH-001：QC 失败/环境超差/校准失效冻结全部受影响成员，保留原批次与数据，不得选择性重开。
- OD-030 最小口径：LIMS 只存最小执行记录，原始数据/ELN 为不可变版本化外部引用；不做仪器控制。
- 其余同既往：PRD 只读、失败关闭、不触碰 Release baseline。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| `ATC-BATCH-001@1.0.0` 不存在 | 1 | 预期缺口；起草任务卡。 |
