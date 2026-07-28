# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TOY-003@1.0.0
# Spec-Fingerprint: 2c7e575c9f013854f1911b551591c1eb06e6812de39af4df261a400b0fd69b05
Feature: ATC-TOY-003 实施 DEV-026 玩具 LabelReview 版本失效与重审
  实验室能证明某一市场和语言的包装/标签/说明书/营销年龄声明究竟审过哪个不可变版本；年龄决定或产品事实变化后，受影响旧审查不会继续悄悄放行，新审查与变更原因完整串联。

  @generated @atc-toy-003 @positive
  Scenario: TC-TOY-003-01 工件四维版本化
    Given 两种语言和市场的四类工件
    When 分别创建和追加版本
    Then 类型/语言/市场/图片证据与内容哈希固定
    And 旧版本原样可取回

  @generated @atc-toy-003 @positive
  Scenario: TC-TOY-003-02 年龄变化局部失效
    Given 中文与英文审查均批准
    When 年龄 V2 只命中中文审查范围
    Then 中文 INVALIDATED/RE_REVIEW_REQUIRED
    And 英文保持 VALID 且影响证据存在

  @generated @atc-toy-003 @negative
  Scenario: TC-TOY-003-03 UNKNOWN 影响失败关闭
    Given 影响规则版本未知
    When 年龄决定变化并查询旧审查
    Then TOY.LABEL_IMPACT_UNKNOWN 或 UNKNOWN
    And 旧审查不能继续放行

  @generated @atc-toy-003 @positive
  Scenario: TC-TOY-003-04 重审引用完整
    Given 旧审查已失效
    When 新工件和新审查批准
    Then 新审查引用旧版本与触发变更
    And 旧历史未改写

  @generated @atc-toy-003 @boundary
  Scenario: TC-TOY-003-05 非法工件边界
    Given 缺语言、市场、哈希、图片或非法工件类型
    When 提交版本
    Then 逐项 TOY.LABEL_ARTIFACT_INVALID
    And 业务事实为零

  @generated @atc-toy-003 @permission
  Scenario: TC-TOY-003-06 审查权限
    Given 有工件管理但无审查能力
    When 批准审查
    Then TOY.NOT_AUTHORIZED
    And 失败尝试留痕

  @generated @atc-toy-003 @concurrency
  Scenario: TC-TOY-003-07 并发审查决定
    Given 两个请求决定同一 DRAFT reviewVersion
    When 并发批准/拒绝
    Then 恰一个成功
    And 另一方 TOY.EXPECTED_VERSION_CONFLICT

  @generated @atc-toy-003 @audit
  Scenario: TC-TOY-003-08 证据失败回滚
    Given 图片对象确认、审计或 Outbox 注入失败
    When 创建工件、决定或失效
    Then 对应事实整体回滚
    And 失败证据保留

  @generated @atc-toy-003 @database-boundary
  Scenario: TC-TOY-003-09 不可变数据库强制
    Given 工件、审查和失效已保存
    When UPDATE/DELETE 任一行
    Then 数据库 55000 拒绝
    And 历史仍完整
