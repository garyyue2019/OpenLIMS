# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-DEPLOY-001@0.1.0
# Spec-Fingerprint: 1a8c17fd620180e9ea7b9642adb5a256e4ea11e0f08e129e6d1eb53f71e13820
Feature: AC-DEPLOY-001 集团间独立数据平面
  两个检测集团的部署在运行时、数据、凭据、检索/AI 和备份恢复层面相互独立。

  @generated @prd-acceptance
  Scenario: AC-DEPLOY-001 集团间独立数据平面
    Given 集团甲和集团乙分别部署 OpenLIMS
    And 两套环境具有独立身份、密钥和数据资源
    When 使用集团甲的身份、网络入口、密钥、对象引用或检索请求尝试访问集团乙数据
    Then 访问因不存在共享运行时、共享凭据或共享数据平面而失败
    And 数据库、对象存储、队列、检索/AI 和备份恢复证据证明物理或账号级独立
    And 隔离失败被视为发布阻断
