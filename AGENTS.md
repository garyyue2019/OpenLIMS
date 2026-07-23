# OpenLIMS AI 协作规则

本文件适用于整个仓库。AI 或自动化代理在修改任何文件前必须遵守以下规则。

## 权威边界

- `docs/AI原生第三方产品检测LIMS产品需求文档.md` 是产品叙述和来源文档；不得由生成器修改。
- `spec/` 是经人工评审后供机器执行的结构化规格源。
- `generated/spec/` 完全属于生成器；禁止人工或 AI 直接编辑。
- 未来的 `src/`、人工测试、数据库迁移和运行手册由工程实现维护，需求编译器永不覆盖。
- 已发布迁移、Seal、验收证据和已完成任务证据只能追加，禁止原地重写。

## 开始任务前

1. 运行 `python -m tools.specgen validate`。
2. 运行 `python -m tools.specgen source-status`；有来源漂移时先停止编码并做影响评审。
3. 运行 `python -m tools.specgen impact`，确认直接和传递影响。
4. 对 AI Task Card 运行 `python -m tools.specgen ready --story <ATC-ID@version>`。
5. Story 为 `BLOCKED` 时不得自行补充业务默认值；只能处理明确批准的 Spike 或治理任务。

## 修改规格

- ID 一旦分配不得复用或改名；Release、Epic、Feature 与任务稳定 ID 分字段保存。
- 所有依赖使用 `ID@x.y.z` 精确版本，禁止 `latest`、范围版本或缺省版本。
- 已封存版本不得修改；新语义必须新建 SemVer 文件。
- PATCH 不得改变行为哈希；MINOR 只用于兼容增加；删除、放宽、状态/权限/接口/数据语义变化默认 MAJOR。
- `priority` 与 `activation.applicability` 分开维护；`UNKNOWN` 默认阻断。
- 不能把 `proposed`、`in_review` 或 AI 建议标成 `approved`。
- AI 可以起草 OD/ADR，不能成为最终批准主体。

## 修改实现

- 只修改任务卡 `allowed_paths` 内的路径；需要扩大范围时先改任务卡并重新评审。
- 不直接访问其他模块私有表；使用版本化公共端口或事件。
- 不从运行中对象解析最新版规则；必须绑定 requirements lock、对象版本和规则版本。
- 正向、反向、边界、权限、并发、恢复和审计测试必须与实现同时提交。
- 不得为了让测试通过而降低门禁、静默补数据、删除失败记录或绕过审计。

## 完成任务前

```powershell
python -m tools.specgen validate --strict-warnings
python -m tools.specgen source-status
python -m tools.specgen verify-history
python -m tools.specgen generate
python -m tools.specgen check
python -m unittest discover -s tests -p "test_*.py"
```

相同输入第二次运行 `generate` 必须显示 `written=0`。若生成目录出现未知文件、手工修改、缺失或旧文件残留，必须修复源或工具，不能手工掩盖差异。
