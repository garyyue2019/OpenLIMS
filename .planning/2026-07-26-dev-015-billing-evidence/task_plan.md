# DEV-015 唯一计费事实

## 目标

交付 billing 模块最小切片：从服务完成事实（ResultAdoptionPort ALLOWED 门禁）生成唯一计费证据（AC-BILL-001 防重复）、免费项零金额证据、正负调整更正、阶段区分；失败关闭状态端口。

## 阶段

1. [completed] 语义核对：FIN-BILL-001~005（核心 Must）、AC-BILL-001（核心适用：相同服务事实+合同基线+收费维度+规则版本重复触发只存在一条有效计费证据）均在基线；开票/应收 OD-014~019 只挡条件接口不挡核心证据。用户明确选择跳过 RPT（待 OD-011/022/029）直接做 BILL。
2. [completed] 基线依授权收敛：计费证据锚定服务完成事实=（resultGroupId+groupVersion+有效采用，经 ResultAdoptionPort gate-then-commit 固定）×合同基线引用×收费维度×规则版本；唯一键防重复（FIN-BILL-005/AC-BILL-001）；零金额需原因（FIN-BILL-003）；更正只经正负调整证据引用原证据（FIN-BILL-004）；阶段仅记录 SERVICE_COMPLETED→BILLABLE_CANDIDATE，开票/应收/收入确认非目标（FIN-BILL-002 区分即不混同）；单币种（OD-017 open——货币作为调用方声明的固定引用，不做换算）；billing.record 单一能力（五维范围）。
3. [completed] 创建后继规格（BUS-BILL-001~003 + AC-BILL-001@1.0.0 + ATC-BILL-001@1.0.0，无新增 OD）并 READY。
4. [completed] 实现 contracts/billing + src/modules/billing（镜像 result 范式：advisory lock + expectedCurrentVersion、追加式、独立 audit_attempt、专用 openlims_billing_test 库）。
5. [completed] 单元/契约/集成/架构测试。
6. [in_progress] 完整门禁，CI 全绿后按授权自动提交/PR/合并。

## 约束

- AC-BILL-001：相同（服务事实、合同基线、收费维度、规则版本）只能存在一条有效计费证据——数据库唯一约束 + 领域校验双重保证。
- FIN-BILL-004：已确认证据不可改写，更正=追加 ADJUSTMENT（正/负金额）引用原证据。
- 不实现开票、应收、收入确认、税务、对账（OD-014~019 条件接口/BusinessOps）。
- 其余同既往：PRD 只读、失败关闭、不触碰 Release baseline。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| `ATC-BILL-001@1.0.0` 不存在 | 1 | 预期缺口；起草任务卡。 |
