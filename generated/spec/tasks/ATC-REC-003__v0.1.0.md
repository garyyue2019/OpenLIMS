<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-003@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-003：身份评估前实施统一隔离门禁

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@0.1.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-QUARANTINE` |
| 开发就绪度 | `blocked` |
| 变更级别 | `major` |
| 负责人角色 | 收样产品负责人, 质量负责人, 相关下游模块负责人, QA负责人 |
| 影响模块 | receiving, disassembly, sample-preparation, task-allocation, authorization, audit, automated-test |
| 来源 | PRD-MAIN#OPS-RECEIPT-003, PRD-MAIN#OD-002, PRD-MAIN#ORG-STRUCT-001, PRD-MAIN#OPS-IDENTITY-003, PRD-MAIN#AC-REC-001, PRD-MAIN#SEC-AUD-001 |
| 固定依赖 | ATC-REC-001@0.1.0, OD-002@1.0.0, ORG-STRUCT-001@0.1.0, OPS-RECEIPT-003@0.1.0, OPS-IDENTITY-003@0.1.0, AC-REC-001@0.1.0, SEC-AUTH-001@0.1.0, SEC-AUD-001@0.1.0, OD-005@0.1.0, NFR-ARCH-001@0.1.0 |
| 规格指纹 | `3102bb7bcc5122e838285d269f059e7bc831d355bb29967fe9f7ed4453b372dd` |

## 业务结果

身份未确认、被隔离、待定、拒收或安全封存的实物不能因页面遗漏、接口调用或并发竞争进入实验室执行。

## 主要参与者

拆解员、制样员、检测计划员及调用相应命令的集成身份

## 触发条件

任一用户或接口尝试以 ReceivedItem 或其派生上下文创建拆解、制样或 TestObjectAllocation

## 前置条件

- ATC-REC-001 已交付并能查询版本化 ReceivedItem 状态
- OD-005 已批准状态与允许动作矩阵，包括条件接收的精确语义
- receiving 模块发布只读公共端口或版本化事件投影
- 拆解、制样和任务分配命令均已识别统一门禁接入点

## 正常路径

- 下游命令先执行自身权限校验
- 通过 receiving 公共端口在服务端绑定的集团上下文中查询对象的法人、实验室、客户、状态、身份决定、限制和期望版本
- 按 OD-005 固定版本矩阵计算 CanEnterLabExecution
- 只有明确 ALLOWED 才继续下游事务
- 门禁判断、规则版本和对象版本进入下游证据
- 成功后由下游模块写自身审计，不由 receiving 直接写下游私表

## 失败路径

- REGISTERED、QUARANTINED、IDENTITY_ASSESSING、AWAITING_CUSTOMER、REJECTED、SAFETY_HOLD 一律拒绝
- 状态或适用性未知时按 BLOCK 处理，不使用默认允许
- 对象版本在门禁后到提交前变化时通过乐观并发或条件写入失败
- 对象不存在，或调用方无权访问对应法人、实验室、客户或对象时按安全策略拒绝且不泄露差异
- receiving 公共端口不可用时失败关闭，不缓存为永久允许
- 拒绝不创建拆解、试样、数量流水、分配或发件箱业务事件

## 领域不变量

- 统一策略返回 ALLOWED/BLOCKED/UNKNOWN，UNKNOWN 等同 BLOCKED
- 下游模块不得读取 receiving 私有表
- 门禁基于固定规则版本和对象版本，不读取 latest 配置
- 拒绝路径无业务副作用但有追加式阻断审计
- 同一对象的三个入口必须共享契约测试

## 数据契约

```json
{
  "decisionEnum": [
    "ALLOWED",
    "BLOCKED",
    "UNKNOWN"
  ],
  "input": [
    "laboratoryId",
    "receivedItemId",
    "requestedAction",
    "expectedItemVersion",
    "ruleSetVersion"
  ],
  "output": [
    "decision",
    "currentState",
    "identityDecisionId",
    "constraints",
    "evaluatedRuleIds",
    "itemVersion",
    "decisionVersion"
  ],
  "query": "GetLabExecutionEligibility",
  "serverContext": [
    "organizationGroupId",
    "authenticatedSubjectId",
    "grantedOrganizationScopes"
  ]
}
```

## API / 命令契约

```json
{
  "blockedError": "RECEIVED_ITEM_NOT_RELEASED",
  "errorFields": [
    "code",
    "objectId",
    "currentState",
    "blockedAction",
    "ruleIds",
    "correlationId"
  ],
  "protectedCommands": [
    "CreateDisassemblyPlan",
    "CreatePreparation",
    "AllocateTestObject"
  ],
  "publicPort": "ReceivingEligibilityPort@v1",
  "retryableErrors": [
    "RECEIVING_PORT_UNAVAILABLE",
    "EXPECTED_VERSION_CONFLICT"
  ]
}
```

## 状态转换

- 门禁查询本身不改变 ReceivedItem 状态
- 被阻断的下游命令不得改变任何业务状态
- 并发版本冲突要求调用方重新读取状态并重新评估，不得盲重试提交

## 权限与职责分离

- 下游命令权限与 receiving 对象访问权限分别校验
- eligibility 端口只返回完成门禁所需最小数据
- 服务身份必须由部署绑定集团，并固定法人/实验室范围、用途和有效期
- 审计员只读，不获得执行资格

## 审计要求

- 阻断事件类型 LAB_EXECUTION_ENTRY_BLOCKED
- 记录对象、用户或服务身份、动作、当前状态、规则版本、规则ID、原因、期望/实际版本和 correlationId
- 重复相同请求可在指标层聚合，但每次受控操作尝试按政策保留
- 不得记录未脱敏客户正文或访问令牌

## UX 状态

- 隔离对象详情显示不可进入执行的明确原因和责任方
- 下游创建页在预检查时禁用动作，但提交时仍由服务端再次校验
- 并发冲突提示刷新状态，不将其显示为普通字段校验
- UNKNOWN 显示为系统或配置阻断，不能显示为已允许
- 只有具备对应权限者可看到处理入口

## 可观测性

- 指标 lab_execution_gate_total 按动作、决策和状态聚合
- 指标 eligibility_port_duration_seconds 和 availability
- UNKNOWN 决策与端口不可用分别告警
- 关联日志可从下游命令追溯至门禁查询和审计事件

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-003-01 | negative | ReceivedItem状态为QUARANTINED | 分别尝试拆解、制样和检测分配 | 三个命令均返回RECEIVED_ITEM_NOT_RELEASED；没有业务副作用；各自记录阻断审计 |
| TC-REC-003-02 | boundary | 规则不认识新的状态值或适用性为UNKNOWN | 请求进入执行 | 决策为UNKNOWN并按阻断处理；产生配置告警 |
| TC-REC-003-03 | positive | 对象处于OD-005明确允许状态；身份决定和限制满足 | 创建下游命令 | 门禁返回ALLOWED；下游可继续自身事务；证据固定规则和对象版本 |
| TC-REC-003-04 | concurrency | 预检查时对象允许；提交前对象被安全封存 | 使用旧expectedItemVersion提交 | 条件写入失败；不创建下游对象；调用方必须重新评估 |
| TC-REC-003-05 | security | 调用方无实物所属法人、实验室或客户授权 | 请求资格并提交下游命令 | 统一拒绝；不泄露对象状态；记录安全审计 |
| TC-REC-003-06 | recovery | receiving eligibility端口暂时不可用 | 创建制样 | 失败关闭且标记可重试；不使用过期允许缓存；恢复后重新完整校验 |

## 明确非目标

- 不在本卡决定条件接收矩阵
- 不实现身份评估录入页面
- 不实现拆解、制样和任务分配的完整业务
- 不允许复制 receiving 状态到多个模块后各自解释
- 不以数据库触发器替代领域契约

## 允许修改路径

- `src/modules/receiving/public-contracts/**`
- `src/modules/disassembly/**`
- `src/modules/sample-preparation/**`
- `src/modules/lab-execution/**`
- `contracts/receiving/eligibility/**`
- `tests/architecture/**`
- `tests/receiving/eligibility/**`

## 验证命令

- `python -m tools.specgen check`
- `TECH_STACK_TEST_COMMAND_REQUIRED_BY_ED-001`
- `CROSS_MODULE_CONTRACT_TEST_REQUIRED`
- `MODULE_PRIVATE_TABLE_ACCESS_CHECK_REQUIRED`

## 完成定义

- 三个入口共享版本化资格端口和契约测试
- 阻断状态、UNKNOWN、权限、并发和恢复测试全部通过
- 拒绝路径经数据库断言无业务副作用
- 阻断审计包含 AC-REC-001 要求字段
- 无跨模块私表访问
- OD-005 版本固定在发布基线中
- 需求—风险—设计—测试—证据追踪完整

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
