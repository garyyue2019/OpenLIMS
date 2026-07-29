<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-WEB-002@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-WEB-002：实施 Instrument、Result、QC 与 Report 实验室工作台

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-LAB-WORKBENCH-SECOND-FLOW` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室运营负责人, 实验室技术负责人, 质量负责人, 授权签字人, Web应用负责人, QA负责人 |
| 影响模块 | web, instrument, result, qc, report, operator-workbench, accessibility, automated-test |
| 来源 | PRD-MAIN#INT-INST-001, PRD-MAIN#INT-INST-002, PRD-MAIN#INT-DATA-001, PRD-MAIN#LAB-RAW-001, PRD-MAIN#LAB-RAW-002, PRD-MAIN#LAB-PROV-001, PRD-MAIN#LAB-PROV-002, PRD-MAIN#LAB-RES-001, PRD-MAIN#LAB-RES-002, PRD-MAIN#LAB-RES-003, PRD-MAIN#LAB-RES-004, PRD-MAIN#AC-RETEST-001, PRD-MAIN#LAB-QC-001, PRD-MAIN#LAB-QC-002, PRD-MAIN#LAB-QC-003, PRD-MAIN#AC-QC-001, PRD-MAIN#RPT-TRACE-001, PRD-MAIN#RPT-GATE-001, PRD-MAIN#RPT-GATE-002, PRD-MAIN#RPT-SIGN-001, PRD-MAIN#RPT-VERS-001, PRD-MAIN#RPT-VERS-002, PRD-MAIN#RPT-VERS-003, PRD-MAIN#RPT-VERS-004, PRD-MAIN#AC-RPT-001, PRD-MAIN#AC-RPT-002 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-INST-001@1.0.0, ATC-RESULT-001@1.0.0, ATC-QC-001@1.0.0, ATC-RPT-001@1.0.0, ATC-RPT-002@1.0.0, OD-002@1.0.0, AC-RETEST-001@1.0.0, AC-QC-001@1.0.0, AC-RPT-001@1.0.0, AC-RPT-002@1.0.0, SEC-AUTH-001@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `0959d0fd44400fbafe0d7227dd301a42792b5f4232f8ec0d1bef40b43cd1fc77` |

## 业务结果

实验室操作员可以在同一套经过身份认证、键盘可操作且错误可恢复的 Web 工作台中，从批次后的仪器证据开始，完成结果来源和采用、QC 判定与五门放行、报告组装门禁以及受控签发和版本验证，不再依赖直接调用 API 或读取数据库。

## 主要参与者

具有相应对象范围和 instrument.import、result.record、qc.manage、report.manage 能力的仪器导入操作员、结果录入人、质量负责人、技术负责人和授权签字人

## 触发条件

已登录操作员从实验室工作台导航进入 Instrument、Result、QC 或 Report 页面，创建、追加、决定、签发或查询现有对象

## 前置条件

- ATC-INST-001@1.0.0、ATC-RESULT-001@1.0.0、ATC-QC-001@1.0.0、ATC-RPT-001@1.0.0 与 ATC-RPT-002@1.0.0 已批准、READY 且运行时已交付
- 用户已通过平台 OIDC 登录，API 基址来自受保护的运行配置
- UI 只提交业务字段、稳定 ID、精确版本、规则集引用、证据引用和 expectedCurrentVersion，不提交可信组织、行为人、权限或审计身份

## 正常路径

- 登记仪器文件及其稳定外部引用、哈希、来源和解析器版本，追加解析行，处理异常并查看导入状态
- 从允许的 Batch 创建结果组，追加原始观察和推导关系，在重测前记录采用规则并采用精确结果版本
- 创建 QC run，追加规则结果和 verdict，记录完整影响范围、偏差批准与五个放行门，全部满足后解除阻断
- 创建报告草稿并追加采用结果行，评估签发门禁并提交审批，读取待签哈希后携带重认证证据与签署意图签发
- 对已签发报告执行批准的受控动作，读取验证页和指定历史版本；所有成功只由服务器响应驱动

## 失败路径

- 未登录或会话过期 → 返回登录入口并保留安全 return URL，不发送业务请求
- 服务器返回 401/403 → 显示无权操作且不泄露对象存在性，不把本地表单标记为成功
- 服务器返回 RFC 9457 Problem Details → 显示稳定 errorCode、correlationId、说明和安全下一步
- 版本冲突、规则集未知、证据不完整、QC 门未满足或报告哈希变化 → 保留服务器事实并阻止不允许的后续动作
- 网络或 5xx 失败 → 保留非敏感表单输入，允许用户显式重试且不自动重复写操作
- 输入缺失、JSON 不是对象/数组、数值非正或版本不是整数 → 客户端立即提示；服务器仍为最终校验权威

## 领域不变量

- Web 端不得推导最新版本、伪造成功、自动采用结果、自动解除 QC 阻断或把 UNKNOWN 映射为允许
- 所有跨步骤引用均携带稳定 ID 与精确正整数版本；零版本只在批准契约明确允许的首次并发边界中提交
- 可信组织、行为人、权限、签字人和审计身份只由服务器上下文提供；客户端只提交业务目标和证据引用
- 写操作只有收到成功响应后才更新工作台结果；失败不清空可安全重试的非敏感输入
- 报告签发必须先读取服务器待签哈希并显式提交重认证证据和签署意图；客户端不计算权威签名状态
- UI 不读取任何模块私表，不引入跨模块后端依赖，只消费现有公共 HTTP 契约
- Receiving 与第一批 Scope、Quantity、Allocation、Batch 页面和路由保持兼容

## 数据契约

```json
{
  "instrument": [
    "fileId",
    "version",
    "externalRef@version",
    "sha256",
    "sourceSystem",
    "instrumentRef@version",
    "parserVersion",
    "rows",
    "exceptions",
    "importStatus",
    "reasonCodes"
  ],
  "problem": [
    "title",
    "detail",
    "status",
    "errorCode",
    "correlationId",
    "nextAction"
  ],
  "qc": [
    "qcRunId",
    "version",
    "batchRef@version",
    "methodRef@version",
    "ruleSetVersion",
    "results",
    "verdict",
    "impact",
    "deviationApproval",
    "gates",
    "release",
    "reportability"
  ],
  "report": [
    "reportId",
    "version",
    "reportNumber",
    "lines",
    "gateEvaluation",
    "issuanceGate",
    "pendingContentHash",
    "signature",
    "controlledActions",
    "verification",
    "versionSnapshot"
  ],
  "result": [
    "resultGroupId",
    "version",
    "batchRef@version",
    "observations",
    "derivations",
    "adoptionRule",
    "adoptions",
    "adoptionStatus",
    "reasonCodes"
  ]
}
```

## API / 命令契约

```json
{
  "instrument": [
    "POST /api/v1/instrument-files",
    "POST /api/v1/instrument-files/{id}/rows",
    "POST /api/v1/instrument-files/{id}/exceptions/{exceptionId}/resolution",
    "GET /api/v1/instrument-files/{id}",
    "GET /api/v1/instrument-files/{id}/import-status"
  ],
  "qc": [
    "POST /api/v1/qc-runs",
    "POST /api/v1/qc-runs/{id}/results",
    "POST /api/v1/qc-runs/{id}/verdict",
    "POST /api/v1/qc-runs/{id}/impact",
    "POST /api/v1/qc-runs/{id}/deviation-approval",
    "POST /api/v1/qc-runs/{id}/gates",
    "POST /api/v1/qc-runs/{id}/release",
    "GET /api/v1/qc-runs/{id}",
    "GET /api/v1/qc-runs/{id}/reportability"
  ],
  "report": [
    "POST /api/v1/reports",
    "POST /api/v1/reports/{id}/lines",
    "POST /api/v1/reports/{id}/gate-evaluation",
    "POST /api/v1/reports/{id}/submit-for-approval",
    "GET /api/v1/reports/{id}",
    "GET /api/v1/reports/{id}/issuance-gate",
    "GET /api/v1/reports/{id}/pending-content-hash",
    "POST /api/v1/reports/{id}/issuance",
    "POST /api/v1/reports/{id}/controlled-actions",
    "GET /api/v1/reports/{id}/verification",
    "GET /api/v1/reports/{id}/versions/{versionNumber}"
  ],
  "result": [
    "POST /api/v1/result-groups",
    "POST /api/v1/result-groups/{id}/observations",
    "POST /api/v1/result-groups/{id}/derivations",
    "POST /api/v1/result-groups/{id}/adoption-rule",
    "POST /api/v1/result-groups/{id}/adoptions",
    "GET /api/v1/result-groups/{id}",
    "GET /api/v1/result-groups/{id}/adoption-status"
  ]
}
```

## 状态转换

- UI 仅展示并驱动四个既有模块的批准状态机，不新增或重解释任何状态转换
- 本地 loading/submitting/success/error 是交互状态，不是业务事实；刷新后始终从服务器重建
- Instrument 异常未解决、Result 未采用、QC BLOCKED/UNKNOWN、Report 门禁未通过或哈希不匹配时，后续动作按服务器事实禁用并显示原因

## 权限与职责分离

- 所有路由要求已认证会话；匿名用户被引导至登录
- 按钮级 capability 提示只改善体验，服务器 401/403 始终为最终权威
- 报告重认证证据是业务签发输入而不是客户端授权声明，签字人身份仍由服务器上下文决定
- 无权读取与对象不存在使用同一安全错误呈现，不泄露跨组织对象存在性

## 审计要求

- UI 不直接写审计；每个写请求由对应后端模块在同事务内记录审计和 Outbox
- 错误视图显示服务器 correlationId 便于支持与审计定位
- 浏览器日志和错误呈现不得包含令牌、Secret、原始仪器文件内容、完整客户文档或不必要个人数据

## UX 状态

- 每个模块提供 loading、empty、ready、submitting、success、blocked/unknown、forbidden、not-found 和 retryable-error 状态
- 表单字段具有可见标签、关联错误、键盘提交和焦点可见状态；深层契约输入使用带示例和错误定位的结构化 JSON 编辑器
- 导航明确显示 Instrument、Result、QC、Report，并支持稳定 URL 深链接
- 对象详情显示 ID、精确版本、规则集、证据、状态、原因码和允许的下一步，不只使用颜色表达
- 窄屏下表单和详情纵向排列，关键动作不依赖悬停

## 可观测性

- 客户端错误记录 operationId、HTTP 状态、errorCode 和 correlationId，不记录令牌或请求正文
- 页面可见 correlationId 与 API Problem Details 一致
- 前端构建和测试覆盖四个工作台描述符、路由、客户端契约和核心交互

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-WEB-002-01 | positive | 已认证且服务器依次返回 Instrument、Result、QC、Report 成功响应 | 操作员完成导入、采用、放行和签发并重新读取对象 | 导航和四个页面可用；精确版本和证据传递到下一步；成功只由响应驱动 |
| TC-WEB-002-02 | negative | 导入异常、结果未采用、QC 未释放或报告门禁未满足 | 操作员尝试后续动作 | 显示服务器阻断原因；不伪造后续成功；输入和查询结果保持可恢复 |
| TC-WEB-002-03 | boundary | 版本为小数/负数、必需数量非正或结构化输入不是期望对象/数组 | 操作员提交 | 客户端阻止明显无效输入；不发送请求 |
| TC-WEB-002-04 | permission | 会话匿名、缺 UX capability 或 API 返回 403 | 进入路由或执行操作 | 引导登录或显示只读/无权；服务端拒绝不被覆盖；不泄露对象存在性 |
| TC-WEB-002-05 | recovery | 首次网络请求失败 | 用户确认后重试 | 不自动重复写操作；非敏感表单内容保留；成功响应后才更新详情 |
| TC-WEB-002-06 | audit | API 返回稳定 errorCode 和 correlationId | 错误面板呈现失败 | 显示可支持关联信息；不显示令牌、可信身份或原始文件内容 |
| TC-WEB-002-07 | regression | 现有 Receiving 和第一批实验室工作台路由与导航 | 注册第二批工作台 | 所有现有路由和测试保持通过；无重复 route/navigation ID |

## 明确非目标

- 不修改 Instrument、Result、QC、Report 的后端契约、数据库、状态机、权限或业务默认值
- 不实现 Receiving 扩展、Billing、Toy、Textile、Labeling 或 AI 页面；它们属于后续功能批次
- 不上传或解析二进制仪器文件；本卡只消费已批准的外部引用、哈希和解析行 HTTP 契约
- 不生成或渲染报告 PDF，不接外部签章、CA、交付渠道或公众验证页
- 不新增运行时 feature discovery、前端本地业务事实库或离线写队列
- 不创建 OD、ADR、Seal、Release、tag、部署或生产迁移

## 允许修改路径

- `spec/stories/ATC-WEB-002__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-29-lab-workbench-second-flow/**`
- `.planning/.active_plan`
- `apps/web/src/**`
- `tests/test_repository_contract.py`

## 验证命令

- `python -m tools.specgen ready --story ATC-WEB-002@1.0.0`
- `pnpm -C apps/web test:unit`
- `pnpm -C apps/web typecheck`
- `pnpm -C apps/web lint`
- `pnpm -C apps/web build`
- `python -m tools.specgen check`

## 完成定义

- ATC-WEB-002@1.0.0 与全部精确依赖均为 approved 且 READY 后才修改产品代码
- 四个工作台在 Web registry 显式注册，路由和导航稳定且不与现有功能冲突
- 四模块全部批准 HTTP 操作、错误、边界、权限、恢复和回归状态具有自动测试
- UI 只消费现有 API，不提交可信身份、伪造签字或新增后端业务语义
- 前端 test、typecheck、lint、build 与全仓门禁通过，二次 generate written=0
- 所有改动位于本 Story allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
