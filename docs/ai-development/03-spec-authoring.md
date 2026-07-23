# 03 结构化规格编写规范

## 1. 一版本一文件

文件名必须严格匹配：

```text
<ID>__v<SemVer>.json
```

例如：

```text
OPS-RECEIPT-003__v1.0.0.json
OPS-RECEIPT-003__v1.1.0.json
```

逻辑 ID 稳定不变；版本键写成 `OPS-RECEIPT-003@1.1.0`。旧文件永久保留。

## 2. 通用字段

| 字段 | 说明 |
|---|---|
| `schema_version` | 当前固定为 `1` |
| `kind` | requirement、acceptance、rule、nfr、decision、story、release-baseline |
| `id` | 永不复用的稳定 ID |
| `version` | SemVer，不含 `v` |
| `status` | proposed、in_review、approved、deprecated、retired |
| `title` | 人类标题，不用于文件路径 |
| `summary` | 规范语义摘要 |
| `owners` | 负责角色，不填具体临时人员 |
| `source_refs` | `document + item`，指向 PRD 带 ID 条目 |
| `depends_on` | 精确版本键数组 |
| `affects` | 受影响模块/能力标签 |
| `change_class` | patch、minor、major |

## 3. 评审状态

```text
proposed → in_review → approved → deprecated → retired
```

- AI 新建内容只能从 `proposed` 开始。
- 字段完整不表示 `approved`。
- 只有被指定责任方完成语义评审后才能批准。
- 已批准版本若被 Seal 引用，不得原地改为 deprecated；应通过新生命周期记录或派生索引表达替代关系。

## 4. 优先级与适用性必须分开

```json
{
  "priority": "Must",
  "activation": {
    "mode": "conditional",
    "applicability": "UNKNOWN",
    "condition": "仅在批准 ERP 接口纳入本发布时启用"
  }
}
```

`Must` 表示能力启用后的强制程度，不表示所有部署都必须启用。`UNKNOWN` 一律阻断，不允许模型推测。

可用 activation mode：

- `core`
- `enabled_pack`
- `conditional`
- `business_ops`
- `release`

## 5. Decision

Decision 使用 `decision_state`：open、decided、deferred、rejected。只有 `decided` 可以与 `status=approved` 组合。

Decision 应包含：

- 可选方案；
- 最终决定；
- 阻塞范围；
- 退出标准；
- 责任方和证据。

AI 可以整理方案和权衡，不能自行把 open 改成 decided。

## 6. Acceptance

验收使用结构化 Given/When/Then：

```json
"scenario": {
  "given": ["实物处于隔离状态"],
  "when": ["用户请求制样"],
  "then": ["系统拒绝", "状态不变", "记录阻断审计"]
}
```

工具只做确定性 Gherkin 渲染，不在生成时补测试断言。

## 7. AI Task Card

Story 使用稳定 `ATC-*` ID，Release 不写进 ID：

```json
{
  "id": "ATC-REC-003",
  "target_release": "REL-R1-RECEIVING-PILOT@0.1.0",
  "epic_id": "EP-RECEIVING",
  "feature_id": "FEAT-REC-QUARANTINE"
}
```

`body` 必须覆盖：

- readiness；
- business outcome、actor、trigger；
- preconditions；
- happy/failure paths；
- invariants；
- data/API contract；
- state transitions；
- permissions、audit；
- UI states、observability；
- positive/negative/boundary/security/concurrency/recovery tests；
- non-goals；
- allowed paths；
- verification commands；
- definition of done。

任务出现以下情况应继续拆分：

- 同时拥有两个主要聚合；
- 同时实现两个独立审批流程；
- 同时接入两个外部系统；
- 多于一个不可逆迁移；
- 标题只能写成“实现某模块”；
- 无法在五条左右描述非目标；
- AI 必须自行决定业务政策才能开始。

## 8. Release Baseline

发布基线必须列出所有精确版本：

```json
{
  "kind": "release-baseline",
  "runtime_resolution": "pinned_only",
  "selected_specs": [
    "OD-005@1.0.0",
    "OPS-RECEIPT-003@1.1.0",
    "AC-REC-001@1.0.0",
    "ATC-REC-003@1.0.0"
  ],
  "breaking_change_approvals": []
}
```

发布选择必须形成依赖闭包。`breaking_change_approvals` 只能保存书面批准对应的逻辑 ID 或版本键，不能作为临时命令行绕过。

## 9. SemVer

| 等级 | 允许变化 |
|---|---|
| PATCH | 文字、负责人、解释性元数据；行为哈希必须不变 |
| MINOR | 兼容性增加；旧消费者和义务继续成立 |
| MAJOR | 删除、放宽、替换，或改变状态、权限、数据、接口、错误码和业务语义 |

自然语言兼容性不能完全自动证明。行为正文变化至少需要人工兼容性评审。
