<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: project-spec-catalog
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# 结构化规格目录

| 版本键 | 类型 | 状态 | 标题 | 变更级别 | 影响模块 | 指纹 |
|---|---|---|---|---|---|---|
| `AC-DEPLOY-001@0.1.0` | `acceptance` | `in_review` | 集团间独立数据平面 | `major` | deployment-test, security-test, disaster-recovery, infrastructure-evidence | `1a8c17fd6201` |
| `AC-ID-001@0.1.0` | `acceptance` | `in_review` | 身份错配 | `major` | identity, exception, conditional-acceptance, audit, automated-test | `a56a740130e6` |
| `AC-REC-001@0.1.0` | `acceptance` | `in_review` | 隔离控制 | `major` | receiving, sample-preparation, task-allocation, audit, automated-test | `8e184359a694` |
| `AC-SEC-001@0.1.0` | `acceptance` | `in_review` | 集团内多维越权防护 | `major` | authorization, search, export, object-storage, ai-retrieval, automated-test | `e762dae769c4` |
| `ATC-REC-001@0.1.0` | `story` | `proposed` | 登记到货批、包装单元和收到实物 | `minor` | receiving, authorization, audit, receiving-ui, automated-test | `071aeb85adf1` |
| `ATC-REC-002@0.1.0` | `story` | `proposed` | 生成、打印并校验包装和实物标识 | `minor` | receiving, identifier, barcode, mobile, audit, automated-test | `d7b86a278ed8` |
| `ATC-REC-003@0.1.0` | `story` | `proposed` | 身份评估前实施统一隔离门禁 | `major` | receiving, disassembly, sample-preparation, task-allocation, authorization, audit, automated-test | `3102bb7bcc51` |
| `ATC-REC-004@0.1.0` | `story` | `proposed` | 记录身份证据并形成匹配或冲突结论 | `major` | identity, receiving, exception, evidence, authorization, audit, automated-test | `5a9566ef366a` |
| `ATC-REC-005@0.1.0` | `story` | `proposed` | 处理收样异常并执行授权决定 | `major` | exception, receiving, identity, scope-change, authorization, audit, automated-test | `a108abc80862` |
| `ATC-REC-006@0.1.0` | `story` | `proposed` | 受控解除隔离并发布执行资格 | `major` | receiving, identity, exception, outbox, lab-execution, audit, automated-test | `ad0c4e4db885` |
| `BUS-PROD-003@0.1.0` | `requirement` | `in_review` | 被业务引用的产品版本禁止原地修改 | `major` | product, product-variant, versioning, impact-analysis, evidence | `faf2128cf612` |
| `BUS-REQ-003@0.1.0` | `requirement` | `in_review` | 要求更新生成影响清单且不改写冻结委托 | `major` | requirements, impact-analysis, service-order, report-template, migration | `344c1c5fe26c` |
| `ED-001@0.1.0` | `decision` | `proposed` | 应用技术栈与工程仓库基线 | `major` | repository, backend, frontend, database, ci, deployment | `a4210690ac62` |
| `NFR-ARCH-001@0.1.0` | `nfr` | `in_review` | 首期模块化单体 | `major` | architecture, module-boundaries, ci | `7d8902e4ce56` |
| `NFR-ARCH-002@0.1.0` | `nfr` | `in_review` | 事务发件箱与幂等消费者 | `major` | outbox, integration, reliability, observability | `cf08fe497161` |
| `OD-001@0.1.0` | `decision` | `proposed` | Release 1 唯一灯塔试点切片 | `major` | release-governance, all-release-1-modules | `5fe4ffb5dcfa` |
| `OD-002@1.0.0` | `decision` | `approved` | 集团多机构与单集团独立部署模式 | `major` | organization-model, deployment-boundary, authorization, data-platform, ai-security, backup-recovery | `81a2d96fb09e` |
| `OD-005@0.1.0` | `decision` | `proposed` | 条件接收与身份异常审批矩阵 | `major` | receiving, identity, exception, sample-preparation, task-allocation | `10daafe00d40` |
| `OD-009@0.1.0` | `decision` | `proposed` | 收到实物唯一识别粒度 | `major` | receiving, identifier, barcode, lineage | `962080700bba` |
| `OD-020@0.1.0` | `decision` | `proposed` | 容量、并发与部署拓扑基线 | `major` | architecture, performance, availability, disaster-recovery | `f360f3f9b949` |
| `OD-025@0.1.0` | `decision` | `proposed` | 平台内核、行业包与技术包边界 | `major` | modular-monolith, industry-packs, technical-packs, configuration | `4823d8880043` |
| `OD-027@0.1.0` | `decision` | `proposed` | TestScopeMatrix 范围行粒度与变更影响 | `major` | test-scope, quotation, planning, reporting | `3b8ac36dfd16` |
| `OD-029@0.1.0` | `decision` | `proposed` | 认可范围数据粒度与签发门禁 | `major` | accreditation, result-review, reporting, signature | `85e197b51ea7` |
| `OD-030@0.1.0` | `decision` | `proposed` | 各方法族最小执行记录与外部系统边界 | `major` | execution, batch, qc, raw-data, instrument-integration | `03dbc6d00f58` |
| `OD-031@0.1.0` | `decision` | `proposed` | 首期条码、移动流程和仪器接口清单 | `major` | barcode, mobile, instrument-integration, validation-data | `3626d4882314` |
| `OD-032@0.1.0` | `decision` | `proposed` | 多方参与角色、访问和付款权限模型 | `major` | party, authorization, report-delivery, billing | `0664721d0fb0` |
| `OD-034@0.1.0` | `decision` | `proposed` | 检测结论层级与全面合规引用边界 | `major` | conformity, reporting, external-compliance-reference | `d9645e91b677` |
| `OPS-EXC-001@0.1.0` | `requirement` | `in_review` | 收样异常分类建档 | `minor` | exception, receiving, identity, evidence, workflow | `84fdb3643f7e` |
| `OPS-EXC-002@0.1.0` | `requirement` | `in_review` | 异常不得自动降低范围或默认条件接收 | `major` | exception, scope-change, conditional-acceptance, authorization, audit | `8cf1d2cd7b59` |
| `OPS-IDENTITY-001@0.1.0` | `requirement` | `in_review` | 分离客户声明身份、实验室观察和匹配结论 | `minor` | identity, evidence, data-model, audit, ux | `0a417c32d185` |
| `OPS-IDENTITY-002@0.1.0` | `requirement` | `in_review` | 实物身份映射到实际送检项与受试配置 | `major` | identity, submission, product-variant, configuration, task-allocation | `b00534b8eb9a` |
| `OPS-IDENTITY-003@0.1.0` | `requirement` | `in_review` | 身份异常对象禁止进入制样和检测 | `major` | identity, sample-preparation, task-allocation, authorization, audit | `7eb34bdf50c2` |
| `OPS-RECEIPT-001@0.1.0` | `requirement` | `in_review` | 分别登记到货批、包装单元和收到的实物 | `minor` | receiving, data-model, api, ux, audit | `7fc24d0d2a7d` |
| `OPS-RECEIPT-002@0.1.0` | `requirement` | `in_review` | 包装单元与收到实物唯一标识 | `minor` | receiving, identifier, barcode, mobile, audit | `c226b56a7fbf` |
| `OPS-RECEIPT-003@0.1.0` | `requirement` | `in_review` | 身份评估完成前保持隔离 | `major` | receiving, identity, sample-preparation, task-allocation, audit | `3e4f6d543a54` |
| `ORG-COLLAB-001@0.1.0` | `requirement` | `in_review` | 集团内跨机构协作责任分离 | `major` | service-order, receiving, lab-execution, reporting, billing, authorization, audit | `be0c4ffabc11` |
| `ORG-STRUCT-001@0.1.0` | `requirement` | `in_review` | 集团多机构组织层级 | `major` | organization-model, master-data, authorization-context, integration, audit | `433e35c3fb90` |
| `REL-R1-RECEIVING-PILOT@0.1.0` | `release-baseline` | `proposed` | Release 1 收样与身份纵向切片候选基线 | `major` | release-governance, requirements-lock, receiving-pilot, migration, evidence | `a99ea5b7aa58` |
| `RULE-004@0.1.0` | `rule` | `in_review` | 身份映射、任务使用与代表性分离 | `major` | identity, task-allocation, coverage, lineage | `cb054b841019` |
| `RULE-026@0.1.0` | `rule` | `in_review` | 配置升级只影响明确生效范围 | `major` | versioning, release-baseline, configuration, migration, evidence | `cfaf997d49a1` |
| `SEC-AUD-001@0.1.0` | `requirement` | `in_review` | 受控操作追加式审计事件 | `minor` | audit, all-domain-commands, observability | `8aa132fc72a4` |
| `SEC-AUTH-001@0.1.0` | `requirement` | `in_review` | 服务端多维授权校验 | `major` | authorization, all-domain-commands, organization-scope, audit | `9f69be78d53d` |
| `SEC-DEPLOY-001@0.1.0` | `requirement` | `in_review` | 集团间独立部署与数据平面 | `major` | deployment, database, object-storage, secrets, messaging, search, ai-runtime, backup-recovery | `e02276ed1c4a` |
