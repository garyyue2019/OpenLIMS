# DEV-016 进度

## 2026-07-26

- DEV-015 合并后用户选择"侦察并做 AI-001"；侦察发现运行时被未决 OD-006/007 阻断（AI-BOM-014 启动前置条件）；按授权例外询问，用户明确选择"AI 降级为契约切片"。
- 从 `main@30d39ac` 创建分支 `codex/dev-016-ai-extraction`。
- 已追加 5 项批准源规格（BUS-AI-001~003、AC-AI-003、ATC-AI-001，conditional/DISABLED 激活——首次使用 conditional 模式）；validate 134、READY、written=0、Python 40/40。
- 已实现 contracts/ai 纯契约（运行封套、事实类别税则、候选/缺口/处置）与 AiGovernanceRules 纯规则；8 个契约测试一次通过（封套、隔离、提升拒绝、分支/弃权、原值保留、缺口独立、序列化冻结、确定性）。
- 全解决方案 30 个测试项目全部通过；路径审计 32 个文件全部在 allowed_paths。按授权自动提交/PR/合并。
