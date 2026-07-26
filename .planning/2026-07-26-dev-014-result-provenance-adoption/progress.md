# DEV-014 进度

## 2026-07-26

- DEV-013 合并（`main@c948fc9`）且 main CI 全绿后，INST/QC 撞上未决 OD-001；按授权例外询问，用户明确选择"跳到 RESULT"。
- 从 `main@0f1e58b` 创建分支 `codex/dev-014-result-provenance-adoption`。
- 语义核对完成：12.6 节 LAB-* 八条 Must + AC-RETEST-001 全部在来源基线；无未决 OD 阻断。
- 已追加 5 项批准源规格（BUS-RES-001~003、AC-RETEST-001、ATC-RESULT-001）；validate 124、READY、written=0、Python 40/40。
- 已实现 contracts/result + src/modules/result（六类观测+证据哈希、来源图 DAG、预先采用规则、两策略反挑选校验、唯一有效采用、BatchStatusPort gate-then-commit、result.record 五维能力），接入宿主/OpenAPI/slnx/verify。
- Result 单元 8/8、契约 11/11、集成 8/8（专用 openlims_result_test 库）、架构 13/13 一次通过；全解决方案 26 个测试项目全部通过。
- 路径审计：61 个变更文件全部在 allowed_paths，outside_allowed=0。按授权自动提交/PR/合并。
- PR #14 CI 全绿后按授权以 squash 合并为 `main@5a46910`；本地 main 已快进。main 现包含 14 个已交付切片，DEV-014 全部完成。
