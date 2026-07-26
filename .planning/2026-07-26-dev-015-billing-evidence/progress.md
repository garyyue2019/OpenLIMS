# DEV-015 进度

## 2026-07-26

- DEV-014 合并（`main@5a46910`）后，RPT 两卡被未决 OD-011/022/029 阻断；按授权例外询问，用户明确选择"跳到 BILL"。
- 从 `main@cac5699` 创建分支 `codex/dev-015-billing-evidence`。
- 语义核对完成：FIN-BILL-001~005 与 AC-BILL-001 均在来源基线；无未决 OD 阻断核心计费证据。
- 已追加 5 项批准源规格（BUS-BILL-001~003、AC-BILL-001、ATC-BILL-001）；validate 129、READY、written=0、Python 40/40。
- 已实现 contracts/billing + src/modules/billing（采用门禁 gate-then-commit、四元组唯一键双层防重复、零金额原因、正负调整链、billing.record 五维能力），接入宿主/OpenAPI/slnx/verify。
- Billing 单元 6/6、契约 9/9、集成 7/7（专用 openlims_billing_test 库）、架构 14/14 一次通过；全解决方案 29 个测试项目全部通过。
- 路径审计：62 个变更文件全部在 allowed_paths，outside_allowed=0。按授权自动提交/PR/合并。
- PR #15 CI 全绿后按授权以 squash 合并；本地 main 已快进。main 现包含 15 个已交付切片，DEV-015 全部完成。
