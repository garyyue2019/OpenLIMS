<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: project-spec-catalog
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# 文件所有权规则

| 区域 | 所有权 | 修改规则 |
|---|---|---|
| `spec/` | 人工/评审所有 | 通过评审修改；每个版本独立文件 |
| `generated/spec/` | 生成器所有 | 禁止手改；运行 generate 更新 |
| `tools/specgen/` | 工具代码 | 正常代码评审，修改后必须跑完整测试 |
| 未来 `src/` | 实现所有 | 不由需求编译器覆盖；由契约和测试驱动同步 |
| 数据库迁移 | 追加历史 | 已执行迁移禁止重写，只能新增迁移 |
| 验收证据 | 不可变证据 | 固定需求版本、发布基线和哈希，不重新生成覆盖 |

生成目录不得混入手写文件。需要人工扩展时，应在生成目录之外通过明确端口引用。
