# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-QC-001@1.0.0
# Spec-Fingerprint: fb078df40ed131d567ab2a6c9c9e83a56989475cec933856535f388075c40be0
Feature: AC-QC-001 QC 阻断
  给定某 QC 失败，当偏差仅被批准而影响范围和结果有效性尚未决定时，相关结果必须继续保持不可报告。

  @generated @prd-acceptance
  Scenario: AC-QC-001 QC 阻断
    Given 某分析批的 QC 运行中至少一条 QC 规则判定为 FAIL，QC 运行判定为 FAILED
    And 影响集覆盖该批内全部受影响结果目标，不只是发现异常的那一条
    And 授权人已记录偏差获批
    When 影响范围确认与结果有效性决定尚未记录
    And 查询任一受影响结果的 QC 可报告性并尝试解除阻断
    Then 可报告性必须为 BLOCKED 并列明未满足的关口
    And 解除阻断被拒绝且不产生解除事实
    And 偏差获批不改变可报告性（偏差获批不等于结果可报告）
    And 五关口（调查、影响范围、有效性决定、采用规则、技术复核）全部满足并解除后可报告性才为 ALLOWED
    And 关口事实、审计和 Outbox 同一事务提交或一起回滚
