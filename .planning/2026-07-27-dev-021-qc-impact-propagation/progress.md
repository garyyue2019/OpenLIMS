# DEV-021 进度

## 2026-07-27

- DEV-020 合并（main@a3c927e）后继续；从 `main@680fc1b` 创建分支 `codex/dev-021-qc-impact-propagation`。
- 规格 BUS-QC-001/002/003 + AC-QC-001 + ATC-QC-001 落盘并 READY；validate=150；二次 generate written=0。
- contracts/qc + src/modules/qc 八件套交付；宿主/slnx/verify/OpenAPI/架构测试接线完成，编译零错零警告。
- 测试：单元 14 + 契约 12 + 集成 10（专用 openlims_qc_test）一次全绿；全仓 37 个测试项目、468 个测试全绿。
- 对抗式评审工作流（4 维审查 × 15 项发现 → 逐项反驳验证）：**零确认缺陷**，15 项全部驳回（多为变异覆盖偏好或对端口作用域的误读）。
- 三名独立评审员均对"PASSED 分支对任意 targetId 返回 ALLOWED"提出质疑，验证者一致认定端口按契约是按运行的谓词而非按目标的聚合——非缺陷，但语义不够显眼，据此在 IQcReportabilityPort 与领域文档补充作用域说明（零行为变更，测试仍全绿）。
- PR #21 CI 全绿后按授权 squash 合并为 main@cc4a303；本地 main 已快进。main 现包含 21 个已交付切片，DEV-021 全部完成。OD-001 解锁的三张卡（GOV/INST/QC）全部交付。
