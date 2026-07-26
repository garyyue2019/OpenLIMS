# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TEX-001@1.0.0
# Spec-Fingerprint: 2174f31c3221485eb9866f1a3a268288193e8116e21ae591ba57e36d322f0750
Feature: ATC-TEX-001 实施 DEV-011 纺织样品需求未来适配契约切片
  纺织行业包获得可版本化、可测试的契约基线：样品需求计算维度、互斥裁样与不足阻断规则、CuttingPlan 结构在纺织包正式纳入发布前即被冻结，未来生产化不需要破坏性契约变更。

  @generated @atc-tex-001 @positive
  Scenario: TC-TEX-001-01 试样数与面积计算
    Given 三平行加复测预留加留样的需求行
    When 纯规则计算
    Then 所需试样数恒等于三者之和
    And 需求面积等于试样数乘长乘宽

  @generated @atc-tex-001 @boundary
  Scenario: TC-TEX-001-02 AC-TEXTILE-001 不足缺口
    Given 同一面料两个互斥破坏项目
    And 可用面积不足
    When 计算充足性
    Then INSUFFICIENT
    And 缺口按款色部件部位聚合并列明方向与项目

  @generated @atc-tex-001 @negative
  Scenario: TC-TEX-001-03 互斥共享拒绝
    Given 跨互斥破坏组的共享声明
    When 计算
    Then TEX.EXCLUSIVE_SHARE_REJECTED
    And 不产生试样计划

  @generated @atc-tex-001 @positive
  Scenario: TC-TEX-001-04 非破坏性共享
    Given 两条非破坏性同规格行声明共享
    When 计算
    Then 共享组按最大需求取试样数
    And 不重复累加面积

  @generated @atc-tex-001 @negative
  Scenario: TC-TEX-001-05 未知语义失败关闭
    Given 未知规则集版本或未知方向
    When 计算
    Then UNKNOWN 或校验失败
    And 无部分结果

  @generated @atc-tex-001 @boundary
  Scenario: TC-TEX-001-06 CuttingPlan 校验
    Given 完整与缺失字段的 CuttingPlan
    When 结构校验
    Then 完整计划通过
    And 尺寸非正、方向未知或试样数不一致失败

  @generated @atc-tex-001 @regression
  Scenario: TC-TEX-001-07 序列化冻结
    Given 全部契约记录的样例载荷
    When JSON 往返并比对形状
    Then 字段名与结构与冻结样例一致
    And 反序列化等值

  @generated @atc-tex-001 @regression
  Scenario: TC-TEX-001-08 确定性
    Given 同一输入重复计算
    When 多次执行
    Then 结果逐字段一致
    And 无时钟或随机依赖
