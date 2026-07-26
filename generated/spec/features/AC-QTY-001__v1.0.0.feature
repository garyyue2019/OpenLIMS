# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-QTY-001@1.0.0
# Spec-Fingerprint: 2491d853f11ec0dfde8d7955d88e516f15d67fdae2ce5d1d5408281be4d5a649
Feature: AC-QTY-001 并发超分配阻断与不可变流水链
  可用量 100 克时两名用户并发分配 80 克最多一笔成功；流水不可原地修改，更正只能冲销重记；缺失、越权或未知语义统一失败关闭。

  @generated @prd-acceptance
  Scenario: AC-QTY-001 并发超分配阻断与不可变流水链
    Given 调用人具有 quantity.post 及对象范围
    And 账户维度、单位、精度和守恒公差已在建账时固定
    And 账户经收货过账形成可用量 100 克
    When 两名用户使用相同 expectedCurrentVersion 并发提交 80 克分配
    And 随后对错误条目提交冲销和重记
    Then 最多一笔分配成功且有效分配不超过 100 克
    And 另一笔因版本冲突或可用量不足失败且无副作用
    And 已过账条目不可修改或删除，更正条目引用原条目
    And 流水、审计和 Outbox 在同一事务提交或整体回滚
    And 缺失配置、维度或单位不匹配、越权和 UNKNOWN 均阻断
