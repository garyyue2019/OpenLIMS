# DEV-007 发现

## 前置门禁

- `validate`：通过，86 个规格版本、389 个 PRD 来源条目。
- `source-status`：来源基线一致。
- `impact`：无待处理漂移或影响。
- `ATC-REC-006@1.0.0`：BLOCKED；旧故事和多项旧依赖未批准。

## 当前基线

- DEV-005 和 DEV-006 已合并至 `main`。
- `OPS-RECEIPT-003@1.0.0`、`OPS-IDENTITY-003@1.0.0` 与 `AC-REC-001@1.0.0` 已批准，当前资格端口故意失败关闭。
- DEV-006 已提供异常事实和 `OPEN`、`AWAITING_CUSTOMER`、`CONDITIONALLY_ACCEPTED`、`REJECTED`、`SAFETY_HOLD` 决定。

## 待核实

- Web 放行面板的最小交互是否需要在本切片交付；任务卡已允许复用现有 Receiving 页面。

## 已核实

- 当前实现集中在 `contracts/receiving/**`、`src/modules/receiving/**`、`src/host/api/**`、`apps/web/src/**` 和三层 Receiving 测试目录。
- 现有事务协调器、失败尝试审计、模块本地审计、Outbox、授权端口和迁移链可以直接复用。
- `IReceivingEligibilityPort` v1 固定使用 `REC-ELIGIBILITY@1.0.0` 并故意始终失败关闭；DEV-007 应新增 v2 接口，避免改变 v1 调用者语义。
- DEV-006 条件接收已经校验非空允许/禁止动作、有效期、证据与质量能力；DEV-007 只需在锁内聚合最新状态和限制。
- 严格校验不允许同一逻辑 requirement/acceptance 同时存在两个 approved 版本；为避免改写旧版本或引入发布基线负担，最终只追加 `ATC-REC-006@2.0.0`，并精确依赖现有批准规格。

## PR #7 CI 与发布边界

- PR #7 可自动合并，但当前 3 项检查中 1 项失败，Squash 按钮因此禁用。
- 唯一失败为 `Matched_item_without_exceptions_releases_atomically_and_only_v2_allows_execution`：数据库实际状态历史数为 3，测试期望 2。
- 注册流程为每个对象记录 RECEIVED 与 QUARANTINED，受控放行再追加 ACCEPTED；因此实现和数据库结果正确，失败来自陈旧断言。
- 仓库没有明确的生产 deploy/publish 工作流、版本号或目标环境；`REL-R1-RECEIVING-PILOT@1.0.0` 仍为 proposed，不能把 PR 合并当成正式发布或擅自创建 Seal/tag/GitHub Release。
