# DEV-014 结果来源与采用

## 交付范围

result 模块最小切片（EP-QUALITY 首篇，INST/QC 待 OD-001）：

- **不可变观测**（LAB-RAW/RES-001/002）：INITIAL/DUPLICATE/RETEST/SUPPLEMENT/RE_PREPARATION/RE_SAMPLING 六类；非 INITIAL 必附触发原因与批准引用；每条固定外部证据（来源系统 + 稳定 ID + 版本 + SHA-256 + 解析器版本，OD-030 边界）；禁止覆盖。
- **追加式来源图**（LAB-PROV-001/002）：派生结果引用全部纳入/排除输入（排除必附理由）与聚合规则版本；输入必须组内已存在（按构造无环）、禁止重复计入。
- **预先采用规则与唯一有效采用**（LAB-RES-003/004、AC-RETEST-001）：RETEST 观测前必须已记录采用规则；策略 RETEST_REPLACES_ORIGINAL（采用目标必须是最新复测或纳入其的派生）或 TECHNICAL_REVIEW_SELECTS（必须附技术复核批准）；采用版本递增、最新有效、历史保留——执行人员不得任意选择有利结果。
- **批次门禁**：建组前 BatchStatusPort 必须 ALLOWED（gate-then-commit，决定原样固定）；冻结批次不能开新结果组。

能力 `result.record`（五维对象范围）；组级 advisory lock + expectedCurrentVersion；`IResultAdoptionPort`（`RESULT-ADOPTION@1.0.0`）返回 ALLOWED/BLOCKED/UNKNOWN。

## 迁移与验证

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration result
```

集成测试使用专用数据库 `openlims_result_test`。QC 规则执行/解除阻断（LAB-QC-001/003）与报告签发属后续卡。
