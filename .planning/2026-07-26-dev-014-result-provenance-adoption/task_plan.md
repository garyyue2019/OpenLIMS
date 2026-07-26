# DEV-014 结果来源与采用

## 目标

交付 result 模块最小切片：不可变结果观测（原始证据哈希引用+解析器版本）、追加式来源图（禁环/禁悬空/禁重复计入）、复测预先采用规则与唯一有效采用结果；批次门禁 gate-then-commit。

## 阶段

1. [completed] 语义核对：LAB-RAW-001/002、LAB-PROV-001/002、LAB-RES-001~004、AC-RETEST-001 均在基线；无未决 OD 阻断（OD-030 已决定原始数据边界）。用户明确选择跳过 INST/QC（待 OD-001）直接做 RESULT。
2. [completed] 基线依授权收敛：结果组锚定批次成员并经 BatchStatusPort 门禁；观测类型六分（INITIAL/DUPLICATE/RETEST/SUPPLEMENT/RE_PREPARATION/RE_SAMPLING），非 INITIAL 需触发原因+批准引用；RETEST 观测前必须已记录采用规则；来源图追加式 DAG；采用规则两策略（RETEST_REPLACES_ORIGINAL / TECHNICAL_REVIEW_SELECTS）；唯一有效采用=最新采用版本；result.record 单一能力。
3. [completed] 创建后继规格与任务卡，生成派生物并 READY。
4. [completed] 实现 contracts/result + src/modules/result（镜像 batch 范式）。
5. [completed] 单元/契约/集成（专用 openlims_result_test 库）/架构测试。
6. [completed] 完整门禁通过；已按授权自动提交、PR #14、CI 全绿并 squash 合并为 `main@5a46910`。

## 约束

- LAB-RAW-002：禁止覆盖已提交观测与结果——全表追加式+触发器。
- LAB-PROV-002：来源图禁环（按构造：输入必须已存在）、禁悬空、禁重复计入；排除输入必须有理由且不可删除。
- LAB-RES-003/AC-RETEST-001：复测执行前记录采用规则；采用必须引用预先记录的规则版本，不得任意选择有利结果。
- LAB-RES-004：每组只有一个有效采用结果（最新采用版本）。
- QC 规则执行（LAB-QC-001/003）与报告资格属后续卡（待 OD-001 / RPT）。
- 其余同既往：PRD 只读、失败关闭、不触碰 Release baseline。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| `ATC-RESULT-001@1.0.0` 不存在 | 1 | 预期缺口；起草任务卡。 |
