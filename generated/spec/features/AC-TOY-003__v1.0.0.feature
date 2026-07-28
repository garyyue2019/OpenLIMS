# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TOY-003@1.0.0
# Spec-Fingerprint: 3ec688ceb9e9cceef64eb00727ee71769306409f24b991ece2bebfcc49a0194a
Feature: AC-TOY-003 TestUnit 计划与样品需求技术批准
  给定批准范围与版本化样品规则，系统按危险域、TestUnit、平行、序列、互斥破坏试验、化学最低取样量、复测预留和留样产生可解释草案；技术批准后方可驱动数量预留和任务分配。

  @generated @prd-acceptance
  Scenario: AC-TOY-003 TestUnit 计划与样品需求技术批准
    Given 批准范围固定了危险域与方法/样品规则版本
    And 任务包含两个平行、两个互斥破坏试验、化学最低取样量、复测预留和留样
    And 可用实物与 Quantity 账户版本已知
    When 系统生成 TestUnit 计划和样品需求草案
    And 技术人员检查输入、规则、分量和单位后批准
    And 系统据批准版本请求数量校验/预留并创建分配
    Then 每个 TestUnit 固定危险域、平行号和连续序列；互斥破坏任务使用不同 TestUnit
    And 需求分别展示基础量、化学最低量、平行增量、互斥增量、复测预留和留样，不丢失来源
    And 批准记录固定输入哈希、规则集、批准人和时间，批准后内容不可改写
    And 只有 APPROVED 版本可以调用 Quantity/Allocation 公共端口，端口决定与对象版本原样固定
    And 缺规则、UNKNOWN 适用性、单位冲突、数量不足或并发版本冲突均失败关闭且不产生半完成分配
