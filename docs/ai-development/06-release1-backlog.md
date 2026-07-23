# 06 Release 1 Backlog 拆解

本章给出建议的完整产品 backlog 骨架。它是规划结构，不代表所有 Feature 已批准进入 Release 1。

## 1. 主依赖链

```text
EP-GOVERNANCE + EP-PLATFORM
        ↓
EP-KNOWLEDGE
        ↓
EP-SCOPE-COMMERCIAL
        ↓
EP-RECEIVING
        ↓
EP-EXECUTION
        ↓
EP-QUALITY
        ↓
EP-REPORT
        ↓
EP-BILLING-INTEGRATION

EP-AI-GOVERNANCE 是可关闭旁路
EP-OPERATIONS 横跨全部 Epic
```

## 2. EP-GOVERNANCE：发布与配置治理

- `FEAT-GOV-R1-BASELINE`：唯一试点、灯塔客户、产品、市场/协议、主技术包和 Pareto 方法集。
- `FEAT-GOV-APPLICABILITY`：Core、行业包、条件接口、BusinessOps 和排除项适用矩阵。
- `FEAT-GOV-DECISIONS`：OD 状态、责任人、证据、批准和生效日期。
- `FEAT-GOV-CONFIG-VERSION`：行业包、技术包、客户配置兼容和迁移策略。
- `FEAT-GOV-TRACEABILITY`：需求—风险—设计—测试—证据闭环。
- `FEAT-GOV-RELEASE-SEAL`：requirements lock、Breaking Gate、Seal 和历史验证。

## 3. EP-PLATFORM：平台控制底座

- `FEAT-PLT-ORG-CONTEXT`：单部署集团上下文、法人、实验室、部门、工作中心及服务端绑定的请求上下文；客户端不能选择集团。
- `FEAT-PLT-AUTHORIZATION`：角色 + 对象 + 客户 + 法人 + 实验室 + 有效期授权。
- `FEAT-PLT-SOD-ESIGN`：职责分离、重新认证、签署意图和签名失效。
- `FEAT-PLT-VERSIONING`：业务对象版本、冻结、替代和历史不可变。
- `FEAT-PLT-ATTACHMENT`：对象存储、附件哈希和不可变引用。
- `FEAT-PLT-AUDIT`：追加式审计与完整性检测。
- `FEAT-PLT-OUTBOX`：事务发件箱、幂等消费者和失败重放。
- `FEAT-PLT-OBSERVABILITY`：关联 ID、日志、指标和业务异常队列。

## 4. EP-KNOWLEDGE：受控主数据

- `FEAT-KNW-PARTY-ROLE`：申请、付款、品牌、工厂、样品和报告参与方角色。
- `FEAT-KNW-PROTOCOL`：客户项目、品牌/OEM 协议和有效期。
- `FEAT-KNW-REQUIREMENT-LIFECYCLE`：标准、版本、勘误、实施日和过渡期。
- `FEAT-KNW-METHOD`：方法、选项、样品需求和执行边界。
- `FEAT-KNW-RULES`：限值、计算、判定和采用规则版本。
- `FEAT-KNW-ACCREDITATION`：站点、方法、参数、量程、期限和签字授权。
- `FEAT-KNW-CAPABILITY`：人员、设备、场地、夹具、分包和能力数据。

## 5. EP-SCOPE-COMMERCIAL：询价、范围与报价

- `FEAT-INQ-INTAKE`：询价、资料导入和最低资料校验。
- `FEAT-INQ-GAP-QUEUE`：资料缺口、澄清和责任队列。
- `FEAT-CAP-REVIEW`：合同、能力、认可、样品量和交期评审。
- `FEAT-SCOPE-MATRIX`：版本化 TestScopeMatrix。
- `FEAT-SCOPE-LINE-GATE`：ScopeLine 完整性和生产门禁。
- `FEAT-SCOPE-COVERAGE`：代表性、证据、失效条件和重新评估。
- `FEAT-QUOTE-FORMAL`：正式报价、价格、TAT、排除项和合同引用。
- `FEAT-SCOPE-CHANGE`：范围变化及价格、周期、样品、在制和报告影响。

## 6. EP-RECEIVING：收样与样品控制

- `FEAT-REC-REGISTRATION`：Receipt、Container、ReceivedItem 登记。
- `FEAT-REC-BARCODE`：标签、扫码、上下文校验和人工回退。
- `FEAT-REC-QUARANTINE`：隔离和下游动作门禁。
- `FEAT-REC-IDENTITY`：声明、观察和匹配决定。
- `FEAT-REC-EXCEPTION`：数量、温控、破损、污染、标签和身份冲突。
- `FEAT-REC-CONDITIONAL-ACCEPTANCE`：条件接收、拒收、封存和批准矩阵。
- `FEAT-SAMPLE-LINEAGE`：物理谱系、复合样和非破坏使用关系。
- `FEAT-SAMPLE-QUANTITY`：不可变数量流水、守恒和并发预留。
- `FEAT-SAMPLE-CUSTODY`：位置、领用、归还、留样和处置。
- `FEAT-TEXTILE-SAMPLE-REQUIREMENT`：款色部位、平行、互斥、复测和留样需求。
- `FEAT-TEXTILE-CUTTING`：CuttingPlan、方向、部位和试样。
- `FEAT-TEXTILE-PRECONDITION`：调湿、洗涤及超差影响。

首批已结构化的 6 张任务卡：

| Task | Feature | 当前状态 |
|---|---|---|
| `ATC-REC-001` | Registration | proposed/blocked |
| `ATC-REC-002` | Barcode | proposed/blocked |
| `ATC-REC-003` | Quarantine | proposed/blocked |
| `ATC-REC-004` | Identity | proposed/blocked |
| `ATC-REC-005` | Exception | proposed/blocked |
| `ATC-REC-006` | Controlled Release | proposed/blocked |

## 7. EP-EXECUTION：策划、资源与执行

- `FEAT-PLAN-GENERATION`：批准 ScopeLine 形成计划和任务。
- `FEAT-PLAN-SEQUENCE`：前置条件、顺序和破坏性约束。
- `FEAT-RESOURCE-CONFLICT`：人员、设备、场地和夹具硬冲突。
- `FEAT-WORK-QUEUE`：单件和批量工作队列。
- `FEAT-TASK-ALLOCATION`：TestObjectAllocation 和样品资格。
- `FEAT-BATCH-MANAGEMENT`：制备、预处理、分析和仪器批次。
- `FEAT-MANUAL-RECORDING`：结构化手工记录。
- `FEAT-INSTRUMENT-IMPORT`：首类真实仪器或文件导入。
- `FEAT-RAW-EVIDENCE`：原文件、解析值、哈希和解析器版本。

## 8. EP-QUALITY：QC 与结果

- `FEAT-QC-RULES`：方法和批次 QC。
- `FEAT-QC-IMPACT`：QC、环境和校准失效影响传播。
- `FEAT-CALC-DETERMINISTIC`：单位、稀释、舍入、LOD/LOQ 和限值。
- `FEAT-RESULT-PROVENANCE`：结果来源图和聚合规则。
- `FEAT-RESULT-ADOPTION`：唯一采用结果。
- `FEAT-RETEST`：复测、补测、重新制备和预先采用规则。
- `FEAT-CONFORMITY`：EvaluationMode 和限定范围结论。
- `FEAT-ACCREDITATION-GATE`：执行/结果层认可资格。

## 9. EP-REPORT：报告与签发

- `FEAT-RPT-ASSEMBLY`：报告行和当前采用结果。
- `FEAT-RPT-TRACE`：报告行全链追溯。
- `FEAT-RPT-CLAIM`：实测、覆盖、未评价、认可和分包披露。
- `FEAT-RPT-REVIEW`：技术复核和阻断清单。
- `FEAT-RPT-SIGN`：签发资格、报告哈希和电子签名。
- `FEAT-RPT-DELIVERY`：交付、下载权限和通知。
- `FEAT-RPT-CORRECTION`：更正、补充、撤回、替代和历史链接。

## 10. EP-BILLING-INTEGRATION：计费事实与条件接口

- `FEAT-BILL-EVIDENCE`：唯一计费事实、防重复和零金额证据。
- `FEAT-BILL-ADJUSTMENT`：正负调整证据。
- `FEAT-BILL-EXPORT`：可审计导出。
- `FEAT-ERP-HANDOFF`：条件 ERP 交接和差异队列。
- `FEAT-INVOICE-HANDOFF`：条件开票提交、回查和文件验签。

完整应收、收款、核销、红票和对账不进入无条件 R1 主链。

## 11. EP-AI-GOVERNANCE：P0 AI 旁路

R1 的 P0 AI 建议优先“资料字段提取 + 缺口/澄清建议”，而不是已被 Release 1 明确排除产品化的图片近似 BOM。

- `FEAT-AI-RUN-CONTROL`：模型、提示、Schema、路由和输入版本。
- `FEAT-AI-DOC-EXTRACTION`：字段候选和来源定位。
- `FEAT-AI-GAP-SUGGESTION`：缺失信息和澄清建议。
- `FEAT-AI-VALIDATION`：未知字段、非法单位和来源缺失失败关闭。
- `FEAT-AI-HUMAN-REVIEW`：接受、修改、拒绝和原值保留。
- `FEAT-AI-INJECTION-DEFENSE`：不可信内容、出站限制、集团内跨法人/实验室/客户越权和集团外独立数据平面测试。
- `FEAT-AI-DEGRADE`：关闭 AI 后完整人工流程。
- `FEAT-AI-EVALUATION`：固定评估集、阈值、停止条件和回归。

## 12. EP-OPERATIONS：上线与验证

- `FEAT-OPS-MIGRATION`：迁移来源、批次、转换、错误和对账。
- `FEAT-OPS-FUTURE-CONTRACTS`：其余行业序列化与契约测试。
- `FEAT-OPS-PERFORMANCE`：容量基线和性能测试。
- `FEAT-OPS-SECURITY`：威胁模型、扫描和渗透测试。
- `FEAT-OPS-BACKUP-DR`：备份、RPO/RTO 和全链恢复。
- `FEAT-OPS-SHADOW-RUN`：影子运行和差异对账。
- `FEAT-OPS-GO-LIVE`：小流量切换、回滚和上线批准。

## 13. 建议下一批任务卡

```text
ATC-GOV-001  冻结 R1 适用性基线
ATC-PLT-001  请求上下文与对象级授权
ATC-PLT-002  事务内审计和发件箱
ATC-SCP-001  ScopeLine 生产可用门禁
ATC-QTY-001  不可变数量流水与并发预留
ATC-TEX-001  纺织样品需求和互斥裁样门禁
ATC-TEX-002  CuttingPlan
ATC-TEX-003  调湿/洗涤及超差
ATC-ALLOC-001 任务分配资格
ATC-BATCH-001 制备/分析批
ATC-INST-001 首类仪器导入
ATC-QC-001   QC 影响传播
ATC-RESULT-001 结果来源与采用
ATC-RETEST-001 复测预先采用规则
ATC-RPT-001  报告签发门禁
ATC-RPT-002  报告更正版本
ATC-BILL-001 唯一计费事实
ATC-AI-001   资料抽取与缺口建议
```
