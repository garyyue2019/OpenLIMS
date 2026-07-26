# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-AI-001@1.0.0
# Spec-Fingerprint: ba2fc8779dceef1736e9ab0f9e0921a9fb41427daf28e2adbef266e17787ce6a
Feature: ATC-AI-001 实施 DEV-016 AI 资料抽取与缺口建议契约切片
  AI 旁路在获得法务/隐私批准前即拥有被冻结的治理契约：运行封套、事实类别税则、失败关闭校验和人工处置结构可被未来生产化直接复用，且任何消费方都无法绕过'近似、非约束、未验证'的治理边界。

  @generated @atc-ai-001 @positive
  Scenario: TC-AI-001-01 封套完整性
    Given 全固定引用的封套
    When 校验
    Then 通过
    And 缺任一引用即失败

  @generated @atc-ai-001 @negative
  Scenario: TC-AI-001-02 AC-AI-003 失败关闭
    Given 含未知字段、非法单位或缺来源的输出
    When 校验
    Then 整体 QUARANTINED
    And 错误明细列出字段与代码
    And 无下游产物

  @generated @atc-ai-001 @negative
  Scenario: TC-AI-001-03 AC-AI-002 类别不得提升
    Given AI_INFERENCE 候选无权威来源或验证方法
    When 声明 VERIFIED_FACT 或提升
    Then AIX.FACT_CLASS_PROMOTION_REJECTED

  @generated @atc-ai-001 @boundary
  Scenario: TC-AI-001-04 不确定性表达
    Given 同一字段多候选分支与弃权
    When 校验
    Then 分支与弃权合法
    And 伪装单一确定答案的重复字段拒绝

  @generated @atc-ai-001 @regression
  Scenario: TC-AI-001-05 处置原值保留
    Given MODIFY 处置
    When 校验
    Then AI 原值、人工值、原因、责任人齐备
    And 缺任一即失败
    And 类别不变

  @generated @atc-ai-001 @positive
  Scenario: TC-AI-001-06 缺口建议独立
    Given 缺失信息与澄清问题
    When 校验
    Then 建议独立于候选表达
    And 不写入受控对象

  @generated @atc-ai-001 @regression
  Scenario: TC-AI-001-07 序列化冻结
    Given 全部契约记录样例载荷
    When JSON 往返并比对形状
    Then 字段与结构与冻结样例一致

  @generated @atc-ai-001 @regression
  Scenario: TC-AI-001-08 确定性
    Given 同一输入重复校验
    When 多次执行
    Then 结果逐字段一致
    And 无时钟或随机依赖
