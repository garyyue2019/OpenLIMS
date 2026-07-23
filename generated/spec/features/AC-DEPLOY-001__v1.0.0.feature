# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-DEPLOY-001@1.0.0
# Spec-Fingerprint: d09e3a7d43b1382ff12b1647157b7f44739d13b07e145ac254f9c443b9af2f74
Feature: AC-DEPLOY-001 集团间独立数据平面真实交叉访问验收
  使用两套真实隔离的验证环境，对数据库、Bucket、IdP、遥测和备份凭据执行交叉访问，并验证集团乙令牌访问集团甲Host在数据访问前被拒绝；仅比较配置字符串不构成通过证据。

  @generated @prd-acceptance
  Scenario: AC-DEPLOY-001 集团间独立数据平面真实交叉访问验收
    Given 集团甲和集团乙分别部署独立OpenLIMS Host，并绑定不同OrganizationGroup
    And 两套环境分别具有可实际连接的数据库、对象存储Bucket、IdP、日志/指标/Trace入口和备份恢复资源
    And 为数据库、Bucket、IdP、遥测查询和恢复流程准备按集团签发的真实验证凭据与合成数据
    And 验证环境不包含生产Secret、生产数据或共享SaaS多租户数据平面
    When 使用集团甲数据库凭据连接集团乙数据库，并反向交叉执行
    And 使用集团甲对象存储凭据读取或列举集团乙Bucket，并反向交叉执行
    And 使用集团乙用户令牌、服务令牌或issuer/audience组合访问集团甲Host的数据端点
    And 使用集团甲遥测凭据查询集团乙日志、指标或Trace，并反向交叉执行
    And 尝试使用集团甲备份、WAL、对象副本、密钥或恢复凭据恢复到集团乙环境，并反向交叉执行
    Then 所有数据库、Bucket、遥测和恢复交叉访问均由资源、账号、网络或密钥边界拒绝，未读取任何对方合成数据
    And 集团乙令牌访问集团甲Host在创建业务数据访问前失败，稳定错误、Trace和不含令牌正文的审计尝试可关联
    And 跨集团备份恢复在校验或执行步骤被阻断，目标环境保持未变且失败证据被追加保存
    And 基础设施清单、访问日志、资源策略、数据库会话、对象请求、遥测查询和恢复记录共同证明不存在共享数据平面
    And 任一交叉访问成功、在数据访问后才拒绝、证据缺失或仅比较配置字符串均视为验收失败和发布阻断
