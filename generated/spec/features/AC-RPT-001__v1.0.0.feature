# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-RPT-001@1.0.0
# Spec-Fingerprint: 1ed56a51a81c12ea469a45de398fe5724a115181e6b2854d479e755eb4ce30e1
Feature: AC-RPT-001 签发关口
  给定报告存在一个未解决身份冲突、一个 QC 阻断或一个无效签字授权，当签字人尝试签发时，系统必须阻止签发并逐项显示受影响对象、规则版本和下一步。

  @generated @prd-acceptance
  Scenario: AC-RPT-001 签发关口
    Given 一份已装配的报告，其报告行分别受三类问题影响
    And 第一行的收样项存在未解决身份冲突（收样资格端口 BLOCKED）
    And 第二行所属结果目标存在未解除的 QC 阻断（QC 可报告性端口 BLOCKED）
    And 第三行的授权签字人不具备该行方法/参数的认可签字资格
    When 签字人尝试推进该报告至待批准或签发
    Then 签发被阻止，报告不进入待批准
    And 逐项返回三个阻断项，每项含受影响对象引用、规则集版本、原因码与允许的下一步
    And 阻断项不得聚合为单一布尔值或单条汇总消息
    And 任一来源端口返回 UNKNOWN 时同样阻断（失败关闭）
    And 门禁评估、审计与 Outbox 在同一事务提交或一起回滚
