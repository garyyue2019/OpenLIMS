# 首类仪器文件导入（DEV-020 / ATC-INST-001）

OD-001 已决（玩具 × 物理机械方法族）解锁本卡。交付 `instrument` 模块，规则集 `INST-IMPORT@1.0.0`。

## 三层事实

| 事实 | 内容 | 依据 |
|---|---|---|
| `instrument.file_registration` | 稳定外部引用+版本、SHA-256、来源系统（INSTRUMENT/CDS/MIDDLEWARE）、仪器引用+版本、解析器版本、声明总行数 | INT-INST-001、LAB-RAW-001 |
| `instrument.parsed_row` | 样品号/批次位置/参数/单位/限定符五维映射 + **解析前原始值与解析后值并存** | INT-INST-002 |
| `instrument.import_exception` + `exception_resolution` | 异常行完整原始内容 + 人工决议（ACCEPT_WITH_MAPPING / REJECT_ROW，含决议人与原因） | INT-INST-002 异常队列、LAB-RAW-002 |

原文件内容本体不入 LIMS——登记的是不可变清单与哈希（OD-030 权威边界、INT-DATA-001）。

## 关键不变量

- **原始值永不改写**：`raw_value` 与 `parsed_value` 并存；人工决议只能补充修正映射，不能触碰原始值（DB 触发器 `INS.INSTRUMENT_APPEND_ONLY` / errcode 55000 强制）。
- **异常必经人工确认**：未知样品号、非法单位、值不可解析、行号重复、限定符冲突五类异常不产生可消费解析行，登记进入 `BLOCKED`。
- **重复文件拒绝**：同一组织内相同 SHA-256 唯一索引 → `INS.DUPLICATE_FILE`。
- **不可超发**：已落行数 + 本次提交行数不得超过声明总行数，否则整批拒绝。
- **状态端口 UNKNOWN=阻断**：`IInstrumentImportPort` 按 `expectedFileVersion` + `ruleSetVersion` 固定返回 ALLOWED/BLOCKED/UNKNOWN，供批次/结果后续卡消费。
- 事实、平台 `audit_intent` 与 `outbox` 同事务提交；失败仅写模块 `audit_attempt`（独立于回滚存活）。

## 验证数据集基准（PRD §22 第 15 条）

契约测试 `Approved_validation_dataset_matches_field_by_field_at_full_rate` 用冻结数据集（含限定符、非法单位、未知样品号样例）逐字段比较原始值、解析值、单位、限定符、样品/批次映射与异常处理，一致率 100%，比较次数由断言固定。

## 明确不在本卡范围

生产仪器/文件接口清单排序（OD-031 延后）、真实仪器驱动与中间件轮询、批次/结果对导入数据的消费、CDS 重新积分版本链。

## 行号占用语义（对抗式评审修正）

行号在两张表上各自唯一，因此"已占用"要分来源处理：

| 行号已被 | 再次提交该行号 | 依据 |
|---|---|---|
| `parsed_row`（合法行事实） | 排队为 `DUPLICATE_ROW` 异常 | 可持久化，happy_path 列明的异常原因 |
| `import_exception`（异常队列） | `INS.VALIDATION_FAILED`，整批拒绝、零事实 | 队列按行号唯一，第二条异常无法持久化；对应 failure_paths「行号重复 → INS.VALIDATION_FAILED」 |

若不加区分，操作者"修好源文件后按同一行号重投"会触发唯一约束 23505，并被错误映射为 `INS.EXPECTED_VERSION_CONFLICT`（409），诱使客户端无限重试同一载荷。同时兜底把裸 23505 映射为 `INS.VALIDATION_FAILED`（与 batch/result 等模块一致），使 409 只保留给真正的乐观并发冲突。
