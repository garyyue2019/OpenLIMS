# 07 发布、Seal 与历史治理

## 1. 三种“基线”不要混淆

| 名称 | 文件 | 含义 |
|---|---|---|
| Source baseline | `spec/source-baseline.json` | 已审阅 PRD 来源指纹，不是业务批准 |
| Release baseline | `spec/releases/*.json` | 某候选发布精确选择哪些规格版本 |
| Seal | `spec/seals/**/*.seal.json` | 已批准发布的不可覆盖哈希记录 |

## 2. Release baseline 规则

- `selected_specs` 只允许精确版本键；
- 必须形成完整依赖闭包；
- 在制对象保存 release/requirements lock 引用；
- 不允许运行时解析最新版；
- 候选发布可以 proposed/in_review；
- 只有发布和所有选择规格均 approved 才能 Seal。

## 3. Seal 内容

Seal 包含：

- 发布 ID、版本和完整指纹；
- 每个选择规格的完整/行为指纹；
- 当前生成 lock 指纹；
- 输出文件哈希；
- PRD 来源文档哈希；
- 前一个 Seal 路径和哈希；
- 封存责任人、日期和理由；
- Seal 自身哈希。

目标文件用 exclusive-create 创建，存在时直接失败，不提供覆盖开关。

## 4. 历史验证

`verify-history` 检查：

- Seal 自身哈希；
- 前驱 Seal 是否存在且哈希匹配；
- 已封存 release/spec 文件是否仍存在；
- 已封存版本完整指纹是否变化。

同一版本变化一律视为历史篡改，而不是普通需求变更。

## 5. Breaking Gate

候选发布与前一 Seal 比较：

- 旧版本同键哈希变化：失败；
- 版本倒退：失败；
- PATCH 行为指纹变化：失败；
- 移除旧规格：默认 breaking；
- MAJOR 行为变化：需要 release 中明确批准；
- 未固定依赖或选择未闭包：失败。

`breaking_change_approvals` 应引用经过书面批准的逻辑 ID 或新版本键，并在变更记录中补充：

- 原因；
- 数据/配置迁移；
- 在制业务处理；
- API/客户兼容；
- 回滚；
- 验收证据。

## 6. 数据库和业务历史

- 已执行迁移禁止编辑，只新增迁移；
- 已冻结委托继续引用原 requirements lock；
- 规则升级不自动重算或改写历史结果；
- 需要迁移时创建影响评估和批准决定；
- 已签发报告、电子签名、原始数据和审计链保持不可变；
- 更正或撤回通过新版本和引用表达。

## 7. 生产级防篡改补充

当前本地 Seal 主要防止误操作。生产还应启用：

1. Git 主分支保护和强制评审；
2. CODEOWNERS 指定产品、质量、架构和安全责任人；
3. CI 在外部可信存储保存最新 Seal head；
4. 发布构建签名和制品证明；
5. 法规要求时使用 WORM 或外部受控证据库；
6. 定期恢复演练验证 Seal、报告、结果、附件和审计链引用。
