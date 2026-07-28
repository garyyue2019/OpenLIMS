# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TOY-004@0.1.0
# Spec-Fingerprint: 1100697807f471625f44244c8d3ca7c9575eeeca880262045223a95e591b445d
Feature: AC-TOY-004 LabelReview 版本失效与重审
  给定按类型、语言、市场和图片证据版本化的玩具工件，当产品事实或年龄判定变化影响既有审查范围时，旧 LabelReview 必须追加失效并阻断继续使用；新审查引用变更和旧版本，历史不得改写。

  @generated @prd-acceptance
  Scenario: AC-TOY-004 LabelReview 版本失效与重审
    Given 同一玩具有中文/中国市场和英文/另一市场的标签工件版本，均带不可变图片证据
    And 中文工件 V1 的 LabelReview 已批准并固定 AgeGradeDecision V1
    And 英文工件审查范围不与本次中文声明变更重叠
    When AgeGradeDecision V2 冻结，并通过版本化影响规则判定中文市场年龄声明受影响
    And 尝试继续使用中文 V1 审查
    And 提交中文工件 V2 并完成引用旧审查与变更原因的新审查
    Then 中文 V1 审查追加 INVALIDATED 事实并进入 RE_REVIEW_REQUIRED，旧审查不可继续作为有效证据
    And 英文审查因范围不重叠保持原状态，且影响评估证据可重建
    And 中文工件 V1、图片、旧审查和失效原因原样保留；V2 与新审查形成新不可变版本
    And 影响规则 UNKNOWN 时旧审查按 UNKNOWN 阻断，不得静默保持 APPROVED
    And 工件、审查、失效、审计与 Outbox 同事务提交或一起回滚
