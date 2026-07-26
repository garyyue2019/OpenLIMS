# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-BATCH-001@1.0.0
# Spec-Fingerprint: 6edaaa437d6e66bdb92c3d067c6626157ab1cf8cedaea72b1275c80efe888d1a
Feature: AC-BATCH-001 批次 QC 影响传播
  分析批包含三个客户试样和一组 QC，QC 失败影响整个运行时必须冻结全部受影响成员，保留原批次和数据，通过批准的新运行处理；不得只重开其中一个有利结果。

  @generated @prd-acceptance
  Scenario: AC-BATCH-001 批次 QC 影响传播
    Given 一个分析批包含来自三个不同委托的试样成员和一个批准 QC 样成员
    And 全部试样成员均经 AllocationStatusPort ALLOWED 且版本固定
    And QC 失败影响整个运行
    When 提交 QC_FAILURE 冻结事件
    And 冻结后尝试新增成员、追加证据和查询状态
    And 以批准的新运行引用记录后续处理
    Then 整批全部成员被冻结且冻结事件列明原因和影响成员数
    And 原批次、成员与外部证据引用全部保留且数据库拒绝改写
    And 冻结后新增成员与证据被拒绝
    And 状态查询返回 BLOCKED 且不得只重开单个有利结果
    And 冻结、审计和 Outbox 同一事务提交或整体回滚
