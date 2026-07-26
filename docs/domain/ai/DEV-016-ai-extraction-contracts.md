# DEV-016 AI 资料抽取与缺口建议契约切片

## 交付范围与定位

按 AI-BOM-014 前置条件（未决 OD-006 数据分类/处理区域/模型许可、OD-007 品类/复核阈值/停止条件），本卡仅交付 **AI 治理契约层**（`OpenLIMS.Contracts.Ai` + `Profile=ai` 契约测试）：

- **运行控制封套**（AI-BOM-007）：模型、网关路由、提示模板、输出模式、输入版本全部固定，缺一即校验失败；
- **事实类别税则**（AI-BOM-002 / AC-AI-002）：OBSERVATION/ASSUMPTION/AI_INFERENCE/VERIFIED_FACT；VERIFIED_FACT 必须同时携带权威来源与验证方法——**AI_INFERENCE 永不自动提升**（`AIX.FACT_CLASS_PROMOTION_REJECTED`）；
- **失败关闭校验**（AI-BOM-008 / AC-AI-003）：未知字段、非法单位、缺来源、重复确定值 → 输出整体 `QUARANTINED` 并列明错误，不产生任何下游产物；
- **不确定性表达**（AI-BOM-004）：候选分支与弃权合法，伪装单一确定答案被拒绝；
- **人工处置**（AI-BOM-009/010）：ACCEPT/MODIFY/SPLIT/MERGE/REJECT；MODIFY 必须保留 AI 原值、人工值、原因、责任人，且类别不变；
- **缺口/澄清建议**独立表达，不自动写入受控对象。

**零运行时**：不运行模型、不处理客户数据、无模块/schema/端点/能力；OD-006/007 保持 open。规则为纯函数，序列化形状被契约测试冻结。

## 验证

```powershell
pwsh -File scripts/verify.ps1 -Profile task -Module ai
```
