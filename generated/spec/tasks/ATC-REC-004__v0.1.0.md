<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-004@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-004：记录身份证据并形成匹配或冲突结论

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@0.1.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-IDENTITY` |
| 开发就绪度 | `blocked` |
| 变更级别 | `major` |
| 负责人角色 | 身份评估产品负责人, 质量负责人, QA负责人 |
| 影响模块 | identity, receiving, exception, evidence, authorization, audit, automated-test |
| 来源 | PRD-MAIN#OD-002, PRD-MAIN#OPS-IDENTITY-001, PRD-MAIN#OPS-IDENTITY-002, PRD-MAIN#AC-ID-001 |
| 固定依赖 | ATC-PLT-000@0.1.0, ATC-REC-001@0.1.0, OD-002@1.0.0, OPS-IDENTITY-001@0.1.0, OPS-IDENTITY-002@0.1.0, OPS-EXC-001@0.1.0, OPS-EXC-002@0.1.0, AC-ID-001@0.1.0, OD-005@0.1.0, OD-009@0.1.0, SEC-AUTH-001@0.1.0, SEC-AUD-001@0.1.0 |
| 规格指纹 | `a26dc988872baf92a91daf93215d2a9e90ef38e0069c6c307f1d45b44c378a34` |

## 业务结果

实验室可以证明收到的究竟是什么、依据是什么以及与委托是否一致；身份冲突不会被操作员无依据地改成一致。

## 主要参与者

具有相应产品类别和实验室授权的身份评估员

## 触发条件

身份评估员打开隔离实物，记录标签、外观、型号、批次、照片等观察并提交匹配结论

## 前置条件

- 实物已登记并处于 QUARANTINED 或 IDENTITY_ASSESSING
- 客户原始声明和委托快照可读取但不可被身份评估覆盖
- OD-009 已定义识别粒度和所需证据
- OD-005 已定义错配、待定、条件接收和安全封存的授权路径

## 正常路径

- 固定当前客户声明和委托快照版本
- 追加 LaboratoryIdentityObservation 和不可变附件引用
- 根据批准字段形成 MATCHED、MISMATCHED 或 INDETERMINATE 候选
- 评估员提交理由和证据，系统校验其能力型授权
- MATCHED 结论建立实际 SubmissionItem/ProductVariant/Feature 映射
- MISMATCHED 或 INDETERMINATE 自动创建异常并保持隔离
- 所有结论版本、规则、证据和批准路径写入审计

## 失败路径

- 必需观察字段或照片缺失时不能提交结论
- 声明和观察冲突时禁止提交 MATCHED，除非有受控更正或授权证据
- 存在多个可能归属且未消歧时结论必须为 INDETERMINATE
- 无产品类别或实验室授权时拒绝
- 对象版本变化时拒绝旧表单提交并要求重新比对
- 任何异常不得自动触发条件接收或范围缩减

## 领域不变量

- CustomerDeclaredIdentity、LaboratoryObservation、IdentityDecision 独立保存
- 更正通过新版本表达，不覆盖原始观察和结论
- 身份映射只包含实际收到对象，不表达代表性
- 错配和待定保持隔离
- 决定引用明确的委托、产品版本、规则版本和证据哈希

## 数据契约

```json
{
  "decision": [
    "observationVersion",
    "declarationSnapshotVersion",
    "outcome",
    "reasonCode",
    "rationale",
    "actualIdentityMappings",
    "ruleSetVersion"
  ],
  "mapping": [
    "submissionItemId",
    "productVariantVersion",
    "featureNodeVersions",
    "scopeOfIdentity",
    "evidenceRefs"
  ],
  "observation": [
    "receivedItemId",
    "expectedItemVersion",
    "observedLabels",
    "observedModel",
    "observedBatch",
    "appearance",
    "attachmentHashes",
    "observedAt"
  ],
  "outcomeEnum": [
    "MATCHED",
    "MISMATCHED",
    "INDETERMINATE"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "IDENTITY_EVIDENCE_INCOMPLETE",
    "IDENTITY_CONFLICT_REQUIRES_EXCEPTION",
    "IDENTITY_AMBIGUOUS",
    "AUTHORIZATION_DENIED",
    "EXPECTED_VERSION_CONFLICT"
  ],
  "operations": [
    "POST /api/v1/received-items/{id}/identity-observations",
    "POST /api/v1/received-items/{id}/identity-decisions"
  ],
  "success": [
    "201 IdentityObservation",
    "200 IdentityDecisionResult"
  ]
}
```

## 状态转换

- QUARANTINED -> IDENTITY_ASSESSING 在首次受控观察开始时发生
- MATCHED 结论不直接解除隔离；后续放行动作由 OD-005 和 ATC-REC-006 控制
- MISMATCHED -> AWAITING_CUSTOMER 或其他受控状态只能由授权决定触发
- INDETERMINATE 保持阻断

## 权限与职责分离

- 要求 receiving.identity.evaluate 能力和产品类别范围
- 评估员不得修改客户原始声明
- 收样员默认不能批准技术身份例外
- 跨客户和跨实验室对象过滤在服务端执行

## 审计要求

- 记录观察创建、结论提交、版本冲突和异常创建
- 审计包含原声明版本、观察版本、结论、规则、责任人和证据哈希
- 附件下载单独审计
- 历史结论不可删除

## UX 状态

- 三栏明确区分客户声明、实验室观察和最终结论
- 差异字段逐项高亮但不自动选择结论
- 证据缺失显示阻断清单
- 错配时显示将创建异常且保持隔离
- 并发冲突保留草稿但要求重新载入最新对象
- 历史版本只读可比较

## 可观测性

- 指标 identity_assessment_total 按结论和产品类别聚合
- 指标 identity_assessment_wait_duration_seconds 区分客户等待和内部等待
- 异常创建失败触发高优先级告警并回滚结论提交
- 结构化日志携带 receipt、receivedItem 和 exception correlationId

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-004-01 | positive | 声明型号与实物标签和观察一致；证据完整 | 授权评估员提交MATCHED | 保存三层记录；建立实际身份映射；对象仍等待受控放行 |
| TC-REC-004-02 | negative | 合同型号A；观察型号C | 提交不一致结论 | 创建身份冲突异常；禁止正常接收；保留声明和观察 |
| TC-REC-004-03 | boundary | 实物可能对应两个委托项且证据不足 | 尝试选择其中一个为MATCHED | 拒绝无依据匹配；保存INDETERMINATE并阻断 |
| TC-REC-004-04 | security | 用户没有该产品类别身份评估授权 | 提交观察或结论 | 服务端拒绝；不产生结论；记录审计 |
| TC-REC-004-05 | concurrency | 用户打开版本3；另一人先提交版本4 | 用户提交旧版本结论 | 返回EXPECTED_VERSION_CONFLICT；不得覆盖版本4 |
| TC-REC-004-06 | recovery | 错配结论需要创建异常；异常持久化模拟失败 | 提交错配 | 结论和状态均回滚；重试后只创建一个异常 |

## 明确非目标

- 不由 AI 自动做最终身份结论
- 不在本卡解除隔离
- 不把身份映射当作代表性覆盖
- 不实现客户补资料门户
- 不决定 OD-005 或 OD-009

## 允许修改路径

- `src/modules/receiving/identity/**`
- `src/modules/exception/public-contracts/**`
- `contracts/receiving/identity/**`
- `apps/web/receiving/identity/**`
- `tests/receiving/identity/**`

## 验证命令

- `python -m tools.specgen check`
- `TECH_STACK_TEST_COMMAND_REQUIRED_BY_ED-001`
- `IDENTITY_CONTRACT_TEST_REQUIRED`
- `TENANT_ISOLATION_TEST_REQUIRED`

## 完成定义

- 三层事实的数据、API和页面清晰分离
- AC-ID-001 正向与反向场景自动化
- 错配异常和结论事务原子
- 权限、并发、历史不可变和附件审计测试通过
- 实际身份映射不包含覆盖语义
- 需求—设计—测试—证据追踪完整

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
