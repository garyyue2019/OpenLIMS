# DEV-019 进度

## 2026-07-26

- 用户正式决定 OD-001："玩具 + 机械物理方法族"，解锁 GOV/INST/QC 三张卡；按连续授权恢复运行。
- 侦察工作流（od001/gov/inst/qc/conventions 五路）完成；从 `main@6426191` 创建分支 `codex/dev-019-gov-applicability-baseline`。
- OD-001__v1.0.0（decided，技术包翻转说明与灯塔证据政策保留）+ BUS-GOV-001 + ATC-GOV-001 落盘并 READY；validate=141。
- snapshot r1-applicability-baseline 创建成功且重复创建被拒绝（不可覆盖验证）。
- 契约测试新增 test_r1_applicability_baseline_is_frozen_and_consistent（决策内容、激活分组、快照一致性），18/18 通过。
- PR #19 CI 全绿后按授权 squash 合并为 main@c6954fe；本地 main 已快进。main 现包含 19 个已交付切片，DEV-019 全部完成；OD-001 正式 decided，INST/QC 卡解锁。
