# DEV-028 下次会话行动清单

## 📊 当前状态

- **分支**: `codex/dev-028-textile-test-unit`
- **已完成**: 任务规划和会话总结
- **待完成**: 规格文件创建和实现

## 🎯 下次会话立即执行

### 1. 创建规格文件

由于 Story schema 非常详细（需要约 20+ 个必填字段），建议参考现有 Story 文件：

**参考模板**：
```bash
# 查看玩具 Story 作为模板
cat spec/stories/ATC-TOY-002__v1.0.0.json
cat spec/stories/ATC-TOY-004__v1.0.0.json
```

**需要创建**：
1. `spec/decisions/OD-035__v1.0.0.json` - 纺织品模块正式化决策
2. `spec/stories/ATC-TEX-002__v1.0.0.json` - DEV-028 实施 Story

**注意事项**：
- OD-035 已被其他规格引用（10个文件引用），必须创建
- Story schema 包含以下必填部分：
  - `body.actor`, `body.business_outcome`, `body.preconditions`, `body.trigger`
  - `body.happy_path`, `body.failure_paths`, `body.state_transitions`
  - `body.data_contract`, `body.api_contract`, `body.permissions`
  - `body.invariants`, `body.test_cases`, `body.audit`, `body.observability`
  - `body.ui_states`, `body.non_goals`
  - `delivery.allowed_paths`, `delivery.verification_commands`, `delivery.definition_of_done`

### 2. 批准并验证规格

```bash
python -m tools.specgen validate --strict-warnings
python -m tools.specgen ready --story ATC-TEX-002@1.0.0
python -m tools.specgen generate
python -m tools.specgen check
```

### 3. 开始实现

参考 DEV-025 (玩具 TestUnit) 的实现模式：

**文件清单**：
- `contracts/textile/OpenLIMS.Contracts.Textile/TextileContracts.cs` (扩展)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileSampleRequirementDomain.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileTestUnitService.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileTestUnitPersistence.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileTestUnitMigration.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileEndpoints.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileModule.cs` (新建或修改)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileTelemetry.cs` (新建)

**实施顺序**：
1. 合约层扩展（错误码、能力、DTO）
2. 领域层（复用 DEV-011 纯规则）
3. 应用服务层（权限、事务、审计）
4. 持久化层（数据库表、迁移）
5. 端点层（REST API）
6. 模块注册（服务、遥测）

## 📚 参考资料

**已完成的类似任务**：
- DEV-025: `src/modules/toy/OpenLIMS.Modules.Toy/ToyTestUnitService.cs`
- DEV-027: `src/modules/toy/OpenLIMS.Modules.Toy/ToyConclusionService.cs`

**已有的纺织品契约**：
- `contracts/textile/OpenLIMS.Contracts.Textile/` (DEV-011/012)

**规划文档**：
- `.planning/2026-07-28-dev-028-textile-test-unit/task_plan.md`
- `.planning/2026-07-28-dev-028-textile-test-unit/session-summary.md`

## ⚠️ 已知问题

1. **OD-035 被多个规格引用**：必须创建此决策文件
2. **Story schema 复杂**：需要参考现有 Story 的完整结构
3. **规格文件创建失败**：本次会话因 schema 验证失败而推迟

## 🎯 成功标准

- [ ] OD-035@1.0.0 通过 `specgen validate`
- [ ] ATC-TEX-002@1.0.0 通过 `specgen ready`
- [ ] 所有工具检查通过
- [ ] 纺织品模块成功注册
- [ ] 样品需求计算实现
- [ ] 技术批准工作流实现

## 📞 联系信息

如有疑问，查看：
- 任务计划：`.planning/2026-07-28-dev-028-textile-test-unit/task_plan.md`
- 会话总结：`.planning/2026-07-28-dev-028-textile-test-unit/session-summary.md`
