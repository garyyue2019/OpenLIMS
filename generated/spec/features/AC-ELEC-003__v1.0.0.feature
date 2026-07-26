# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-ELEC-003@1.0.0
# Spec-Fingerprint: 735b5f0293cd72731d9337ecb4fcdbe438ffd712e1766efce3b7903bd034a145
Feature: AC-ELEC-003 破坏性分配互斥与资格门禁全链
  破坏性异常试验分配活跃时，同一样机不得再分配至要求原始结构的任务；三端口全 ALLOWED 才创建分配；缺失、越权、过期或未知语义统一失败关闭。

  @generated @prd-acceptance
  Scenario: AC-ELEC-003 破坏性分配互斥与资格门禁全链
    Given 调用人具有 allocation.assign 及对象范围
    And 同一物理对象已存在一条活跃破坏性分配
    And Receiving、Scope 和 Quantity 端口对新请求均可评估
    When 尝试为同一物理对象创建新分配
    And 释放破坏性分配后重试
    And 并发提交两笔使用相同 expectedCurrentVersion 的分配
    Then 活跃破坏性分配存在时新分配被阻断且无副作用
    And 释放后三端口全 ALLOWED 的新分配成功并原样固定端口决定
    And 并发分配最多一笔成功，另一笔版本冲突
    And 任一端口 BLOCKED/UNKNOWN、有效期过期或越权均失败关闭
    And 分配、审计和 Outbox 同一事务提交或整体回滚
