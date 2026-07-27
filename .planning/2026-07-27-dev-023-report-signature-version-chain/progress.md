# DEV-023 进度

## 2026-07-27

- DEV-022 合并（main@4950649）后用户指示"继续 不要停"；OD-011/022 已决，本卡无需新决策。
- 从 `main@89f5215` 创建分支 `codex/dev-023-report-signature-version-chain`。
- 规格 BUS-RPT-004/005 + AC-RPT-002 + ATC-RPT-002 落盘并 READY；validate=164、特性 58；二次 generate written=0。
- 交付：report-0002 迁移（version_snapshot / version_signature / controlled_action，全部追加式触发器 + VOID/WITHDRAWAL 唯一索引）、ReportVersionRules（规范化哈希 + 三要素 + 五种动作）、ReportVersionService + IReportVersionChainPort、5 个新端点与 OpenAPI。
- 健壮性修正（自测发现）：门禁评估原按 evaluated_at 排序，固定时钟下两次评估时间戳相同会让"最新评估"变成随机；改为按单调的 report_version 排序，受控动作按版本号排序。
- 测试：单元 40 + 契约 16 + 集成 26 = 报告模块 82 个全绿；全仓 40 个项目、551 个测试全绿。
- 对抗式复核（4 维 × 32 项候选，逐项由独立反驳者审查）确认 2 项真实缺陷，均已修复：
  1. `SUPERSESSION` 是五种受控动作里唯一没有重复防护的——领域层只特判撤回与已取代，唯一索引也只覆盖另外四种。两次相同 POST 都会成功，而 `controlled_action` 的追加式触发器让重复事实永久无法更正，违反 BUS-RPT-005 不变量 7。修复：领域层按 `chain.SupersedingReportNumber` 拦截 + `ux_controlled_action_supersession` 链级唯一索引兜底。
  2. `WITHDRAWAL`/`VOID` 携带空白取代号时，领域层按 `IsNullOrWhiteSpace` 判缺省而 CHECK 约束按 NULL 判，空串绕过校验撞 23514，被报成 503 而非 400。修复：两层对"缺省"取同一定义（NULL）。
  另外 30 项被反驳者否决，其中多数是变异覆盖偏好或对已批准规格的改写请求（例如要求把签署人放进哈希、要求验证页返回取代关系数组），均与 ATC-RPT-002 冻结的数据契约相悖。
- 复核后回归：单元 42 + 契约 16 + 集成 27 = 报告模块 85 全绿；全仓 40 个项目、553 个测试全绿。
- 全量门禁：validate --strict-warnings=164、check、verify-history、二次 generate written=0、仓库契约测试 18 项全绿；路径审计 32 文件 outside_allowed=0。
