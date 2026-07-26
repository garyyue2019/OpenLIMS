# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-AI-003@1.0.0
# Spec-Fingerprint: cd7af0fa22c97abfd7e6bb3c964e497c86fb8196164872febeb8ea5127542392
Feature: AC-AI-003 AI 输出失败关闭
  模型返回未知字段、非法单位或缺少必需来源的结构化输出时，验证必须隔离该输出，禁止生成下游产物并记录验证错误。本验收在纯契约层以确定性测试表达。

  @generated @prd-acceptance
  Scenario: AC-AI-003 AI 输出失败关闭
    Given 一个固定模型、路由、提示模板、输出模式和输入版本的运行控制封套
    And 模型输出包含未知字段、非法单位或缺少必需来源的候选
    When 以纯规则执行输出模式校验
    And 尝试把 AI_INFERENCE 候选提升为 VERIFIED_FACT
    And 对合法候选执行 MODIFY 处置
    Then 非法输出整体 QUARANTINED 并列明验证错误，不产生下游产物
    And 无权威来源和验证方法的提升被拒绝
    And MODIFY 处置保留 AI 原值、人工值、原因和责任人且类别不变
    And 合法输出的候选、缺口建议与处置可序列化冻结
