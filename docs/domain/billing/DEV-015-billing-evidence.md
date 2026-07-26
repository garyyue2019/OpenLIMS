# DEV-015 唯一计费事实

## 交付范围

billing 模块最小切片（EP-BILLING-INTEGRATION 核心证据层，RPT 待 OD-011/022/029）：

- **唯一计费候选**（FIN-BILL-001/005、AC-BILL-001）：证据从服务完成事实生成——创建前 ResultAdoptionPort 必须 ALLOWED（gate-then-commit），有效采用目标原样固定；唯一键 =（结果组+版本+采用目标）×合同基线引用×收费维度×计费规则版本，领域校验 + 数据库唯一约束双重防重复（报告重发/接口重试/并发均只留一条）。
- **阶段区分**（FIN-BILL-002）：只记录 SERVICE_COMPLETED→BILLABLE_CANDIDATE；开票、应收确认、收入确认为非目标（FIN-INV-*/BusinessOps 条件接口）。
- **零金额证据**（FIN-BILL-003）：免费项必附原因，非零禁附。
- **正负调整链**（FIN-BILL-004）：证据不可改写，更正=追加非零金额调整引用原证据，净额由消费方按链计算。

能力 `billing.record`（五维对象范围）；货币为声明引用不做换算（OD-017 open）；`IBillingEvidencePort`（`BILLING-EVIDENCE@1.0.0`）失败关闭。

## 迁移与验证

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration billing
```

集成测试使用专用数据库 `openlims_billing_test`。
