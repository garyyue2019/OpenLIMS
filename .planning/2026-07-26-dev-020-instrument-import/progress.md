# DEV-020 进度

## 2026-07-27

- DEV-019 合并（main@c6954fe）后继续；从 `main@d59dbc5` 创建分支 `codex/dev-020-instrument-import`。
- 规格 BUS-INST-001/002/003 + ATC-INST-001@1.0.0 落盘并 READY；validate=145；二次 generate written=0。
- contracts/instrument + src/modules/instrument 八件套交付；宿主/slnx/verify/OpenAPI/架构测试接线完成，编译零错零警告。
- 测试：单元 12 + 契约 11（含 PRD §22-15 验证数据集 100% 一致）+ 集成 9（专用 openlims_instrument_test）全部一次通过；全仓 34 个测试项目全绿。
- 对抗式评审工作流（4 维审查 × 22 项发现 → 逐项反驳验证）确认 4 项真实缺陷、驳回 18 项：
  1) 行号被异常队列占用时重投触发唯一约束 23505（高危，两名验证者独立确认）；
  2) 同批两条同行号异常同样撞约束并丢失整批事实；
  3) 裸 23505 被错映射为 EXPECTED_VERSION_CONFLICT，与 batch/result 等模块不一致且诱导无效重试；
  4) 缺同版本并发提交测试（各兄弟模块均有）。
- 修复：ClassifyRows 区分"行事实占用"（排队 DUPLICATE_ROW）与"异常队列占用"（INS.VALIDATION_FAILED），批内异常行号也纳入队列集；FailAsync 裸 23505 → ValidationFailed。
- 回归：单元 +2、集成 +2（重投失败关闭、同版本并发单赢家），instrument 36 个测试全绿；全仓 34 项目 431 测试全绿。
- PR #20 CI 全绿后按授权 squash 合并为 main@a3c927e；本地 main 已快进。main 现包含 20 个已交付切片，DEV-020 全部完成。
