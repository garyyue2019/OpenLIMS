# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TOY-001@1.0.0
# Spec-Fingerprint: 35015245e39461e6ba73683a3b323b64e0d3c0ee3315af83b31379188a7ebef4
Feature: AC-TOY-001 年龄改判与新暴露部件
  给定年龄判定已冻结且可触及性初始评估已完成，当客户改口声明并且滥用后出现新暴露部件时，系统必须追加新的判定版本与评估版本、留下原判定原样、并对机械/化学/标签三个范围各触发一次重评。

  @generated @prd-acceptance
  Scenario: AC-TOY-001 年龄改判与新暴露部件
    Given 客户已申报年龄声明'3 岁及以上'，实验室据此做出 V1 年龄判定并冻结为生效
    And 该产品的 INITIAL 可触及性评估已记录，部件集合不含内部电池仓
    When 客户提交新的年龄声明'18 个月及以上'
    And 实验室据新声明追加 V2 年龄判定并冻结
    And 记录一次 AFTER_ABUSE 评估，其部件集合新增内部电池仓
    Then 客户声明与年龄判定分别留痕，两条声明与两个判定版本都可独立取回
    And V1 年龄判定内容原样保留并转为 SUPERSEDED，按 V1 取回仍返回其自身依据与批准人
    And V2 成为唯一生效判定，且携带自己的依据、标准引用、批准人与冻结时间
    And AFTER_ABUSE 评估为新版本，INITIAL 评估内容不被改写
    And 新暴露的内部电池仓为机械、化学、标签三个范围各生成一条 PENDING 重评触发
    And 三条触发结清前，该产品对应范围的可触及性状态为 REASSESSMENT_PENDING
    And 无新增暴露部件的评估版本不产生任何重评触发
    And 判定、评估、触发、审计与 Outbox 在同一事务提交或一起回滚
