# ATC-PLT-000 联合评审与依赖裁剪审批包

## 1. 文件状态

- 状态：`DRAFT / NOT APPROVED / DO NOT IMPLEMENT`
- 变更提案：`CHANGE-PLT-DEPENDENCY-SCOPE-001`
- 当前任务：`ATC-PLT-000@0.1.0`
- 当前技术决策：`ED-001@0.1.0`
- 当前评审投影：15个`1.0.0`未批准机器草案
- 编制日期：2026-07-23
- 目的：把“工程骨架可以开始实施”与“Release 1业务范围、生产容量和上线可以批准”严格分开

本文件只提供推荐方案、评审问题和证据格式，不是批准记录。任何空白、`PENDING`、缺少受控身份或缺少授权证据的行，都不能使任务进入Ready。

配套材料：

- [当前工程骨架任务评审说明](ATC-PLT-000-ENGINEERING-SKELETON-REVIEW.md)
- [当前生成任务卡](../../generated/spec/tasks/ATC-PLT-000__v0.1.0.md)
- [ED-001技术栈候选ADR](ED-001-TECH-STACK-CANDIDATE.md)
- [下一Major版本精确变更集](ATC-PLT-000-NEXT-VERSION-CHANGESET.md)
- [逐角色PENDING评审工作清单](review-records/CHANGE-PLT-NEXT-VERSIONS-001__draft.csv)
- [受控评审记录模板](templates/atc-plt-000-review-record.csv)
- [Release 1决策冲刺包](REL-R1-DECISION-SPRINT-001.md)

## 2. 本轮不请求批准什么

本轮不请求、也不能借工程骨架评审顺带批准：

- 真实付费灯塔、真实法人、真实实验室或生产上线主体；
- 玩具分析化学方法、QC、仪器、报告或完整Release 1范围；
- 日均500订单对应的真实峰值、对象倍率、节点规格或容量承诺；
- 生产SLA、可用性、RPO、RTO或恢复演练结果；
- 生产IdP、生产对象存储供应商、WORM、KMS或生产网络拓扑；
- `OD-020@0.1.0`或`OD-025@0.1.0`的完整业务/生产结论；
- 收样、身份、隔离、制样、分析化学、QC、报告或计费业务代码。

上述事项继续由Release、生产验证和业务包决策阻断，不会因为平台骨架获准而自动变成已批准。

## 3. 为什么当前0.1.0不能直接批准

当前依赖链为：

```text
ATC-PLT-000@0.1.0
├─ ED-001@0.1.0
│  ├─ OD-020@0.1.0
│  │  └─ OD-001@0.1.0
│  └─ OD-025@0.1.0
│     └─ OD-001@0.1.0
├─ OD-020@0.1.0
├─ OD-025@0.1.0
└─ NFR-ARCH-001@0.1.0
   └─ OD-025@0.1.0
```

这会产生三类问题：

1. 纯工程空壳被真实灯塔、业务方法、生产容量和报告边界传递阻塞；
2. `OD-020`主要回答生产容量/RPO/RTO，却没有给出Host健康探测和CI恢复测试所需的非生产参数；
3. `OD-025`主要回答玩具、分析化学、物理机械和微生物的产品包语义，而本任务明确禁止创建真实业务模块或Pack。

因此，推荐结论不是“降低门禁”，而是把门禁放回正确层级：工程骨架只等待工程、安全、审计和测试边界；Release 1和生产上线继续等待业务、容量和真实证据。

## 4. 推荐目标依赖结构

```text
OD-002@1.0.0（已批准：每集团独立部署）
├─ ED-001@新Major版本（收窄：工具链、仓库、开发/CI基线）
├─ ED-002@首个版本（新增：通用模块与持久化边界）
│  └─ NFR-ARCH-001@新Major版本
│     └─ NFR-ARCH-002@新Major版本
├─ SEC-DEPLOY-001@后续批准版本
├─ SEC-AUD-001@后续批准版本
└─ AC-DEPLOY-001@后续批准版本
   └─ ATC-PLT-000@新Major版本

REL-R1-RECEIVING-PILOT@后续版本
├─ ATC-PLT-000@新Major版本
├─ OD-020@后续批准版本（生产容量、可用性和恢复）
└─ OD-025@后续批准版本（行业包、技术包和分析化学业务边界）
```

说明：

- `OD-020`和`OD-025`仍保留在Release基线，不从产品门禁删除；
- 新版平台任务不再直接或传递依赖这两项；
- 新版收样任务必须升级精确依赖，不能继续指向旧平台任务；
- 所有新版本必须在责任人完成评审后创建，当前文件不预先分配“已批准”状态。

## 5. 评审项目

### RV-PLT-001：依赖范围裁剪

推荐结论：`ACCEPT`

批准内容：

- 从新版平台任务的直接依赖和来源中移除`OD-020`；
- 从新版平台任务的直接依赖中移除`OD-025`；
- 新建通用工程边界Decision，替代`OD-025`对模块化单体NFR的阻塞；
- 生产容量、RPO/RTO和具体业务Pack仍由`OD-020/OD-025`阻断Release，不因裁剪而放行。

必须批准角色：产品负责人、架构负责人、工程负责人。

拒绝的影响：当前平台任务继续等待完整Release 1和生产证据，不能进入工程实现。

### RV-PLT-002：工程技术栈

推荐结论：`ACCEPT_CANDIDATE_WITH_EXACT_VERSION_LOCKS`

推荐候选：

- .NET 10 LTS / ASP.NET Core 10；
- EF Core 10 / Npgsql 10 / PostgreSQL 18受支持补丁；
- Vue 3.5 / TypeScript 5.9 / Vite 7 / Ant Design Vue 4；
- Node.js 24 LTS / pnpm 10；
- REST / OpenAPI 3.1 / JSON Schema 2020-12 / RFC 9457；
- Linux OCI制品、GitHub Actions、锁依赖、SAST/SCA、SBOM和镜像扫描。

批准条件：

- 新版`ED-001`必须填写实际SDK patch、包版本、容器digest和GitHub Action commit；
- `global.json`、集中包版本、NuGet锁、pnpm锁和镜像digest必须是权威版本源；
- 依赖机器人不得无人工评审自动合并生产依赖升级；
- 本结论不批准生产拓扑或生产供应商。

必须批准角色：架构负责人、工程负责人、安全负责人、运维负责人。

### RV-PLT-003：模块化单体工程边界

推荐结论：`ACCEPT_AND_DRAFT_ED-002`

新Decision只应冻结：

- Host、building-blocks、Contracts、未来模块和Pack的引用方向；
- 每个未来模块独立Schema、`DbContext`和迁移历史；
- 禁止跨模块Infrastructure、EF实体、`DbContext`和私表访问；
- 跨模块同步只走版本化公共端口，异步只走Outbox和版本化事件；
- Shared Kernel只保存技术原语；
- Pack采用编译期注册并由requirements lock固定版本；
- 骨架只用`tests/**`夹具证明边界，不创建真实业务模块或Pack。

不得写入该Decision：

- 玩具与其他行业包激活；
- 分析化学、物理机械或微生物业务边界；
- 方法、QC、仪器、报告或真实数据库所有权矩阵。

必须批准角色：架构负责人、工程负责人；QA负责人复核测试可执行性。

### RV-PLT-004：非生产工程验证环境

推荐结论：`ACCEPT`

推荐范围：

- 本地：Docker Compose启动PostgreSQL、Keycloak和MinIO，只使用合成配置；
- CI：Testcontainers或等价确定性方式启动真实依赖，不依赖开发机状态；
- 隔离：两个独立集团夹具使用不同数据库、Bucket、IdP、密钥、遥测和备份引用；
- 恢复：测试PostgreSQL、IdP元数据、S3、Worker崩溃、Inbox竞争和依赖超时；
- 迁移：应用启动不得自动改变验证或生产Schema。

明确非效果：

- 不证明日均500订单容量；
- 不证明生产SLA、可用性、RPO或RTO；
- 不选择生产IdP、对象存储或基础设施供应商；
- 不使用真实业务数据。

必须批准角色：工程负责人、运维负责人、QA负责人；安全负责人复核Secret和隔离配置。

### RV-PLT-005：集团独立部署安全契约

推荐结论：`ACCEPT_EXISTING_DIRECTION_WITH_TEST_EVIDENCE`

必须证明：

- 客户端集团覆盖以稳定HTTP/errorCode整体拒绝，不能静默忽略；
- 跨集团数据库、Bucket、IdP、遥测和备份凭据访问失败；
- 跨集团令牌在数据访问前拒绝；
- 跨集团备份恢复在写入前被清单/身份校验阻断；
- 同一不可变镜像可以复用，但运行和数据平面不能共享。

必须批准角色：架构负责人、安全负责人、运维负责人；QA负责人批准自动化证据设计。

### RV-PLT-006：审计、Outbox和失败尝试语义

推荐结论：`ACCEPT_OPTION_A`

选项A（推荐）：

```text
模块业务事务
├─ 测试夹具业务事实
├─ module-local audit_pending
└─ module-local outbox
      ↓ 幂等汇聚
中央追加式audit.event
```

约束：

- 业务事实、审计意图和Outbox原子提交；任一失败则业务事务回滚；
- 中央账本允许最终一致，但待汇聚意图不得丢失或覆盖；
- 被拒绝、无权限和事务回滚的尝试通过独立追加式安全/审计写入路径记录；
- 若该失败尝试审计也无法可靠写入，受控命令必须保持失败并触发运维告警；
- 运行日志不能替代审计账本；
- Secret、令牌和未脱敏正文不得进入任何载体。

选项B：中央账本立即事务可见。采用该选项必须另行设计受控数据库追加端口或存储过程，并解释跨模块耦合、可用性和恢复影响。

必须批准角色：质量负责人、审计负责人、安全负责人、架构负责人、运维负责人。

### RV-PLT-007：供应链和验证入口

推荐结论：`ACCEPT`

必须固定：

- Windows/Linux均提供`task/architecture/contracts/all`入口；
- 脚本只做可见编排，任何失败必须传播非零退出码；
- 禁止按机器环境静默跳过安全、架构或集成测试；
- CI执行锁定恢复、警告即错误构建、测试、Secret扫描、SAST/SCA、SBOM和镜像扫描；
- `specgen`完整门禁继续先于应用实现门禁；
- 同一输入第二次生成必须`written=0`。

必须批准角色：工程负责人、安全负责人、运维负责人、QA负责人。

### RV-PLT-008：任务范围与实施授权

推荐结论：`ACCEPT_SCOPE_ONLY_AFTER_RV-PLT-001..007`

批准内容仅限当前任务的`allowed_paths`和非目标。批准后仍不得：

- 创建`src/modules/**`或`src/packs/**`生产实现；
- 实现收样、分析化学、报告或其他业务；
- 修改`spec/**`或人工编辑`generated/spec/**`；
- 使用真实客户数据、生产Secret或执行生产迁移；
- 引入Kubernetes、Kafka/RabbitMQ、Redis、OpenSearch、向量数据库或独立Python AI服务。

必须批准角色：产品负责人、架构负责人、工程负责人、安全负责人、运维负责人、QA负责人。

## 6. 责任角色与最低证据

| 角色槽 | 至少评审项目 | 必填证据 |
|---|---|---|
| 产品负责人 | RV-PLT-001、RV-PLT-008 | 受控身份、任务范围批准、业务/生产事项继续后移的确认 |
| 架构负责人 | RV-PLT-001、002、003、005、006、008 | 技术选择、边界、审计模型和反对意见处理记录 |
| 工程负责人 | RV-PLT-001、002、003、004、007、008 | 团队能力、版本锁、命令和维护责任 |
| 安全负责人 | RV-PLT-002、004、005、006、007、008 | 威胁边界、Secret、身份、供应链和隔离证据 |
| 运维负责人 | RV-PLT-002、004、005、006、007、008 | 开发/CI依赖、故障恢复、升级和告警责任 |
| 质量负责人 | RV-PLT-006 | 审计意图、失败尝试、中央账本一致性语义 |
| 审计负责人 | RV-PLT-006 | 追加性、字段、保留和查询/导出边界 |
| QA负责人 | RV-PLT-003、004、005、007、008 | 正反向、边界、权限、并发、恢复和供应链测试可执行性 |

每条受控评审记录必须包含：

- `subject_ref`和评审时的`subject_hash`；
- 稳定`role_slot`；
- `reviewer_identity_ref`，不能只写姓名或“已有人”；
- `authority_scope`与`authority_evidence_ref`；
- `decision`、条件、反对意见和证据引用；
- 带时区的`reviewed_at`；
- `signature_or_approval_ref`；
- `record_status=VERIFIED`后才可计入批准闭包。

## 7. 允许的评审结论

每个评审项目只能使用以下结论：

- `ACCEPT`：无附加条件接受；
- `ACCEPT_WITH_CONDITIONS`：条件已明确，未满足前仍阻断；
- `REJECT`：拒绝并说明替代方案；
- `ABSTAIN`：无相应授权，不计入批准；
- `PENDING`：尚未评审，不计入批准。

不得使用“原则同意”“基本同意”“先开发再补”“默认接受”等无法确定是否放行的文字。

## 8. 版本升级计划

字段级版本、依赖和状态变化以[下一Major版本精确变更集](ATC-PLT-000-NEXT-VERSION-CHANGESET.md)及其SHA-256为签署对象；本节只描述执行顺序。

15个`1.0.0`机器草案已经以`proposed/in_review/blocked`状态创建，用于让责任人评审真实字段、依赖图和生成结果；创建草案不属于批准状态变更。只有RV-PLT-001至008形成完整、可验证的责任人记录后，才能执行批准语义和实现授权：

1. 保留当前`0.1.0`文件，不原地改成`approved`；
2. 逐字段评审当前`1.0.0`草案及其精确依赖，不把草案状态当作接受结论；
3. 补齐技术锁值、证据、受控身份、授权和所有阻塞意见；
4. 任何评审导致的语义或状态变化均按SemVer创建后继版本，不由AI原地提升本轮草案；
5. 用受控批准证据决定哪些后继版本可标记`approved`，不能由AI推断；
6. Release和六张`ATC-REC-*`继续使用`proposed/blocked`后继版本并保留业务/生产门禁；
7. 运行`validate --strict-warnings`、`source-status`、`impact`、`verify-history`、两次`generate`、`check`和全部测试；
8. `ready --story ATC-PLT-000@批准后继版本`必须返回READY后，才可把任务交给实现代理。

若任一责任角色拒绝，先更新变更提案和草案版本，再重新评审；不得修改门禁或删除失败记录。

## 9. 发起人最小回复格式

发起人可以先用以下格式表达产品/方案选择：

```text
CHANGE-PLT-DEPENDENCY-SCOPE-001
RV-PLT-001 DEPENDENCY_SCOPE_SPLIT = ACCEPT / REJECT
RV-PLT-002 STACK_CANDIDATE = ACCEPT / REJECT
RV-PLT-003 MODULE_BOUNDARY = ACCEPT / REJECT
RV-PLT-004 NON_PRODUCTION_ENV = ACCEPT / REJECT
RV-PLT-005 GROUP_ISOLATION = ACCEPT / REJECT
RV-PLT-006 AUDIT_MODEL = OPTION_A / OPTION_B / REVISE
RV-PLT-007 SUPPLY_CHAIN_GATES = ACCEPT / REJECT
RV-PLT-008 TASK_SCOPE = ACCEPT / REJECT
```

这份回复只记录发起人的选择，不能替代各责任角色的受控评审记录。若同一人合法兼任多个角色，也必须分别给出角色槽、授权范围和批准证据。

### 9.1 已收到的发起人方案方向

- 记录状态：`USER_CONFIRMED_PENDING_CONTROLLED_IDENTITY_AND_ROLE_APPROVAL`
- 对应变更集：`CHANGE-PLT-NEXT-VERSIONS-001`
- 记录日期：2026-07-23
- 受控身份引用：未提供
- 代表角色与授权依据：未提供
- 记录边界：以下内容仅表示当前会话用户的方案方向选择，不构成任何责任角色槽的`ACCEPT/VERIFIED`记录，也不是实现授权或数字签名。

| 评审项 | 发起人选择 |
|---|---|
| `RV-PLT-001 DEPENDENCY_SCOPE_SPLIT` | `ACCEPT` |
| `RV-PLT-002 STACK_CANDIDATE` | `ACCEPT`，实施前必须补齐全部精确版本锁及证据 |
| `RV-PLT-003 MODULE_BOUNDARY` | `ACCEPT` |
| `RV-PLT-004 NON_PRODUCTION_ENV` | `ACCEPT` |
| `RV-PLT-005 GROUP_ISOLATION` | `ACCEPT` |
| `RV-PLT-006 AUDIT_MODEL` | `OPTION_A` |
| `RV-PLT-007 SUPPLY_CHAIN_GATES` | `ACCEPT` |
| `RV-PLT-008 TASK_SCOPE` | `ACCEPT` |

在受控身份、角色授权、授权证据、逐角色签名和技术锁证据闭合前，`CHANGE-PLT-NEXT-VERSIONS-001__draft.csv`的33条活动记录继续保持`decision=PENDING`和`record_status=DRAFT`。

## 10. 当前结论

- `ATC-PLT-000@0.1.0`继续保持`proposed/blocked`；
- `ATC-PLT-000@1.0.0`及其14个配套新版本已作为未批准评审投影创建；
- 发起人已对8项方案给出方向选择，但受控身份、代表角色和授权依据仍待补充；
- 当前不实施工程骨架；
- 当前没有任何新Decision、NFR、Acceptance或Story被标为`approved/decided/ready`；
- 当前先收集评审结论和受控身份/授权证据；
- 依赖裁剪获批后，再按SemVer创建由证据支持的后继版本并重新执行Ready门禁。
