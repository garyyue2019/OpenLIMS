# 04 CLI 命令参考

所有命令从项目根目录运行，也可以使用：

```powershell
.\scripts\spec.ps1 validate
```

## `validate`

```powershell
python -m tools.specgen validate [--strict-warnings]
```

校验配置、严格 JSON、文件名、ID、SemVer、必填字段、来源引用、固定版本依赖、循环、Story 字段和发布选择。

## `source-status`

```powershell
python -m tools.specgen source-status [--json]
```

比较当前 PRD 扫描结果和 `spec/source-baseline.json`。报告新增、变化、删除条目和整份文档变化。存在漂移时退出非零。

## `source-accept`

建立初始技术基线：

```powershell
python -m tools.specgen source-accept `
  --bootstrap `
  --reviewer "评审记录" `
  --reviewed-on 2026-07-23 `
  --reason "建立初始来源指纹" `
  --acknowledgement bootstrap
```

日常只接受已审阅条目：

```powershell
python -m tools.specgen source-accept `
  --document PRD-MAIN `
  --item OPS-RECEIPT-003 `
  --accept-document `
  --reviewer "评审记录" `
  --reviewed-on 2026-07-23 `
  --reason "语义变化已落实到1.1.0规格"
```

`--waive-spec-change` 需要书面理由，只用于 PRD 文字变化确实不影响结构化语义的情况。

## `impact`

```powershell
python -m tools.specgen impact [--json] [--fail-on-major]
```

比较当前规格与上一个生成 lock，合并当前/旧依赖图，输出新增、变化、删除、来源漂移、直接和传递影响。

## `generate`

```powershell
python -m tools.specgen generate
```

在内存中渲染期望输出，拒绝来源漂移和未知生成文件，写入变化文件，删除上个 lock 拥有的旧派生物，最后更新 lock。

`--allow-source-drift` 仅供诊断，不应进入 CI 或正式流程。

## `check`

```powershell
python -m tools.specgen check
```

完全只读。比较：

- PRD 与来源基线；
- 期望和实际文件集合；
- 每个文件的完整内容；
- lock 与当前输入/输出哈希。

## `ready`

```powershell
python -m tools.specgen ready --story ATC-REC-003@0.1.0
```

检查 Story 状态、readiness、来源漂移、依赖状态和 Decision 是否关闭。当前示例预期返回 BLOCKED。

## `explain`

```powershell
python -m tools.specgen explain OPS-RECEIPT-003@0.1.0
```

输出文件、类型、状态、哈希、来源、固定依赖、反向依赖和影响模块。

## `scaffold`

```powershell
python -m tools.specgen scaffold `
  --kind requirement `
  --id OPS-RECEIPT-003 `
  --version 1.1.0
```

只创建不存在的新版本骨架；目标存在时拒绝覆盖。Story 骨架需要人工/AI 补齐完整 body 后才能通过校验。

## `snapshot`

```powershell
python -m tools.specgen snapshot --name receiving-review-01
```

保存当前生成 lock 的不可覆盖人工快照，供评审比较。Snapshot 不等于批准 Seal。

## `seal`

```powershell
python -m tools.specgen seal `
  --release REL-R1-RECEIVING-PILOT@1.0.0 `
  --sealed-by "发布批准记录" `
  --sealed-on 2026-08-31 `
  --reason "Release 1 生产批准"
```

只有发布基线及全部选择对象均为 approved、依赖闭包完整、生成物一致时才成功。目标已存在时使用 exclusive-create 拒绝覆盖。

## `verify-history`

```powershell
python -m tools.specgen verify-history
```

验证 Seal 自身哈希、前驱链，以及所有已封存规格仍存在且完整哈希不变。

## `gate`

```powershell
python -m tools.specgen gate `
  --from-seal spec/seals/REL-R1-RECEIVING-PILOT/1.0.0.seal.json `
  --release REL-R1-RECEIVING-PILOT@1.1.0
```

阻止同版本篡改、版本倒退、PATCH 行为变化和未经发布级批准的 MAJOR 删除/替换。

## `list`

```powershell
python -m tools.specgen list --kind story
```

列出结构化规格版本键、类型、状态和标题。

## 退出码

当前 CLI 使用：

| 退出码 | 含义 |
|---:|---|
| 0 | 成功 |
| 2 | 配置、严格 JSON、Schema 语义或参数错误 |
| 3 | 来源或生成物漂移 |
| 4 | Story 未就绪、历史或破坏性门禁阻断 |

CI 应依赖退出码，不应解析中文提示文本。
