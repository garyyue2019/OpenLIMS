<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-WEB-006@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-WEB-006：实施 Receiving 既有对象续办 Web 入口

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-RECEIVING-CONTINUATION-WEB` |
| 开发就绪度 | `ready` |
| 变更级别 | `minor` |
| 负责人角色 | 收样产品负责人, 身份评估负责人, 质量负责人, EHS负责人, Web应用负责人, QA负责人 |
| 影响模块 | web, receiving, identity-assessment, exception, release, operator-workbench, accessibility-ui, automated-test |
| 来源 | PRD-MAIN#OPS-RECEIPT-003, PRD-MAIN#OPS-IDENTITY-001, PRD-MAIN#OPS-IDENTITY-002, PRD-MAIN#OPS-IDENTITY-003, PRD-MAIN#OPS-EXC-001, PRD-MAIN#OPS-EXC-002, PRD-MAIN#AC-REC-001, PRD-MAIN#AC-ID-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-REC-003@2.0.0, ATC-REC-005@2.0.0, ATC-REC-006@2.0.0, ATC-WEB-005@1.0.0, OD-002@1.0.0, OD-005@1.0.0, OD-035@1.0.0, OPS-RECEIPT-003@1.0.0, OPS-IDENTITY-001@1.0.0, OPS-IDENTITY-002@1.0.0, OPS-IDENTITY-003@1.0.0, OPS-EXC-001@1.0.0, OPS-EXC-002@1.0.0, AC-REC-001@1.0.0, AC-ID-001@1.0.0, SEC-AUTH-001@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `080235ea3564b980b0f4ca720fee9dda155a3aa7c3f897aa164f49c0d00c7088` |

## 业务结果

收样、身份评估、质量和 EHS 人员可在刷新、跨班次或从外部工作清单进入后，使用稳定对象引用重新打开既有 ReceivedItem 的身份、异常和放行操作，不必伪造一次新的收样登记。

## 主要参与者

具有 receiving.identity.evaluate、exception.create/read/quality.approve/ehs.approve 或 receiving.release.approve 中至少一种能力并具有对象范围的操作员

## 触发条件

操作员从 Receiving 续办导航进入，输入或通过稳定深链接携带 receivedItemId、精确对象版本/状态和可选 exceptionId 后打开工作区

## 前置条件

- ATC-REC-003/005/006@2.0.0 已批准、READY 且运行时已交付
- 用户已通过平台 OIDC 登录并从工作清单或已有记录获得稳定 receivedItemId；并发写需要显式精确 itemVersion
- 打开既有异常时另有稳定 exceptionId；页面不得按类型、描述或时间猜测异常

## 正常路径

- 页面校验稳定 receivedItemId、正整数 itemVersion 和批准状态枚举，并把它们写入稳定路由查询参数
- 复用 IdentityAssessmentPanel 加载身份评估并在服务器响应后推进可见对象版本
- 没有 exceptionId 时保留创建新异常能力；提供 exceptionId 时通过既有 GET 接口载入异常并继续受控决定
- 复用 ReceivingReleasePanel，以当前页面固定的对象版本和状态提交放行，成功响应后更新版本和状态
- 刷新深链接后恢复同一对象和可选异常入口，不重新执行收样登记

## 失败路径

- 缺少/空 receivedItemId、非正整数版本或未知状态时不打开工作区且不发送请求
- 无对应能力、跨对象范围或对象不存在时使用现有安全错误，不泄露对象是否存在
- exceptionId 与 receivedItemId 不一致时服务端拒绝；页面不改写返回的对象绑定
- 版本冲突时保留显式版本并提示刷新，不自动改用所谓最新版
- UNKNOWN、持久化或网络失败时保持当前事实和隔离状态，不自动重复写操作

## 领域不变量

- 续办入口只组合已交付的公共 HTTP 契约和现有面板，不访问 Receiving 私表、不新增 API 或业务状态
- receivedItemId、itemVersion、itemState 和 exceptionId 都是显式稳定输入；客户端不推导最新版本或异常
- 页面不提交 organizationGroupId、actorId、批准人、能力或审计身份
- 身份结论和异常决定不解除隔离；只有受控放行成功响应可更新可见 itemState
- 动态深链接和普通导航入口均稳定，刷新不会再次登记或自动执行写操作
- 现有新建收样页面、嵌入面板和事件契约保持兼容

## 数据契约

```json
{
  "continuation": [
    "receivedItemId",
    "itemVersion",
    "itemState",
    "exceptionId?"
  ],
  "exception": [
    "exceptionId",
    "receivedItemId",
    "itemVersion",
    "severity",
    "status",
    "version",
    "decisions"
  ],
  "identity": [
    "assessmentState",
    "assessmentVersion",
    "itemVersion",
    "declarationSnapshot",
    "observations",
    "decisions"
  ],
  "release": [
    "releaseDecisionId",
    "boundItemVersion",
    "itemVersion",
    "state",
    "outcome",
    "constraints"
  ]
}
```

## API / 命令契约

```json
{
  "newBackendOperations": [],
  "operations": [
    "GET /api/v1/received-items/{id}/identity-assessment",
    "POST /api/v1/received-items/{id}/identity-observations",
    "POST /api/v1/received-items/{id}/identity-decisions",
    "POST /api/v1/exceptions",
    "GET /api/v1/exceptions/{id}",
    "POST /api/v1/exceptions/{id}/decisions",
    "POST /api/v1/received-items/{id}/release-decisions"
  ]
}
```

## 状态转换

- 页面关闭 -> 输入稳定引用 -> 工作区打开
- 身份/异常写成功 -> 服务器返回的新 itemVersion 更新页面固定版本
- 受控放行成功 -> QUARANTINED 更新为 ACCEPTED 或 CONDITIONALLY_ACCEPTED
- 刷新 -> 从路由恢复输入并重新读取，绝不自动写

## 权限与职责分离

- 身份评估仍要求 receiving.identity.evaluate
- 异常载入要求 exception.read，创建/决定继续使用既有细分能力
- 放行仍要求 receiving.release.approve，按钮提示不替代服务器授权
- 拥有部分能力的用户只使用对应面板，不因续办入口获得额外权限

## 审计要求

- 续办页本身不写业务审计；所有读取和写入由既有后端记录
- 浏览器不记录令牌、证据正文、可信身份或完整错误请求，只呈现稳定错误码

## UX 状态

- 提供未打开、输入无效、已打开、加载、成功、阻断/隔离、无权限、不可访问、版本冲突和可恢复错误状态
- 入口在新建收样旁独立导航，标题明确为既有实物续办
- 版本和状态输入始终可见，当前固定对象摘要不会被子面板加载前隐藏
- 所有控件支持键盘和窄屏，状态不只依赖颜色

## 可观测性

- 不增加新的业务遥测语义；沿用现有 API correlation 和安全日志
- Web 测试覆盖深链接恢复、精确版本、既有异常载入、权限分离、失败关闭和现有登记回归

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-WEB-006-01 | positive | 稳定 receivedItemId、itemVersion、QUARANTINED | 打开并刷新续办深链接 | 身份、异常、放行面板重新出现；不创建新收样 |
| TC-WEB-006-02 | positive | 稳定 exceptionId 且有 exception.read | 打开续办工作区 | 调用既有异常 GET；展示版本和决定；可继续批准 |
| TC-WEB-006-03 | boundary | 空 ID、版本 0 或未知状态 | 尝试打开 | 本地阻止；不发送请求 |
| TC-WEB-006-04 | permission | 仅有部分 Receiving 能力 | 打开工作区 | 对应操作可用；其他操作禁用；服务端仍为权威 |
| TC-WEB-006-05 | concurrency | 页面固定旧 itemVersion | 提交异常或放行 | 显示冲突；不自动取最新版重试 |
| TC-WEB-006-06 | negative | exceptionId 属于其他 receivedItemId | 载入 | 不显示为当前实物异常；失败关闭 |
| TC-WEB-006-07 | recovery | 读取或写入失败 | 页面保持打开 | 稳定输入保留；无自动重复写入 |
| TC-WEB-006-08 | regression | 现有新建收样页面 | 注册续办路由并复用面板 | 原路由和嵌入流程继续通过；无重复组件实现 |

## 明确非目标

- 不新增按业务号搜索、收样列表、通用 ReceivedItem 查询 API 或后端聚合端点
- 不重做新建收样、Identity、Exception、Release 或 Labeling 页面
- 不自动发现最新对象版本、异常或放行决定
- 不新增数据库迁移、权限、状态机、规则、审计、Seal、发布或治理任务

## 允许修改路径

- `spec/stories/ATC-WEB-006__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-08-05-business-web-workbenches/**`
- `.planning/.active_plan`
- `apps/web/src/**`
- `tests/test_repository_contract.py`

## 验证命令

- `python -m tools.specgen ready --story ATC-WEB-006@1.0.0`
- `pnpm -C apps/web test:unit`
- `pnpm -C apps/web typecheck`
- `pnpm -C apps/web lint`
- `pnpm -C apps/web build`
- `python -m tools.specgen check`

## 完成定义

- ATC-WEB-006@1.0.0 与全部精确依赖均 approved 且 READY 后才修改产品代码
- Receiving feature 注册普通入口和稳定续办深链接且不与现有路由冲突
- 既有 Identity/Exception/Release 面板被复用，异常面板可按稳定 exceptionId 载入
- 页面不推导最新版本或提交可信上下文，身份/异常继续隔离且放行只认服务器响应
- 正反向、边界、权限、并发、恢复、审计安全和回归测试通过
- 前端 test、typecheck、lint、build 与全仓门禁通过，二次 generate written=0
- 所有改动位于本 Story allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
