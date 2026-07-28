# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TOY-002@1.0.0
# Spec-Fingerprint: 05c0c0e03bc143956809b333c1b569c4c55938b09b65dd2b3b5e094cc17338a3
Feature: AC-TOY-002 可接触性、互斥 TestUnit 与危险域覆盖结论
  完整保留 PRD 的组合验收：滥用后新暴露部件须保存前后可接触性与照片并触发范围评估（DEV-024 已交付）；互斥破坏任务不得复用同一 TestUnit（DEV-025 已交付）；多 TestUnit 汇总必须逐一展示危险域覆盖依据、强制披露未覆盖项，且只能签发已测范围符合结论，永久拒绝整件产品全面合规表述。

  @generated @prd-acceptance
  Scenario: AC-TOY-002 可接触性、互斥 TestUnit 与危险域覆盖结论
    Given 一个玩具在 INITIAL 评估时内部件不可接触，并保存初始图片证据
    And 扭力/拉力后该部件暴露，后续跌落会破坏样品
    And 同一产品版本的 TestUnit 计划固定了 3 个 TestUnit，分别覆盖机械物理不同危险域，化学迁移危险域本次未测
    And 每个 TestUnit 均已固定物理实物版本、危险域版本、采用结果版本与已批准的覆盖决定
    When 记录扭力/拉力后的可接触性版本及图片证据
    And 尝试把互斥的破坏性任务分配给同一 TestUnit
    And 由技术负责人对单个检测项目请求 ITEM_CONFORMITY 结论
    And 由授权签字人对产品版本请求 TESTED_SCOPE_CONFORMITY 结论并完成重认证签署
    And 尝试请求整件产品全面合规结论或传入自选结论措辞
    And 提交仅带外部认证证书而缺少 uncoveredScopes 的汇总请求
    Then 事件前后可接触性版本和图片证据均不可变保存，新暴露部件触发受影响机械与化学范围评估
    And 同一 TestUnit 的互斥破坏任务分配被稳定错误码拒绝，失败不产生分配事实
    And ITEM_CONFORMITY 以固定的 adoptedResultRef@version 与 requirementRef@version 生成，无需重认证，SoD 校验通过
    And TESTED_SCOPE_CONFORMITY 逐 TestUnit 显示实际危险域、结果版本与批准覆盖依据，coveredHazardDomains 列出 3 个已测危险域，uncoveredScopes 显式披露化学迁移为 NOT_TESTED
    And 结论正文按固定模板渲染为『所检 3 个 TestUnit 就下列已测危险域符合…；未覆盖项：…』，未覆盖项段落存在且不可省略
    And 整件产品全面合规请求以 TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION 拒绝，不产生业务事实
    And 自选措辞、自选批准角色或未知结论层级以 TOY.CONCLUSION_POLICY_UNKNOWN 拒绝
    And 缺少 uncoveredScopes 的请求以 TOY.CONCLUSION_EVIDENCE_INCOMPLETE 拒绝；外部证书不得填补未覆盖项
    And 外部认证引用以 notPartOfThisConclusion=true 记录为信息性旁注，不参与判定且不出现在符合性意见中
    And 结论批准人同时是所引用结果录入人时以 TOY.CONCLUSION_SOD_VIOLATION 拒绝
    And 已批准结论不可就地修改，UPDATE/DELETE 被数据库层拒绝，审计与发件箱在同一事务写入
