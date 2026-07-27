# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-RPT-002@1.0.0
# Spec-Fingerprint: 2d98ab932e3da46e37f42cbd3a7e63232078eaeeed05219a561c7e1ab53d76c9
Feature: AC-RPT-002 更正版本
  给定 V1 已签发，当样品描述需要更正时，系统必须执行结果归属影响评估、生成 V2、重新审批和签发；V1 保留且旧链接仍返回 V1 及其历史状态。

  @generated @prd-acceptance
  Scenario: AC-RPT-002 更正版本
    Given 报告 V1 已通过签发门禁并以内容哈希绑定签名完成签发
    And 样品描述需要更正，因而报告行的贡献链引用发生变化
    When 以更正动作提交结果归属影响评估引用并生成 V2
    And 对 V2 重新执行签发门禁、重新审批并签发
    And 分别按 V1 与 V2 的版本号取回报告
    Then V2 为序号加一的新版本，携带影响评估引用与其自身的内容哈希与签名
    And V1 的快照、哈希与签名原样保留，不被删除或覆盖
    And 按 V1 版本号取回仍返回 V1 自身的内容及其历史状态，绝不返回 V2 的内容
    And 验证页显示 V2 为当前有效版本、V1 为历史版本及二者的取代关系
    And 缺影响评估引用的更正被拒绝；就地修改已签发版本被拒绝
    And 版本快照、签名、受控动作、审计与 Outbox 在同一事务提交或一起回滚
