# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-PLT-000@1.0.0
# Spec-Fingerprint: f45a6ee6de5f3a58d10e30dda8cc30c1227c57275025d8c251651b03de48be8d
Feature: ATC-PLT-000 建立可验证的模块化单体工程骨架
  后续AI开发任务可以在同一套已锁版本、可启动、可测试且边界可证明的工程底座上工作，不再自行选择技术栈、创建平行Host或跨模块访问私表，并禁止重新引入共享SaaS多租户数据平面。

  @generated @atc-plt-000 @positive
  Scenario: TC-PLT-000-01 锁定版本的确定性恢复与构建
    Given 批准的SDK、包源和锁文件
    And 干净工作区
    When 连续执行两次restore/build/web build
    Then 两次均成功
    And 锁文件和生成源码不变化
    And 无未声明latest依赖

  @generated @atc-plt-000 @smoke
  Scenario: TC-PLT-000-02 API、Worker与Web空壳烟雾测试
    Given 开发Compose依赖可用
    And 仅使用合成配置
    When 启动API、Worker和Web
    Then liveness/readiness符合契约
    And Web完成OIDC壳流程
    And 不存在检测业务端点或菜单

  @generated @atc-plt-000 @architecture
  Scenario: TC-PLT-000-03 模块引用越界失败
    Given 测试夹具模块A和B各有Contracts与Infrastructure
    When 夹具A引用B的Infrastructure、DbContext或EF实体
    Then 架构测试失败并定位违规边
    And 删除越界引用后通过

  @generated @atc-plt-000 @database-boundary
  Scenario: TC-PLT-000-04 数据库私有Schema访问被拒
    Given 两个夹具模块使用独立Schema和数据库角色
    When 模块A角色直接查询或更新模块B私表
    Then 数据库拒绝访问
    And 公共端口路径仍可按契约工作

  @generated @atc-plt-000 @security
  Scenario: TC-PLT-000-05 客户端不能选择集团
    Given 部署绑定集团甲
    And 客户端提交集团乙字段/Header/Query
    When 访问Host技术端点或未来命令绑定管道
    Then 请求整体以HTTP 400和PLT.GROUP_CONTEXT_OVERRIDE_FORBIDDEN拒绝，不得静默忽略
    And 受信集团上下文保持集团甲且不执行后续处理
    And 记录脱敏安全诊断且不泄露集团乙信息

  @generated @atc-plt-000 @deployment-isolation
  Scenario: TC-PLT-000-06 两个集团部署数据平面隔离
    Given 集团甲和乙使用独立运行实例、数据库、Bucket、IdP、密钥、OTLP/可观测性存储和备份夹具
    And 两边可以复用同一不可变构建镜像
    When 分别使用甲/乙数据库、对象存储和可观测性凭据尝试访问对方数据平面
    And 把集团乙令牌发送到集团甲Host并尝试读取直接ID、列表、对象链接和健康详情
    And 尝试把集团甲备份恢复到集团乙环境
    Then 交叉数据库、Bucket、IdP、日志/指标/Trace和备份访问全部失败且无信息泄露
    And 集团乙令牌以HTTP 403和AUTH.ORGANIZATION_GROUP_MISMATCH拒绝且不触发数据访问
    And 跨集团备份恢复在写入前被身份/清单校验阻断
    And 任何运行配置都不存在共享Secret、Bucket、数据库Schema、遥测数据平面或可切换集团入口

  @generated @atc-plt-000 @transaction
  Scenario: TC-PLT-000-07 审计或Outbox失败时事务回滚
    Given 测试夹具在保存AuditIntent或Outbox时失败
    When 提交夹具业务事务
    Then 夹具业务事实、审计意图和Outbox全部回滚
    And 恢复后重试只产生一套记录

  @generated @atc-plt-000 @idempotency
  Scenario: TC-PLT-000-08 Worker崩溃后的Inbox幂等
    Given 同一事件重复投递
    And 第一次处理在副作用后模拟崩溃
    When Worker重启并重新消费
    Then 可见副作用最多一次
    And Inbox/重试证据完整
    And 原失败记录保留

  @generated @atc-plt-000 @recovery
  Scenario: TC-PLT-000-09 必要依赖故障与恢复
    Given API已经READY
    When 依次中断并恢复PostgreSQL、IdP元数据或对象存储
    Then readiness失败关闭且有稳定诊断
    And 不使用过期允许状态
    And 恢复后重新完整探测并READY

  @generated @atc-plt-000 @migration
  Scenario: TC-PLT-000-10 生产启动不自动迁移
    Given 数据库存在待执行迁移
    And 环境为验证或生产
    When 启动API和Worker
    Then 应用不改变Schema
    And readiness报告迁移待处理
    And 独立迁移命令可审计执行

  @generated @atc-plt-000 @supply-chain
  Scenario: TC-PLT-000-11 供应链和Secret门禁
    Given 应用制品、容器和SBOM候选
    When 执行锁文件、SAST/SCA、Secret和镜像扫描
    Then 无未锁依赖或高危未处置项
    And 任一Secret样例使CI失败
    And 制品可追溯到提交和锁文件

  @generated @atc-plt-000 @cross-platform
  Scenario: TC-PLT-000-12 Windows与Linux验证入口一致
    Given 相同提交、锁文件和合成夹具
    When 分别运行verify.ps1和verify.sh的task/architecture/contracts/all配置
    Then 同名Profile执行同一门禁集合
    And 任一失败均返回非零
    And 不按平台静默跳过测试

  @generated @atc-plt-000 @scope-boundary
  Scenario: TC-PLT-000-13 业务能力保持为空
    Given 工程骨架构建完成
    When 扫描路由、模块、迁移、导航和OpenAPI
    Then 不存在收样、分析化学、报告或计费业务实现
    And 不存在src/modules或src/packs生产实现
    And 只有Host技术壳和测试夹具

  @generated @atc-plt-000 @reproducibility
  Scenario: TC-PLT-000-14 从空环境恢复工程验证
    Given 仅有仓库、批准工具链和合成配置
    And 无本机缓存和手工数据库状态
    When 按工程说明恢复、启动、测试并停止环境
    Then 全流程可重复完成
    And 清理不删除审计/测试证据
    And specgen check保持通过

  @generated @atc-plt-000 @concurrency
  Scenario: TC-PLT-000-15 两个Worker并发争抢同一Inbox消息
    Given 两个Worker实例同时收到相同消息ID和幂等键
    And Inbox尚无完成记录
    When 两个实例并发尝试领取并提交夹具副作用
    Then 只有一个实例取得有效处理权
    And 可见副作用和完成记录各只有一份
    And 失败或失去租约的实例不删除原消息、失败证据或成功记录

  @generated @atc-plt-000 @permission
  Scenario: TC-PLT-000-16 健康诊断遵守运维权限边界
    Given 匿名调用者、已认证但无运维权限调用者和获许运维调用者
    When 分别访问liveness、readiness摘要和详细诊断入口
    Then liveness只返回最小状态
    And 未授权调用者不能获得依赖名称、地址、版本或配置
    And 获许运维调用者只获得脱敏诊断且仍看不到Secret

  @generated @atc-plt-000 @audit
  Scenario: TC-PLT-000-17 审计意图字段完整且不可被日志替代
    Given 测试夹具执行成功、失败和重试动作
    When 检查事务内AuditIntent、Outbox和结构化日志
    Then AuditIntent包含actor、organizationGroup、object、action、rule/version、before/after version、correlationId和occurredAt
    And 失败与重试证据保留且只追加
    And 日志不冒充审计账本且任何载体都不含Secret或未脱敏正文

  @generated @atc-plt-000 @negative
  Scenario: TC-PLT-000-18 非法Host输入失败关闭
    Given 非法关联ID、客户端集团字段、未知配置字段或缺失必要部署配置
    When 启动Host或调用技术端点
    Then 返回稳定Problem Details或启动失败
    And 不采用开发默认值、不切换集团上下文且不产生业务副作用
    And 错误信息不泄露堆栈、Secret或内部路径

  @generated @atc-plt-000 @boundary
  Scenario: TC-PLT-000-19 技术端点输入与超时边界
    Given 刚好满足和超过关联ID长度/字符边界的请求
    And 依赖探测刚好满足和超过批准超时边界
    When 执行Host绑定与readiness探测
    Then 边界内输入确定性接受
    And 越界输入使用稳定错误拒绝
    And 探测超时使readiness失败关闭且不会沿用上一次成功状态
