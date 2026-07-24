<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: project-spec-catalog
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# 结构化规格目录

| 版本键 | 类型 | 状态 | 标题 | 变更级别 | 影响模块 | 指纹 |
|---|---|---|---|---|---|---|
| `AC-DEPLOY-001@0.1.0` | `acceptance` | `in_review` | 集团间独立数据平面 | `major` | deployment-test, security-test, disaster-recovery, infrastructure-evidence | `1a8c17fd6201` |
| `AC-DEPLOY-001@1.0.0` | `acceptance` | `in_review` | 集团间独立数据平面真实交叉访问验收 | `major` | deployment-test, runtime-security-test, identity-test, telemetry-test, disaster-recovery, infrastructure-evidence | `d09e3a7d43b1` |
| `AC-ID-001@0.1.0` | `acceptance` | `in_review` | 身份错配 | `major` | identity, exception, conditional-acceptance, audit, automated-test | `a56a740130e6` |
| `AC-REC-001@0.1.0` | `acceptance` | `in_review` | 隔离控制 | `major` | receiving, sample-preparation, task-allocation, audit, automated-test | `8e184359a694` |
| `AC-SEC-001@0.1.0` | `acceptance` | `in_review` | 集团内多维越权防护 | `major` | authorization, search, export, object-storage, ai-retrieval, automated-test | `e762dae769c4` |
| `AC-SEC-001@1.0.0` | `acceptance` | `approved` | 集团内收样多维越权防护 | `major` | authorization, receiving, automated-test, audit | `da7cd0c5db0a` |
| `ATC-PLT-000@0.1.0` | `story` | `proposed` | 建立可验证的模块化单体工程骨架 | `major` | engineering-skeleton, repository, api-host, worker-host, web-shell, module-boundaries, postgresql, identity, object-storage, outbox, audit, observability, ci, deployment, automated-test | `af9924a0b1fa` |
| `ATC-PLT-000@1.0.0` | `story` | `proposed` | 建立可验证的模块化单体工程骨架 | `major` | engineering-skeleton, repository, api-host, worker-host, web-shell, module-boundaries, postgresql, identity, object-storage, outbox, audit, observability, ci, deployment, automated-test | `f45a6ee6de5f` |
| `ATC-PLT-003@1.0.0` | `story` | `approved` | 建立业务模块接入与验证通道 | `major` | module-composition, api-host, worker-host, web-composition, architecture-tests, verification | `b2a7af44a3db` |
| `ATC-REC-001@0.1.0` | `story` | `proposed` | 登记到货批、包装单元和收到实物 | `minor` | receiving, authorization, audit, receiving-ui, automated-test | `9b8952decd3e` |
| `ATC-REC-001@1.0.0` | `story` | `proposed` | 登记到货批、包装单元和收到实物 | `minor` | receiving, authorization, audit, receiving-ui, automated-test | `2c78ba34a872` |
| `ATC-REC-001@2.0.0` | `story` | `approved` | 登记到货批、包装单元和收到实物 | `major` | receiving, authorization, audit, outbox, receiving-ui, automated-test | `5f5d2d7c7e2f` |
| `ATC-REC-002@0.1.0` | `story` | `proposed` | 生成、打印并校验包装和实物标识 | `minor` | receiving, identifier, barcode, mobile, audit, automated-test | `01083113a16c` |
| `ATC-REC-002@1.0.0` | `story` | `proposed` | 生成、打印并校验包装和实物标识 | `minor` | receiving, identifier, barcode, mobile, audit, automated-test | `3d08c534ec64` |
| `ATC-REC-002@2.0.0` | `story` | `approved` | 生成、打印并校验包装和实物标识 | `major` | receiving, identifier, barcode, label-printing, worker, scan-resolution, audit, automated-test | `89010ab6fd1a` |
| `ATC-REC-003@0.1.0` | `story` | `proposed` | 身份评估前实施统一隔离门禁 | `major` | receiving, disassembly, sample-preparation, task-allocation, authorization, audit, automated-test | `98da53ec10f9` |
| `ATC-REC-003@1.0.0` | `story` | `proposed` | 身份评估前实施统一隔离门禁 | `major` | receiving, disassembly, sample-preparation, task-allocation, authorization, audit, automated-test | `cb41b44b7f3b` |
| `ATC-REC-004@0.1.0` | `story` | `proposed` | 记录身份证据并形成匹配或冲突结论 | `major` | identity, receiving, exception, evidence, authorization, audit, automated-test | `a26dc988872b` |
| `ATC-REC-004@1.0.0` | `story` | `proposed` | 记录身份证据并形成匹配或冲突结论 | `major` | identity, receiving, exception, evidence, authorization, audit, automated-test | `8b686110ac21` |
| `ATC-REC-005@0.1.0` | `story` | `proposed` | 处理收样异常并执行授权决定 | `major` | exception, receiving, identity, scope-change, authorization, audit, automated-test | `0cbee64dcc55` |
| `ATC-REC-005@1.0.0` | `story` | `proposed` | 处理收样异常并执行授权决定 | `major` | exception, receiving, identity, scope-change, authorization, audit, automated-test | `285d13528f6d` |
| `ATC-REC-006@0.1.0` | `story` | `proposed` | 受控解除隔离并发布执行资格 | `major` | receiving, identity, exception, outbox, lab-execution, audit, automated-test | `d385a6cf535d` |
| `ATC-REC-006@1.0.0` | `story` | `proposed` | 受控解除隔离并发布执行资格 | `major` | receiving, identity, exception, outbox, lab-execution, audit, automated-test | `e40cd4a7c107` |
| `BUS-PROD-003@0.1.0` | `requirement` | `in_review` | 被业务引用的产品版本禁止原地修改 | `major` | product, product-variant, versioning, impact-analysis, evidence | `faf2128cf612` |
| `BUS-REQ-003@0.1.0` | `requirement` | `in_review` | 要求更新生成影响清单且不改写冻结委托 | `major` | requirements, impact-analysis, service-order, report-template, migration | `344c1c5fe26c` |
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
| `OD-009@0.1.0` | `decision` | `proposed` | 收到实物唯一识别粒度 | `major` | receiving, identifier, barcode, lineage | `962080700bba` |
| `OD-009@1.0.0` | `decision` | `approved` | 试点玩具收到实物唯一识别粒度 | `major` | receiving, identifier, barcode, lineage, toy-pilot | `d207509591d2` |
| `OD-020@0.1.0` | `decision` | `proposed` | 容量、并发与部署拓扑基线 | `major` | architecture, performance, availability, disaster-recovery | `aec2558b472f` |
| `OD-025@0.1.0` | `decision` | `proposed` | 平台内核、行业包与技术包边界 | `major` | modular-monolith, industry-packs, technical-packs, configuration | `d29f5f9dc805` |
| `OD-027@0.1.0` | `decision` | `proposed` | TestScopeMatrix 范围行粒度与变更影响 | `major` | test-scope, quotation, planning, reporting | `3b8ac36dfd16` |
| `OD-029@0.1.0` | `decision` | `proposed` | 认可范围数据粒度与签发门禁 | `major` | accreditation, result-review, reporting, signature | `85e197b51ea7` |
| `OD-030@0.1.0` | `decision` | `proposed` | 各方法族最小执行记录与外部系统边界 | `major` | execution, batch, qc, raw-data, instrument-integration | `03dbc6d00f58` |
| `OD-031@0.1.0` | `decision` | `proposed` | 首期条码、移动流程和仪器接口清单 | `major` | barcode, mobile, instrument-integration, validation-data | `3626d4882314` |
| `OD-031@1.0.0` | `decision` | `approved` | 首期条码、打印和扫码流程 | `major` | barcode, label-printing, receiving, scan-resolution, audit | `04a15707f209` |
| `OD-032@0.1.0` | `decision` | `proposed` | 多方参与角色、访问和付款权限模型 | `major` | party, authorization, report-delivery, billing | `0664721d0fb0` |
| `OD-034@0.1.0` | `decision` | `proposed` | 检测结论层级与全面合规引用边界 | `major` | conformity, reporting, external-compliance-reference | `d9645e91b677` |
| `OPS-EXC-001@0.1.0` | `requirement` | `in_review` | 收样异常分类建档 | `minor` | exception, receiving, identity, evidence, workflow | `84fdb3643f7e` |
| `OPS-EXC-002@0.1.0` | `requirement` | `in_review` | 异常不得自动降低范围或默认条件接收 | `major` | exception, scope-change, conditional-acceptance, authorization, audit | `8cf1d2cd7b59` |
| `OPS-IDENTITY-001@0.1.0` | `requirement` | `in_review` | 分离客户声明身份、实验室观察和匹配结论 | `minor` | identity, evidence, data-model, audit, ux | `0a417c32d185` |
| `OPS-IDENTITY-002@0.1.0` | `requirement` | `in_review` | 实物身份映射到实际送检项与受试配置 | `major` | identity, submission, product-variant, configuration, task-allocation | `b00534b8eb9a` |
| `OPS-IDENTITY-003@0.1.0` | `requirement` | `in_review` | 身份异常对象禁止进入制样和检测 | `major` | identity, sample-preparation, task-allocation, authorization, audit | `7eb34bdf50c2` |
| `OPS-RECEIPT-001@0.1.0` | `requirement` | `in_review` | 分别登记到货批、包装单元和收到的实物 | `minor` | receiving, data-model, api, ux, audit | `7fc24d0d2a7d` |
| `OPS-RECEIPT-001@1.0.0` | `requirement` | `approved` | 分别登记到货批、包装单元和收到的实物 | `major` | receiving, data-model, api, ux, audit, quarantine | `f8458e721ae4` |
| `OPS-RECEIPT-002@0.1.0` | `requirement` | `in_review` | 包装单元与收到实物唯一标识 | `minor` | receiving, identifier, barcode, mobile, audit | `c226b56a7fbf` |
| `OPS-RECEIPT-002@1.0.0` | `requirement` | `approved` | 包装单元与收到实物唯一标识 | `major` | receiving, identifier, barcode, label-printing, scan-resolution, audit | `1938f7c2e390` |
| `OPS-RECEIPT-003@0.1.0` | `requirement` | `in_review` | 身份评估完成前保持隔离 | `major` | receiving, identity, sample-preparation, task-allocation, audit | `3e4f6d543a54` |
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
