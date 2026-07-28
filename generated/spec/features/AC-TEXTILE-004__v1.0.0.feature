# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TEXTILE-004@1.0.0
# Spec-Fingerprint: 8602bb7a5983e6bd61dcee99edccfc6b5560b782f663e0e912e0a8527195d0e4
Feature: AC-TEXTILE-004 样品不足、互斥裁样与运行时批准门禁验收
  在 AC-TEXTILE-001 契约验收上追加运行时证据：面积不足显示缺口并阻断 CuttingPlan 批准，互斥任务不得共享裁片，补样/范围变更证据、权限、并发和回滚可追溯。

  @generated @prd-acceptance
  Scenario: AC-TEXTILE-004 样品不足、互斥裁样与运行时批准门禁验收
    Given 同一块面料的两个互斥破坏项目需求行，每行三个平行并含复测预留和留样
    And 该款色部位可用面积不足
    And 行为人具备或缺少精确的纺织管理/批准能力
    When 运行时计算并保存样品需求
    And 创建 CuttingPlan 并尝试批准
    And 尝试跨互斥组共享、使用未知方向/规则集、并发追加同一计划版本或注入审计失败
    Then 结果为 INSUFFICIENT，缺口按款号、颜色、部件、部位聚合并列明方向与项目
    And CuttingPlan 批准被阻断，补样/范围变更 Outbox 证据与失败审计可追溯
    And 互斥共享、UNKNOWN、无权限、并发冲突均失败关闭且不产生半完成批准
    And SUFFICIENT 且结构有效的计划可由具备 textile.cutting-plan.approve 的行为人批准并冻结
    And UPDATE/DELETE 已发布事实被数据库拒绝，事务/审计/Outbox 失败整体回滚
