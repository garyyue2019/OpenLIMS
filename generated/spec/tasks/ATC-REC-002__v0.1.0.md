<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-002@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-002：生成、打印并校验包装和实物标识

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@0.1.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-BARCODE` |
| 开发就绪度 | `blocked` |
| 变更级别 | `minor` |
| 负责人角色 | 收样产品负责人, 移动与条码负责人, QA负责人 |
| 影响模块 | receiving, identifier, barcode, mobile, audit, automated-test |
| 来源 | PRD-MAIN#OPS-RECEIPT-002, PRD-MAIN#OD-002, PRD-MAIN#ORG-STRUCT-001, PRD-MAIN#SEC-AUD-001, PRD-MAIN#OD-031 |
| 固定依赖 | ATC-PLT-000@0.1.0, ATC-REC-001@0.1.0, OD-002@1.0.0, ORG-STRUCT-001@0.1.0, OPS-RECEIPT-002@0.1.0, OD-009@0.1.0, OD-031@0.1.0, SEC-AUTH-001@0.1.0, SEC-AUD-001@0.1.0 |
| 规格指纹 | `01083113a16c88a273fa4a1d5e96a0a7b1326ff05068d28f2dacf29b35e29a4b` |

## 业务结果

收样员能可靠区分包装和实物并通过扫码定位正确对象；标签重印不会创造新身份，也不会绕过授权或审计。

## 主要参与者

收样员、样品管理员和具有受控重印权限的主管

## 触发条件

登记成功后请求打印标签，或后续通过扫码定位包装/实物，或经批准重印标签

## 前置条件

- ATC-REC-001 已交付
- OD-009 已批准识别粒度
- OD-031 已批准编码格式、打印设备和移动流程
- 标签模板已版本化并通过可读性验证

## 正常路径

- 编号服务在部署集团和对象类型命名空间内原子分配业务编号，可按法人或实验室使用受控前缀但不得产生重复身份
- 编码载荷包含非敏感对象引用、校验信息和格式版本，不嵌入客户敏感正文
- 打印请求固定标签模板版本、打印机、份数和原因
- 扫码由服务端绑定部署集团上下文，并重新执行法人、实验室、客户和对象授权
- 重印沿用原对象身份和业务编号，生成新的 PrintEvent
- 页面明确展示对象类型，防止把 Container 当作 ReceivedItem

## 失败路径

- 编号并发冲突时由数据库唯一约束和重试保证只分配一次
- 未知编码版本或校验失败时拒绝解析并记录安全事件
- 未授权的跨法人、跨实验室或跨客户扫码统一拒绝，不返回客户或对象详情
- 无重印权限、无原因或超过份数阈值时阻断
- 打印机失败不回滚已存在身份，但记录失败事件并允许幂等重试

## 领域不变量

- 身份编号永不复用且不因标签重印改变
- 编码不作为授权凭证
- 标签模板和每次打印均有版本与审计
- 包装和实物使用不同对象类型前缀或等效防错机制

## 数据契约

```json
{
  "identifier": [
    "objectType",
    "businessNumber",
    "groupScopedSequence",
    "formatVersion",
    "checksum"
  ],
  "printRequest": [
    "objectId",
    "objectVersion",
    "templateVersion",
    "printerId",
    "copies",
    "reason",
    "idempotencyKey"
  ],
  "scanResult": [
    "objectType",
    "objectId",
    "businessNumber",
    "status",
    "allowedActions"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "BARCODE_INVALID",
    "BARCODE_VERSION_UNSUPPORTED",
    "OBJECT_NOT_ACCESSIBLE",
    "REPRINT_REASON_REQUIRED",
    "REPRINT_LIMIT_EXCEEDED",
    "PRINTER_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/labels/print",
    "POST /api/v1/labels/reprint",
    "POST /api/v1/scans/resolve"
  ],
  "success": [
    "202 PrintJobAccepted",
    "200 ScanResolution"
  ]
}
```

## 状态转换

- 标签打印不改变 ReceivedItem 业务状态
- PrintJob: REQUESTED -> PRINTED 或 FAILED
- 重试必须复用同一逻辑打印请求，不生成重复份数

## 权限与职责分离

- 打印要求 receiving.label.print
- 重印要求 receiving.label.reprint 且按对象和实验室授权
- 扫码结果仍经过 SEC-AUTH-001 多维授权
- 普通系统管理员没有默认重印权限

## 审计要求

- 记录编号分配、打印、失败、扫码和重印
- 重印记录原标签、模板、份数、原因和批准信息
- 记录编码格式版本但不记录可重放的敏感令牌

## UX 状态

- 打印待处理、成功和失败状态可见
- 扫描结果突出对象类型、业务编号和当前隔离状态
- 重印对话框强制原因并显示历史打印次数
- 无权限时不显示重印动作，服务端仍强制校验

## 可观测性

- 指标 label_print_total、label_print_failure_total 和 scan_resolution_duration_seconds
- 打印机错误进入可重试异常队列
- 连续无效或跨法人、跨实验室、跨客户扫码产生安全告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-002-01 | positive | 同一集团的多个实验室并发登记包装和实物 | 分配编号 | 集团和对象类型命名空间内所有编号唯一；对象类型和必要的机构前缀可区分 |
| TC-REC-002-02 | positive | 用户有对象权限；编码有效 | 扫码 | 返回正确对象、状态和允许动作 |
| TC-REC-002-03 | security | 用户无编码所属法人、实验室或客户权限 | 扫码 | 拒绝且不泄露对象信息；记录安全事件 |
| TC-REC-002-04 | authorization | 已有标签；主管有重印权限并填写原因 | 重印 | 沿用同一身份；新增PrintEvent；审计包含原因 |
| TC-REC-002-05 | recovery | 打印机首次不可用 | 以相同幂等键重试 | 身份不变；只增加一次成功打印副作用 |

## 明确非目标

- 不实现离线扫码
- 不选择具体打印机品牌
- 不以条码代替电子签名
- 不改变 OD-031 决策

## 允许修改路径

- `src/modules/receiving/**`
- `src/modules/labeling/**`
- `contracts/labeling/**`
- `apps/web/receiving/**`
- `tests/labeling/**`

## 验证命令

- `python -m tools.specgen check`
- `TECH_STACK_TEST_COMMAND_REQUIRED_BY_ED-001`
- `BARCODE_CONTRACT_TEST_REQUIRED_BY_OD-031`

## 完成定义

- 编号并发测试、扫码安全测试和重印审计测试通过
- 打印失败可恢复且不重复创建身份
- 编码与标签模板契约版本化
- 页面能够明确区分包装和实物
- 需求—设计—测试—证据追踪更新

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
