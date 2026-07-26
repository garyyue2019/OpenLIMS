# DEV-022 进度

## 2026-07-27

- 用户定下 OD-011 / OD-022 / OD-029 三个决策与卡切分方案，报告链解锁；从 `main@8e7b609` 创建分支 `codex/dev-022-report-issuance-gate`。
- OD-011@1.0.0、OD-022@1.0.0、OD-029@1.0.0 落盘（三者显式互相声明边界，去除「撤回」与「认可」的重复定义）；validate=153。
- 规格 BUS-RPT-001/002/003 + AC-RPT-001/AC-ACC-001/AC-TRACE-001 + ATC-RPT-001 落盘并 READY；validate=160、特性 56；二次 generate written=0。
- contracts/report + src/modules/report 八件套交付；新增 IAccreditationScopePort（默认失败关闭，OD-012 未决）与 ISignatoryAuthorityPort；宿主/slnx/verify/OpenAPI/架构测试接线完成，编译零错零警告。
- 测试：单元 20 + 集成 13 + 契约 16（含契约层反射断言"不存在报告级认可布尔"）一次全绿；全仓 40 个测试项目、518 个测试全绿。
- 对抗式评审工作流（4 维 × 15 项发现 → 逐项反驳验证）确认 4 项真实缺陷、驳回 11 项：
  1) 结果采用端口未纳入门禁扇出（高危，三名验证者独立确认）——被取代的采用永不复检，违反 BUS-RPT-002 不变量 1 与 RULE-005；
  2) QC 可报告性只问一个运行，违反 BUS-RPT-002 不变量 2「必须询问每一个运行」；
  3) 签发门禁端口回放过期 ALLOWED（高危）——评估后追加的行从未经过任何端口；
  4) 收样端口被传入认可 SiteId 而非 LaboratoryId。
- 修复：补第七来源并校验有效采用目标未漂移；报告行改为引用一组 QC 运行（新增 report_line_qc_run 追加式子表）；以认可判定条数与行数比对作为端口新鲜度判别；收样端口改传报告自身实验室 id 并让夹具的 SITE 维度与实验室 id 显式不同。
- 回归：单元 +2、集成 +4，报告模块 55 个测试全绿；全仓 40 个项目、524 个测试全绿。
- 期间发现本地 openlims_report_test 残留旧 schema（迁移用 create table if not exists，不改既有表）；本迁移尚未合并，以一次性 schema 重建验证后撤回临时代码，正常路径复跑通过。
- PR #22 CI 全绿后按授权 squash 合并为 main@4950649；本地 main 已快进。main 现包含 22 个已交付切片，DEV-022 全部完成。报告链前半（装配+全链追溯+签发门禁）落地，OD-011/022/029 三个决策正式关闭。
