# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TOY-004@0.1.0
# Spec-Fingerprint: 962b89343dbf84ffed312730c1b66e632966505b81c681927e8e64b77c670259
Feature: ATC-TOY-004 阻断 DEV-027 多 TestUnit 危险域覆盖结论
  在开放决策被正式解决前，仓库对多 TestUnit 产品结论保持可见且可验证的阻断，防止代理把多个实物的局部结果拼成一件并不存在的全面通过；决定后可以从已冻结证据边界创建新的 MAJOR 任务版本。

  @generated @atc-toy-004 @governance
  Scenario: TC-TOY-004-01 readiness 明确阻断
    Given OD-034@0.1.0 proposed/open
    When 运行 ready
    Then BLOCKED
    And 列出 open decision 与 proposed dependencies

  @generated @atc-toy-004 @scope-boundary
  Scenario: TC-TOY-004-02 无结论接口或迁移
    Given 当前 blocked 规格
    When 扫描路由、公共端口、迁移和报告门禁
    Then 不存在 toy ConformityDecision 写接口
    And RPT.CONFORMITY_DECISION_UNAVAILABLE 保持

  @generated @atc-toy-004 @negative
  Scenario: TC-TOY-004-03 虚构整件结论拒绝
    Given 多个 TestUnit 分别覆盖不同危险域
    When 请求描述为同一整件全部通过
    Then TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION
    And 不产生业务事实

  @generated @atc-toy-004 @negative
  Scenario: TC-TOY-004-04 未知策略失败关闭
    Given 调用方提供自选结论措辞或角色
    When 请求结论
    Then TOY.CONCLUSION_POLICY_UNKNOWN
    And 不得采用调用方默认值
