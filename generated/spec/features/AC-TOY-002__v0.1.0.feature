# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TOY-002@0.1.0
# Spec-Fingerprint: bd3aa9fd440507fcec07235ad177192723f98cd31ba19993d6248db751e8f443
Feature: AC-TOY-002 可接触性、互斥 TestUnit 与危险域覆盖
  完整保留 PRD 的组合验收：滥用后新暴露部件须保存前后可接触性与照片并触发范围评估，互斥破坏任务不得复用 TestUnit；多 TestUnit 汇总结论还须逐一展示危险域与覆盖依据。汇总结论部分受 OD-034 阻断。

  @generated @prd-acceptance
  Scenario: AC-TOY-002 可接触性、互斥 TestUnit 与危险域覆盖
    Given 一个玩具在 INITIAL 评估时内部件不可接触，并保存初始图片证据
    And 扭力/拉力后该部件暴露，后续跌落会破坏样品
    And 同一产品计划使用多个 TestUnit 覆盖不同危险域
    When 记录扭力/拉力后的可接触性版本及图片证据
    And 尝试把互斥的破坏性任务分配给同一 TestUnit
    And 尝试把多个 TestUnit 结果汇总为产品/型号结论
    Then 事件前后可接触性版本和图片证据均不可变保存，新暴露部件触发受影响机械与化学范围评估
    And 同一 TestUnit 的互斥破坏任务分配被稳定错误码拒绝，失败不产生分配事实
    And 汇总证据逐 TestUnit 显示实际危险域、结果版本与批准覆盖依据，并披露未覆盖项
    And 不得把多个 TestUnit 拼接成未实际存在的同一整件全部通过
    And OD-034 未批准前，结论生成保持 BLOCKED/UNKNOWN，不能产生可签发 ConformityDecision
