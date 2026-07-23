# OpenLIMS

本工作区当前包含 OpenLIMS 产品需求、AI 开发结构化规格和一套零第三方运行依赖的需求编译工具链。

## 快速开始

```powershell
python -m tools.specgen validate
python -m tools.specgen source-status
python -m tools.specgen impact
python -m tools.specgen generate
python -m tools.specgen check
python -m unittest discover -s tests -p "test_*.py"
```

生成目录为 `generated/spec/`，禁止人工修改。需求和任务的机器源位于 `spec/`。完整流程见 `docs/ai-development/README.md`。

当前候选发布和所有示例均保持 `proposed` / `in_review` / `blocked`，因为原 PRD 仍待业务、实验室、技术、质量、财务、法务与架构联合评审。生成文件不等于生产批准。
