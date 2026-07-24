<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-002@2.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-002：生成、打印并校验包装和实物标识

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `2.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-BARCODE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 收样产品负责人, 实验室运营负责人, IT负责人, 质量负责人, QA负责人 |
| 影响模块 | receiving, identifier, barcode, label-printing, worker, scan-resolution, audit, automated-test |
| 来源 | PRD-MAIN#OPS-RECEIPT-002, PRD-MAIN#OD-002, PRD-MAIN#ORG-STRUCT-001, PRD-MAIN#SEC-AUD-001, PRD-MAIN#OD-031 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-REC-001@2.0.0, ED-001@2.0.0, OD-002@1.0.0, ORG-STRUCT-001@1.0.0, OPS-RECEIPT-001@1.0.0, OPS-RECEIPT-002@1.0.0, OD-009@1.0.0, OD-031@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `89010ab6fd1a1f4e66e5e3e731bab4288d929e99644303936255d2b35c2f4e35` |

## 业务结果

收样员可以批量打印并扫描校验包装和实物标签；每个对象身份稳定、不可复用，打印和重印不会绕过多机构权限、审计或隔离状态。

## 主要参与者

收样员、样品管理员、具有受控重印权限的主管，以及超过阈值时具有覆盖权限的质量人员

## 触发条件

成功登记 Container 和 ReceivedItem 时分配身份；收样员随后请求批量打印、扫描校验或受控重印

## 前置条件

- ATC-REC-001@2.0.0 已交付 Receipt、Container、ReceivedItem 登记与隔离初态
- 部署绑定唯一 OrganizationGroup，客户端不能选择或覆盖集团
- 实验室具有稳定且非空的显示代码，逻辑打印机明确绑定实验室
- REC-CT-50X30@1.0.0 和 REC-RI-50X30@1.0.0 模板以不可变版本实现
- 收样操作者具有对象所属客户、法人、实验室、委托和对应 capability 的有效授权

## 正常路径

- 登记事务为每个 Container 和 ReceivedItem 原子分配 LABCODE-{CT|RI}-YYYYMMDD-###### 业务编号和 OL1 二维码不透明引用
- 业务编号序列按部署集团、对象类型和日期原子递增，编号和实验室代码快照永久不变且永不复用
- OL1 载荷只包含格式版本、CT或RI对象类型、随机128位不透明公开引用和差错校验，不包含客户或产品正文
- 批量打印为每个对象创建一份、固定模板版本与逻辑打印机的幂等 PrintJob
- Worker 生成 50×30毫米、203dpi 的 TSPL/TSPL2 指令，并向对象实验室绑定的 TCP 9100 打印机发送
- 发送成功进入 DISPATCHED，但页面明确提示尚未证明物理出纸；操作者扫描标签后进入 VERIFIED
- 扫码由服务端解析 OL1 载荷，重新执行集团、法人、实验室、客户、对象和 capability 授权，再返回对象类型、业务编号、隔离状态和允许动作
- 重印沿用同一业务编号和 OL1 身份，每个请求只打印一份并追加新的 PrintEvent

## 失败路径

- 编号并发冲突由数据库唯一约束和受控重试保证每个对象只分配一个身份，失败时登记事务整体回滚
- 未知格式版本、错误对象类型、校验失败或未知不透明引用统一拒绝，不泄露对象存在性并追加安全审计
- 未授权的跨法人、跨实验室、跨客户或跨对象扫码统一返回 OBJECT_NOT_ACCESSIBLE，不返回对象详情
- 逻辑打印机未配置、未启用、实验室不匹配或目标地址非法时打印请求失败关闭，不向其他打印机回退
- 确定在发送前失败的任务可以用同一幂等键重试；已发送但结果不确定的任务进入 UNKNOWN 且禁止自动重发
- UNKNOWN 只能通过扫描疑似标签完成 VERIFIED，或由有权人员填写原因创建受控重印
- 无重印权限、缺少原因或每请求份数不等于一时阻断；累计成功重印超过三次时额外要求 receiving.label.reprint.override 并告警
- 打印和扫码失败不得改变 ReceivedItem 的 QUARANTINED 状态，不得删除原失败记录或审计

## 领域不变量

- 只标识 Container 和 ReceivedItem；不为 Receipt、派生样品、试样、检测份或制备份创建本卡标签
- 身份分配与对象登记同事务，打印是登记后的独立副作用
- 业务编号和 OL1 不透明引用均不因重印、失败、扫描或模板升级改变
- 二维码不作为授权凭证，客户端不能提交 organizationGroupId 或用标签切换部署集团
- Container 与 ReceivedItem 使用 CT 和 RI 明显区分，模板用中文大字显示包装或实物
- ReceivedItem 标签贴在受控样品袋或吊牌，不直接贴到可能影响检测的实物表面
- DISPATCHED 只表示适配器完成发送；只有成功扫描同一标签才是 VERIFIED
- 失败、UNKNOWN、拒绝和重印证据全部只追加

## 数据契约

```json
{
  "identifier": [
    "objectType",
    "objectId",
    "businessNumber",
    "laboratoryCodeSnapshot",
    "sequenceDate",
    "sequenceValue",
    "formatVersion",
    "opaqueReference",
    "checksum",
    "createdAt"
  ],
  "printJob": [
    "printJobId",
    "objectType",
    "objectId",
    "objectVersion",
    "templateVersion",
    "printerId",
    "copies=1",
    "reason",
    "isReprint",
    "idempotencyKey",
    "status",
    "attemptCount",
    "createdBy",
    "createdAt",
    "updatedAt"
  ],
  "printer": [
    "printerId",
    "laboratoryId",
    "displayName",
    "host",
    "port=9100",
    "protocol=TSPL2",
    "enabled",
    "configurationVersion"
  ],
  "scanResult": [
    "objectType",
    "objectId",
    "businessNumber",
    "laboratoryId",
    "status",
    "allowedActions",
    "printVerificationStatus"
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
    "LABEL_OBJECT_TYPE_UNSUPPORTED",
    "PRINTER_NOT_CONFIGURED",
    "PRINTER_SCOPE_MISMATCH",
    "REPRINT_REASON_REQUIRED",
    "REPRINT_LIMIT_OVERRIDE_REQUIRED",
    "PRINT_DELIVERY_UNKNOWN",
    "IDEMPOTENCY_CONFLICT"
  ],
  "operations": [
    "POST /api/v1/label-jobs",
    "GET /api/v1/label-jobs/{printJobId}",
    "POST /api/v1/label-jobs/{printJobId}/reprint",
    "POST /api/v1/scans/resolve"
  ],
  "success": [
    "202 LabelPrintJobAccepted",
    "200 LabelPrintJob",
    "200 ScanResolution"
  ]
}
```

## 状态转换

- PrintJob: REQUESTED -> DISPATCHING -> DISPATCHED -> VERIFIED
- PrintJob: REQUESTED或DISPATCHING -> FAILED
- PrintJob: DISPATCHING -> UNKNOWN
- FAILED 在确认未发送时可以用同一幂等键重新调度；UNKNOWN 不得自动重发
- 标签打印和校验不改变 Container 或 ReceivedItem 业务状态，ReceivedItem 保持 QUARANTINED

## 权限与职责分离

- 首次打印要求 receiving.label.print 以及对象所属客户、法人、实验室和委托访问权
- 普通重印要求 receiving.label.reprint、对象访问权和非空原因
- 同一对象累计成功重印超过三次时额外要求 receiving.label.reprint.override
- 扫码要求 receiving.label.scan，返回对象前仍执行 SEC-AUTH-001@1.0.0 多维授权
- 逻辑打印机实验室必须与对象实验室一致，普通系统管理员没有默认打印、扫码或重印权限

## 审计要求

- 对象登记事务记录编号与 OL1 身份分配审计意图，但不记录可重放的完整不透明载荷
- 记录 PrintJob 请求、调度、DISPATCHED、FAILED、UNKNOWN、VERIFIED 和重印事件
- 打印审计包含 actor、集团、法人、实验室、客户、对象和对象版本、模板版本、打印机与配置版本、原因、幂等键、规则版本和时间
- 扫码成功、无效码、未知版本和未授权访问均追加脱敏事件；未授权事件不记录会泄露对象存在性的正文
- 超过重印阈值的允许或拒绝均产生质量/安全告警事件

## UX 状态

- 登记成功页按包装和实物分组显示业务编号、打印未请求、等待发送、已发送待校验、已校验、失败和不确定状态
- 允许选择同一实验室逻辑打印机并批量打印尚未打印的包装和实物，每个对象默认一份
- 扫码输入支持扫码枪快速回车提交，结果突出包装或实物、业务编号、当前隔离状态和打印校验状态
- 重印对话框强制填写原因并显示累计成功重印次数；无权限时不显示动作，服务端仍强制校验
- UNKNOWN 状态明确要求先扫描疑似标签或执行受控重印，禁止显示普通重试按钮

## 可观测性

- 指标包含 label_print_job_total、label_dispatch_failure_total、label_dispatch_unknown_total、label_verification_total 和 scan_resolution_duration_seconds
- 打印机连接失败和 UNKNOWN 进入可操作队列，但指标标签不包含客户名、对象ID、业务编号或打印机地址
- 连续无效、未知版本、跨法人、跨实验室或跨客户扫码产生脱敏安全告警
- 不得在日志、Trace、指标或审计中输出完整二维码载荷、打印机凭据或客户正文

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-002-01 | concurrency | 同一集团多个实验室并发登记包装和实物 | 提交收样登记事务 | 每个对象恰有一个不可变标识；集团、对象类型和日期命名空间内编号唯一；任一标识或审计写入失败时登记整体回滚 |
| TC-REC-002-02 | positive | 同实验室的包装和实物已有标识；逻辑打印机启用且适配器可达 | 请求批量打印 | 每个对象建立一份幂等任务；生成固定模板版本TSPL2；发送后状态为DISPATCHED而非VERIFIED |
| TC-REC-002-03 | positive | 操作者有完整对象权限；标签任务处于DISPATCHED | 扫码枪提交OL1载荷 | 返回正确包装或实物及业务编号；打印任务进入VERIFIED；ReceivedItem仍为QUARANTINED |
| TC-REC-002-04 | security | 用户缺少编码对象的法人、实验室、客户或委托权限 | 提交有效OL1载荷 | 统一返回OBJECT_NOT_ACCESSIBLE；不返回对象类型或业务编号；追加脱敏安全事件 |
| TC-REC-002-05 | authorization | 已有成功打印；主管有重印权限并填写原因 | 逐次请求重印 | 每次沿用同一身份且只新增一份；前三次按普通重印权限处理；超过三次需要override权限并告警 |
| TC-REC-002-06 | recovery | 连接打印机前确定失败；对象身份和PrintJob已存在 | 以相同幂等键重试 | 返回同一逻辑任务；不新增身份或重复任务；恢复后只发送一次 |
| TC-REC-002-07 | recovery | 发送字节后连接中断且无法确认结果 | Worker或用户尝试普通重试 | 任务保持UNKNOWN；不再次发送；只允许扫码校验或受控重印 |
| TC-REC-002-08 | permission | 对象属于实验室甲；打印机绑定实验室乙 | 请求打印 | 返回PRINTER_SCOPE_MISMATCH；未连接打印机乙；记录拒绝审计 |
| TC-REC-002-09 | boundary | 合法、损坏、未知版本和伪造OL1载荷 | 解析扫码 | 只接受合法当前版本；错误响应不泄露对象存在性；载荷和日志不包含客户或产品正文 |
| TC-REC-002-10 | deployment-isolation | 部署绑定集团甲；请求字段或二维码尝试包含集团乙 | 打印或扫码 | 请求失败关闭；集团上下文保持集团甲；不访问集团乙数据平面 |

## 明确非目标

- 不为 Receipt、派生样品、试样、检测份、迁移液、提取液或制备份生成标签
- 不实现手机摄像头、原生移动App或离线扫码
- 不使用浏览器打印对话框、厂商专用SDK或运行时下载打印插件
- 不选择具体打印机品牌，不实现仪器或文件接口
- 不实现身份评估、条件接收、拒收、异常审批或解除隔离
- 不以条码代替电子签名、业务授权或集团隔离

## 允许修改路径

- `spec/decisions/OD-031__v1.0.0.json`
- `spec/requirements/OPS-RECEIPT-002__v1.0.0.json`
- `spec/stories/ATC-REC-002__v2.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-24-dev-004-barcode-printing/**`
- `OpenLIMS.slnx`
- `contracts/receiving/**`
- `contracts/labeling/**`
- `src/modules/receiving/**`
- `src/modules/labeling/**`
- `src/host/api/**`
- `src/host/worker/**`
- `apps/web/src/**`
- `tests/architecture/**`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/unit/receiving/**`
- `tests/unit/labeling/**`
- `tests/integration/receiving/**`
- `tests/integration/labeling/**`
- `tests/contract/receiving/**`
- `tests/contract/labeling/**`
- `tests/e2e/labeling/**`
- `tests/test_repository_contract.py`
- `docs/domain/labeling/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`
- `.github/workflows/application-ci.yml`

## 验证命令

- `python -m tools.specgen ready --story ATC-REC-002@2.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module labeling`
- `pwsh -File scripts/verify.ps1 -Profile task -Module receiving`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `corepack pnpm@10.34.5 --dir apps/web lint`
- `corepack pnpm@10.34.5 --dir apps/web typecheck`
- `corepack pnpm@10.34.5 --dir apps/web test:unit`
- `python -m tools.specgen check`

## 完成定义

- 编号与对象原子提交、并发唯一和事务回滚测试通过，已发布 DEV-003 迁移不被修改
- TSPL2模板快照、打印机实验室边界、确定失败和UNKNOWN恢复测试通过
- 合法扫码、格式边界、跨组织不泄露和打印VERIFIED闭环测试通过
- 普通重印、阈值覆盖、缺少原因和无权限审计测试通过
- API、Worker、模块迁移和Web收样页通过显式模块接入通道注册，不访问其他模块私表
- 页面明确区分包装与实物，并显示真实打印状态、重印次数和UNKNOWN处理指引
- Windows、Linux、后端、前端、PostgreSQL集成和规格门禁全部通过
- 相同规格输入第二次generate显示written=0，所有变更均位于allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。
