<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-INST-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-INST-001：实施 DEV-020 首类仪器文件导入

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-INSTRUMENT-IMPORT` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 技术负责人, 实验室负责人, 质量负责人, QA负责人 |
| 影响模块 | instrument, raw-evidence, parsing, exception-queue, hash, audit, outbox, authorization, automated-test |
| 来源 | PRD-MAIN#INT-INST-001, PRD-MAIN#INT-INST-002, PRD-MAIN#INT-DATA-001, PRD-MAIN#LAB-RAW-001, PRD-MAIN#LAB-RAW-002 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, OD-001@1.0.0, OD-030@1.0.0, BUS-INST-001@1.0.0, BUS-INST-002@1.0.0, BUS-INST-003@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `a970c2e9a43bc42c3a371fe3edeb254a910a410ffba1851ec9f33c2f6673cdc8` |

## 业务结果

实验室获得首个受治理的仪器文件导入通道：原始证据不可变、解析映射可追、异常必经人工确认，为物理机械方法族的仪器数据接入提供 LAB-RAW/INT-INST 合规基础，且验证数据集比对基准（100% 一致率）从第一天固定在 CI。

## 主要参与者

仪器数据导入操作者（instrument.import 能力）与导入异常确认人

## 触发条件

操作者登记仪器导出文件并提交解析行；解析异常触发人工确认

## 前置条件

- 平台依赖（Postgres/OIDC/对象存储探针）就绪
- OD-030 已决定 LIMS 最小记录+外部引用口径
- OD-001 已决定试点切片（玩具×物理机械）

## 正常路径

- POST 文件登记：稳定外部引用+版本、SHA-256、来源系统（INSTRUMENT/CDS/MIDDLEWARE）、仪器引用+版本、解析器版本、声明总行数 → INGESTED
- POST 解析行批次：每行五维映射（样品号/批次位置/参数/单位/限定符）+ 解析前原始值 + 解析后值；合法行落为不可变行事实
- 异常行（未知样品号/非法单位/值不可解析/行号重复/限定符冲突）进入人工确认队列，导入批次 BLOCKED
- 确认人对异常行决议：ACCEPT_WITH_MAPPING（记录修正映射与原因）或 REJECT_ROW（记录原因）；原始值原样保留
- 全部行落定（行事实 + 决议）且计数与声明一致 → 导入批次 COMPLETED
- GET 状态端口按 expectedVersion+ruleSetVersion 返回 ALLOWED（COMPLETED 且版本匹配）/BLOCKED/UNKNOWN

## 失败路径

- 相同组织内相同 SHA-256 再次登记 → INS.DUPLICATE_FILE（409）
- 行号重复或归属未知文件 → INS.VALIDATION_FAILED，不产生行事实
- 对已决议异常再次决议 → INS.EXCEPTION_ALREADY_RESOLVED
- 存在未决异常时查询状态 → BLOCKED[PENDING_EXCEPTIONS]；版本不匹配/规则集未知 → UNKNOWN（视为阻断）
- UPDATE/DELETE 任何 instrument 事实 → 数据库 55000（INS.INSTRUMENT_APPEND_ONLY）
- 行为人缺失/组织不匹配/能力拒绝 → INS.NOT_AUTHORIZED，仅 audit_attempt 留痕
- 平台审计/发件箱写入失败 → 整体回滚，业务事实不产生

## 领域不变量

- 原文件内容不入 LIMS——登记的是清单+哈希（OD-030/INT-DATA-001）
- 解析前原始值逐字节保留且永不修改；解析后值并存不覆盖（LAB-RAW-002）
- 文件登记、行事实、异常、决议全部追加式，DB 触发器强制（55000）
- 导入批次乐观并发：expectedCurrentVersion + advisory lock，重复 event_id 幂等拒绝
- 事实、平台审计意图与发件箱事件同一事务提交；模块 audit_attempt 独立于回滚存活
- 单一新能力 instrument.import（含异常决议）；状态端口消费方自行担责
- 本卡不做 OD-031 延后的生产仪器清单排序，不接批次/结果消费（后续卡经端口消费）

## 数据契约

```json
{
  "exceptionResolution": [
    "resolutionId",
    "exceptionId",
    "kind(ACCEPT_WITH_MAPPING/REJECT_ROW)",
    "correctedMapping?",
    "reason",
    "resolvedBy",
    "resolvedAt"
  ],
  "fileRegistration": [
    "fileRegistrationId",
    "ruleSetVersion(INST-IMPORT@1.0.0)",
    "objectScope{legalEntityId, laboratoryId}",
    "externalRef{id, version}",
    "sha256",
    "sourceSystem(INSTRUMENT/CDS/MIDDLEWARE)",
    "instrumentRef{id, version}",
    "parserVersion",
    "declaredRowCount",
    "state(INGESTED/BLOCKED/COMPLETED)",
    "version"
  ],
  "importException": [
    "exceptionId",
    "fileRegistrationId",
    "rowNumber",
    "reasonCode(UNKNOWN_SAMPLE/ILLEGAL_UNIT/UNPARSABLE_VALUE/DUPLICATE_ROW/QUALIFIER_CONFLICT)",
    "rawContent",
    "state(PENDING/RESOLVED)"
  ],
  "parsedRow": [
    "rowId",
    "fileRegistrationId",
    "rowNumber",
    "sampleNumber",
    "batchPosition",
    "parameter",
    "unit",
    "qualifier?",
    "rawValue",
    "parsedValue",
    "parserVersion"
  ],
  "statusResult": [
    "decision(ALLOWED/BLOCKED/UNKNOWN)",
    "reasonCodes[]",
    "fileRegistrationId",
    "currentVersion?",
    "completedRowCount?",
    "pendingExceptionCount?",
    "ruleSetVersion"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "INS.VALIDATION_FAILED",
    "INS.DUPLICATE_FILE",
    "INS.EXCEPTION_ALREADY_RESOLVED",
    "INS.EXPECTED_VERSION_CONFLICT",
    "INS.NOT_AUTHORIZED",
    "INS.OBJECT_NOT_ACCESSIBLE",
    "INS.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/instrument-files → 201 文件登记",
    "POST /api/v1/instrument-files/{id}/rows → 201 解析行批次（合法行+异常行分流）",
    "POST /api/v1/instrument-files/{id}/exceptions/{exceptionId}/resolution → 201 异常决议",
    "GET /api/v1/instrument-files/{id} → 200 登记与行/异常明细",
    "GET /api/v1/instrument-files/{id}/import-status → 200 状态端口决策"
  ],
  "publicPort": "IInstrumentImportPort.EvaluateAsync(InstrumentImportStatusRequest) → ALLOWED/BLOCKED/UNKNOWN，版本+规则集固定，供批次/结果后续卡消费"
}
```

## 状态转换

- 文件登记：INGESTED →（出现异常）BLOCKED →（全部异常决议且行数落定）COMPLETED；INGESTED →（无异常且行数落定）COMPLETED；不可逆
- 异常：PENDING → RESOLVED（单向一次）

## 权限与职责分离

- 新增能力 instrument.import（登记/行提交/异常决议/读取共用，操作差异由对象状态约束）；HttpClaims 精确 claim 检查
- 状态端口要求 instrument.import 能力，跨模块消费方自行担责（不放宽也不复制）

## 审计要求

- 每个命令写平台 audit_intent（同事务）+ outbox 事件（Instrument.FileRegistered/RowsSubmitted/ExceptionResolved）
- 失败尝试写 instrument.audit_attempt（SHA-256 目标哈希，独立于回滚）
- 读取写 READ_INSTRUMENT_FILE 审计

## UX 状态

- 本卡不新增前端页面
- 异常确认队列为 API 面——前端界面属后续卡

## 可观测性

- 计数器：登记数、行数（合法/异常）、决议数（接受/拒绝）、状态端口决策分布
- 结构化日志固定 correlationId 与错误码

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-INST-001-01 | positive | 合法文件元数据 | 登记 | INGESTED；哈希/仪器/解析器版本固定；审计+发件箱同事务；重复 SHA-256 拒绝 409 |
| TC-INST-001-02 | positive | 合法行批次 | 提交 | 行事实含五维映射+rawValue+parsedValue；行号唯一；计数落定后 COMPLETED |
| TC-INST-001-03 | negative | 含未知样品号与非法单位的行 | 提交 | 异常行不产生行事实；队列 PENDING；登记 BLOCKED；状态端口 BLOCKED[PENDING_EXCEPTIONS] |
| TC-INST-001-04 | positive | PENDING 异常 | ACCEPT_WITH_MAPPING 与 REJECT_ROW 各一 | 决议人/原因/时间固定；原始值逐字节不变；重复决议拒绝；全部落定后 COMPLETED |
| TC-INST-001-05 | regression | 冻结验证数据集（含限定符/单位/异常样例） | 完整导入后逐字段比较 | 原始值、解析值、单位、限定符、样品/批次映射、异常处理一致率 100%（PRD §22-15） |
| TC-INST-001-06 | negative | 已有事实 | UPDATE/DELETE 及并发同版本提交 | 55000 拒绝；恰一个提交成功，另一方 EXPECTED_VERSION_CONFLICT |
| TC-INST-001-07 | negative | 审计或发件箱注入失败 | 登记 | 业务事实回滚为零；audit_attempt 恰一次 |
| TC-INST-001-08 | boundary | COMPLETED 登记 | 正确/过期版本与未知规则集查询 | ALLOWED / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN] |

## 明确非目标

- 不做 OD-031 延后的生产仪器/文件接口清单排序（能力交付≠清单决定）
- 不实现真实仪器驱动、中间件轮询或高频流式采集（首类为文件/CSV 形态）
- 不实现批次/结果对导入数据的消费（后续卡经 IInstrumentImportPort）
- 不实现 CDS 重新积分版本链（INT-CDS-001 权威边界已由 OD-030 定口径，运行语义属后续卡）
- 不新增前端页面
- 不触碰未决 OD，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-INST-001__v1.0.0.json`
- `spec/requirements/BUS-INST-002__v1.0.0.json`
- `spec/requirements/BUS-INST-003__v1.0.0.json`
- `spec/stories/ATC-INST-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-020-instrument-import/**`
- `OpenLIMS.slnx`
- `contracts/instrument/**`
- `src/modules/instrument/**`
- `src/host/api/OpenLIMS.Api/**`
- `src/host/worker/OpenLIMS.Worker/**`
- `tests/unit/instrument/**`
- `tests/contract/instrument/**`
- `tests/integration/instrument/**`
- `tests/architecture/**`
- `tests/contract/labeling/OpenLIMS.Labeling.ContractTests/packages.lock.json`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/contract/receiving/OpenLIMS.Receiving.ContractTests/packages.lock.json`
- `tests/contract/scope/OpenLIMS.Scope.ContractTests/packages.lock.json`
- `tests/contract/quantity/OpenLIMS.Quantity.ContractTests/packages.lock.json`
- `tests/contract/allocation/OpenLIMS.Allocation.ContractTests/packages.lock.json`
- `tests/contract/batch/OpenLIMS.Batch.ContractTests/packages.lock.json`
- `tests/contract/result/OpenLIMS.Result.ContractTests/packages.lock.json`
- `tests/contract/billing/OpenLIMS.Billing.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/instrument/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-INST-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module instrument`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `python -m tools.specgen check`

## 完成定义

- 文件登记/解析行/异常队列/决议/状态端口全部落地且追加式 DB 强制
- 验证数据集契约测试 100% 一致率固定在 CI
- 全部既有测试项目保持绿色
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
