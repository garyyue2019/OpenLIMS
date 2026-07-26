<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-PLT-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-PLT-001：实施 DEV-018 请求上下文与对象级授权正式化

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-PLATFORM` |
| Feature | `FEAT-PLT-REQUEST-CONTEXT` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 技术负责人, 安全负责人, 质量负责人, QA负责人 |
| 影响模块 | platform, authorization, request-context, correlation, cross-module, audit, automated-test |
| 来源 | PRD-MAIN#SEC-AUTH-001, PRD-MAIN#SEC-AUD-001, PRD-MAIN#AC-SEC-001 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, BUS-PLT-002@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, AC-SEC-001@1.0.0, NFR-ARCH-001@2.0.0, ATC-PLT-002@1.0.0 |
| 规格指纹 | `013e485b4c1285652c986b4e460e2b8b9991c15fafa32e438276000d62cefa5e` |

## 业务结果

对象级授权与跨组织隔离从各模块分散测试升级为平台级组合证据：能力拒绝在真实链路中失败关闭、跨组织探测无法区分'不存在'与'无权访问'、correlation 全链可追，为审计与安全评审提供单一验证入口。

## 主要参与者

平台安全评审者与 QA（E2E 中的固定测试身份：授权行为人、越权行为人、跨组织行为人）

## 触发条件

QA 在专用数据库上执行请求上下文与对象级授权组合验证

## 前置条件

- ATC-PLT-002 的全链 E2E 基础设施（openlims_chain_test、单容器全模块装配）已交付
- 各模块授权端口与组织分区加载已在既有卡中实现

## 正常路径

- 授权行为人在部署组织内执行链路命令成功，platform.audit_intent 逐行固定 actor、组织与调用方 correlation
- 组织上下文由容器部署配置提供，测试证明请求载荷中不存在可覆盖组织的输入面
- 能力拒绝（授权端口 Deny）时命令失败关闭：无业务事实、无平台审计意图/发件箱泄漏，仅模块 audit_attempt 留痕且 correlation 原样
- 跨组织行为人读取他组对象得到 OBJECT_NOT_ACCESSIBLE，与读取不存在对象的错误不可区分
- 行为人组织与部署组织不一致时命令直接 NOT_AUTHORIZED 失败关闭

## 失败路径

- 能力拒绝 → NOT_AUTHORIZED，业务事实为零，audit_attempt 恰记一次失败
- 跨组织读取 → OBJECT_NOT_ACCESSIBLE（不泄露存在性）
- 行为人缺失或组织不匹配 → NOT_AUTHORIZED
- 以上任何失败均不产生 platform.audit_intent 成功记录或 outbox 事件

## 领域不变量

- 集团上下文部署绑定且客户端不可指定（SEC-AUTH-001）
- 对象级授权服务端强制，拒绝失败关闭
- 跨组织不泄露对象存在性（AC-SEC-001）
- correlation 原样贯穿 platform.audit_intent 与模块 audit_attempt
- 本卡零产品代码变更——仅规格、E2E 测试与文档
- 不修改任何既有规格，不触碰未决 OD

## 数据契约

```json
{
  "auditEvidence": [
    "platform.audit_intent(actor_id, organization_group_id, correlation_id)",
    "<module>.audit_attempt(actor_id, organization_group_id, correlation_id, outcome)"
  ],
  "authorizationRequest": [
    "organizationGroupId",
    "actorId",
    "objectScope",
    "capability"
  ],
  "requestContext": [
    "organizationGroupId（部署配置）",
    "actorId + actorOrganizationGroupId（受信身份）",
    "correlationId（调用方提供或端口生成）"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "<MOD>.NOT_AUTHORIZED",
    "<MOD>.OBJECT_NOT_ACCESSIBLE"
  ],
  "operations": [],
  "publicPort": "无新增端口——验证既有模块授权端口与服务的组合行为"
}
```

## 状态转换

- 无新增状态机——授权决策为无状态服务端校验

## 权限与职责分离

- 不新增能力或 claim；E2E 以 Deny/跨组织固定桩注入既有授权端口接口验证失败关闭

## 审计要求

- 授权拒绝仅在模块 audit_attempt 留痕；成功命令的 actor/组织/correlation 在 platform.audit_intent 固定

## UX 状态

- 本卡不新增前端页面
- 无客户端交互面——交付物为规格与测试

## 可观测性

- 无新增指标；组合回归由 FullyQualifiedName~Platform 过滤器纳入 CI

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-PLT-001-01 | positive | 授权行为人与部署组织一致 | 执行范围+数量命令并携带调用方 correlation | platform.audit_intent 行固定 actor、组织与原样 correlation |
| TC-PLT-001-02 | negative | 范围授权端口 Deny，其余装配真实 | 创建范围矩阵版本 | NOT_AUTHORIZED；范围事实为零；scope.audit_attempt 恰一次且 correlation 原样；无新增平台审计/发件箱 |
| TC-PLT-001-03 | negative | 组织甲已有范围矩阵；跨组织行为人（组织乙容器） | 读取组织甲对象与读取不存在对象 | 两者均 OBJECT_NOT_ACCESSIBLE，不可区分 |
| TC-PLT-001-04 | negative | 行为人组织与部署组织不一致 | 执行任一模块命令 | NOT_AUTHORIZED；无业务事实 |

## 明确非目标

- 不实现新的授权语义、角色模型或金额/有效期维度（SEC-AUTH-001 其余维度由各业务卡渐进落地）
- 不改任何模块或平台产品代码
- 不新增 HTTP 层测试宿主（HTTP claims 路径已由各模块与 CI 冒烟覆盖）
- 不触碰未决 OD，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-PLT-002__v1.0.0.json`
- `spec/stories/ATC-PLT-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-018-platform-request-context/**`
- `tests/e2e/chain/**`
- `tests/test_repository_contract.py`
- `docs/domain/platform/**`

## 验证命令

- `python -m tools.specgen ready --story ATC-PLT-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module platform`
- `python -m tools.specgen check`

## 完成定义

- 四个测试用例在真实端口组合下通过
- 零产品代码变更（git 差异仅规格/测试/文档/规划）
- 全部测试项目保持绿色
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
