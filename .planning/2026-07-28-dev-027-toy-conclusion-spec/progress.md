# DEV-027 实施进度

## 已完成

### 阶段 1：规格批准 ✅
- [x] OD-034@1.0.0 - 两级结论层级决策已批准
- [x] BUS-TOY-006@1.0.0 - 业务需求已批准
- [x] AC-TOY-002@1.0.0 - 验收标准已批准
- [x] ATC-TOY-004@1.0.0 - Story 已批准并解除阻断
- [x] 所有规格文件通过 `specgen validate`
- [x] `specgen ready --story ATC-TOY-004@1.0.0` 返回 READY

### 阶段 2：合约层 ✅
- [x] ToyErrorCodes 添加结论错误码
  - ConclusionEvidenceIncomplete
  - ConclusionPolicyUnknown
  - FictitiousWholeItemConclusion
  - ConclusionSodViolation
- [x] ToyCapabilities 添加结论批准能力
  - ConclusionApproveItem (技术负责人)
  - ConclusionApproveScope (授权签字人)
- [x] ToyConclusionContract 常量定义
- [x] ToyConclusionLevels 枚举（永久不包含 WHOLE_PRODUCT_COMPLIANCE）
- [x] ToyUncoveredReasons 枚举
- [x] 请求/响应 DTO：
  - CreateItemConformityConclusionRequest
  - CreateTestedScopeConformityConclusionRequest
  - ToyConclusionResult
  - TestUnitEvidenceInput
  - UncoveredScopeInput
  - ExternalReferenceInput
- [x] IToyConclusionService 接口

### 阶段 3：领域模型 ✅
- [x] ToyConclusionDomain.cs
  - ValidateItemConformityRequest
  - ValidateTestedScopeConformityRequest
  - ValidateSeparationOfDuty
  - GenerateItemConformityStatement (固定模板)
  - GenerateTestedScopeConformityStatement (固定模板含未覆盖项)
- [x] ItemConformityConclusionDraft 记录类型
- [x] TestedScopeConformityConclusionDraft 记录类型
- [x] 关键不变式实现：
  - 整件全面合规永久拒绝
  - 未覆盖项强制披露（null 或空数组拒绝）
  - 固定模板措辞（自选措辞拒绝）
  - SoD 验证

### 阶段 4：应用服务层 ✅
- [x] ToyConclusionService.cs
  - CreateItemConformityConclusionAsync
  - CreateTestedScopeConformityConclusionAsync
  - GetConclusionAsync
  - GetConclusionsByProductAsync
- [x] 权限验证集成
- [x] SoD 验证集成
- [x] 事务协调器集成
- [x] 审计写入集成

### 阶段 5：持久化层 ✅
- [x] ToyConclusionPersistence.cs
  - InsertItemConformityConclusionAsync
  - InsertTestedScopeConformityConclusionAsync
  - GetResultRecordersAsync (TODO: 跨模块查询)
  - GetConclusionAsync
  - GetConclusionsByProductAsync
- [x] ToyConclusionMigration.cs
  - toy.conclusion 主表
  - toy.conclusion_test_unit 测试单元证据表
  - toy.conclusion_hazard_domain 已覆盖危险域表
  - toy.conclusion_uncovered_scope 未覆盖项表（强制）
  - toy.conclusion_external_reference 外部引用表
  - 数据库触发器：禁止 UPDATE/DELETE（不可变性）
  - 审计字段（created_at, created_by 等）

### 阶段 6：端点层 ✅
- [x] ToyEndpoints.cs 添加结论端点
  - POST /api/v1/toy/conclusions/item-conformity
  - POST /api/v1/toy/conclusions/tested-scope-conformity
  - GET /api/v1/toy/conclusions/{id}
  - GET /api/v1/toy/conclusions (按产品查询)
- [x] 错误码映射更新
- [x] CreateItemConformityConclusionAsync 处理器
- [x] CreateTestedScopeConformityConclusionAsync 处理器
- [x] GetConclusionAsync 处理器
- [x] GetConclusionsByProductAsync 处理器

### 阶段 7：模块注册 ✅
- [x] ToyModule.cs 服务注册
  - IToyConclusionService
  - ToyConclusionStore
- [x] ToyModule.cs 迁移注册
  - ToyConclusionMigrator.ApplyAsync
- [x] ToyTelemetry.cs 遥测计数器
  - toy_conclusion_total (按 level 标签)

## 未完成

### 阶段 8：单元测试 ⏳
- [ ] TC-TOY-004-01: ITEM_CONFORMITY 正向测试
- [ ] TC-TOY-004-02: TESTED_SCOPE_CONFORMITY 正向测试
- [ ] TC-TOY-004-03: 整件全面合规拒绝测试
- [ ] TC-TOY-004-04: 自选措辞拒绝测试
- [ ] TC-TOY-004-05: 未覆盖项缺失拒绝测试
- [ ] TC-TOY-004-06: 外部证书不参与判定测试
- [ ] TC-TOY-004-07: SoD 拒绝测试
- [ ] TC-TOY-004-08: 结论不可变测试

### 阶段 9：集成测试 ⏳
- [ ] 完整 AC-TOY-002@1.0.0 验收场景
- [ ] 审计与发件箱同事务回滚测试
- [ ] 权限集成测试

### 阶段 10：文档 ⏳
- [ ] API 文档
- [ ] 使用示例
- [ ] 部署指南

## 已知限制

1. **SEC-SIGN-001 重认证签署**：TESTED_SCOPE_CONFORMITY 需要重认证签署，但签名服务尚未实现。当前代码标记了 TODO 位置。

2. **跨模块 SoD 查询**：`GetResultRecordersAsync` 需要查询 Result 模块以获取 adoptedResult 的录入人信息。当前返回空列表（SoD 检查将通过）。真实实现需要跨模块通信机制。

3. **编译验证**：由于环境缺少 .NET 10 SDK，无法编译验证。代码遵循项目现有模式编写，应该可以编译通过。

## 关键设计遵循规格

✅ **OD-034 两级结论层级**：
- ITEM_CONFORMITY：技术负责人批准，无需重认证
- TESTED_SCOPE_CONFORMITY：授权签字人批准，需重认证签署
- WHOLE_PRODUCT_COMPLIANCE：永久禁用，无枚举值

✅ **未覆盖项强制披露**：
- uncoveredScopes 为 null 或空数组时以 CONCLUSION_EVIDENCE_INCOMPLETE 拒绝
- 未覆盖项段落在固定模板中不可省略

✅ **固定模板措辞**：
- 系统生成，调用方不可传入自选措辞
- ITEM: "检测项目 X@version 符合要求 Y@version"
- SCOPE: "所检 N 个 TestUnit 就下列已测危险域符合...；未覆盖项：..."

✅ **追加式不可变**：
- 数据库触发器阻止 UPDATE 和 DELETE
- 变更通过新版本记录

✅ **职责分离 (SoD)**：
- 结论批准人不得是所引用结果的录入人
- 违反以 CONCLUSION_SOD_VIOLATION 拒绝

✅ **外部证书仅信息性**：
- notPartOfThisConclusion 强制为 true
- 不参与判定，不减少未覆盖项

## 下一步

1. 等待 .NET 10 SDK 或在有 SDK 的环境编译验证
2. 实现单元测试套件
3. 实现集成测试
4. 补充 SEC-SIGN-001 重认证签署集成
5. 补充跨模块 SoD 查询实现
6. 创建 PR 并合并到 main
