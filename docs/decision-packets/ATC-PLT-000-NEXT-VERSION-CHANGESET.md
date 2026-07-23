# ATC-PLT-000 下一Major版本精确变更集

## 1. 文件状态

- 状态：`DRAFT / NOT APPROVED / DO NOT APPLY`
- 变更集ID：`CHANGE-PLT-NEXT-VERSIONS-001`
- 上游提案：`CHANGE-PLT-DEPENDENCY-SCOPE-001`
- 当前基线：`ATC-PLT-000@0.1.0`
- 当前评审投影：15个`1.0.0`未批准机器草案
- 编制日期：2026-07-23
- 目的：让责任人签署确定的版本、依赖、状态和字段变化，而不是让实施代理在批准后自行解释

本文件是待签署变更对象。用户回复“继续”授权把这里定义的结构先投影为`proposed/in_review/blocked`机器草案，用于确定性校验和逐字段评审；它不表示接受任何评审项。只有配套PENDING清单中的全部必需记录形成可验证结论后，责任人才能决定批准版本及其SemVer；草案存在本身不能成为批准、Ready或实施证据。

## 2. 不变量

本变更集不得改变以下已确认边界：

- `OD-002@1.0.0`继续有效：每集团独立部署，禁止共享SaaS多租户数据平面；
- 当前所有`0.1.0`文件继续保留，不原地改状态、依赖或正文；
- `OD-020`继续负责生产容量、可用性、RPO、RTO和生产拓扑；
- `OD-025`继续负责行业包、技术包、玩具分析化学和跨包业务边界；
- 工程骨架不得实现`src/modules/**`、`src/packs/**`或任何检测业务；
- Release和六张收样任务继续保持`proposed/blocked`，不得因平台链批准而自动放行；
- `generated/spec/**`只由生成器刷新；
- 新依赖仍全部使用`ID@x.y.z`精确版本。

## 3. 版本集合

### 3.1 已创建的未批准平台草案与未来审批目标

| 新版本 | 当前机器草案状态 | 未来审批目标 | 变化类别 | 作用 |
|---|---|---|---|---|
| `ED-001@1.0.0` | `proposed/open` | `approved/decided` | Major | 收窄为工程技术栈、仓库、开发/CI依赖和版本锁 |
| `ED-002@1.0.0` | `proposed/open` | `approved/decided` | 首个正式版本 | 新增通用模块化单体与持久化边界 |
| `SEC-DEPLOY-001@1.0.0` | `in_review` | `approved` | Major | 固定集团间运行、数据、遥测和备份隔离 |
| `SEC-AUD-001@1.0.0` | `in_review` | `approved` | Major | 固定审计意图、失败尝试和敏感信息边界 |
| `NFR-ARCH-001@1.0.0` | `in_review` | `approved` | Major | 改为依赖通用工程边界并启用模块化单体门禁 |
| `NFR-ARCH-002@1.0.0` | `in_review` | `approved` | Major | 固定Outbox/Inbox、并发领取和恢复语义 |
| `AC-DEPLOY-001@1.0.0` | `in_review` | `approved` | Major | 扩充真实交叉凭据、令牌、遥测和恢复测试 |
| `ATC-PLT-000@1.0.0` | `proposed/blocked` | `approved/ready` | Major | 使用收窄依赖形成可实施工程骨架任务 |

未来审批目标只能由完整受控评审证据支持。当前文件只允许使用表中的机器草案状态，不得以`proposed/in_review`文件冒充签署结论，也不得在缺少新SemVer与历史评审时原地提升状态。

### 3.2 计划新增但继续阻塞的发布/业务链

| 新版本 | 目标状态 | 变化类别 | 作用 |
|---|---|---|---|
| `REL-R1-RECEIVING-PILOT@1.0.0` | `proposed` | Major | 选择新版平台链和新版收样任务，同时保留业务/生产门禁 |
| `ATC-REC-001@1.0.0` | `proposed/blocked` | Major | 指向新版Release、平台、技术、安全和NFR版本 |
| `ATC-REC-002@1.0.0` | `proposed/blocked` | Major | 同步新版平台、安全、审计和前置收样版本 |
| `ATC-REC-003@1.0.0` | `proposed/blocked` | Major | 同步新版平台、审计、架构和前置收样版本 |
| `ATC-REC-004@1.0.0` | `proposed/blocked` | Major | 同步新版平台、审计和前置收样版本 |
| `ATC-REC-005@1.0.0` | `proposed/blocked` | Major | 同步新版平台、审计和前置异常版本 |
| `ATC-REC-006@1.0.0` | `proposed/blocked` | Major | 同步新版平台、NFR和三个前置收样版本 |

本节7个版本已按表中状态创建为评审投影；它们仍受原业务Decision、Requirement、Acceptance和生产门禁阻断。

## 4. 精确依赖图

### 4.1 平台批准链

```text
OD-002@1.0.0
├─ ED-002@1.0.0
│  └─ NFR-ARCH-001@1.0.0
│     └─ NFR-ARCH-002@1.0.0
├─ ED-001@1.0.0
│  └─ ED-002@1.0.0
├─ SEC-DEPLOY-001@1.0.0
│  └─ AC-DEPLOY-001@1.0.0
└─ ATC-PLT-000@1.0.0
   ├─ ED-001@1.0.0
   ├─ ED-002@1.0.0
   ├─ OD-002@1.0.0
   ├─ SEC-DEPLOY-001@1.0.0
   ├─ SEC-AUD-001@1.0.0
   ├─ NFR-ARCH-001@1.0.0
   ├─ NFR-ARCH-002@1.0.0
   └─ AC-DEPLOY-001@1.0.0
```

附加精确关系：

- `NFR-ARCH-002@1.0.0`依赖`NFR-ARCH-001@1.0.0`和`SEC-AUD-001@1.0.0`；
- `AC-DEPLOY-001@1.0.0`依赖`OD-002@1.0.0`和`SEC-DEPLOY-001@1.0.0`；
- `SEC-AUD-001@1.0.0`无机器依赖，但需要质量、审计、安全、架构和运维评审证据。

### 4.2 新版平台任务的精确`depends_on`

```json
[
  "ED-001@1.0.0",
  "ED-002@1.0.0",
  "OD-002@1.0.0",
  "SEC-DEPLOY-001@1.0.0",
  "SEC-AUD-001@1.0.0",
  "NFR-ARCH-001@1.0.0",
  "NFR-ARCH-002@1.0.0",
  "AC-DEPLOY-001@1.0.0"
]
```

明确删除：

- `OD-020@0.1.0`直接依赖；
- `OD-025@0.1.0`直接依赖；
- 通过`NFR-ARCH-001`传递回`OD-025`的依赖。

### 4.3 Release依赖替换

`REL-R1-RECEIVING-PILOT@1.0.0.depends_on`必须为：

```json
[
  "ED-001@1.0.0",
  "ED-002@1.0.0",
  "OD-001@0.1.0",
  "OD-002@1.0.0",
  "OD-005@0.1.0",
  "OD-009@0.1.0",
  "ATC-PLT-000@1.0.0",
  "ATC-REC-001@1.0.0",
  "ATC-REC-002@1.0.0",
  "ATC-REC-003@1.0.0",
  "ATC-REC-004@1.0.0",
  "ATC-REC-005@1.0.0",
  "ATC-REC-006@1.0.0"
]
```

`selected_specs`执行以下替换和增加，其他项保持原精确版本：

| 当前 | 新版 |
|---|---|
| `ED-001@0.1.0` | `ED-001@1.0.0` |
| 无 | `ED-002@1.0.0` |
| `SEC-DEPLOY-001@0.1.0` | `SEC-DEPLOY-001@1.0.0` |
| `SEC-AUD-001@0.1.0` | `SEC-AUD-001@1.0.0` |
| `NFR-ARCH-001@0.1.0` | `NFR-ARCH-001@1.0.0` |
| `NFR-ARCH-002@0.1.0` | `NFR-ARCH-002@1.0.0` |
| `AC-DEPLOY-001@0.1.0` | `AC-DEPLOY-001@1.0.0` |
| `ATC-PLT-000@0.1.0` | `ATC-PLT-000@1.0.0` |
| 六张`ATC-REC-*@0.1.0` | 对应`ATC-REC-*@1.0.0` |

`OD-020@0.1.0`和`OD-025@0.1.0`必须继续保留在`selected_specs`，证明它们只是从平台Ready链移走，而不是从Release门禁删除。

### 4.4 六张REC的统一替换规则

每张新版REC必须：

- `target_release`改为`REL-R1-RECEIVING-PILOT@1.0.0`；
- `ATC-PLT-000@0.1.0`改为`ATC-PLT-000@1.0.0`；
- 出现的`ED-001`、`SEC-DEPLOY-001`、`SEC-AUD-001`、`NFR-ARCH-001`、`NFR-ARCH-002`改为对应`1.0.0`；
- 出现的前置`ATC-REC-*`改为对应`1.0.0`；
- 其他业务Decision、Requirement、Rule和Acceptance依赖保持当前精确版本；
- 状态继续`proposed`，`body.readiness`继续`blocked`。

## 5. 字段级变更

### 5.1 `ED-001@1.0.0`

保留：

- .NET、Vue、PostgreSQL模块化单体主方向；
- OIDC/S3公共端口；
- PostgreSQL Outbox/Inbox；
- Linux OCI、GitHub Actions、供应链和版本锁原则；
- 每集团独立数据和遥测边界。

收窄：

- `depends_on`只包含`OD-002@1.0.0`和`ED-002@1.0.0`；
- 草案只覆盖开发、CI和工程验证参考实现候选；
- Keycloak/MinIO只作为非生产参考，不批准生产产品；
- 不批准真实容量、SLA、RPO、RTO、WORM、KMS或生产拓扑；
- 不批准任何行业包、技术包、方法、QC、仪器或报告业务语义。

当前草案必须新增：

- `decision`写明待评审工程候选、适用边界和明确排除；
- `candidate_stack.status=PENDING_REVIEW_FOR_ENGINEERING_SKELETON_ONLY`；
- `evidence_refs`引用本变更集、哈希侧车和PENDING工作清单，`verified_review_record_refs`保持空数组；
- 15项工具和容器锁均保留`exact_value=null`和`PENDING_VERIFICATION`，等待责任人补证；
- 版本升级、退出和长期维护责任。

未来批准后继版本只有在评审和锁值闭合后，才可使用等价于`APPROVED_FOR_ENGINEERING_SKELETON_ONLY`的明确状态；本轮草案不得预填。

### 5.2 `ED-002@1.0.0`

稳定标题：`模块化单体代码与持久化边界基线`。

当前草案固定以下待评审内容：

- Host只组合模块，不拥有业务状态机；
- building-blocks只保存技术原语；
- 每个未来业务模块独立Domain/Application/Infrastructure/Contracts；
- 每模块独立PostgreSQL Schema、`DbContext`和迁移历史；
- 禁止跨模块Infrastructure、EF实体、`DbContext`和私表访问；
- 同步协作只走版本化公共端口；异步协作只走Outbox和版本化事件；
- Pack只规定编译期注册和requirements lock，不选择具体Pack；
- 平台骨架只创建测试夹具模块，生产Host不得引用夹具。

明确排除：

- 真实业务模块清单的最终所有权；
- 玩具、分析化学、物理机械和微生物边界；
- 方法、QC、仪器、报告和生产迁移。

### 5.3 安全、审计、NFR和验收

`SEC-DEPLOY-001@1.0.0`：

- 增加日志、指标、Trace接收/存储/查询/凭据按集团独立；
- 增加跨集团令牌在数据访问前拒绝；
- 保留备份不得跨集团恢复。

`SEC-AUD-001@1.0.0`：

- `activation.applicability=ENABLED`；
- 业务事实、`audit_pending`和Outbox原子提交；
- 被拒绝、无权限和回滚尝试走独立追加路径；
- 中央账本允许幂等最终汇聚；
- 审计路径失败时受控命令保持失败并告警；
- 日志不得冒充审计，敏感正文不得写入。

`NFR-ARCH-001@1.0.0`：

- 依赖`ED-002@1.0.0`；
- `activation.applicability=ENABLED`；
- 目标继续为私表零越权、循环依赖为零。

`NFR-ARCH-002@1.0.0`：

- 依赖`NFR-ARCH-001@1.0.0`和`SEC-AUD-001@1.0.0`；
- `activation.applicability=ENABLED`；
- 增加两个Worker并发领取同一消息时至多一次可见副作用；
- 保留崩溃、重试、原失败证据和Inbox记录。

`AC-DEPLOY-001@1.0.0`：

- 增加数据库、Bucket、IdP、遥测凭据真实交叉访问；
- 增加集团乙令牌访问集团甲Host；
- 增加跨集团备份恢复阻断；
- 隔离测试不能只比较配置字符串。

### 5.4 `ATC-PLT-000@1.0.0`

必须变更：

- 使用4.2节精确依赖；
- 删除`OD-020/OD-025`的直接`source_refs`和前置条件；
- 新增`non_production_verification_envelope`，只覆盖合成依赖、健康、故障、恢复、并发和测试证据；
- 明确非生产包络不证明500订单容量、SLA、RPO、RTO或生产拓扑；
- `status=approved`和`body.readiness=ready`只能在评审记录闭合后写入；
- 当前机器草案必须保持`status=proposed`和`body.readiness=blocked`；
- `allowed_paths`、业务非目标和测试矩阵不放宽。

## 6. 精确版本锁待办

以下锁值在签署前不得为`latest`、范围版本或空值：

| 锁ID | 当前候选系列 | 必须填写 |
|---|---|---|
| `PIN-DOTNET-SDK` | .NET 10 | `global.json`中的完整SDK版本和rollForward策略 |
| `PIN-ASPNET-RUNTIME` | ASP.NET Core 10 | 完整运行时/基础镜像digest |
| `PIN-EFCORE` | EF Core 10 | 完整NuGet版本 |
| `PIN-NPGSQL` | Npgsql 10 | 完整NuGet版本 |
| `PIN-POSTGRES` | PostgreSQL 18 | 完整补丁版本和OCI digest |
| `PIN-NODE` | Node.js 24 LTS | 完整版本和基础镜像digest |
| `PIN-PNPM` | pnpm 10 | 完整版本及Corepack/校验策略 |
| `PIN-VUE` | Vue 3.5 | 完整包版本 |
| `PIN-TYPESCRIPT` | TypeScript 5.9 | 完整包版本 |
| `PIN-VITE` | Vite 7 | 完整包版本 |
| `PIN-ANTDV` | Ant Design Vue 4 | 完整包版本 |
| `PIN-KEYCLOAK-DEV` | Keycloak 26 | 完整开发镜像digest |
| `PIN-MINIO-DEV` | MinIO参考实现 | 完整开发镜像digest与许可核验引用 |
| `PIN-GITHUB-ACTIONS` | GitHub Actions | 每个Action的commit SHA |
| `PIN-SBOM-SCANNERS` | 待选 | 工具版本、规则包版本和阻断阈值 |

所有锁值当前状态均为`PENDING_VERIFICATION`。因此任何责任人可以记录`ACCEPT_WITH_CONDITIONS`，但在锁值补齐前不得把RV-PLT-002或最终平台链判为闭合。

## 7. 评审对象与PENDING清单

正式签署对象为：

- `subject_ref=CHANGE-PLT-NEXT-VERSIONS-001`；
- `subject_hash`取本文件SHA-256侧车；
- `review_item_id`沿用`RV-PLT-001..008`；
- 每个必需角色使用独立记录，不允许一条记录代表多个角色槽；
- 工作清单初始`decision=PENDING`、`record_status=DRAFT`；
- `reviewer_identity_ref`、授权和签名字段为空，等待责任人填写。

## 8. 应用顺序

1. 发起人明确选择RV-PLT-001..008；
2. 工程负责人补齐第6节全部锁值和证据引用；
3. 若正文变化，更新变更集SHA并把全部旧记录作废；
4. 各责任角色填写独立记录；
5. 校验身份、授权范围、时间、签名和对象哈希；
6. 使用当前15个未批准机器草案完成逐字段差异、依赖和生成验证；
7. 所有阻塞意见关闭后，按仓库SemVer与历史规则创建由证据支持的批准后继版本；不得由AI原地把本轮草案改成`approved/ready`；
8. 同步创建相应Release和六张REC的阻塞后继版本，继续保留业务/生产门禁；
9. 运行严格校验、影响、历史、两次生成、check和全部测试；
10. 只有`ready --story ATC-PLT-000@批准后继版本`返回READY，才允许实施工程骨架。

## 9. 失败与回滚

- 任一角色`REJECT`：不创建批准后继版本，保留当前草案和拒绝证据，修订本变更集并重新计算哈希；
- 任一角色`ABSTAIN/PENDING`：批准闭包不成立；
- 任一`ACCEPT_WITH_CONDITIONS`未满足：批准闭包不成立；
- 新规格校验失败：保留失败记录，修正规格源，不手工修改生成目录；
- Ready仍BLOCKED：按输出修复真实依赖，不修改门禁；
- 已创建但未Seal的新版本若需调整：按SemVer再建新版本，不删除历史；
- 已Seal版本：只能追加后继版本和生命周期记录。

## 10. 当前结论

- 本文件尚未获得任何接受结论；
- 当前平台任务仍为`ATC-PLT-000@0.1.0 proposed/blocked`；
- `ED-002@1.0.0`及本文件列出的15个新Major机器草案已经创建，仅用于评审、校验和生成预览；
- 所有新平台对象仍为`proposed/in_review`，所有新Story仍为`proposed/blocked`，不存在新`approved/decided/ready`对象；
- 当前不得实施工程骨架；
- 下一步只允许复核机器草案、填写PENDING评审工作清单和补齐版本锁证据。
