# 02 需求变更与同步流程

## 1. 标准变更链

```text
提出变更
  → 修改 PRD 或提出结构化变更
  → source-status / impact
  → 业务、技术、质量评审
  → 创建新规格版本或修改未封存草稿
  → 确认来源基线
  → generate
  → check + tests
  → 业务代码按失败契约修改
  → 验收与证据
  → 批准发布并 seal
```

## 2. PRD 先变化时

### 步骤 1：检查来源漂移

```powershell
python -m tools.specgen source-status
python -m tools.specgen impact --json
```

`source-status` 非零退出是正常门禁，表示当前 PRD 与 `spec/source-baseline.json` 不一致。不要立即运行带 `--allow-source-drift` 的生成，也不要直接更新整个 baseline。

### 步骤 2：判断变化类型

| 变化 | 动作 |
|---|---|
| 仅错别字、格式或解释性文字 | 记录 PATCH 评审；行为规格可不变 |
| 新增兼容场景 | 创建 MINOR 规格版本或更新尚未封存草稿 |
| 状态、权限、接口、字段、错误码、业务规则变化 | 默认 MAJOR；需要迁移和批准 |
| 删除/放宽原有义务 | MAJOR；发布基线必须列出 breaking approval |
| PRD 条目删除 | 不直接删除历史规格；建立 retired/superseded 生命周期记录 |

### 步骤 3：更新结构化规格

未封存且仍为 `proposed/in_review` 的版本可在评审中调整。已被 Seal 引用的版本必须新建文件：

```powershell
python -m tools.specgen scaffold --kind requirement --id OPS-RECEIPT-003 --version 1.1.0
```

然后显式更新依赖它的 Acceptance、Story 和发布基线版本键。

### 步骤 4：显式确认来源

只确认已审阅条目；如果 PRD 还有不带 ID 的变化，还要显式确认文档哈希：

```powershell
python -m tools.specgen source-accept `
  --document PRD-MAIN `
  --item OPS-RECEIPT-003 `
  --accept-document `
  --reviewer "产品/质量联合评审记录编号" `
  --reviewed-on 2026-07-23 `
  --reason "明确条件接收后的制样门禁" `
  --acknowledgement reviewed
```

如果来源语义变化但关联结构化规格没有变化，工具默认拒绝确认。`--waive-spec-change` 只能用于确实没有工程语义变化且已有书面理由的场景。

### 步骤 5：生成并验证

```powershell
python -m tools.specgen validate --strict-warnings
python -m tools.specgen impact
python -m tools.specgen generate
python -m tools.specgen check
python -m unittest discover -s tests -p "test_*.py" -v
```

此时 OpenAPI、任务卡或 Gherkin 等派生物会变化。业务代码不会被覆盖；相应契约和验收测试应失败，直到实现完成。

## 3. 只改结构化规格时

适用于补齐工程契约、任务文件范围、测试场景等不需要修改 PRD 规范正文的情况：

1. 修改或创建 `spec/**`；
2. `validate`；
3. `impact`；
4. `generate`；
5. `check` 和测试；
6. 由产品/技术/质量判断是否需要回写 PRD 说明。

## 4. 生成文件被手工修改时

`check` 会报告：

- `生成文件已过期或被手改`；
- `缺少生成文件`；
- `生成目录存在未知文件`；
- `生成锁文件已过期`。

处理方法不是继续编辑生成文件，而是：

1. 判断变更是否应进入结构化规格或 renderer；
2. 恢复/删除手工派生文件；
3. 修改正确的源；
4. 重新生成并检查。

## 5. 已发布版本发生变化时

已封存版本变化将使 `verify-history` 失败。正确做法：

1. 恢复被原地修改的旧版本；
2. 新建 SemVer 版本文件；
3. 更新候选发布的精确版本选择；
4. 使用 `gate` 对比前一 Seal；
5. 为破坏性变化补充迁移、回滚、批准和客户/在制影响；
6. 新发布批准后创建新 Seal。

## 6. 变更传播示例

假设 `OPS-RECEIPT-003` 改变：

```text
OPS-RECEIPT-003
  ├─ AC-REC-001
  ├─ ATC-REC-003 隔离门禁
  │    ├─ 拆解入口
  │    ├─ 制样入口
  │    └─ 检测分配入口
  ├─ ATC-REC-006 受控放行
  └─ REL-R1-RECEIVING-PILOT requirements lock
```

工具沿当前和旧 lock 的反向依赖传播，避免因为删除旧依赖而漏掉原消费者。

## 7. 禁止的捷径

- 不使用 `source-accept --bootstrap --force` 处理日常变化；
- 不在 CI 中运行 `generate` 后自动提交，以免隐藏未审阅变化；
- 不靠全 PRD 文件哈希决定全量任务重写；
- 不在需求编译器里调用 AI；
- 不用 `--allow-source-drift` 作为正式流程；
- 不把 `source-accept` 当成业务批准；
- 不覆盖已关闭任务、数据库迁移或验收证据。
