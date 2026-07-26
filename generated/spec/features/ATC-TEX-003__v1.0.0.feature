# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TEX-003@1.0.0
# Spec-Fingerprint: d39d389de0fb5854bbb44b6fae3156de00e5acd25949b63d67812c748ca1fa89
Feature: ATC-TEX-003 实施 DEV-012 纺织调湿/洗涤及超差契约切片
  纺织行业包的预处理（调湿/洗涤）与超差语义在纳入发布前即被契约冻结：计划/实际分离、逐字段公差评估、影响未批准前不得报告的阻断规则可被未来生产化直接复用，无破坏性契约变更。

  @generated @atc-tex-003 @positive
  Scenario: TC-TEX-003-01 公差内评估
    Given 实际条件全部在显式公差内
    When 纯规则评估
    Then WITHIN_TOLERANCE
    And reportingAllowed=true
    And 无偏差项

  @generated @atc-tex-003 @boundary
  Scenario: TC-TEX-003-02 超差逐字段偏差
    Given 温度超差而其余在公差内
    When 评估
    Then OUT_OF_TOLERANCE
    And 偏差项列明字段、计划值、实际值、偏差与公差
    And reportingAllowed=false

  @generated @atc-tex-003 @positive
  Scenario: TC-TEX-003-03 批准解锁报告
    Given 超差记录补充批准引用
    When 重新评估
    Then 决定仍为 OUT_OF_TOLERANCE
    And reportingAllowed=true
    And 偏差事实保留

  @generated @atc-tex-003 @boundary
  Scenario: TC-TEX-003-04 类型条件字段
    Given 调湿缺湿度或洗涤缺程序/洗涤剂/干燥方式
    When 校验
    Then 校验失败
    And 无部分结果

  @generated @atc-tex-003 @negative
  Scenario: TC-TEX-003-05 未知语义失败关闭
    Given 未知规则集版本或未知类型
    When 评估
    Then UNKNOWN 且 reportingAllowed=false 或校验失败

  @generated @atc-tex-003 @regression
  Scenario: TC-TEX-003-06 AC-TEXTILE-003 关联链
    Given 记录关联来源布批、CuttingPlan 与生成试样
    When 序列化并评估
    Then 关联链字段完整往返
    And 超差评估携带全部关联

  @generated @atc-tex-003 @regression
  Scenario: TC-TEX-003-07 序列化冻结
    Given 记录与评估结果样例载荷
    When JSON 往返并比对形状
    Then 字段名与结构与冻结样例一致

  @generated @atc-tex-003 @regression
  Scenario: TC-TEX-003-08 确定性
    Given 同一输入重复评估
    When 多次执行
    Then 结果逐字段一致
    And 无时钟或随机依赖
