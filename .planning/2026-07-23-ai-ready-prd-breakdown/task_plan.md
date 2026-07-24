# Task Plan: AI-ready PRD Breakdown

## Goal
基于现有 OpenLIMS PRD，形成并实际交付一套可运行、可追踪、可增量同步的 AI 开发规格与需求编译工具链。

## Current Phase
Phase 49: 分支提交与GitHub发布

## Phases

### Phase 1: PRD 结构盘点
- [x] 识别文档章节、业务边界、角色、流程、需求 ID 与验收现状
- [x] 判断当前 PRD 与 AI 可执行规格之间的主要缺口
- **Status:** complete

### Phase 2: AI 开发拆解模型
- [x] 定义从产品域到 Epic、Feature、Story、Task 的层级
- [x] 定义每类交付物的必填信息和完成定义
- **Status:** complete

### Phase 3: 结合 OpenLIMS 生成实例
- [x] 给出适合该 PRD 的工作包、依赖关系和开发顺序
- [x] 给出一个可直接投喂 AI 的任务卡样例
- **Status:** complete

### Phase 4: 验证与交付
- [x] 核对拆解覆盖业务、数据、接口、权限、AI、测试和运维
- [x] 汇总实施建议与下一步
- **Status:** complete

### Phase 5: 同步机制与规格架构
- [x] 设计规范源、版本、逐条哈希、依赖图、派生物清单和历史不可变策略
- [x] 设计 OpenLIMS 目录、Schema、首批结构化需求和任务卡模板
- [x] 并行复核领域拆分、生成器边界和验收策略
- **Status:** complete

### Phase 6: 需求编译器与交付物实现
- [x] 实现无第三方运行依赖的 CLI、校验、影响分析、生成和过期检查
- [x] 实现纯生成文件、脚手架和人工维护文件的所有权控制
- [x] 生成追踪矩阵、影响清单、任务卡、Gherkin 与文档索引
- **Status:** complete

### Phase 7: 测试、CI 与变更演练
- [x] 建立自动化单元/集成测试和 CI 门禁示例
- [x] 演练需求变更、局部重生成、过期检测、破坏性变更与历史版本保留
- [x] 验证原 PRD 未被修改且所有派生物可重复生成
- **Status:** complete

### Phase 8: 使用文档与最终验收
- [x] 编写细致的中文操作手册、维护规则和 AI 协作约束
- [x] 核对目录、Schema、样例、命令、测试和追踪覆盖
- [x] 交付结果、已知边界和下一阶段建议
- **Status:** complete

### Phase 9: 集团多机构产品决策与影响盘点
- [x] 按仓库门禁校验规格、来源状态和直接/传递影响
- [x] 盘点 PRD、规格和任务卡中的 Tenant、多租户与跨租户假设
- [x] 固化“一集团一套独立部署、集团内多机构、禁止共享 SaaS 多租户”的术语和边界
- **Status:** complete

### Phase 10: PRD 与结构化规格迁移
- [x] 更新 PRD 中的产品定位、组织模型、数据边界、权限和待决策事项
- [x] 新增已批准的 OD-002，并同步发布基线、架构决策、部署决策和任务卡
- [x] 增加防止共享 SaaS 多租户语义回流的契约测试
- **Status:** complete

### Phase 11: 重新生成与最终验收
- [x] 接受经评审的 PRD 来源变更并重新生成全部派生物
- [x] 运行严格校验、来源/历史/生成一致性、自动化测试和编译检查
- [x] 验证二次生成 written=0，并交付变更范围与仍待评审事项
- **Status:** complete

### Phase 12: Release 1 决策输入审查
- [x] 将用户提供的行业、产品、方法、灯塔机构、实验室、容量和资料可用性映射到 OD
- [x] 区分已知事实、候选范围、口径歧义和不可代填的缺失决策
- [x] 复核 ATC-REC-001 的最小依赖闭包
- **Status:** complete

### Phase 13: 决策冲刺评审包与草案规格
- [x] 编写可供产品、实验室、架构和安全联合评审的 Release 1 决策冲刺包
- [x] 将已知输入写入相关 proposed/in_review 规格，所有未决值保持显式阻断
- [x] 为历史数据、黄金场景和容量基线给出采集模板
- **Status:** complete

### Phase 14: 生成、门禁与交付
- [x] 运行来源、规格、历史、生成和测试门禁
- [x] 确认 Story 仍只因真实未决事项 BLOCKED
- [x] 交付最少补充问题和下一轮批准顺序
- **Status:** complete

### Phase 15: 用户确认吸收与范围收敛
- [x] 将虚拟机构、玩具方向、三市场、双技术包、微生物排除、日均 500 订单和无技术栈限制写入事实/假设边界
- [x] 复核玩具最小可验收切片、虚拟灯塔治理边界和技术栈候选
- [x] 明确仍需用户批准的唯一产品类别、市场/协议版本和主技术包
- **Status:** complete

### Phase 16: 决策包与草案规格更新
- [x] 将决策冲刺包由纺织候选改为玩具候选，并补充角色、容量与资料采集要求
- [x] 更新 OD-001、OD-020、OD-025、ED-001 等 proposed/open 规格，不伪造批准
- [x] 形成可评审的技术栈候选 ADR/决策材料并更新配套说明
- **Status:** complete

### Phase 17: 重新生成、全门禁与交付
- [x] 使用生成器更新 generated/spec，禁止直接编辑生成文件
- [x] 运行严格校验、来源状态、历史、幂等生成、check、测试与编译检查
- [x] 交付已收敛内容、剩余决策和下一轮最小用户选择
- **Status:** complete

### Phase 18: 分析化学首发选择吸收与影响复核
- [x] 记录用户确认的玩具资格、市场顺序和分析化学优先选择，并保持正式批准边界
- [x] 复核玩具分析化学最小生产切片、黄金场景、QC/批次/仪器证据和跨技术包转交
- [x] 识别 OD-001、OD-020、OD-025、评审包、模板和契约测试的完整影响范围
- **Status:** complete

### Phase 19: 规格、评审材料与采集资产重构
- [x] 将物理机械首发候选重构为分析化学首发候选，物理机械后移
- [x] 新增或扩充分析化学方法、材料颜色覆盖、制备/分析批和 QC 证据模板
- [x] 更新仓库契约测试，固定用户选择但不伪造 approved/decided
- **Status:** complete

### Phase 20: 重新生成、全门禁与交付
- [x] 仅通过 specgen 刷新 generated/spec，并验证二次生成 written=0
- [x] 运行严格校验、来源、历史、check、全部测试、编译和差异检查
- [x] 交付已确认范围、剩余正式审批/证据门禁和下一步工程任务
- **Status:** complete

### Phase 21: ATC-PLT-000 任务边界与依赖设计
- [x] 复核现有Story结构、生成器渲染、Release基线和任务依赖规则
- [x] 定义工程骨架任务的目标、允许路径、非目标、模块/数据/安全/审计/部署契约
- [x] 定义正反向、边界、权限、并发、恢复、供应链和架构测试矩阵
- **Status:** complete

### Phase 22: 结构化任务卡与依赖闭包实现
- [x] 新增ATC-PLT-000@0.1.0机器规格及人工评审说明
- [x] 将Release基线和六张收样任务精确依赖工程骨架任务
- [x] 更新契约测试和AI开发导航，保持任务proposed/blocked
- **Status:** complete

### Phase 23: 生成、Ready门禁与最终验收
- [x] 仅通过specgen生成任务卡、追踪和发布锁，并验证二次generate written=0
- [x] 运行完整仓库门禁及ATC-PLT-000/ATC-REC-001 Ready检查
- [x] 交付任务卡、阻塞项、实施前批准顺序和未提交状态
- **Status:** complete

### Phase 24: ATC-PLT-000 联合评审输入与依赖裁剪复核
- [x] 逐项复核ED-001、OD-020、OD-025、SEC/NFR/AC阻塞语义及所需证据
- [x] 判断平台骨架依赖是否夹带业务/生产批准，并形成保留、拆分或替代建议
- [x] 固定推荐技术结论、不可由AI代批事项和最小人工选择集
- **Status:** complete

### Phase 25: 联合评审包、签署模板与契约实现
- [x] 创建ATC-PLT-000聚焦审批包，逐项给出推荐结论、反对选项、条件和责任角色
- [x] 创建受控评审记录模板，覆盖身份、授权、结论、条件、证据、时间和签名/哈希引用
- [x] 更新导航与仓库契约测试，保证草案状态、链接、必填证据和禁止虚假批准
- **Status:** complete

### Phase 26: 生成、门禁与评审交付
- [x] 运行严格规格、来源、历史、生成幂等、check和全部测试门禁
- [x] 重新执行ATC-PLT-000 Ready，确认剩余阻塞与评审包一致
- [x] 交付责任人可直接执行的评审顺序、最小回复格式和未提交状态
- **Status:** complete

### Phase 27: 下一Major版本精确变更集设计
- [x] 固定ED/NFR/ATC/Release/REC下一版本映射、依赖图和Major变更理由
- [x] 逐字段定义收窄、保留、后移和禁止变化，避免评审后由实现代理再解释
- [x] 设计可签署变更集、哈希和PENDING责任矩阵，不推断任何ACCEPT结论
- **Status:** complete

### Phase 28: 可签署变更集与PENDING评审清单实现
- [x] 创建精确下一版本变更集和SHA-256侧车
- [x] 创建逐评审项逐角色的PENDING工作清单，预填对象哈希但不预填身份、授权或批准
- [x] 更新审批包、导航和契约测试，固定变更集/清单一致性和禁止虚假批准
- **Status:** complete

### Phase 29: 全门禁与评审工作台交付
- [x] 运行严格规格、来源、历史、幂等生成、check和全部测试
- [x] 核对当前Ready仍BLOCKED、impact为空且现有机器规格状态未变
- [x] 交付责任人只需填写的字段、后续签署顺序和未提交状态
- **Status:** complete

### Phase 30: 下一Major机器草案建模与契约复核
- [x] 复核15个目标对象的Schema、现有0.1.0字段、精确依赖和版本迁移边界
- [x] 固定仅允许proposed/in_review/blocked的状态矩阵，并验证平台链不再直接或传递依赖OD-020/OD-025
- [x] 定义旧0.1.0保留、新1.0.0并存及Release/REC同步升级的契约断言
- **Status:** complete

### Phase 31: 机器草案、评审工作台与契约实现
- [x] 创建15个1.0.0机器草案，不创建任何approved/decided/ready对象
- [x] 同步下一版本变更集、SHA侧车、33条PENDING清单subject_hash、README计数和评审说明
- [x] 增加1.0.0草案契约测试，保留0.1.0历史契约并禁止平台链夹带业务/生产批准
- **Status:** complete

### Phase 32: 重新生成、全门禁与草案交付
- [x] 仅通过specgen刷新generated/spec，并验证第二次generate written=0
- [x] 运行严格校验、来源状态、历史、check、全部测试、Ready、impact、编译和差异门禁
- [x] 核对59个规格、14张任务Markdown、19个Feature及所有新Story继续BLOCKED
- **Status:** complete

### Phase 33: 人工评审输入门禁设计
- [x] 复核specgen CLI、错误码、评审CSV、变更集哈希和ED-001版本锁结构
- [x] 定义PENDING、ACCEPT、条件接受、拒绝、弃权、缺失身份/授权/签名与锁值的确定性判定
- [x] 固定只读命令、输出格式、退出码和禁止自动补值边界
- **Status:** complete

### Phase 34: Review Status CLI、文档与测试实现
- [x] 实现按change-set发现评审清单、校验对象哈希并汇总33个角色槽
- [x] 扫描关联规格的版本锁，逐项报告空精确值、待核验状态和缺失证据
- [x] 更新AI开发导航和自动化测试，覆盖正常、阻断、篡改、缺字段和条件接受场景
- **Status:** complete

### Phase 35: 全门禁与评审输入工作流交付
- [x] 验证当前真实清单返回BLOCKED且不修改任何评审数据或规格状态
- [x] 运行AGENTS.md完整门禁、二次幂等生成、编译和差异检查
- [x] 交付责任人填写顺序、命令和机器可判定的解除条件
- **Status:** complete

### Phase 36: 联合评审发起与明确选择收集
- [x] 恢复评审上下文并重新运行规格、来源、影响与平台Story Ready前置门禁
- [ ] 取得发起人的受控身份引用、参与角色槽和对应授权范围
- [x] 取得RV-PLT-001至RV-PLT-008的逐项明确选择，不把“继续推进”解释为ACCEPT
- **Status:** in_progress

### Phase 37: 责任人证据与技术版本锁闭合
- [ ] 由八类责任角色按33个角色槽提交身份、授权、证据、结论、时间和签名
- [ ] 为ED-001的15项技术锁补齐精确值、VERIFIED状态和证据引用
- [ ] 运行review-status直到返回EVIDENCE_READY，保留拒绝、条件和取代记录
- **Status:** pending

### Phase 38: 批准后继版本与Ready门禁
- [ ] 依据已核验评审证据创建新的SemVer后继版本，不原地改写现有1.0.0草案
- [ ] 仅通过specgen刷新generated/spec并执行AGENTS.md完整门禁及二次幂等生成
- [ ] 仅在ATC-PLT-000批准后继版本返回READY后移交工程骨架实现
- **Status:** pending

### Phase 39: 受控提交与 GitHub 发布
- [x] 恢复活动计划，核对规格/来源/影响/Story Ready前置门禁及Git目标
- [x] 执行AGENTS.md完整终审、二次幂等生成、评审阻断核验和差异检查
- [x] 暂存完整交付、创建提交、推送`main`到`origin/main`并核对远端CI
- **Status:** complete

### Phase 40: DEV-001 Spike 边界与环境
- [x] 恢复计划并运行规格、来源、影响与Story Ready前置门禁
- [x] 核对任务允许路径、仓库现状和本机工具链，固定不引入业务默认值的Spike边界
- [x] 创建独立开发分支并把后端、前端、运行验证拆给互不冲突的代理
- **Status:** complete

### Phase 41: DEV-001 并行工程实现
- [x] 创建.NET 10解决方案、API/Worker/BuildingBlocks骨架和基础自动化测试
- [x] 创建Vue 3/TypeScript前端、可访问应用壳和前端测试
- [x] 创建本地依赖编排、跨平台验证入口和CI应用门禁
- **Status:** complete

### Phase 42: DEV-001 集成与修复
- [x] 主代理审查所有代理产物，统一命名、模块边界、配置和健康检查契约
- [x] 运行后端、前端、脚本、架构、规格和生成幂等测试并修复失败
- [x] 启动可运行应用并验证浏览器/API健康路径；记录Docker缺失导致的真实限制
- **Status:** complete

### Phase 43: DEV-001 交付
- [x] 核对只修改允许路径且未触碰spec/generated/治理文档
- [x] 汇总实际可运行命令、已验证范围、剩余环境限制和下一任务包入口
- **Status:** complete

### Phase 44: DEV-001B 前置门禁、边界与基线
- [x] 恢复计划并运行规格、来源、影响与ATC-PLT-000 Ready门禁
- [x] 核对任务允许路径、现有工程代码和未提交DEV-001基线
- [x] 固定DEV-001B为用户明确批准的基础依赖与登录集成Spike，不补业务默认值
- **Status:** complete

### Phase 45: DEV-001B 并行实现
- [x] 实现PostgreSQL连接、迁移、Outbox/Inbox及审计意图最小持久化
- [x] 实现Keycloak OIDC/JWT登录、退出、可信集团上下文和授权失败路径
- [x] 实现MinIO对象存储端口、外部依赖readiness及本地依赖初始化
- **Status:** complete

### Phase 46: DEV-001B 集成与自动化验证
- [x] 集成Web/API/Worker配置并补齐正反向、权限、并发、恢复和契约测试
- [x] 在可用环境实际启动依赖并执行端到端验证；若本机仍无Docker Engine则提供可重复的替代验证证据
- [x] 运行应用构建、依赖审计、Compose静态检查和浏览器/API验证并修复失败
- **Status:** complete

### Phase 47: DEV-001B 完整门禁与交付
- [x] 执行AGENTS.md六道完成门禁和二次幂等generate
- [x] 核对受保护规格/生成目录未被人工修改，汇总实现范围、已知限制和下一任务包入口
- [x] 保留为独立、可审查的未提交增量，除非用户另行要求提交或推送
- **Status:** complete

### Phase 48: DEV-001/DEV-001B 受控提交前终审
- [x] 恢复发布上下文并重跑规格、来源、影响与Story Ready前置门禁
- [x] 重跑AGENTS.md六道门禁、应用锁定构建测试、依赖审计和Compose静态检查
- [x] 核对受保护路径、提交范围、远端目标和工作区无未知文件
- **Status:** complete

### Phase 49: 分支提交与GitHub发布
- [x] 暂存完整DEV-001/DEV-001B增量并复核暂存差异
- [ ] 创建单一可审查提交并推送`codex/dev-001-engineering-skeleton`
- [ ] 核对远端分支提交SHA与本地一致，不直接合并main
- **Status:** in_progress

### Phase 50: GitHub Actions验证与交付
- [ ] 监控该提交触发的规格与应用工作流直到终态
- [ ] 失败时读取具体日志、修复、重新门禁并追加提交；成功时记录运行URL和证据
- [ ] 交付提交、远端分支、CI状态、真实容器Smoke结果和下一步建议
- **Status:** pending

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| 本轮只读分析 PRD，不直接改写原文档 | 用户当前询问拆解方法，尚未明确要求落盘改造 PRD |
| 使用独立 `.planning` 目录 | 避免覆盖项目根目录中上一轮已经完成的规划文件 |
| 以纵向业务切片作为 AI 任务单位 | 单条 PRD 需求往往同时涉及多个工程层；按页面、表或前后端横切无法独立验收 |
| 开发前先冻结 Release 1 决策包和技术 ADR | 当前 34 个 OD 及技术栈空白中有多项会改变数据、接口与部署方案 |
| 以需求 ID + AC + RULE + NFR 构成追踪闭环 | 防止 AI 只实现正常路径或忽略权限、审计、并发和恢复 |
| 继续扩展现有活动计划而不新建平行计划 | 本次是上一轮设计结论的直接实现，符合技能对追加工作的要求 |
| 原 PRD 保持只读，结构化规格作为显式批准的机器源 | 避免解析自由文本后静默改变工程语义，同时保留产品叙述与工程规格的边界 |
| AI 不在生成器或 CI 中运行 | AI 可以起草结构化变更，但编译、哈希、影响、渲染和门禁必须是确定性工具 |
| 任务稳定 ID 使用 `ATC-*`，发布/Epic/Feature 独立建模 | 调整目标发布不应破坏任务历史身份和证据追踪 |
| 使用完整对象哈希、行为哈希和不可覆盖 Seal | 分别控制原地篡改、SemVer 行为变化和已发布历史链 |
| 产品采用集团多机构、每集团独立部署，禁止共享 SaaS 多租户 | 用户已明确作出产品方向决策；集团是部署与数据平面边界，法人、实验室、部门和工作中心是集团内组织单元，送检客户不是租户 |
| 广东华瑾及其测试中心按虚拟组织处理 | 用户已说明主体为虚拟；可用于需求、测试数据和沙箱，不得充当真实付费灯塔或生产上线批准证据 |
| Release 1 候选方向由纺织改为玩具 | 用户明确拒绝纺织首发，并说明近 3—6 个月玩具订单最多、方法最集中；具体玩具产品类别仍需收窄 |
| 微生物/生物不进入首个生产切片 | 用户已明确首期不需要；继续作为显式排除项，后续若启用必须独立决策和验收 |
| 容量事实记录为日均 500 订单 | 用户已澄清单位和统计口径；峰值、倍率、并发与存储增长仍需历史资料形成工程基线 |
| Release 1 用户选择分析化学先行、物理机械后移 | 用户明确否决物理机械先行并选择分析化学；该选择用于重构候选切片，但在责任角色和证据闭合前仍保持 proposed/open |
| ATC-PLT-000只创建工程骨架而不实现检测业务 | 用户授权创建完整任务卡；该卡用于解决方案、Host、公共技术端口、CI和边界证明，业务模块由后续纵向任务实现 |
| ATC-PLT-000创建后先形成聚焦联合评审包，不由AI直接提升状态 | 当前阻塞跨技术、容量、模块、安全、架构和验收多个责任域；必须把推荐方案与批准证据分离，避免“用户说继续”被误解释为代表所有角色批准 |
| 依赖裁剪只形成变更提案，本轮不原地改写已交付的0.1.0机器语义 | AGENTS要求新语义使用新SemVer；在责任人接受裁剪前先保留现有BLOCKED版本和完整审计依据，批准后再创建Major版本链 |
| 用户再次“继续”只授权准备精确变更集和PENDING清单，不解释为接受8项推荐 | 正式选择和角色批准会改变版本链与Ready状态；缺少明确结论、受控身份和授权证据时只能继续做无状态提升的可逆准备工作 |
| 下一机器版本使用Major链而不原地覆盖0.1.0 | 依赖、状态、适用性和Ready语义均变化；按仓库规则必须保留旧草案，并让Release与六张REC同步精确引用新版平台链 |
| 用户要求按建议顺序推进，当前先发起联合评审而不记录任何ACCEPT | 对推进顺序的授权不等于对8项方案、33个角色槽或15项技术锁的正式批准；必须先取得明确身份、授权和逐项结论 |
| 用户已明确接受RV-PLT-001至005、007至008，并为RV-PLT-006选择方案A | 该记录完成发起人方案方向确认；由于受控身份、代表角色和授权依据仍缺失，不能据此把评审CSV改为ACCEPT/VERIFIED或提升规格状态 |
| 用户明确要求“提交并发布”，本轮发布当前完整工作区到现有GitHub远端 | 该授权覆盖Git提交与推送，不等于业务审批；所有PENDING评审记录、技术锁及proposed/in_review/blocked规格保持原状 |
| 用户明确要求停止扩写治理文档并开始执行DEV-001，且授权使用多个子代理 | 本轮作为用户明确批准的工程骨架Spike实施，只新增应用代码、测试、运行配置和必要工程说明；不改spec/generated，不补业务默认值，不提升任何审批状态 |

## Errors Encountered
| Error | Resolution |
|-------|------------|
| `init-session.ps1` 未按文档所述创建隔离计划，而是检测并跳过根目录旧文件 | 手工创建 `.planning/2026-07-23-ai-ready-prd-breakdown/`，并设置活动计划指针 |
| 首次规格校验把任务卡中“不得读取 latest”的禁止性说明误判为实际 latest 引用 | 将检查收窄到 `depends_on` 和 `selected_specs` 的版本键，允许文档解释禁止行为 |
| 首次批量补充 Story 元数据的补丁 hunk 格式无效，未修改文件 | 改用每个文件带上下文的标准补丁，成功加入独立发布、Epic 和 Feature 字段 |
| 更新规划文件时使用的旧上下文与当前错误行文字不完全一致，补丁未应用 | 重新读取相关规划区段并使用当前精确上下文更新 |
| 领域复核代理在返回完整报告前遭遇 429 重试上限 | 已采用其先行返回的关键结论；另外两路复核完整返回，剩余内容由主代理结合 PRD 补齐 |
| 仓库契约测试要求所有生成文件正文含 generated 标记，CSV 无法安全放注释而失败 | CSV 由 lock 的完整哈希和文件树所有权控制；测试对 CSV 免除正文标记但仍校验 UTF-8/LF 和 check 一致性 |
| 恢复会话时按技能建议执行 `git diff --stat`，但工作区尚未初始化 Git | 记录为已知环境事实；继续依靠规格生成器的哈希、来源状态和生成清单核对本轮变更 |
| 新增 Phase 18—20 的首个多文件补丁因 progress.md 上下文缺少空行而整体未应用 | 重新读取三个规划文件顶部，改用按文件精确上下文的独立补丁 |
| 新增 Phase 21—23 的首个多文件补丁再次因 progress.md 顶部空行上下文不匹配而整体未应用 | 保持任务计划补丁独立，progress/findings使用精确文件上下文分别更新 |
| 读取Story校验时把`tests/test_*.py`作为Windows rg路径参数导致os error 123 | 已通过Get-Content取得目标测试区段；后续对tests目录使用`-g 'test_*.py'`过滤 |
| Windows PowerShell 5.1 的 `ConvertFrom-Json` 不支持 `-Depth` 参数，首次字段摘要返回空值 | 去掉输入侧 `-Depth`，仅在 `ConvertTo-Json` 使用深度参数后成功读取；未修改任何文件 |
| 在通用Schema中检索Story body字段名时`rg`因无匹配返回exit 1 | 随后直接读取Schema确认`body`为开放对象；该返回码表示无匹配而非仓库错误 |
| 首轮28项单元测试有2项失败：可观测性断言未命中“每个集团”，共享SaaS禁止性行未命中标准拒绝词 | 收紧Story原文为“每个集团…均独立”和“禁止重新引入共享SaaS多租户数据平面”，随后重新生成并复测 |
| 首次汇总8个阻塞规格的PowerShell `foreach` 后直接接管道，触发“空管道元素”解析错误 | 改为先累积 `$rows` 再统一 `Format-Table`，成功取得状态、依赖和行数；未修改规格 |
| 汇总六张REC依赖时将`FileInfo`直接传给`Get-Content`，PowerShell 5.1只使用文件名并在仓库根查找失败 | 后续读取文件集合统一使用`$path.FullName`；Release映射已成功取得，错误未修改任何文件 |
| 恢复会话时用于统计规划文件行数的PowerShell `foreach` 后直接接管道，再次触发“空管道元素”解析错误 | 改为先赋值给`$rows`再输出，成功完成统计；未修改任何仓库文件 |
| 首次读取评审侧车时把实际`.sha256`文件误写成`.md.sha256` | 使用`rg --files docs/decision-packets`确认真实文件名并改用正确路径；错误只读且未修改文件 |
| 首次同时更新两份审批文档的大补丁因5.4节标题上下文不精确而整体未应用 | 拆为按文件和区段的精确补丁，成功更新草案/批准边界；失败补丁没有产生部分写入 |
| 将预期返回BLOCKED的Ready命令与其他只读检查放入同一并行工具批次，非零退出使批次提前返回 | 改为单独执行Ready并显式记录原始`READY_EXIT=4`，其余检查另行运行；未修改门禁或隐藏阻塞 |
| 首次批量同步五份Review Status文档时，故障排查文档的旧上下文不匹配导致整批补丁未应用 | 读取精确区段后拆为两组补丁，成功更新全部文档；失败补丁没有部分写入 |
| 运行代理选择的MinIO标签`RELEASE.2025-10-15T17-29-55Z`在Docker Registry不存在 | 从Docker Hub官方标签接口选择存在的`RELEASE.2025-09-07T16-13-09Z`并固定manifest digest；同时将PostgreSQL升级到当前18.4并固定digest |
| 主代理审查运行文件时把Keycloak realm文件名写成`openlims-dev-realm.json`导致只读路径错误 | 使用`rg --files deploy`确认实际文件为`openlims-development-realm.json`并完成审查；未修改文件或重复错误命令 |
| 查询Microsoft.OpenApi近期版本时误用了PowerShell不存在的`Select-Object -Join`参数 | 最新版本主查询已成功返回3.9.0；移除无效摘要写法，并采用后端代理已实际兼容验证的无漏洞3.5.1精确锁继续构建 |
| 首次新增DEV-001B三份规划记录时误用了根目录旧规划的乱码末尾作为活动findings上下文，补丁整体未应用 | 读取活动计划精确末尾后拆分补丁；仅更新规划记录，没有业务文件产生部分修改 |
| DEV-001B主集成首次构建发现代理与主代理都新增了`AuthenticationRequired`常量，产生CS0102重复定义 | 删除重复行并保留唯一稳定错误码；其余项目已编译到API阶段，未降低警告即错误门禁 |
| 首次组合执行JSON解析、restore、pnpm锁和build时，在PowerShell cmdlet后检查空`LASTEXITCODE`导致提前以0退出，实际未恢复新Smoke项目 | 改为每个外部程序单独执行并核对资产/锁文件；后续不对纯PowerShell cmdlet使用`LASTEXITCODE` |
| 新增契约认证夹具首次编译缺`ILoggerFactory`命名空间，且常量`Scheme`隐藏基类属性被警告即错误拦截 | 增加精确Logging using并把常量改名`SchemeName`；保持警告即错误，不使用抑制 |
| DEV-001B首次全量.NET测试中架构路由白名单仍只有DEV-001三个端点，新增受保护`/system/status`被正确拦截 | 将该技术端点显式加入唯一白名单；业务路由、`src/modules`和`src/packs`禁止断言保持不变 |
| 首次本地运行态失败关闭检查发现401安全正文虽正确但`WriteAsJsonAsync`把Content-Type覆盖为`application/json` | 改由ASP.NET Core `Results.Problem`统一执行RFC 9457响应，保留稳定错误码、关联ID和no-store头后复测 |
| 终止长运行工具cell只结束了`dotnet run`父进程，子API进程27768仍锁定Release apphost，导致下一次build复制重试失败 | 核对PID路径确属工作区API后停止该进程；后续运行验证在结束时同时核对端口与子进程，不把文件锁误判为代码失败 |
| 浏览器直接跳转`/system/status`被客户端拦截 | 按浏览器技能读取故障排查说明，重新抓取DOM后使用页面中唯一、可见且href精确匹配的导航链接进入；页面验证成功，未重复失败动作 |
| 在Windows PowerShell中直接把`dotnet list --format json`管道给Python时stdin为空，JSON门禁模拟失败 | 改为按CI真实方式先把dotnet输出写入临时JSON再由Python解析；该差异仅影响本地Shell模拟，不修改CI判定逻辑 |
| PowerShell `>`把本地临时NuGet JSON写成UTF-16LE，按GitHub Bash的UTF-8读取方式再次解析失败 | 本地改按UTF-16解析并单独验证Python递归判定；CI继续使用Ubuntu Bash重定向产生UTF-8，不照搬Windows编码 |
| Git Bash执行Unix验证入口时把MSBuild参数`/warnaserror`转换成`C:/Program Files/Git/warnaserror`，导致MSB1008 | Windows/Unix脚本及CI统一改用等价的`-warnaserror`开关，避免MSYS路径转换且继续保持警告即错误 |
| 最终状态汇总命令中把含转义双引号的`rg`模式嵌入PowerShell双引号命令，触发字符串终止符解析错误 | 改用单引号模式并拆分关键行检索；失败发生在只读汇总前，没有修改任何文件 |
| 暂存后首次同步Phase 49规划状态时补丁的第二个文件标记前带多余空格，导致整体未应用 | 修正补丁格式后分别更新任务和进度；失败补丁没有产生部分写入，暂存区内容未受影响 |
| 用`rg`同时读取尚不存在的`tests/architecture`目录导致两次exit 1 | 首次确认目录缺失后由主代理创建正式架构测试项目；后续直接以解决方案测试验证，不再把缺目录当内容搜索目标 |
| PowerShell运行时不提供`[System.IO.Path]::GetRelativePath`，前端批量审查标题生成报错 | 文件正文仍完整输出；后续相对路径使用已验证的字符串前缀截取，不再调用缺失API |
| 前端首次供应链审计发现Vite高危漏洞和Vitest严重漏洞 | 保持审计阻断，升级到同一主版本的Vite 7.3.6、Vitest 3.2.7与插件6.0.8；重新锁定后`pnpm audit`为0已知漏洞 |
| 桌面安全策略拒绝`Start-Process`后台启动API/Web | 改用两个受控前台运行单元托管服务，完成HTTP与浏览器验收后显式清理进程；未绕过系统策略 |
| 浏览器后端不支持文档中列出的`networkidle`等待状态 | 按browser-troubleshooting改用已支持的`load`状态和DOM快照；未重选浏览器或切换控制机制 |
| 浏览器审查发现Ant Design子组件被错误当作插件注册并产生Vue warning | 仅对带install的父组件使用`.use()`；父插件自动注册子组件，最终页面刷新后无新增warning/error |
| 监听进程清理时按仓库工作目录过滤`node.exe`，同时命中了已完成验收的浏览器Node内核 | 浏览器标签已先finalize且无用户状态丢失；后续进程清理只按已验证监听端口或应用可执行文件PID，不再按通用node工作目录过滤 |
| 查询监听进程时再次把PowerShell`foreach`结果直接接管道而解析失败 | 立即改为先收集`$rows`再格式化，精确识别并清理API/Worker；未执行未验证PID操作 |
| 辅助锁扫描把`pnpm-lock.yaml`内第三方peer/engine范围误判为直接依赖未锁 | 保留确定性锁文件，检查收窄到我们维护的package.json、中央NuGet版本、csproj和Compose镜像；直接声明全部精确固定 |
| 收窄后的首次`rg`依赖正则仍因字符类转义错误无法解析 | 改用PowerShell结构化解析JSON/XML和单独Compose检查，避免继续调试脆弱正则；最终声明锁检查通过 |
