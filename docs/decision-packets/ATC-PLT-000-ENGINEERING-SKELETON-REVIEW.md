# ATC-PLT-000 工程骨架任务评审说明

## 1. 文件状态

- 状态：`DRAFT / NOT APPROVED / DO NOT IMPLEMENT`
- 机器规格：`ATC-PLT-000@0.1.0`（原始草案）、`ATC-PLT-000@1.0.0`（收窄依赖评审投影）
- 目标发布：对应`REL-R1-RECEIVING-PILOT@0.1.0`与`@1.0.0`
- Epic：`EP-PLATFORM`
- Feature：`FEAT-PLT-ENGINEERING-SKELETON`
- 编制日期：2026-07-23
- 目的：在任何收样或分析化学业务编码前，建立可运行、可测试、可锁版本且不重新引入共享SaaS多租户的工程底座

本说明用于人工联合评审。任务卡由生成器渲染，最终以结构化规格和批准记录为准：

- [原始生成任务卡](../../generated/spec/tasks/ATC-PLT-000__v0.1.0.md)
- [收窄依赖生成任务卡](../../generated/spec/tasks/ATC-PLT-000__v1.0.0.md)
- [联合评审与依赖裁剪审批包](ATC-PLT-000-JOINT-APPROVAL-PACKET.md)
- [技术栈候选ADR](ED-001-TECH-STACK-CANDIDATE.md)
- [Release 1决策冲刺包](REL-R1-DECISION-SPRINT-001.md)

## 2. 为什么必须单独建卡

现有收样任务的 `allowed_paths` 只覆盖业务模块，不能合法创建：

- 解决方案根和版本锁；
- API、Worker和Web Host；
- 公共技术原语与模块契约；
- PostgreSQL、OIDC和S3本地依赖；
- 架构、数据库边界、供应链和恢复测试；
- 应用CI、稳定验证脚本和部署说明。

如果让 `ATC-REC-001` 顺手完成这些工作，会导致任务范围失控、技术决策混入业务实现、所有后续任务各自搭建不同底座。因此工程骨架必须成为全部业务Story的显式前置依赖。

## 3. 本卡交付结果

```text
OpenLIMS.slnx
global.json
Directory.Build.props
Directory.Packages.props
package.json / pnpm-workspace.yaml / pnpm-lock.yaml

src/
  host/api/
  host/worker/
  building-blocks/
contracts/platform/
apps/web/

tests/
  architecture/
  integration/platform/
  contract/platform/
  e2e/smoke/

deploy/compose/
deploy/config/
scripts/verify.ps1
scripts/verify.sh
.github/workflows/application-ci.yml
docs/engineering/
```

骨架完成后应能从空环境完成锁定依赖恢复、构建、启动、健康探测、测试、停止和清理，但不包含任何检测业务页面、API、状态机或数据库表。

## 4. 明确禁止进入本卡

- `src/modules/**` 和 `src/packs/**` 生产实现；
- 收样、身份、隔离、分析化学、QC、报告和计费业务；
- 真实仪器、ERP、生产IdP或生产对象存储接入；
- 真实客户数据、生产Secret和生产迁移；
- Kubernetes、Kafka/RabbitMQ、Redis、OpenSearch、向量数据库和独立Python AI服务；
- 对 `spec/**`、`generated/spec/**`、PRD、Seal、历史迁移或验收证据的实施侧修改。

模块和数据库边界只能用 `tests/**` 下的夹具模块证明，夹具不得被生产Host引用。

## 5. 关键工程契约

### 5.1 单集团独立部署

- 一个运行部署只绑定一个 `OrganizationGroup`；
- 客户端不能通过Body、Header、Query或前端状态选择集团；
- 不同集团不得共享数据库、Bucket、IdP实例、凭据、密钥、缓存、索引、日志/指标/Trace存储或备份；
- 同一不可变构建镜像可以复用，但运行和数据平面必须独立。
- 隔离测试必须真实尝试交叉数据库/Bucket凭据和跨集团令牌访问；只比较配置字符串不能作为隔离证据。

### 5.2 模块边界

- 未来每个业务模块独立Schema、`DbContext`、迁移历史、`audit_pending`和Outbox；
- 模块不得引用其他模块Infrastructure、EF实体、`DbContext`或私表；
- 跨模块同步只允许版本化公共端口，异步只允许版本化事件；
- Host和building-blocks不得拥有业务状态机；
- 行业包和技术包只能依赖公共Contracts，并由requirements lock固定版本。

### 5.3 审计和Outbox

- 本卡只定义端口、事务约束和测试工具；
- 测试夹具证明业务事实、审计意图和Outbox同事务提交；
- Inbox必须处理重复、崩溃和重启；
- 中央审计不可用不能丢失审计意图；
- 运行日志不得冒充审计账本。

### 5.4 应用启动与迁移

- 验证和生产环境存在待执行迁移时，应用不得自动修改Schema；
- 迁移只能通过独立、可审计的受控步骤执行；
- 必要依赖不可用时readiness失败关闭，不能使用开发默认值继续运行。

## 6. 必须通过的测试族

| 测试族 | 证明内容 |
|---|---|
| 确定性构建 | 锁文件、版本和连续构建不漂移 |
| Host烟雾 | API、Worker、Web可启动且无业务能力 |
| 架构边界 | 跨Infrastructure、DbContext、EF实体和循环依赖失败 |
| 数据库边界 | 模块角色不能直接访问其他Schema私表 |
| 集团上下文 | 客户端集团选择以稳定错误拒绝；数据库/Bucket/遥测交叉凭据、跨集团令牌和跨集团备份恢复均失败 |
| 事务与审计 | AuditIntent或Outbox失败导致整个事务回滚 |
| Inbox幂等 | 重复投递和Worker崩溃不会产生重复副作用 |
| 并发争抢 | 两个Worker同时领取相同消息时只有一个有效处理者和一份可见副作用 |
| 权限 | 匿名和无运维权限调用者不能读取依赖详情、配置或Secret |
| 审计 | AuditIntent字段完整、失败与重试证据只追加，运行日志不能替代审计账本 |
| 反向输入 | 非法关联ID、客户端集团字段、未知/缺失配置失败关闭且无副作用 |
| 边界 | 关联ID和依赖探测超时在阈值内外均有确定行为，不沿用过期就绪状态 |
| 依赖恢复 | PostgreSQL、IdP、S3中断后失败关闭并完整恢复 |
| 迁移 | 应用启动不自动迁移验证/生产数据库 |
| 供应链 | 锁依赖、SAST/SCA、Secret、SBOM和镜像扫描 |
| 跨平台脚本 | Windows/Linux执行相同门禁并正确传播退出码 |
| 范围反向测试 | 不存在业务路由、模块、迁移、导航和OpenAPI operation |
| 空环境复现 | 仅凭仓库、工具链和合成配置重建验证环境 |

## 7. Ready前必须批准

复核发现当前`0.1.0`把工程骨架与`OD-020/OD-025`中的生产容量、真实业务包和方法/QC/仪器证据耦合过紧，因此不得直接把下面的旧依赖清单整体标成批准。应先按[联合评审与依赖裁剪审批包](ATC-PLT-000-JOINT-APPROVAL-PACKET.md)评审`CHANGE-PLT-DEPENDENCY-SCOPE-001`。

15个`1.0.0`未批准机器草案现已存在，供责任人直接检查字段、依赖和生成结果；它们不是批准证据。推荐的后续评审顺序：

1. 先评审依赖裁剪原则，确认`OD-020/OD-025`继续阻断Release和生产，但不进入纯工程空壳的直接或传递依赖；
2. 复核`ED-001@1.0.0`是否只覆盖技术栈、仓库、开发/CI依赖和验证命令，并补齐15项精确版本锁；
3. 复核`ED-002@1.0.0`是否只冻结模块、Schema、`DbContext`、迁移、端口和测试夹具规则；
4. 复核`NFR-ARCH-001/002@1.0.0`只依赖通用工程边界和审计语义，而不是完整业务Pack决策；
5. 复核`SEC-DEPLOY-001@1.0.0` / `AC-DEPLOY-001@1.0.0`的单集团部署隔离；
6. 复核`SEC-AUD-001@1.0.0` / `NFR-ARCH-002@1.0.0`的审计意图、失败尝试、Outbox/Inbox和一致性语义；
7. 架构、安全、运维、工程、质量、审计和QA按责任域提交受控评审记录；
8. 使用新的SemVer规格版本表达结论，不得原地把本草案伪装成Ready。

责任人每次更新评审清单或版本锁后，应运行：

```powershell
python -m tools.specgen review-status `
  --change-set CHANGE-PLT-NEXT-VERSIONS-001
```

当前预期为`REVIEW BLOCKED / exit 4`，并准确报告33个PENDING角色槽和15个未核验技术锁。未来即使返回`REVIEW EVIDENCE READY`，也只表示输入完整，不代表机器规格已批准或工程骨架可以实施；仍须按SemVer创建受控后继版本并重新执行Story Ready门禁。

## 8. 实施完成后的业务任务顺序

```text
ATC-PLT-000 工程骨架
→ ATC-REC-001 到货/包装/实物登记
→ ATC-REC-002 条码
→ ATC-REC-003 隔离门禁
→ ATC-REC-004 身份与异常
→ ATC-REC-005 条件接收/拒收
→ ATC-REC-006 放行闭环
→ 玩具分析化学取样、制备批、分析批、QC、仪器和报告Story
```

六张`ATC-REC-* @0.1.0`必须精确依赖`ATC-PLT-000@0.1.0`，六张`@1.0.0`必须精确依赖`ATC-PLT-000@1.0.0`；任何版本都不能通过复制脚手架绕开。

## 9. 评审结论记录

正式记录请使用[受控评审记录模板](templates/atc-plt-000-review-record.csv)。下表只作为阅读提示，不构成机器可验证批准证据。

| 角色 | 受控身份引用 | 意见 | 状态 | 证据引用 |
|---|---|---|---|---|
| 架构负责人 | 待登记 |  | 待评审 |  |
| 工程负责人 | 待登记 |  | 待评审 |  |
| 安全负责人 | 待登记 |  | 待评审 |  |
| 运维负责人 | 待登记 |  | 待评审 |  |
| QA负责人 | 待登记 |  | 待评审 |  |

本表为空时，任务必须保持 `proposed/blocked`。
