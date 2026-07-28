# DEV-028 纺织品 TestUnit 与样品需求实施计划

## 任务概述

将 DEV-011 的契约层实现为完整的可运行纺织品模块，支持样品需求计算、CuttingPlan 管理和技术批准流程。

## 背景

**已完成**：
- DEV-011 (ATC-TEX-001@1.0.0) - 纺织样品需求契约切片
  - `OpenLIMS.Contracts.Textile` 契约模型
  - `TextileSampleRequirementRules` 纯规则
  - CuttingPlan 结构与校验
  - 契约测试与序列化冻结

**本任务目标**：
- 将契约层实现为完整模块
- 实现运行时业务逻辑
- 注册模块、schema、HTTP 端点
- 权限验证和审计集成

## 相关规格

### 业务需求
- BUS-TEX-001@1.0.0 - 纺织样品需求契约模型
- BUS-TEX-002@1.0.0 - 互斥裁样与样品不足失败关闭规则
- BUS-TEX-003@1.0.0 - CuttingPlan 序列化契约

### 验收标准
- AC-TEXTILE-001@1.0.0 - 互斥共享、不足缺口与 UNKNOWN 失败关闭

### Story
- 需要创建 ATC-TEX-002@1.0.0 (DEV-028 实施 Story)

## 实施范围

### 1. 领域层
- `TextileSampleRequirementDomain.cs`
  - 样品需求计算逻辑（基于 DEV-011 契约规则）
  - 互斥裁样验证
  - 样品不足检测
  - CuttingPlan 验证

### 2. 应用服务层
- `TextileTestUnitService.cs`
  - `CalculateSampleRequirementAsync` - 计算样品需求
  - `CreateCuttingPlanAsync` - 创建裁样计划
  - `ApproveCuttingPlanAsync` - 技术批准裁样计划
  - `GetCuttingPlanAsync` - 查询裁样计划
  - 权限验证：`textile.sample-requirement.calculate`、`textile.cutting-plan.approve`
  - 事务协调与审计集成

### 3. 持久化层
- `TextileTestUnitPersistence.cs`
  - `textile.sample_requirement` - 样品需求记录表
  - `textile.sample_requirement_line` - 需求行明细表
  - `textile.cutting_plan` - 裁样计划表
  - `textile.cutting_plan_item` - 裁样项表
  - 追加式不可变（审计字段）

- `TextileTestUnitMigration.cs`
  - 数据库迁移脚本
  - 约束和索引

### 4. 端点层
- `TextileEndpoints.cs`
  - `POST /api/v1/textile/sample-requirements` - 计算样品需求
  - `POST /api/v1/textile/cutting-plans` - 创建裁样计划
  - `POST /api/v1/textile/cutting-plans/{id}/approve` - 批准裁样计划
  - `GET /api/v1/textile/cutting-plans/{id}` - 查询裁样计划
  - 错误码映射

### 5. 模块注册
- `TextileModule.cs`
  - 服务注册：`ITextileTestUnitService`、`TextileTestUnitStore`
  - 迁移注册：`TextileTestUnitMigrator`
  - 模块描述符

- `TextileTelemetry.cs`
  - 遥测计数器：`textile_sample_requirement_total`、`textile_cutting_plan_total`

### 6. 合约层
- `TextileContracts.cs`（扩展现有）
  - 错误码：`TEXTILE_MUTUAL_EXCLUSION_VIOLATED`、`TEXTILE_SAMPLE_INSUFFICIENT`、`TEXTILE_CUTTING_PLAN_NOT_APPROVED`
  - 能力：`textile.sample-requirement.calculate`、`textile.cutting-plan.approve`
  - 请求/响应 DTO
  - `ITextileTestUnitService` 接口

## 关键不变式

基于 BUS-TEX-001/002/003 和 AC-TEXTILE-001：

1. **样品需求维度完整性**
   - 需求行必须包含：款号、颜色、部件、材料、部位、方向、平行数、预处理、互斥破坏组
   - 方向仅允许：WARP、WEFT、LENGTHWISE、CROSSWISE

2. **互斥裁样验证**
   - 同一破坏组内的项目不得共享同一片样品
   - 违反时以 `TEXTILE_MUTUAL_EXCLUSION_VIOLATED` 拒绝

3. **样品不足失败关闭**
   - 可用样品数量不足以满足所有需求时失败关闭
   - 返回缺口清单和缺失数量

4. **版本固定引用**
   - 所有引用为稳定 ID + 版本的版本固定引用
   - 检测项目引用、规则集版本必须明确

5. **技术批准门禁**
   - CuttingPlan 必须经技术负责人批准后才能执行
   - 批准人不得是创建人（SoD）

## 参考实现

- **DEV-025 (玩具 TestUnit)**: 服务架构、权限模型、审计集成
- **DEV-011 (纺织契约)**: 领域规则、数据模型
- **DEV-027 (玩具结论)**: 不可变性、版本固定

## 实施步骤

### Phase 1: 规格批准
1. [ ] 创建 OD-035 (纺织品模块正式化架构决策)
2. [ ] 创建 ATC-TEX-002@1.0.0 (DEV-028 实施 Story)
3. [ ] 运行 `specgen validate` 和 `specgen ready`

### Phase 2: 合约层扩展
1. [ ] 扩展 `TextileContracts.cs` 添加错误码、能力、DTO
2. [ ] 定义 `ITextileTestUnitService` 接口

### Phase 3: 领域层
1. [ ] 创建 `TextileSampleRequirementDomain.cs`
2. [ ] 实现样品需求计算逻辑
3. [ ] 实现互斥裁样验证
4. [ ] 实现样品不足检测

### Phase 4: 应用服务层
1. [ ] 创建 `TextileTestUnitService.cs`
2. [ ] 实现服务方法
3. [ ] 集成权限验证
4. [ ] 集成事务协调和审计

### Phase 5: 持久化层
1. [ ] 创建 `TextileTestUnitPersistence.cs`
2. [ ] 创建 `TextileTestUnitMigration.cs`
3. [ ] 实现数据库操作

### Phase 6: 端点层
1. [ ] 创建 `TextileEndpoints.cs`
2. [ ] 实现 API 端点
3. [ ] 错误码映射

### Phase 7: 模块注册
1. [ ] 创建/更新 `TextileModule.cs`
2. [ ] 创建 `TextileTelemetry.cs`
3. [ ] 服务和迁移注册

### Phase 8: 测试与交付
1. [ ] 单元测试（待后续补充）
2. [ ] 集成测试（待后续补充）
3. [ ] 文档更新
4. [ ] 创建 PR 并合并

## 已知依赖

- ✅ DEV-011 契约层已完成
- ✅ 平台审计和授权基础设施（DEV-017, DEV-018）
- ✅ 事务协调器

## 验收标准

- [ ] 样品需求计算正确（覆盖所有维度）
- [ ] 互斥裁样验证正确拒绝违规
- [ ] 样品不足时失败关闭并返回缺口
- [ ] 技术批准流程完整（权限、SoD）
- [ ] 数据库不可变性（审计字段）
- [ ] 所有端点返回正确的 HTTP 状态码
- [ ] 遥测计数器正常工作
- [ ] 规格验证通过
- [ ] 生成的规格文档更新

## 交付物清单

### 规格文件
- `spec/decisions/OD-035__v1.0.0.json` (新建)
- `spec/stories/ATC-TEX-002__v1.0.0.json` (新建)

### 源代码
- `contracts/textile/OpenLIMS.Contracts.Textile/TextileContracts.cs` (修改)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileSampleRequirementDomain.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileTestUnitService.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileTestUnitPersistence.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileTestUnitMigration.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileEndpoints.cs` (新建)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileModule.cs` (新建或修改)
- `src/modules/textile/OpenLIMS.Modules.Textile/TextileTelemetry.cs` (新建)

### 规划文档
- `.planning/2026-07-28-dev-028-textile-test-unit/task_plan.md` (本文件)
- `.planning/2026-07-28-dev-028-textile-test-unit/progress.md`
- `.planning/2026-07-28-dev-028-textile-test-unit/findings.md`

## 估算

- **复杂度**: 中等（有契约基础，可参考 DEV-025）
- **预计工作量**: 与 DEV-025 类似
- **风险**: 低（模式已验证）

## 下一步

开始 Phase 1：创建规格文件
