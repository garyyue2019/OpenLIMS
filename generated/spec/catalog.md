<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: project-spec-catalog
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# 结构化规格目录

| 版本键 | 类型 | 状态 | 标题 | 变更级别 | 影响模块 | 指纹 |
|---|---|---|---|---|---|---|
| `AC-AI-003@1.0.0` | `acceptance` | `approved` | AI 输出失败关闭 | `major` | ai, fail-closed, automated-test | `cd7af0fa22c9` |
| `AC-BATCH-001@1.0.0` | `acceptance` | `approved` | 批次 QC 影响传播 | `major` | batch, qc, freeze-propagation, audit, automated-test | `6edaaa437d6e` |
| `AC-BILL-001@1.0.0` | `acceptance` | `approved` | 防重复计费 | `major` | billing, audit, automated-test | `8d0d6511cad8` |
| `AC-DEPLOY-001@0.1.0` | `acceptance` | `in_review` | 集团间独立数据平面 | `major` | deployment-test, security-test, disaster-recovery, infrastructure-evidence | `1a8c17fd6201` |
| `AC-DEPLOY-001@1.0.0` | `acceptance` | `in_review` | 集团间独立数据平面真实交叉访问验收 | `major` | deployment-test, runtime-security-test, identity-test, telemetry-test, disaster-recovery, infrastructure-evidence | `d09e3a7d43b1` |
| `AC-ELEC-003@1.0.0` | `acceptance` | `approved` | 破坏性分配互斥与资格门禁全链 | `major` | allocation, destructive-exclusion, eligibility-gate, audit, automated-test | `735b5f0293cd` |
| `AC-ID-001@0.1.0` | `acceptance` | `in_review` | 身份错配 | `major` | identity, exception, conditional-acceptance, audit, automated-test | `a56a740130e6` |
| `AC-ID-001@1.0.0` | `acceptance` | `approved` | 身份评估三层事实和冲突事件 | `major` | identity-assessment, receiving, audit, automated-test | `8bc40f09987e` |
| `AC-QTY-001@1.0.0` | `acceptance` | `approved` | 并发超分配阻断与不可变流水链 | `major` | quantity, concurrency, availability-gate, audit, automated-test | `2491d853f11e` |
| `AC-REC-001@0.1.0` | `acceptance` | `in_review` | 隔离控制 | `major` | receiving, sample-preparation, task-allocation, audit, automated-test | `8e184359a694` |
| `AC-REC-001@1.0.0` | `acceptance` | `approved` | 隔离资格统一失败关闭 | `major` | receiving, sample-preparation, task-allocation, audit, automated-test | `9d4fdb3bf6b8` |
| `AC-RETEST-001@1.0.0` | `acceptance` | `approved` | 复测采用 | `major` | result, retest, adoption, audit, automated-test | `578ffe8b2ade` |
| `AC-SCOPE-001@1.0.0` | `acceptance` | `approved` | ScopeLine 完整链与生产资格门禁 | `major` | test-scope, production-gate, audit, automated-test | `0a563aaed598` |
| `AC-SEC-001@0.1.0` | `acceptance` | `in_review` | 集团内多维越权防护 | `major` | authorization, search, export, object-storage, ai-retrieval, automated-test | `e762dae769c4` |
| `AC-SEC-001@1.0.0` | `acceptance` | `approved` | 集团内收样多维越权防护 | `major` | authorization, receiving, automated-test, audit | `da7cd0c5db0a` |
| `AC-TEXTILE-001@1.0.0` | `acceptance` | `approved` | 样品不足与互斥裁样契约验收 | `major` | textile, sample-requirement, destructive-exclusion, automated-test | `5bf4aeacf8ab` |
| `AC-TEXTILE-003@1.0.0` | `acceptance` | `approved` | 裁样方向与预处理超差契约验收 | `major` | textile, preconditioning, cutting-plan, automated-test | `7f112061a2b3` |
| `ATC-AI-001@1.0.0` | `story` | `approved` | 实施 DEV-016 AI 资料抽取与缺口建议契约切片 | `major` | ai, run-control, fact-class, extraction, gap-suggestion, human-review, contracts, serialization, automated-test | `ba2fc8779dce` |
| `ATC-ALLOC-001@1.0.0` | `story` | `approved` | 实施 DEV-010 任务分配资格 | `major` | allocation, receiving, scope, quantity, eligibility-gate, authorization, audit, outbox, automated-test | `d8e63c13c345` |
| `ATC-BATCH-001@1.0.0` | `story` | `approved` | 实施 DEV-013 制备/分析批最小切片 | `major` | batch, allocation, qc, raw-data, authorization, audit, outbox, automated-test | `6028422fb5b0` |
| `ATC-BILL-001@1.0.0` | `story` | `approved` | 实施 DEV-015 唯一计费事实 | `major` | billing, result, authorization, audit, outbox, automated-test | `b6802a9d2521` |
| `ATC-PLT-000@0.1.0` | `story` | `proposed` | 建立可验证的模块化单体工程骨架 | `major` | engineering-skeleton, repository, api-host, worker-host, web-shell, module-boundaries, postgresql, identity, object-storage, outbox, audit, observability, ci, deployment, automated-test | `af9924a0b1fa` |
| `ATC-PLT-000@1.0.0` | `story` | `proposed` | 建立可验证的模块化单体工程骨架 | `major` | engineering-skeleton, repository, api-host, worker-host, web-shell, module-boundaries, postgresql, identity, object-storage, outbox, audit, observability, ci, deployment, automated-test | `f45a6ee6de5f` |
| `ATC-PLT-001@1.0.0` | `story` | `approved` | 实施 DEV-018 请求上下文与对象级授权正式化 | `major` | platform, authorization, request-context, correlation, cross-module, audit, automated-test | `013e485b4c12` |
| `ATC-PLT-002@1.0.0` | `story` | `approved` | 实施 DEV-017 事务内审计和发件箱正式化与全链验证 | `major` | platform, audit, outbox, migration, cross-module, scope, quantity, allocation, batch, result, billing, automated-test | `83df7240aff4` |
| `ATC-PLT-003@1.0.0` | `story` | `approved` | 建立业务模块接入与验证通道 | `major` | module-composition, api-host, worker-host, web-composition, architecture-tests, verification | `b2a7af44a3db` |
| `ATC-QTY-001@1.0.0` | `story` | `approved` | 实施 DEV-009 不可变数量流水与并发预留 | `major` | quantity, authorization, audit, outbox, availability-gate, automated-test | `29b0d4a4add0` |
| `ATC-REC-001@0.1.0` | `story` | `proposed` | 登记到货批、包装单元和收到实物 | `minor` | receiving, authorization, audit, receiving-ui, automated-test | `9b8952decd3e` |
| `ATC-REC-001@1.0.0` | `story` | `proposed` | 登记到货批、包装单元和收到实物 | `minor` | receiving, authorization, audit, receiving-ui, automated-test | `2c78ba34a872` |
| `ATC-REC-001@2.0.0` | `story` | `approved` | 登记到货批、包装单元和收到实物 | `major` | receiving, authorization, audit, outbox, receiving-ui, automated-test | `5f5d2d7c7e2f` |
| `ATC-REC-002@0.1.0` | `story` | `proposed` | 生成、打印并校验包装和实物标识 | `minor` | receiving, identifier, barcode, mobile, audit, automated-test | `01083113a16c` |
| `ATC-REC-002@1.0.0` | `story` | `proposed` | 生成、打印并校验包装和实物标识 | `minor` | receiving, identifier, barcode, mobile, audit, automated-test | `3d08c534ec64` |
| `ATC-REC-002@2.0.0` | `story` | `approved` | 生成、打印并校验包装和实物标识 | `major` | receiving, identifier, barcode, label-printing, worker, scan-resolution, audit, automated-test | `89010ab6fd1a` |
| `ATC-REC-003@0.1.0` | `story` | `proposed` | 身份评估前实施统一隔离门禁 | `major` | receiving, disassembly, sample-preparation, task-allocation, authorization, audit, automated-test | `98da53ec10f9` |
| `ATC-REC-003@1.0.0` | `story` | `proposed` | 身份评估前实施统一隔离门禁 | `major` | receiving, disassembly, sample-preparation, task-allocation, authorization, audit, automated-test | `cb41b44b7f3b` |
| `ATC-REC-003@2.0.0` | `story` | `approved` | 实施隔离门禁和 ReceivedItem 身份评估 | `major` | receiving, identity-assessment, lab-execution-gate, authorization, audit, automated-test | `8f80f420f858` |
| `ATC-REC-004@0.1.0` | `story` | `proposed` | 记录身份证据并形成匹配或冲突结论 | `major` | identity, receiving, exception, evidence, authorization, audit, automated-test | `a26dc988872b` |
| `ATC-REC-004@1.0.0` | `story` | `proposed` | 记录身份证据并形成匹配或冲突结论 | `major` | identity, receiving, exception, evidence, authorization, audit, automated-test | `8b686110ac21` |
| `ATC-REC-005@0.1.0` | `story` | `proposed` | 处理收样异常并执行授权决定 | `major` | exception, receiving, identity, scope-change, authorization, audit, automated-test | `0cbee64dcc55` |
| `ATC-REC-005@1.0.0` | `story` | `proposed` | 处理收样异常并执行授权决定 | `major` | exception, receiving, identity, scope-change, authorization, audit, automated-test | `285d13528f6d` |
| `ATC-REC-005@2.0.0` | `story` | `approved` | 实施 DEV-006 收样异常与授权决定 | `major` | exception, receiving, identity, authorization, audit, outbox, automated-test | `00c62c57cf6c` |
| `ATC-REC-006@0.1.0` | `story` | `proposed` | 受控解除隔离并发布执行资格 | `major` | receiving, identity, exception, outbox, lab-execution, audit, automated-test | `d385a6cf535d` |
| `ATC-REC-006@1.0.0` | `story` | `proposed` | 受控解除隔离并发布执行资格 | `major` | receiving, identity, exception, outbox, lab-execution, audit, automated-test | `e40cd4a7c107` |
| `ATC-REC-006@2.0.0` | `story` | `approved` | 实施 DEV-007 受控放行与版本固定资格 | `major` | receiving, identity, exception, authorization, audit, outbox, lab-execution-gate, automated-test | `fdf0bc2308e1` |
| `ATC-RESULT-001@1.0.0` | `story` | `approved` | 实施 DEV-014 结果来源与采用 | `major` | result, batch, raw-data, provenance, adoption, retest, authorization, audit, outbox, automated-test | `89ae098800fc` |
| `ATC-SCP-001@1.0.0` | `story` | `approved` | 实施 DEV-008 ScopeLine 生产可用门禁 | `major` | scope, authorization, audit, outbox, production-gate, automated-test | `1ae1eaf0c359` |
| `ATC-TEX-001@1.0.0` | `story` | `approved` | 实施 DEV-011 纺织样品需求未来适配契约切片 | `major` | textile, sample-requirement, cutting-plan, contracts, serialization, automated-test | `2174f31c3221` |
| `ATC-TEX-003@1.0.0` | `story` | `approved` | 实施 DEV-012 纺织调湿/洗涤及超差契约切片 | `major` | textile, preconditioning, out-of-tolerance, contracts, serialization, automated-test | `d39d389de0fb` |
| `BUS-AI-001@1.0.0` | `requirement` | `approved` | AI 运行控制封套契约 | `major` | ai, run-control, contracts, serialization | `95e7831fe908` |
| `BUS-AI-002@1.0.0` | `requirement` | `approved` | 事实类别与不得提升规则 | `major` | ai, fact-class, rules | `89380e537235` |
| `BUS-AI-003@1.0.0` | `requirement` | `approved` | 抽取候选、缺口建议与人工处置契约及失败关闭 | `major` | ai, extraction, gap-suggestion, human-review, rules | `442696dd4fdf` |
| `BUS-ALLOC-001@1.0.0` | `requirement` | `approved` | 版本固定的 TestObjectAllocation 分配事实 | `major` | allocation, versioning, authorization, audit, outbox | `3eeead680cf1` |
| `BUS-ALLOC-002@1.0.0` | `requirement` | `approved` | 分配前三端口资格门禁 | `major` | allocation, eligibility-gate, receiving, scope, quantity, audit | `8f439560e509` |
| `BUS-ALLOC-003@1.0.0` | `requirement` | `approved` | 并发分配与破坏性互斥阻断 | `major` | allocation, concurrency, destructive-exclusion, authorization, audit | `09e85a6926d8` |
| `BUS-BATCH-001@1.0.0` | `requirement` | `approved` | 类型化不可变批次事实 | `major` | batch, execution, versioning, authorization, audit, outbox | `4e5e753adce8` |
| `BUS-BATCH-002@1.0.0` | `requirement` | `approved` | 跨委托成员、客户隔离与外部证据引用 | `major` | batch, allocation, customer-isolation, raw-data, audit | `021ecf7a878e` |
| `BUS-BATCH-003@1.0.0` | `requirement` | `approved` | 批次冻结与全量影响传播 | `major` | batch, qc, freeze-propagation, audit, outbox | `0293aa7be242` |
| `BUS-BILL-001@1.0.0` | `requirement` | `approved` | 服务完成事实生成唯一计费候选 | `major` | billing, result, versioning, authorization, audit, outbox | `1ea66d5b70f1` |
| `BUS-BILL-002@1.0.0` | `requirement` | `approved` | 零金额证据与原因 | `major` | billing, audit | `e8ad36abf78c` |
| `BUS-BILL-003@1.0.0` | `requirement` | `approved` | 正负调整证据更正 | `major` | billing, versioning, audit | `58850683ebc9` |
| `BUS-PLT-001@1.0.0` | `requirement` | `approved` | 平台审计与发件箱组合不变量 | `major` | platform, audit, outbox, migration, cross-module, transaction | `23725a2fb414` |
| `BUS-PLT-002@1.0.0` | `requirement` | `approved` | 请求上下文与对象级授权不变量 | `major` | platform, authorization, request-context, correlation, cross-module, audit | `f4238a62d8ff` |
| `BUS-PROD-003@0.1.0` | `requirement` | `in_review` | 被业务引用的产品版本禁止原地修改 | `major` | product, product-variant, versioning, impact-analysis, evidence | `faf2128cf612` |
| `BUS-QTY-001@1.0.0` | `requirement` | `approved` | 不可变数量流水与冲销重记 | `major` | quantity, versioning, authorization, audit, outbox | `47e4cca52ccf` |
| `BUS-QTY-002@1.0.0` | `requirement` | `approved` | 账户级计量维度、精度与守恒公差配置 | `major` | quantity, measurement, authorization, audit | `a35cedb07b7f` |
| `BUS-QTY-003@1.0.0` | `requirement` | `approved` | 负余额、超分配与并发预留阻断 | `major` | quantity, concurrency, availability-gate, authorization, audit | `519b56d09ba9` |
| `BUS-REQ-003@0.1.0` | `requirement` | `in_review` | 要求更新生成影响清单且不改写冻结委托 | `major` | requirements, impact-analysis, service-order, report-template, migration | `344c1c5fe26c` |
| `BUS-RES-001@1.0.0` | `requirement` | `approved` | 不可变结果观测与原始证据引用 | `major` | result, raw-data, batch, versioning, audit, outbox | `9a52d7f4d3a1` |
| `BUS-RES-002@1.0.0` | `requirement` | `approved` | 追加式结果来源图 | `major` | result, provenance, versioning, audit | `236b56d1fb85` |
| `BUS-RES-003@1.0.0` | `requirement` | `approved` | 预先采用规则与唯一有效采用结果 | `major` | result, adoption, retest, versioning, audit | `5259ecca35c5` |
| `BUS-SCOPE-001@1.0.0` | `requirement` | `approved` | 版本化 TestScopeMatrix 批准基线 | `major` | test-scope, versioning, authorization, audit, outbox | `b1fc752a6298` |
| `BUS-SCOPE-002@1.0.0` | `requirement` | `approved` | ScopeLine 完整引用与 EvaluationMode 条件语义 | `major` | scope-line, method, sample-requirement, evaluation, production-gate | `b416895da744` |
| `BUS-SCOPE-003@1.0.0` | `requirement` | `approved` | 未经批准候选不得获得生产资格 | `major` | production-gate, quotation-candidate, ai-candidate, authorization, audit | `7fc3f3b546ee` |
| `BUS-TEX-001@1.0.0` | `requirement` | `approved` | 纺织样品需求契约模型 | `major` | textile, sample-requirement, contracts, serialization | `035fe26c8d89` |
| `BUS-TEX-002@1.0.0` | `requirement` | `approved` | 互斥裁样与样品不足失败关闭规则 | `major` | textile, sample-requirement, destructive-exclusion, rules | `c03383bc9852` |
| `BUS-TEX-003@1.0.0` | `requirement` | `approved` | CuttingPlan 序列化契约 | `major` | textile, cutting-plan, contracts, serialization | `f727ac469a11` |
| `BUS-TEX-004@1.0.0` | `requirement` | `approved` | 调湿与洗涤计划/实际契约模型 | `major` | textile, preconditioning, contracts, serialization | `71df32f58af7` |
| `BUS-TEX-005@1.0.0` | `requirement` | `approved` | 预处理超差评估与报告阻断规则 | `major` | textile, preconditioning, out-of-tolerance, rules | `4be32afd90e8` |
| `ED-001@0.1.0` | `decision` | `proposed` | 应用技术栈与工程仓库基线 | `major` | repository, backend, frontend, database, ci, deployment | `1f7d1cb22b55` |
| `ED-001@1.0.0` | `decision` | `proposed` | 工程技术栈、仓库与版本锁基线 | `major` | repository, backend, frontend, database, ci, non-production-deployment, supply-chain | `0bb0bcc2f006` |
| `ED-001@2.0.0` | `decision` | `approved` | 已验证的工程技术栈与版本锁基线 | `major` | repository, backend, frontend, database, ci, non-production-deployment, supply-chain | `fad2f23d20bd` |
| `ED-002@1.0.0` | `decision` | `proposed` | 模块化单体代码与持久化边界基线 | `major` | architecture, host, building-blocks, module-boundaries, database, migrations, contracts, outbox, architecture-tests | `2bca4c8f643c` |
| `NFR-ARCH-001@0.1.0` | `nfr` | `in_review` | 首期模块化单体 | `major` | architecture, module-boundaries, ci | `7d8902e4ce56` |
| `NFR-ARCH-001@1.0.0` | `nfr` | `in_review` | 模块化单体边界强制门禁 | `major` | architecture, module-boundaries, database-permissions, contracts, ci | `29fe5808e5f0` |
| `NFR-ARCH-001@2.0.0` | `nfr` | `approved` | 模块化单体边界强制门禁 | `major` | architecture, module-boundaries, database-permissions, contracts, ci, receiving | `58fc78930c62` |
| `NFR-ARCH-002@0.1.0` | `nfr` | `in_review` | 事务发件箱与幂等消费者 | `major` | outbox, integration, reliability, observability | `cf08fe497161` |
| `NFR-ARCH-002@1.0.0` | `nfr` | `in_review` | 事务Outbox、Inbox与并发恢复语义 | `major` | outbox, inbox, workers, integration, concurrency, reliability, recovery, audit, observability | `6ec21b4b8013` |
| `OD-001@0.1.0` | `decision` | `proposed` | Release 1 唯一灯塔试点切片 | `major` | release-governance, all-release-1-modules | `b6858d2f1d5b` |
| `OD-002@1.0.0` | `decision` | `approved` | 集团多机构与单集团独立部署模式 | `major` | organization-model, deployment-boundary, authorization, data-platform, ai-security, backup-recovery | `81a2d96fb09e` |
| `OD-005@0.1.0` | `decision` | `proposed` | 条件接收与身份异常审批矩阵 | `major` | receiving, identity, exception, sample-preparation, task-allocation | `10daafe00d40` |
| `OD-005@1.0.0` | `decision` | `approved` | DEV-006 精简条件接收与异常审批矩阵 | `major` | receiving, identity, exception, authorization, audit | `3796fceb3efe` |
| `OD-009@0.1.0` | `decision` | `proposed` | 收到实物唯一识别粒度 | `major` | receiving, identifier, barcode, lineage | `962080700bba` |
| `OD-009@1.0.0` | `decision` | `approved` | 试点玩具收到实物唯一识别粒度 | `major` | receiving, identifier, barcode, lineage, toy-pilot | `d207509591d2` |
| `OD-010@1.0.0` | `decision` | `approved` | DEV-009 数量账户计量口径与轻量过账 | `major` | quantity, measurement, authorization, audit, outbox | `5c8856d87619` |
| `OD-020@0.1.0` | `decision` | `proposed` | 容量、并发与部署拓扑基线 | `major` | architecture, performance, availability, disaster-recovery | `aec2558b472f` |
| `OD-025@0.1.0` | `decision` | `proposed` | 平台内核、行业包与技术包边界 | `major` | modular-monolith, industry-packs, technical-packs, configuration | `d29f5f9dc805` |
| `OD-027@0.1.0` | `decision` | `proposed` | TestScopeMatrix 范围行粒度与变更影响 | `major` | test-scope, quotation, planning, reporting | `3b8ac36dfd16` |
| `OD-027@1.0.0` | `decision` | `approved` | DEV-008 ScopeLine 最小粒度与轻量批准 | `major` | test-scope, production-gate, authorization, audit, outbox | `ba8bd756a337` |
| `OD-029@0.1.0` | `decision` | `proposed` | 认可范围数据粒度与签发门禁 | `major` | accreditation, result-review, reporting, signature | `85e197b51ea7` |
| `OD-030@0.1.0` | `decision` | `proposed` | 各方法族最小执行记录与外部系统边界 | `major` | execution, batch, qc, raw-data, instrument-integration | `03dbc6d00f58` |
| `OD-030@1.0.0` | `decision` | `approved` | DEV-013 最小执行记录与外部系统边界 | `major` | execution, batch, qc, raw-data, instrument-integration | `15d19e324c5b` |
| `OD-031@0.1.0` | `decision` | `proposed` | 首期条码、移动流程和仪器接口清单 | `major` | barcode, mobile, instrument-integration, validation-data | `3626d4882314` |
| `OD-031@1.0.0` | `decision` | `approved` | 首期条码、打印和扫码流程 | `major` | barcode, label-printing, receiving, scan-resolution, audit | `04a15707f209` |
| `OD-032@0.1.0` | `decision` | `proposed` | 多方参与角色、访问和付款权限模型 | `major` | party, authorization, report-delivery, billing | `0664721d0fb0` |
| `OD-034@0.1.0` | `decision` | `proposed` | 检测结论层级与全面合规引用边界 | `major` | conformity, reporting, external-compliance-reference | `d9645e91b677` |
| `OD-035@1.0.0` | `decision` | `approved` | DEV-005 隔离与身份评估边界 | `major` | receiving, identity-assessment, lab-execution-gate, audit | `2edfbe80e573` |
| `OPS-EXC-001@0.1.0` | `requirement` | `in_review` | 收样异常分类建档 | `minor` | exception, receiving, identity, evidence, workflow | `84fdb3643f7e` |
| `OPS-EXC-001@1.0.0` | `requirement` | `approved` | DEV-006 收样异常分类建档 | `major` | exception, receiving, identity, evidence, workflow | `f04d497b0ec8` |
| `OPS-EXC-002@0.1.0` | `requirement` | `in_review` | 异常不得自动降低范围或默认条件接收 | `major` | exception, scope-change, conditional-acceptance, authorization, audit | `8cf1d2cd7b59` |
| `OPS-EXC-002@1.0.0` | `requirement` | `approved` | DEV-006 异常不得自动降低范围或默认条件接收 | `major` | exception, scope-change, conditional-acceptance, authorization, audit | `4e46384ec83d` |
| `OPS-IDENTITY-001@0.1.0` | `requirement` | `in_review` | 分离客户声明身份、实验室观察和匹配结论 | `minor` | identity, evidence, data-model, audit, ux | `0a417c32d185` |
| `OPS-IDENTITY-001@1.0.0` | `requirement` | `approved` | 分离客户声明、实验室观察和身份结论 | `major` | identity-assessment, evidence, data-model, audit, ux | `97812afb50d8` |
| `OPS-IDENTITY-002@0.1.0` | `requirement` | `in_review` | 实物身份映射到实际送检项与受试配置 | `major` | identity, submission, product-variant, configuration, task-allocation | `b00534b8eb9a` |
| `OPS-IDENTITY-002@1.0.0` | `requirement` | `approved` | 形成 ReceivedItem 人工身份结论 | `major` | identity-assessment, receiving, evidence, authorization | `acfd63403bd7` |
| `OPS-IDENTITY-003@0.1.0` | `requirement` | `in_review` | 身份异常对象禁止进入制样和检测 | `major` | identity, sample-preparation, task-allocation, authorization, audit | `7eb34bdf50c2` |
| `OPS-IDENTITY-003@1.0.0` | `requirement` | `approved` | 统一阻断未放行对象进入实验室执行 | `major` | receiving, sample-preparation, task-allocation, authorization, audit | `49098432e451` |
| `OPS-RECEIPT-001@0.1.0` | `requirement` | `in_review` | 分别登记到货批、包装单元和收到的实物 | `minor` | receiving, data-model, api, ux, audit | `7fc24d0d2a7d` |
| `OPS-RECEIPT-001@1.0.0` | `requirement` | `approved` | 分别登记到货批、包装单元和收到的实物 | `major` | receiving, data-model, api, ux, audit, quarantine | `f8458e721ae4` |
| `OPS-RECEIPT-002@0.1.0` | `requirement` | `in_review` | 包装单元与收到实物唯一标识 | `minor` | receiving, identifier, barcode, mobile, audit | `c226b56a7fbf` |
| `OPS-RECEIPT-002@1.0.0` | `requirement` | `approved` | 包装单元与收到实物唯一标识 | `major` | receiving, identifier, barcode, label-printing, scan-resolution, audit | `1938f7c2e390` |
| `OPS-RECEIPT-003@0.1.0` | `requirement` | `in_review` | 身份评估完成前保持隔离 | `major` | receiving, identity, sample-preparation, task-allocation, audit | `3e4f6d543a54` |
| `OPS-RECEIPT-003@1.0.0` | `requirement` | `approved` | 身份评估和受控放行前保持隔离 | `major` | receiving, identity-assessment, sample-preparation, task-allocation, audit | `4a0317294f87` |
| `ORG-COLLAB-001@0.1.0` | `requirement` | `in_review` | 集团内跨机构协作责任分离 | `major` | service-order, receiving, lab-execution, reporting, billing, authorization, audit | `be0c4ffabc11` |
| `ORG-COLLAB-001@1.0.0` | `requirement` | `approved` | 集团内跨机构协作责任分离 | `major` | service-order, receiving, lab-execution, reporting, billing, authorization, audit | `f1b8c3cc8e73` |
| `ORG-STRUCT-001@0.1.0` | `requirement` | `in_review` | 集团多机构组织层级 | `major` | organization-model, master-data, authorization-context, integration, audit | `433e35c3fb90` |
| `ORG-STRUCT-001@1.0.0` | `requirement` | `approved` | 集团多机构组织层级 | `major` | organization-model, master-data, authorization-context, integration, audit | `4161307d6356` |
| `REL-R1-RECEIVING-PILOT@0.1.0` | `release-baseline` | `proposed` | Release 1 收样与身份纵向切片候选基线 | `major` | release-governance, requirements-lock, receiving-pilot, migration, evidence | `bfbed529d7f7` |
| `REL-R1-RECEIVING-PILOT@1.0.0` | `release-baseline` | `proposed` | Release 1 收样与身份纵向切片候选基线 | `major` | release-governance, requirements-lock, receiving-pilot, migration, evidence | `711f02b3bdf7` |
| `RULE-004@0.1.0` | `rule` | `in_review` | 身份映射、任务使用与代表性分离 | `major` | identity, task-allocation, coverage, lineage | `cb054b841019` |
| `RULE-026@0.1.0` | `rule` | `in_review` | 配置升级只影响明确生效范围 | `major` | versioning, release-baseline, configuration, migration, evidence | `cfaf997d49a1` |
| `SEC-AUD-001@0.1.0` | `requirement` | `in_review` | 受控操作追加式审计事件 | `minor` | audit, all-domain-commands, observability | `8aa132fc72a4` |
| `SEC-AUD-001@1.0.0` | `requirement` | `in_review` | 受控命令的审计意图、失败尝试与追加账本 | `major` | audit, all-domain-commands, outbox, failure-handling, observability, privacy | `aea4087c5303` |
| `SEC-AUD-001@2.0.0` | `requirement` | `approved` | 受控命令的审计意图、失败尝试与追加账本 | `major` | audit, receiving-commands, outbox, failure-handling, observability, privacy | `e07e4dd75764` |
| `SEC-AUTH-001@0.1.0` | `requirement` | `in_review` | 服务端多维授权校验 | `major` | authorization, all-domain-commands, organization-scope, audit | `9f69be78d53d` |
| `SEC-AUTH-001@1.0.0` | `requirement` | `approved` | 收样命令服务端多维授权校验 | `major` | authorization, receiving-commands, organization-scope, audit | `22eff948c51e` |
| `SEC-DEPLOY-001@0.1.0` | `requirement` | `in_review` | 集团间独立部署与数据平面 | `major` | deployment, database, object-storage, secrets, messaging, search, ai-runtime, backup-recovery | `e02276ed1c4a` |
| `SEC-DEPLOY-001@1.0.0` | `requirement` | `in_review` | 集团间独立运行、数据、遥测与恢复平面 | `major` | deployment, runtime, identity, database, object-storage, secrets, messaging, search, ai-runtime, telemetry, backup-recovery | `2a581330d9a2` |
| `SEC-DEPLOY-001@2.0.0` | `requirement` | `approved` | 集团间独立运行、数据、遥测与恢复平面 | `major` | deployment, runtime, identity, database, object-storage, secrets, messaging, search, ai-runtime, telemetry, backup-recovery | `701cb0e82013` |
