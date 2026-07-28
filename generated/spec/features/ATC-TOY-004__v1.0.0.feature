# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TOY-004@1.0.0
# Spec-Fingerprint: f30a41219cb744920466ed9d3b638ec00ffb5a7fa6973f4e1c3a3686bcccbd85
Feature: ATC-TOY-004 DEV-027 多 TestUnit 危险域覆盖结论
  多 TestUnit 结果可汇总为已测范围符合结论，逐一展示危险域覆盖依据并强制披露未覆盖项；ITEM_CONFORMITY 由技术负责人批准，TESTED_SCOPE_CONFORMITY 由授权签字人重认证签署；永久拒绝整件产品全面合规表述；外部认证证书仅作不参与判定的信息性旁注。

  @generated @atc-toy-004 @happy-path
  Scenario: TC-TOY-004-01 ITEM_CONFORMITY 正向
    Given 单检测项目 adoptedResult@version、requirement@version
    When 技术负责人请求 ITEM 结论且 SoD 通过
    Then 生成固定措辞结论
    And 无需重认证签署
    And 不可变保存

  @generated @atc-toy-004 @happy-path
  Scenario: TC-TOY-004-02 TESTED_SCOPE_CONFORMITY 正向
    Given 产品版本、3 个 TestUnit 各覆盖不同危险域、化学迁移未测
    When 授权签字人请求 SCOPE 结论并完成重认证签署
    Then 逐 TestUnit 显示危险域与覆盖依据
    And coveredHazardDomains 列 3 个
    And uncoveredScopes 显式披露化学迁移为 NOT_TESTED
    And 固定模板措辞含未覆盖项段落

  @generated @atc-toy-004 @negative
  Scenario: TC-TOY-004-03 整件全面合规拒绝
    Given 多个 TestUnit 分别覆盖不同危险域
    When 请求整件产品全面合规结论
    Then TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION
    And 不产生业务事实

  @generated @atc-toy-004 @negative
  Scenario: TC-TOY-004-04 自选措辞拒绝
    Given 调用方提供自选结论措辞或批准角色
    When 请求结论
    Then TOY.CONCLUSION_POLICY_UNKNOWN
    And 不得采用调用方默认值

  @generated @atc-toy-004 @negative
  Scenario: TC-TOY-004-05 未覆盖项缺失拒绝
    Given SCOPE 请求不提供 uncoveredScopes
    When 请求结论
    Then TOY.CONCLUSION_EVIDENCE_INCOMPLETE
    And 不得以空数组默认视为全覆盖

  @generated @atc-toy-004 @boundary
  Scenario: TC-TOY-004-06 外部证书不参与判定
    Given 外部认证引用
    When 记录为 informational 旁注
    Then notPartOfThisConclusion=true
    And 不减少未覆盖项
    And 不渲染为本实验室符合性意见

  @generated @atc-toy-004 @negative
  Scenario: TC-TOY-004-07 SoD 拒绝
    Given 结论批准人同时是 adoptedResult 录入人
    When 请求批准
    Then TOY.CONCLUSION_SOD_VIOLATION
    And 不产生业务事实

  @generated @atc-toy-004 @invariant
  Scenario: TC-TOY-004-08 结论不可变
    Given 已批准 SCOPE 结论
    When 尝试 UPDATE 或 DELETE
    Then 数据库层拒绝
    And 审计与发件箱回滚
