# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-RPT-002@1.0.0
# Spec-Fingerprint: be1df834b2701d4d88f9277375391711e55fd283441f32e6e04873cba3ff1082
Feature: ATC-RPT-002 实施 DEV-023 报告签名与不可变版本链
  报告获得可验证的不可变历史：每个已签发版本都有自己的快照、哈希与签名，改一个字就换一个哈希从而使旧签名对不上；旧引用永远取回它当初对应的那一版，更正只能以新版本表达。这正是 RPT-VERS-002/004 与 SEC-SIGN-002 要防的三件事——覆盖历史、静默换件、改内容不重签。

  @generated @atc-rpt-002 @positive
  Scenario: TC-RPT-002-01 三要素受控签发
    Given 门禁 ALLOWED 且覆盖全部行
    When 携带重认证证据、签署意图与期望哈希签发
    Then 版本 ISSUED
    And 快照+哈希+签名落为不可变事实
    And 审计+发件箱同事务

  @generated @atc-rpt-002 @negative
  Scenario: TC-RPT-002-02 SEC-SIGN-002 内容变化使签名失效
    Given 已取得内容哈希预览
    When 报告行发生变化后仍以旧哈希签发
    Then RPT.CONTENT_HASH_MISMATCH
    And 不产生签名
    And audit_attempt 留痕

  @generated @atc-rpt-002 @negative
  Scenario: TC-RPT-002-03 签发三要素缺一不可
    Given 门禁 ALLOWED
    When 分别缺重认证证据、缺签署意图、缺期望哈希
    Then 逐项 RPT.SIGNATURE_REQUIREMENTS_UNMET

  @generated @atc-rpt-002 @positive
  Scenario: TC-RPT-002-04 AC-RPT-002 更正版本
    Given V1 已签发
    When 带影响评估引用更正并重新签发得 V2
    And 分别按 V1/V2 版本号取回
    Then V2 序号加一且有自己的哈希与签名
    And V1 快照哈希签名原样保留
    And 按 V1 取回仍返回 V1 内容与历史状态
    And 验证页显示当前版本与取代关系

  @generated @atc-rpt-002 @negative
  Scenario: TC-RPT-002-05 更正必须带影响评估
    Given V1 已签发
    When 无影响评估引用地更正
    Then RPT.IMPACT_ASSESSMENT_REQUIRED
    And 不产生新版本

  @generated @atc-rpt-002 @boundary
  Scenario: TC-RPT-002-06 撤回不删除且可取回
    Given V1 已签发
    When 撤回 V1 并再次按版本号取回
    And 重复撤回
    Then V1 状态 WITHDRAWN 但快照与签名保留
    And 取回返回 V1 自身与已撤回状态
    And 重复撤回被拒绝

  @generated @atc-rpt-002 @negative
  Scenario: TC-RPT-002-07 作废终止整链
    Given 已签发版本
    When 作废后再尝试任何受控动作或签发
    Then 链状态 VOIDED
    And 后续动作一律 RPT.VERSION_CHAIN_CLOSED

  @generated @atc-rpt-002 @negative
  Scenario: TC-RPT-002-08 追加式与并发
    Given 已有签名与快照
    When UPDATE/DELETE 及并发同版本签发
    Then 55000 拒绝
    And 恰一个成功，另一方冲突

  @generated @atc-rpt-002 @negative
  Scenario: TC-RPT-002-09 平台证据失败回滚
    Given 审计或发件箱注入失败
    When 签发
    Then 签名与快照回滚为零
    And audit_attempt 恰一次

  @generated @atc-rpt-002 @boundary
  Scenario: TC-RPT-002-10 版本链端口固定
    Given 含历史版本的报告
    When 正确/过期版本与未知规则集查询
    Then 返回当前有效版本与链状态 / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN]
