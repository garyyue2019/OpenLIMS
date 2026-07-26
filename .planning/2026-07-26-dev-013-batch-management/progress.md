# DEV-013 进度

## 2026-07-26

- DEV-012 合并（`main@fc17aea`）后按授权继续；backlog 下一张 ATC-BATCH-001 被未决 OD-030 阻断，按授权例外停下询问。
- 用户明确选择"LIMS 最小记录+外部引用"作为 OD-030 口径，解锁执行链。
- 从 `main@bd690a9` 创建分支 `codex/dev-013-batch-management`。
- 语义核对完成：OPS-BATCH-001~003、AC-BATCH-001（QC 失败冻结全部受影响结果、不得只重开有利结果）、OD-030 草案（depends OD-001@0.1.0——后继版本不沿用该未决依赖，口径与试点行业无关）。
- 已追加 6 项批准源规格（OD-030@1.0.0 决定 + BUS-BATCH-001~003 + AC-BATCH-001 + ATC-BATCH-001）；validate 119、READY、written=0、Python 40/40。
- 已实现 contracts/batch + src/modules/batch（类型化批次、AllocationStatusPort gate-then-commit 成员、QC 样、外部证据哈希引用、整批冻结、批级 advisory lock + expectedCurrentVersion、batch.manage 法人+实验室维度），接入宿主/OpenAPI/slnx/verify。
- Batch 单元 14/14、契约 10/10、集成 8/8（专用 openlims_batch_test 库）、架构 12/12 一次通过；全解决方案 23 个测试项目全部通过。
- 路径审计：61 个变更文件全部在 allowed_paths，outside_allowed=0。按授权自动提交/PR/合并。
- PR #13 CI 全绿后按授权以 squash 合并为 `main@c948fc9`；本地 main 已快进。main 现包含 13 个已交付切片，DEV-013 全部完成。
