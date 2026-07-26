# 平台审计与发件箱（DEV-017 / ATC-PLT-002）

## 数据库强制（platform-0002 迁移）

| 表 | 允许 | 拒绝 |
|---|---|---|
| `platform.audit_intent` | INSERT | UPDATE/DELETE → `PLT.AUDIT_APPEND_ONLY`（errcode 55000） |
| `platform.outbox` | INSERT；UPDATE 仅 `dispatched_at` 从 null 置非空且其余列不变 | DELETE、改其他列、重复派发标记 → `PLT.OUTBOX_DISPATCH_ONLY`（errcode 55000） |

- SEC-AUD-002（普通管理员无法修改或删除审计日志）由此从应用约定升级为数据库触发器强制；superuser/表 owner 的 DDL 与 TRUNCATE 属于运维边界，不在应用保证范围内。
- 就绪探针（`PlatformMigrationRunner.IsCurrentAsync`）要求 `platform-0001` 与 `platform-0002` 均已登记。
- 测试基础设施使用 TRUNCATE 清理，不触发行级触发器，既有各模块集成测试不受影响；冒烟脚本改为断言不可变并演示合法派发标记，不再删除审计证据。

## 全链端到端验证（tests/e2e/chain）

`OpenLIMS.Platform.ChainE2ETests` 在专用库 `openlims_chain_test` 中以单一 DI 容器装配 scope/quantity/allocation/batch/result/billing 六个模块，全部使用真实公共端口：

```
范围矩阵 → 数量账户+入账 → 测试对象分配（范围/数量真实门禁）
        → 批次+试样成员（分配真实门禁） → 结果组/初测/规则/复测/采用（批次真实门禁）
        → 计费证据（采用真实门禁）
```

- 12 步命令各自留下 `platform.audit_intent`（按 correlation 排序单调）与 `platform.outbox` 事件。
- receiving 资格端口以许可桩替代（receiving 模块早于端口纪律交付，纳入真实链路属后续卡）。
- 失败关闭：以过期分配版本挂载批次成员被拒绝，仅 `batch.audit_attempt` 留痕。

## 请求上下文与对象级授权（DEV-018 / ATC-PLT-001）

`RequestContextAuthorizationE2ETests` 在同一专用库上以平台组合证据固定 SEC-AUTH-001 与 AC-SEC-001：

- **部署绑定组织**：组织上下文来自容器部署配置（`DeploymentOrganizationContext`），请求载荷无覆盖入口；行为人组织与部署组织不一致 → `<MOD>.NOT_AUTHORIZED`，零事实。
- **能力拒绝失败关闭**：授权端口 Deny 时命令无业务事实、无平台审计意图/发件箱泄漏，仅模块 `audit_attempt` 留痕且 correlation 原样。
- **跨组织不泄露存在性**：跨组织读取因按组织分区的加载不命中返回 `OBJECT_NOT_ACCESSIBLE`，与读取不存在对象逐字节不可区分（异常类型与消息一致）。
- **correlation 贯穿**：调用方 correlation 原样固定于 `platform.audit_intent`（含 actor 与组织）。
