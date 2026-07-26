# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-RETEST-001@1.0.0
# Spec-Fingerprint: 578ffe8b2ade26b99f45bb148f55208c4fbafb71d4ebffbc26afd66ab971d97b
Feature: AC-RETEST-001 复测采用
  原结果已产生且需要复测时，系统必须在复测执行前记录原因、批准人和采用规则；复测完成后不得由执行人员任意选择更有利结果。

  @generated @prd-acceptance
  Scenario: AC-RETEST-001 复测采用
    Given 结果组已有 INITIAL 观测
    And 需要复测
    When 未记录采用规则时直接提交 RETEST 观测
    And 记录 RETEST_REPLACES_ORIGINAL 规则后提交带触发原因与批准引用的 RETEST 观测
    And 尝试采用对执行人员更有利的 INITIAL 结果
    And 按规则采用最新 RETEST 结果
    Then 无预先规则的 RETEST 观测被拒绝
    And 规则记录后 RETEST 观测成功且原数据、谱系、触发原因和批准全部保留
    And 违反策略的采用被拒绝（不得任意选择有利结果）
    And 合规采用成功且该组只有一个有效采用结果
    And 观测、规则与采用全部不可改写
