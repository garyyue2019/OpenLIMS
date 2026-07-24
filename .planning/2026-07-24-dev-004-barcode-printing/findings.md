# Findings & Decisions: DEV-004 条码生成与打印

## Requirements

- 先合并 PR #3，再开发 DEV-004。
- DEV-004 目标为条码生成与打印，但必须以经人工批准且 READY 的任务卡为准。
- 遵守单集团独立部署、集团多机构边界，不引入共享 SaaS 多租户。
- 不直接编辑 `generated/spec/`，不越过 `allowed_paths`。

## Research Findings

- PR #3 在合并前 Head 为 `c1ed32159b57ea15217025a894aa86d0ff6af7bb`，显示 3/3 checks passed、Ready to merge、无冲突。
- PR #3 已于 2026-07-24 以 Squash merge 成功合并并关闭。
- 新的 `main` 合并提交为 `19766e795483e7de8cd24d579f3211a95cfda33c`。
- DEV-004 现有映射为 `ATC-REC-002@1.0.0`，需以实际 `ready` 输出确认能否编码。
- DEV-004 旧规格可能仍依赖 `ATC-REC-001@1.0.0`；若需改为已交付 `ATC-REC-001@2.0.0`，必须创建新 SemVer 文件，不能原地修改封存语义。
- 本地 `main` 已快进到 `19766e795483e7de8cd24d579f3211a95cfda33c`，与 `origin/main` 一致。
- 已创建分支 `codex/dev-004-barcode-printing`。
- `validate` 通过（71 个规格版本、389 个 PRD 来源条目）；`source-status` 为 CURRENT；`impact` 无任何新增、修改、删除或漂移。
- `ready --story ATC-REC-002@1.0.0` 返回 BLOCKED：Story 为 proposed、readiness=blocked，并依赖未批准的 `ATC-PLT-000@1.0.0`、`ATC-REC-001@1.0.0`、`ORG-STRUCT-001@0.1.0`、`OPS-RECEIPT-002@0.1.0`、`OD-009@0.1.0`、`OD-031@0.1.0`、`SEC-AUTH-001@0.1.0`、`SEC-AUD-001@1.0.0`。
- 其中 `ATC-REC-001@2.0.0`、`ORG-STRUCT-001@1.0.0`、`OD-009@1.0.0`、`SEC-AUTH-001@1.0.0`、`SEC-AUD-001@2.0.0` 已 approved；旧 DEV-004 应改为精确依赖这些新版本。
- `OD-002@1.0.0` 和 `OPS-RECEIPT-001@1.0.0` 也已 approved，可替换旧依赖链中的低版本引用。
- 真正尚未批准的条码业务核心是 `OPS-RECEIPT-002@0.1.0` 与 `OD-031@0.1.0`；前者 applicability=UNKNOWN，后者仍为 open/proposed，尚未决定编码格式、打印设备/协议、移动流程及验收。
- `ATC-PLT-000@1.0.0` 虽仍 proposed/blocked，但其工程骨架已由早期交付形成；新 DEV-004 是否继续直接依赖该旧任务卡需从已交付 Story 的依赖模式判断，不应为了绕过门禁而直接批准旧卡。
- 现有 DEV-004 草案已提出：集团+对象类型命名空间唯一编号；包装/实物防错；非敏感且有版本/校验的编码；服务端多维扫码授权；重印保持原身份并新增 PrintEvent；打印失败保留身份且可幂等重试。
- 旧任务卡 `allowed_paths` 使用 `apps/web/receiving/**`，但当前前端实际位于 `apps/web/src/features/receiving/**`；新版本需校准允许路径，不能让实现越界。
- DEV-003 当前在 `ReceivingRules.CreatePlan` 中用 `CNT-{GUID}` / `ITM-{GUID}` 生成对象业务号；DEV-004 不应重写已发布迁移或既有对象号，而应新增独立的标签身份表和追加迁移。
- DEV-003 的对象登记、隔离状态、模块本地 `audit_pending` 与 Outbox 已在同一 PostgreSQL 事务中提交，可在同一事务追加 Container/ReceivedItem 标签身份，保持原子性。
- 现有模块组合根支持显式 API、Worker 与迁移注册；Labeling 应作为独立模块注册，并通过版本化 Receiving 公共端口读取对象，禁止查询 Receiving 私表。
- Worker 目前只注册 Receiving Outbox monitor；DEV-004 可在 Labeling Worker 模块中加入打印分发后台服务，并保持正常 Host 启动不自动迁移。
- 当前收样请求只有 `LaboratoryId`，没有受信实验室显示代码。实现需从受信授权上下文或明确配置得到 `LaboratoryCode`，不能把客户端任意字段直接当成标签前缀。

## Technical Decisions

| Decision | Rationale |
|----------|-----------|
| 从合并后的 `main` 创建 `codex/dev-004-barcode-printing` | 保证 DEV-004 基于正式集成的 DEV-003 能力 |
| 先跑门禁再检查设计与代码 | AGENTS.md 的强制顺序，也是避免在 BLOCKED Story 上误编码的关键 |
| 对未决业务项一次性归并审批 | 避免无边界扩写治理文档，同时不让 AI 越权批准业务默认值 |

## Approved DEV-004 Business Baseline

User approval: 2026-07-24，用户明确回复“批准 DEV-004 业务基线”。

- Scope: assign and print labels for `Container` and `ReceivedItem`; do not label `Receipt`, derived samples, test portions, or preparations in DEV-004.
- Allocation: identifiers are allocated atomically when DEV-003 registration persists the object; printer failure never rolls back or reallocates the identity.
- Human-readable number: immutable `LABCODE-{CT|RI}-YYYYMMDD-######`; the sequence is atomic in the deployment group + object type + date namespace and never reused. The laboratory code is a snapshotted display prefix, not a tenant selector.
- QR payload: QR Code Model 2 with `OL1` format version, object type, random 128-bit opaque public reference, and corruption checksum; it contains no customer, product, model, legal-entity, laboratory, or business text and never grants authorization.
- Label: two immutable 50×30 mm / 203 dpi templates (`包装` and `实物`) with prominent object type, business number, lab code, QR, and template version; for a ReceivedItem the label is affixed to its controlled bag/tag, not a test surface.
- Printer channel: asynchronous worker sends versioned TSPL/TSPL2 commands to a configured logical network printer over TCP 9100; no specific printer brand is selected. A printer is bound to one laboratory and cannot print another laboratory's object.
- Honest status: use `REQUESTED -> DISPATCHING -> DISPATCHED -> VERIFIED`, plus `FAILED` and `UNKNOWN`; `DISPATCHED` only means bytes were accepted by the adapter, not that paper physically emerged. Scanning the printed label closes the loop as `VERIFIED`.
- Scan channel: R1 supports USB/Bluetooth keyboard-wedge scanners in the Web receiving page; server-side resolve always rechecks group, legal entity, laboratory, customer, object, and capability authorization. No native mobile app, camera scan, or offline scan in this slice.
- First print/reprint: initial print is one copy per object and can be batched. Reprint is one copy per request, requires `receiving.label.reprint`, object/lab access, and a reason; after three cumulative successful reprints, `receiving.label.reprint.override` is additionally required and a security/quality alert is emitted.
- Delivery uncertainty: a definite failure before sending may retry the same job; an uncertain send must not auto-retry. The operator scans the suspected label or performs a controlled reprint, preventing accidental duplicate physical output.
- Audit: append number allocation, print request, dispatch, failure/unknown, verification scan, denied scan, and reprint events with actor, organization scope, object/version, template, printer, reason, idempotency key, and rule/version references; never store replayable sensitive tokens.
- Non-goals: receipt labels, split/derived sample labels, browser print dialogs, printer-brand SDKs, offline/mobile camera scanning, identity assessment, condition acceptance/rejection, and quarantine release.

## Specification Versions Created

- `OD-031@1.0.0`: approved barcode, label, TSPL/TSPL2 printing, scanner, reprint and uncertainty decisions; instrument/file integration deferred.
- `OPS-RECEIPT-002@1.0.0`: approved and applicable unique-label requirement for Container and ReceivedItem.
- `ATC-REC-002@2.0.0`: DEV-004 implementation card with exact approved dependencies, corrected paths and executable verification commands.

The uncommitted, unsealed `ATC-REC-002@2.0.0` draft was corrected before delivery to include the two exact platform-test lock files that necessarily change when the API Host gains the Labeling project. A temporary `2.1.0` compatibility draft was removed because it produced two simultaneous approved versions; no published or sealed version was rewritten.

## Issues Encountered

| Issue | Resolution |
|-------|------------|
| 本机没有 GitHub CLI | 使用已登录的 GitHub 页面执行并验证合并 |
| 首次自动点击 GitHub 合并按钮超时 | 读取最新 DOM，切换到唯一节点点击，并在提交后验证“successfully merged and closed” |
| `ATC-REC-002@1.0.0` 为 BLOCKED | 停止编码，仅做依赖与业务语义影响评审；等待人工批准新版本基线 |

## Resources

- PR #3: https://github.com/garyyue2019/OpenLIMS/pull/3
- DEV-004 旧 Story: `spec/stories/ATC-REC-002__v1.0.0.json`
- DEV-003 READY Story: `spec/stories/ATC-REC-001__v2.0.0.json`

## Visual/Browser Findings

- GitHub PR #3 合并前显示：Ready to merge、3 successful checks、No conflicts with base branch。
- PR #3 最终页面显示：Merged、Pull request successfully merged and closed，并给出合并提交 `19766e7`。
