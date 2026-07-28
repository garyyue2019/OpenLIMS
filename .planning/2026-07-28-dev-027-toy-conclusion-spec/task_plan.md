# DEV-027 任务计划：多 TestUnit 危险域覆盖结论

## 任务概述

实现玩具检测结论系统，支持两级结论层级：
- **ITEM_CONFORMITY**：单检测项目符合，由技术负责人批准
- **TESTED_SCOPE_CONFORMITY**：已测范围符合，由授权签字人重认证签署批准

**核心约束**：
1. 永久禁止整件产品全面合规结论
2. 措辞由系统固定模板渲染，不接受自选措辞
3. 强制披露未覆盖项（uncoveredScopes 不可省略）
4. 外部认证证书仅作信息性旁注（notPartOfThisConclusion=true）
5. 结论为追加式不可变事实
6. 批准人与录入人职责分离（SoD）

## 实施阶段

### 阶段 1：领域模型与值对象
- [ ] ConclusionLevel 枚举（ITEM_CONFORMITY, TESTED_SCOPE_CONFORMITY）
- [ ] ConclusionId 值对象
- [ ] CoveredHazardDomain 值对象
- [ ] UncoveredScope 值对象（包含 reason: NOT_TESTED/UNKNOWN/NOT_APPLICABLE）
- [ ] ExternalReference 值对象（必含 notPartOfThisConclusion=true）
- [ ] ItemConformityConclusion 聚合根
- [ ] TestedScopeConformityConclusion 聚合根

### 阶段 2：领域服务与策略
- [ ] ConclusionStatementGenerator：固定模板措辞生成器
- [ ] SeparationOfDutyValidator：SoD 校验服务
- [ ] EvidenceCompletenessValidator：证据完整性校验
- [ ] HazardCoverageAnalyzer：危险域覆盖分析器

### 阶段 3：应用层命令
- [ ] CreateItemConformityConclusion 命令与处理器
- [ ] CreateTestedScopeConformityConclusion 命令与处理器
- [ ] 命令验证器（版本引用完整性、SoD、证据完整性）

### 阶段 4：应用层查询
- [ ] GetConclusionById 查询与处理器
- [ ] GetConclusionsByProduct 查询与处理器

### 阶段 5：基础设施层
- [ ] ToyConclusion 表迁移（包含审计字段）
- [ ] ToyConclusion_HazardDomains 关联表
- [ ] ToyConclusion_UncoveredScopes 关联表
- [ ] ToyConclusion_ExternalReferences 关联表
- [ ] ConclusionRepository 实现（追加式，禁止 UPDATE/DELETE）
- [ ] 数据库约束（不可变性、审计字段）

### 阶段 6：端口层
- [ ] ToyConformityConclusionPort 接口
- [ ] POST /api/toy/conclusions/item-conformity
- [ ] POST /api/toy/conclusions/tested-scope-conformity
- [ ] GET /api/toy/conclusions/{conclusionId}
- [ ] GET /api/toy/conclusions?productRef=...&version=...
- [ ] 错误码映射（TOY.CONCLUSION_*）

### 阶段 7：单元测试
- [ ] TC-TOY-004-01: ITEM_CONFORMITY 正向
- [ ] TC-TOY-004-02: TESTED_SCOPE_CONFORMITY 正向
- [ ] TC-TOY-004-03: 整件全面合规拒绝
- [ ] TC-TOY-004-04: 自选措辞拒绝
- [ ] TC-TOY-004-05: 未覆盖项缺失拒绝
- [ ] TC-TOY-004-06: 外部证书不参与判定
- [ ] TC-TOY-004-07: SoD 拒绝
- [ ] TC-TOY-004-08: 结论不可变

### 阶段 8：集成测试
- [ ] 完整 AC-TOY-002@1.0.0 验收场景
- [ ] 审计与发件箱同事务回滚测试
- [ ] 权限集成测试（TOY_CONCLUSION_APPROVE_ITEM/SCOPE）

## 关键设计决策

### 1. 固定模板措辞
```csharp
// TESTED_SCOPE_CONFORMITY 固定模板
string template = $"所检 {testUnits.Count} 个 TestUnit 就下列已测危险域符合 {requirements}；未覆盖项：{uncoveredScopes}";
```

### 2. 整件全面合规永久禁用
```csharp
// 枚举中不存在 WHOLE_PRODUCT_COMPLIANCE
public enum ConclusionLevel
{
    ItemConformity = 1,
    TestedScopeConformity = 2
    // WHOLE_PRODUCT_COMPLIANCE 永久禁用
}
```

### 3. 未覆盖项强制披露
```csharp
// uncoveredScopes 为 null 或空时拒绝
if (uncoveredScopes == null || !uncoveredScopes.Any())
{
    throw new ToyException("TOY.CONCLUSION_EVIDENCE_INCOMPLETE", 
        "未覆盖项披露为强制项，不得省略或以空数组默认视为全覆盖");
}
```

### 4. 追加式不可变
```csharp
// Repository 只提供 Add，不提供 Update/Delete
public interface IConclusionRepository
{
    Task<ItemConformityConclusion> AddItemConclusionAsync(ItemConformityConclusion conclusion);
    Task<TestedScopeConformityConclusion> AddScopeConclusionAsync(TestedScopeConformityConclusion conclusion);
    // 无 Update/Delete 方法
}

// 数据库触发器阻止 UPDATE/DELETE
```

## 依赖关系

- **权限系统**：需要 TOY_CONCLUSION_APPROVE_ITEM 和 TOY_CONCLUSION_APPROVE_SCOPE 能力
- **电子签名**：需要 SEC-SIGN-001 重认证签署服务
- **审计系统**：需要 SEC-AUD-001@2.0.0 审计服务
- **TestUnit 模块**：需要 TestUnit、PhysicalObject、HazardDomain 的版本引用
- **Result 模块**：需要 AdoptedResult 版本引用和录入人信息（SoD）

## 风险与缓解

| 风险 | 缓解措施 |
|------|---------|
| 代理可能尝试添加整件全面合规层级 | 枚举中永久不存在该值，代码审查强制检查 |
| 调用方可能传入自选措辞 | 命令不接受措辞参数，完全由领域服务生成 |
| uncoveredScopes 可能被省略 | 验证器强制检查，null 或空数组均拒绝 |
| 已批准结论可能被修改 | Repository 无 Update/Delete，数据库触发器双重保护 |
| SoD 可能被绕过 | 验证器在批准前强制检查批准人与录入人身份 |

## 验收标准

1. ✅ 所有规格文件通过 `specgen validate`
2. ✅ `specgen ready --story ATC-TOY-004@1.0.0` 返回 READY
3. [ ] 所有 TC-TOY-004-* 测试用例通过
4. [ ] AC-TOY-002@1.0.0 完整验收场景通过
5. [ ] 代码覆盖率 ≥ 85%
6. [ ] 无整件全面合规相关代码或注释
7. [ ] 措辞完全由固定模板生成
8. [ ] 未覆盖项披露在所有路径强制执行
