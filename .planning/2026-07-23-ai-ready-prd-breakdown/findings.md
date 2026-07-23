# Findings & Decisions

## Requirements
- 2026-07-23 用户明确要求停止继续扩写治理文档，并授权执行`DEV-001`实际工程骨架；同时授权主代理使用多个子代理处理简单、边界明确的实现任务，复杂架构、领域和集成由主代理负责。
- DEV-001本轮按明确批准的工程Spike处理：只实现代码、数据库/运行配置、测试和必要工程说明，不修改`spec/`、`generated/spec/`或现有审批材料，不把Spike结果表示为生产Ready。
- 2026-07-23 用户明确要求“提交并发布”；这授权把当前完整工作区提交到现有Git仓库并推送`origin/main`，但不授权填写33个评审角色槽、补造15项技术锁或提升任何`proposed/in_review/blocked`规格状态。
- 2026-07-23 用户逐项明确选择：RV-PLT-001接受依赖分离，002接受推荐技术栈并要求精确锁定，003接受模块化单体边界，004接受非生产合成验证环境，005接受单集团独立部署隔离契约，006采用审计方案A，007接受供应链门禁，008接受工程骨架任务范围。该输入记录为`USER_CONFIRMED_PENDING_CONTROLLED_IDENTITY_AND_ROLE_APPROVAL`，不是33个角色槽的正式批准。
- 2026-07-23 用户同意按“联合评审→责任人证据与技术锁→批准后继版本→Ready→实现”的顺序推进；该授权只启动评审流程，不代表RV-PLT-001至008中的任何一项ACCEPT，也未提供受控身份、角色授权或签名证据。
- 2026-07-23 用户要求继续下一步；当前安全可执行范围是建立评审证据的只读机器门禁和缺口报告，不能替33个责任角色填写身份、授权、结论、时间、签名，也不能补造15项技术锁值或批准后继版本。
- 2026-07-23 用户在收到“下一步将创建下一Major的proposed/in_review机器草案且绝不提升批准状态”说明后再次回复“继续”；本轮可据此创建15个未批准新版本，但不得将任何对象或评审记录标为approved、decided、ready或ACCEPT。
- 2026-07-23 用户在收到8项最小回复格式后只回复“继续”；该文字不足以证明任何具体评审项ACCEPT或角色授权，但足以授权继续准备不改变机器批准状态的签署工作台。
- 2026-07-23 用户要求在ATC-PLT-000任务规格闭环后继续下一步；合理授权范围是准备联合评审和批准输入，不代表用户已代表所有责任角色批准技术、安全、运维、质量或验收结论。
- 2026-07-23 用户授权创建 `ATC-PLT-000` 完整工程骨架AI任务卡；当前授权范围是规格和任务设计，不等于批准执行工程骨架编码。
- 2026-07-23 用户明确接受玩具候选资格/排除边界和“中国内销→欧盟→美国”的版本顺序；唯一主技术包选择分析化学，物理机械后移。
- 该输入解决了候选切片的三个产品方向选择，但不自动满足OD-001的真实付费灯塔、方法Pareto、黄金场景、角色授权和正式批准证据。
- 2026-07-23 用户进一步确认：广东华瑾检测有限公司是虚拟主体；广东华瑾检测有限公司测试中心隶属于该虚拟法人。
- 用户明确拒绝“纺织品—常规面料”首发；最近 3—6 个月订单最多、方法最集中的是玩具检测，但尚未收窄到唯一玩具产品类别。
- 用户列出的首期市场为中国内销、欧盟和美国，列出的主技术包为物理机械和分析化学；两者仍是多选输入，不满足单一生产切片冻结条件。
- 用户明确微生物/生物不进入首个切片；容量口径为日均 500 订单；技术栈无硬性限制，由方案方提出推荐；业务、实验室、质量负责人和关键用户均已有人。
- 2026-07-23 用户提供 Release 1 初始输入：覆盖电子电器、食品接触材料、玩具婴童、纺织品、汽车零配件；方法族为物理性能、化学分析、微生物/生物；灯塔机构为广东华瑾检测有限公司；首个实验室为广东华瑾检测有限公司测试中心；预计每日订单/样品量 500；可提供脱敏历史资料。
- 用户未填写首个法人和技术栈限制；“目标市场/客户协议”填写为第三方产品检测机构，实际属于客户类型而非市场/协议版本。
- 用户已明确作出产品决策：OpenLIMS 是集团多机构系统，不提供共享 SaaS 多租户产品模式。
- 部署边界应定义为“一个生产部署/数据平面只服务一个检测集团”；集团内建模 LegalEntity、Laboratory、Department、WorkCenter 等组织单元。
- 不同集团之间应独立部署应用运行环境、数据库、对象存储、密钥、队列、检索/AI、备份与恢复；送检客户是业务对象，不是租户。
- 用户希望知道如何把 `docs/AI原生第三方产品检测LIMS产品需求文档.md` 拆到可交给 AI 开发的粒度。
- 输出需要与该 PRD 的实际结构和复杂度对应，而非泛化的软件需求拆解建议。
- 用户进一步要求实际构建需求编译与同步控制机制，并希望交付尽可能细致。
- 必须控制“规范源变化后派生文件同步变化”，同时防止自动覆盖手写业务代码、历史迁移和已批准证据。

## Research Findings
- 前端工具代理锁定的Vite 7.1.7和Vitest 3.2.4在当前审计库中分别存在高危/严重漏洞；同主版本修复版7.3.6和3.2.7可在不改变技术选择的情况下清零已知漏洞，供应链审计必须在首次锁定后再执行。
- Ant Design Vue的`Layout`和`Descriptions`父插件会自动注册Header/Content/Footer/Item子组件；子组件没有install函数，不能再调用`.use()`或重复`.component()`，否则浏览器控制台产生warning。
- 真实浏览器验证确认Web同源配置`apiBaseUrl=/`与Vite `/health`代理可正确连接127.0.0.1:5080 API；状态页展示后端生成的关联ID，且没有业务导航或业务数据。
- DEV-001 Spike的真实边界：API/Worker/Web/锁/测试/Compose/CI已存在并可启动，但当前`/health/ready`只证明空Host配置与进程就绪；它没有探测PostgreSQL、Keycloak、MinIO，也没有实现OIDC、对象存储操作、迁移、Outbox/Inbox持久化或检测业务。
- 最终依赖安全结果为NuGet和pnpm均0已知漏洞；pnpm锁文件中的peer/engine范围是上游兼容元数据，不是我们的直接浮动声明，直接依赖精确性应从package.json、Directory.Packages.props和镜像digest判断。
- Docker Registry核验结果：`postgres:18.4-alpine`、`quay.io/keycloak/keycloak:26.4.1`和`minio/minio:RELEASE.2025-09-07T16-13-09Z`均存在，可分别固定manifest digest；代理初选的MinIO 2025-10-15标签不存在，不能进入可运行Compose。
- PostgreSQL 18官方容器的数据卷边界应挂载`/var/lib/postgresql`而不是旧版常用的`/var/lib/postgresql/data`；Compose已按18+布局修正，避免升级和初始化路径错误。
- GitHub Actions应以commit SHA引用而不是tag：checkout v4.2.2、setup-dotnet v4.3.1、setup-node v4.4.0和setup-python v5.6.0的tag均已通过GitHub API解析到不可变commit。
- `Microsoft.AspNetCore.OpenApi 10.0.10`的默认传递`Microsoft.OpenApi 2.0.0`以及中央覆盖2.4.1会被NuGet Audit以NU1903阻断；Spike保持SCA警告即错误，中央精确覆盖到后端已验证兼容的3.5.1。
- 当前本机没有Docker；Node/pnpm可用，但只安装.NET SDK 9.0.305。工程骨架不得因此静默改为`net9.0`，应获取并固定仓库本地.NET 10 SDK，Docker编排可以实现和静态校验，但容器集成验证必须如实标记为未在本机运行。
- 发起人方案选择应记录在`ATC-PLT-000-JOINT-APPROVAL-PACKET.md`的独立选择区，而不修改正式签署对象`ATC-PLT-000-NEXT-VERSION-CHANGESET.md`：后者的SHA已被33条PENDING记录引用，当前选择没有改变变更集正文，也不具备受控身份/角色授权，不应触发正式记录哈希迁移。
- 联合评审包可把8项选择标记为`USER_CONFIRMED_PENDING_CONTROLLED_IDENTITY_AND_ROLE_APPROVAL`；其自身SHA侧车需随正文更新，但评审CSV必须继续保持`decision=PENDING/record_status=DRAFT`。
- Review Status最终门禁语义已由40项测试和真实仓库验证：当前返回exit 4而非错误；正文/CSV结构或哈希无效才返回exit 2；模拟完整受控ACCEPT与VERIFIED锁时返回EVIDENCE_READY/exit 0。
- 工具运行前后受控评审文件字节完全一致，规格和generated目录不受影响；它是证据完整性检查，不是批准命令、状态迁移命令或实现授权命令。
- 当前真实Review Status输出为33个活动/必需角色槽、0个已核验ACCEPT、15个技术锁、0个已核验锁和48个阻塞项；这与PENDING清单和ED-001草案逐项一致。
- Review Gate单元测试证明：完整ACCEPT和完整锁可返回EVIDENCE_READY；条件接受、拒绝、弃权、缺证据继续阻断；正文篡改、无时区时间和重复活动角色槽判为无效输入而不是普通阻塞。
- 评审CSV现有33个活动角色槽，表头足以判定身份、授权、结论、条件、反对意见、证据、时间、签名和记录状态；同一角色槽未来可保留`SUPERSEDED`旧行，但必须且只能有一条非SUPERSEDED活动记录。
- 只有`decision=ACCEPT`、`record_status=VERIFIED`、身份/授权/授权证据/证据/带时区评审时间/签名全部非空，且conditions与blocking_objections为空时，单个角色槽才闭合；`PENDING`、`REJECT`、`ABSTAIN`和`ACCEPT_WITH_CONDITIONS`均继续阻断，条件满足后必须形成新的明确ACCEPT证据。
- 版本锁只有`exact_value`非空、`status=VERIFIED`且锁项自身含非空`evidence_refs`时才闭合；当前15项全部同时缺精确值、VERIFIED状态和实际证据引用，因此Review Status应报告15个锁阻塞项。
- `review-status --change-set <ID> [--json]`按约定从`docs/decision-packets/review-records/<ID>__*.csv`发现清单，用subject_hash匹配唯一SHA侧车和正文，再扫描evidence_refs关联的规格锁；不写任何文件。
- specgen CLI集中在`tools/specgen/cli.py`，已有统一`EXIT_BLOCKED=4`、UTF-8输出和`ConfigurationError -> exit 2`处理；Review Status应复用同一语义：证据合法但未闭合返回4，文件/哈希/字段结构无效返回2，闭合才返回0。
- 评审门禁应实现为独立只读模块而非塞入`ready`：`ready`只看规格状态/依赖，Review Status负责CSV身份授权签名、变更集字节哈希和关联规格版本锁；两者都通过才可能创建批准后继版本。
- 当前工具只有`atomic_write_text`用于显式写命令，Review Status不应导入或调用任何写入函数；测试需比较运行前后评审CSV、变更集、侧车和ED-001规格的字节完全不变。
- 新旧版本并存后的确定规模为59个规格、14张生成任务、19个Gherkin Feature和46个受管生成文件；二次生成`written=0`且impact归零。
- 新版平台Ready只受`ED-001/002`、`SEC-DEPLOY/SEC-AUD`、`NFR-ARCH-001/002`和`AC-DEPLOY`七项未批准草案阻断，不再出现`OD-020/OD-025`；新版REC继续同时受平台与真实业务规格阻断。
- `ED-001@1.0.0`诚实保留15项`exact_value=null/PENDING_VERIFICATION`锁、空`verified_review_record_refs`和`implementation_authorized=false`；变更集必须描述当前待评审字段，不能继续要求草案预填`APPROVED_FOR_ENGINEERING_SKELETON_ONLY`或已验证记录。
- `ED-002@1.0.0`把Host、building-blocks、模块四层、独立Schema/DbContext/迁移、公共端口/Outbox、Pack锁和测试夹具边界写成待评审机器候选，同时显式排除真实检测业务语义。
- 15个新规格经严格校验可合法并存：`ED-001/002`为`proposed/open`，`SEC-DEPLOY/SEC-AUD/NFR-ARCH-001/002/AC-DEPLOY`为`in_review`，平台与六张REC Story为`proposed/blocked`，Release为`proposed`；新对象中没有`approved/decided/ready`。
- 新平台依赖闭包精确终止于`OD-002@1.0.0`、`ED-002`、安全/审计/NFR/验收链，不含`OD-020/OD-025`；新版Release的44项`selected_specs`仍保留这两个0.1.0业务/生产门禁。
- 六张REC新版只升级`version`、`target_release`和平台/安全/NFR/前置REC精确引用，其他业务依赖与正文继续保留旧版阻塞语义；它们不直接依赖ED-002，模块边界通过平台链传递。
- 变更集当前仍写“证据未闭合时不得创建proposed文件”和“当前不存在任何新Major机器规格”；创建草案后必须改成“1.0.0评审投影已存在但不是签署结论”，并明确禁止把草案存在解释为审批、Ready或实施授权。
- 联合审批包第8节仍把创建新版机器规格放在全部评审闭合之后；机器草案出现后需区分“供差异/校验的未批准投影”与“由受控证据支持的批准版本”，并重算联合包自身SHA侧车。
- 由于仓库规则把状态变化默认视为Major，后续若将本轮1.0.0草案提升为approved/ready，不能由AI原地修改；必须按届时评审后的SemVer和历史门禁执行，当前文档不得承诺原地提升。
- 哈希侧车命名为`ATC-PLT-000-NEXT-VERSION-CHANGESET.sha256`和`ATC-PLT-000-JOINT-APPROVAL-PACKET.sha256`，不是`.md.sha256`；契约测试按侧车首列摘要校验正文实际字节。
- 现有契约测试已精确固定旧0.1.0的7张任务和11个Feature；新增1.0.0后应改为14张任务、19个Feature，并分别保留旧平台链契约与新增草案链契约，不能用只改总数掩盖缺失版本。
- 当前安全规格目录沿用`spec/requirements/SEC-*.json`，没有独立`spec/security/`根；新版`SEC-DEPLOY-001`和`SEC-AUD-001`必须与旧版本同目录，避免扩大specgen加载根或制造未加载规格。
- 当前规格树共44个对象，下一Major新增15个文件后目标为59；旧0.1.0与`OD-002@1.0.0`均保留，不移动、不改名。
- 下一Major链预计包含15个对象：ED-001、拟新增ED-002、SEC-DEPLOY-001、SEC-AUD-001、NFR-ARCH-001/002、AC-DEPLOY-001、ATC-PLT-000、Release基线和六张ATC-REC；OD-002继续使用已批准1.0.0，OD-020/OD-025继续留在Release选择中。
- REC版本迁移不能只替换平台依赖：当前六张卡还直接引用ED-001、SEC-DEPLOY、SEC-AUD、NFR-ARCH以及前置REC；这些引用必须整体升级到1.0.0，否则一个Release会混用新旧平台安全语义。
- 签署对象选用独立变更集Markdown及SHA，而不是尚不存在的未来机器规格；责任人可以先对完整字段级方案签署，证据闭合后再一次性创建正式Major版本，避免“先建proposed再为状态变化继续膨胀版本”的循环。
- 评审工作清单预填变更集ID、哈希、评审项和角色槽属于可逆准备；身份、授权、decision、时间和签名仍为空/PENDING，因此不会构成虚假批准。
- 最终工作台用33条记录覆盖8个评审项的全部必需角色关系；每条记录独立签署，既允许同一合法人员兼任多个角色，也禁止一条签名隐式代表多个授权范围。
- 当前阶段已耗尽无需外部授权即可安全完成的准备工作；后续若继续但仍无具体结论或身份/授权证据，只能说明阻塞，不能替责任人填充ACCEPT或创建approved机器版本。
- 新版ATC-PLT只依赖批准的工程/安全/NFR/验收版本；新版Release仍可处于proposed并选择未批准业务规格；新版六张REC只替换target_release和平台任务精确版本，其他业务依赖保持原状和BLOCKED。
- ATC-PLT-000当前对OD-020/OD-025的直接依赖过宽：OD-020/OD-025均依赖OD-001，导致工程空壳间接等待真实付费灯塔、生产容量、分析化学方法/QC/仪器和报告范围证据。
- 平台骨架实际只需要两类可独立批准的工程语义：目录/模块/Schema/端口所有权边界，以及本地/CI/验证环境的非生产合成负载与恢复测试包络；它不需要承诺生产拓扑、RPO/RTO或批准Release 1技术包业务契约。
- 建议新增两个proposed工程决策：一个承载模块化单体工程边界，一个承载非生产验证环境包络；随后让ED-001、ATC-PLT-000和NFR-ARCH-001依赖精确工程版本，保留OD-020/OD-025在Release层作为后续生产/业务阻塞。
- SEC-DEPLOY-001与AC-DEPLOY-001直接源自已批准OD-002，适合保留为平台骨架安全验收依赖；SEC-AUD-001/NFR-ARCH-002仍需要质量、审计、架构和运维明确批准审计意图原子性、失败尝试记录与中央账本最终一致语义。
- `ED-001@0.1.0`只有产品系列版本，没有SDK patch、容器digest和GitHub Action commit；正式工程批准版必须补齐精确锁值，否则无法满足ATC“不读取latest、确定性恢复”的前置条件。
- 最小变更应只新增一个通用模块边界Decision；非生产健康/恢复包络若仅服务平台骨架，可直接作为新版任务的受控测试边界，不必为了形式再增加一个全局Decision。
- 现有`0.1.0`已作为完整草案交付。依赖裁剪属于新语义，最佳治理方式是先用评审包批准变更提案，再创建ED/NFR/ATC/Release/REC的Major版本链，而不是AI直接原地改写或提升状态。
- 联合评审包使用8个独立评审项目，把依赖裁剪、技术栈、模块边界、非生产环境、集团隔离、审计模型、供应链和任务范围分开；任何单项`PENDING/ABSTAIN/REJECT`都不会被误当成整体批准。
- 空白CSV模板有意只含表头；这使仓库契约可以证明AI没有预填评审人、授权、时间、签名或ACCEPT结论，同时为后续受控证据导入保留稳定字段。
- 评审包使用独立SHA-256侧车而不把哈希写回自身，避免自引用哈希问题；契约测试将正文实际字节哈希与侧车比较，任何评审文本变化都会强制刷新并重新签署。
- ATC-PLT-000应建立解决方案根、API/Worker/Web空壳、公共技术原语、模块契约、PostgreSQL/IdP/S3本地依赖、架构测试、CI和稳定验证脚本；不得实现收样、分析化学或其他业务工作流。
- 现有六张ATC-REC任务应精确依赖ATC-PLT-000，Release候选基线也必须选择该版本，保证业务编码不能绕开骨架门禁。
- Story渲染器会为每张Story生成一张Markdown任务卡和一个Gherkin feature；新增ATC-PLT-000后生成任务数和feature数都会各增加1，仓库契约测试需从6/10调整为7/11并允许ATC-PLT前缀。
- Story body有18个必填字段，现有渲染器会完整输出业务结果、参与者、路径、数据/API、状态、权限、审计、UX、可观测性、测试、非目标、allowed_paths、命令和DoD；ATC-PLT-000可直接复用，无需改生成器。
- 三路只读复核一致确认：平台任务规格的状态、九个精确依赖、六张收样任务反向依赖、Release选择、路径禁区和非共享SaaS边界正确；剩余陈旧点集中在ED-001状态、ADR/README导航和契约测试。
- 初稿测试矩阵虽已有幂等与恢复，但没有明确证明两个Worker并发争抢同一Inbox消息；已新增独立并发用例，并将权限、审计、反向和输入/超时边界分别建模，直接满足AGENTS完成要求。
- 初稿声明Windows/Linux支持task/architecture/contracts/all，但命令只覆盖PowerShell三个Profile和Bash all；已补齐两平台四Profile的明确命令，避免实现代理自行解释。
- 可观测性最初未明确是否属于集团独立数据平面，而日志和Trace可能携带敏感组织上下文；当前草案已选择最严格默认：每集团独立OTLP接收端、存储、查询、告警和凭据，任何共享管理平面例外都必须另行批准。
- `TC-PLT-000-05`原先允许“拒绝或忽略”客户端集团字段，与失败路径的“必须拒绝”冲突；已用稳定HTTP/errorCode消除两种合法实现的歧义。
- 最终Ready结果证明治理顺序生效：平台骨架任务必须先完成人工Decision/安全/NFR/验收批准，收样任务又明确依赖已批准的平台骨架，业务代理无法绕过工程底座直接编码。
- 生成闭环最终拥有30个受管文件；相同输入再次生成写入0个，随后impact归零，说明规格源、派生文件和锁已同步。
- 工程骨架Task应依赖ED-001、OD-002、OD-020、OD-025、SEC-DEPLOY-001、NFR-ARCH-001/002；收样任务再依赖它。这样骨架本身诚实阻断，但不会引入任务依赖环。
- allowed_paths应覆盖解决方案根、Host、building-blocks、公共contracts、Web shell、架构/集成/烟雾测试、deploy、verify脚本、应用CI和工程文档；明确禁止spec、generated、业务modules/packs、历史迁移和真实证据。
- 分析化学先行后，原 `GS-TOY-PHY-001`、物理机械顺序试验负载、跨包转交方向和首发排除项必须整体重构，不能只替换技术包名称。
- 新首发闭环应覆盖部件/材料/颜色映射、取样与最低量、制备批、分析批、空白/加标/标准物质等QC、仪器原文件、LOD/LOQ与限定符、结果采用和仅对已测化学范围负责的报告边界。
- 玩具年龄和可接触性仍是化学范围输入，但分析化学切片不得自行执行物理使用滥用试验；需要批准的初始可接触部件/材料颜色范围，任何物理后暴露影响通过版本化转交或阻断处理。
- 精确影响扫描命中：OD-001的候选scope、初始排除、technical_pack_governance、GS-TOY-PHY-001和未决问题；OD-020的method_profiles；OD-025的主/延后包和handoff；决策包第6/8/9/15/17/19节；README当前口径；仓库契约测试物理机械断言。
- ED-001的代码布局可同时保留physical-mechanical与analytical-chemistry两个长期包目录，不需要因发布顺序改变而删除物理包候选；只需同步当前Release 1输入说明。
- 现有方法模板缺少分析物/基质材料颜色、制备方法、运行模板、单位、LOD/LOQ、限值、QC计划和原始数据映射字段；应扩充并新增分析化学QC清单与材料颜色取样映射模板。
- 契约测试应新增三个用户选择断言：产品资格ACCEPTED、市场顺序ACCEPTED、primary=分析化学且deferred=物理机械；同时继续断言四项Decision均proposed/open且decision=null。
- 决策包第17节应从“待用户选择”改为“用户选择记录”，并把下一步改为方法/QC/仪器证据、真实灯塔和责任角色正式审批，而不是重复提问。
- 完成重构后的有效物理机械引用均属于长期候选、明确后移、R1生产排除、跨包影响或报告未覆盖边界；不存在仍把物理机械写成首发主包的正向语义。
- 三项用户选择已经从“未决问题”迁移为显式scope_choice_confirmation和决策包选择记录；剩余门禁只要求正式责任角色批准以及真实方法/QC/仪器/灯塔/容量/验收证据。
- 现有决策冲刺包和 OD-001 草案仍把纺织常规面料列为默认候选，与用户最新明确拒绝不一致；必须改为玩具方向并保留具体产品类别待定。
- OD-020 当前仍把 500/日的单位和统计口径标为 UNKNOWN，现可收敛为“日均 500 订单”，但试点占比、峰值、对象倍率与并发仍未知。
- OD-025 当前把微生物标为待单独评审候选；用户已明确首个切片不需要，应改为 Release 1 显式排除，同时保留未来独立立项门禁。
- ED-001 当前仍把技术约束标为 NOT_PROVIDED；现应记录“无硬性限制、授权提出推荐方案”，并给出候选栈和可执行验证命令，但状态继续 proposed/open。
- 规格 Schema 允许 Decision 携带结构化候选栈、替代项和开放问题字段；可在 ED-001 保存候选命令，但六张 Story 中的技术栈/模块边界占位命令应等工程骨架真实存在后再替换，不能把计划中的命令伪装成可执行命令。
- 当前 Release 基线精确依赖 ED-001、OD-001、OD-020 和 OD-025；更新这些 proposed 草案会由生成器传递到 requirements lock、目录与追踪文件，无需也不得手改 generated/spec。
- PRD 已为玩具定义年龄决定、可接触性、测试单元、使用滥用序列、状态检查点和七条行业规则；因此玩具候选不是从零起草，但首个产品类别和市场协议仍必须由真实订单方法清单锁定。
- 现有仓库契约测试适合增加一条“最新 intake 不得被误写成批准”保护：断言 OD-001/020/025/ED-001 仍为 proposed/open、500 口径为日均订单、微生物为 R1 排除、虚拟机构不被标成真实付费灯塔。
- 玩具只读复核建议的工作候选为“年龄决定不低于3岁、常规硬质塑胶、非电动、无磁体/弹射/乘骑/承重/水上/大型活动功能”的单一品类；它只能作为待历史数据验证的候选，不能替用户批准。
- 三市场不得靠新造虚拟协议合并。推荐顺序是 R1.0 中国内销、欧盟和美国后续版本增量；若坚持首期三市场，必须存在真实重复订单使用的同一客户协议 ID@version 及三套独立 Requirements Profile 证据。
- 虚拟华瑾在纯虚构/脱敏化名未澄清前按 `SYNTHETIC_SANDBOX_ONLY` 处理；虚拟组织关系只可用于模型、权限、演示和工程压测，不能关闭真实法人、付费灯塔、生产容量或上线批准门禁。
- 四类负责人“已有人”只可建立 role slot，当前身份引用、权限、时间投入、替代人和批准证据均未提供；规格不得保存臆造姓名或把 role slot 视作已批准。
- 三份证据采集模板应增加 evidence_class、provenance_ref、usage_approval_ref 和 review_role_slot，防止纯合成数据与真实脱敏证据混用。
- 最终推荐技术候选为.NET 10 LTS、ASP.NET Core 10、PostgreSQL 18、EF Core/Npgsql 10、Vue 3.5、TypeScript 5.9、Vite 7、OIDC/OAuth 2.1、S3端口、PostgreSQL Outbox和Linux OCI；所有版本需锁定，候选仍须架构/工程/安全/运维批准。
- 当前不应替换六张Story中的技术占位命令；现有allowed_paths不能创建完整应用骨架，应先独立创建并批准ATC-PLT-000或等效治理任务。
- 虚拟华瑾可作为需求示例、测试数据和沙箱组织，但不能提供真实付费意愿、真实历史运营或上线授权证据；若它只是实际机构的脱敏化名，需由资料提供方明确该治理属性。
- “玩具检测”仍需进一步收敛到可形成稳定测试单元、方法集和报告语义的产品类别；候选可从常规非电动玩具开始，但必须由业务负责人依据历史订单确认。
- 中国内销、欧盟、美国可以有两种治理方式：选择一个首发市场；或批准一个版本化的虚拟三市场客户协议并承担更大的规则、方法、判定和报告范围。当前不得把三者隐式合并。
- 物理机械与分析化学是两个执行、QC、仪器、数据结构和安全语义明显不同的技术包；Release 1 若要求唯一主技术包，建议物理机械先行，分析化学作为后续独立切片或明确受控的从属方法集。
- 日均 500 订单尚不能直接换算容量：仍需峰值日/小时、每单送检项与样品倍率、并发用户、设备/仪器规模、附件和原始数据增长率。
- 当前输入描述的是完整产品组合，不满足 OD-001 要求的唯一 Release 1 试点切片；不得把五类产品和三类方法同时标为首发批准范围。
- “每日订单/样品量 500”未区分订单数、送检项数、包装数、实物数、试样数和结果数，只能作为容量采集起点，不能直接形成并发或存储基线。
- “广东华瑾检测有限公司”可能既是实施集团/法人又被填写为灯塔客户，但该法律与业务角色尚未确认；不能自动把同一名称复制为 OrganizationGroup、LegalEntity 和 Customer。
- PRD 当前六类技术包不包含微生物/生物；产品微生物检测不等于人体/临床生物样本，但若纳入产品路线必须通过 OD-025 另行定义技术包边界、执行记录、QC、样本安全和验收，不能静默并入 Release 1。
- 当前最低风险候选仍是“纺织品 × 常规面料或常规成衣（二选一）× 单一市场/协议版本 × 物理机械主技术包”；化学和微生物应先作为排除项或后续独立切片，除非真实历史订单重新评分证明其他切片更优。
- OD-020 可暂存的唯一容量事实是字符串“500/日（单位、平均/峰值、集团/试点范围均未确认）”；单集团、单实验室集中式独享部署只能作为 R1 候选拓扑，不能因此删除多机构扩展能力。
- PRD 候选性能目标可继续作为待审批目标：查询 p95≤2秒、委托/收样/结果保存 p95≤3秒、扫码 p95≤500毫秒、月可用性99.9%、RPO≤15分钟、RTO≤4小时；不能从 500/日直接推导节点和存储规格。
- ATC-REC-001 的机器依赖闭包除 OD-002 外还有 14 个未批准对象；其中 Core 且 applicability=UNKNOWN 的 OPS-RECEIPT-001、SEC-AUTH-001、SEC-AUD-001、NFR-ARCH-001 在批准前必须先明确适用性，不能只提升 status。
- ED-001 仍有 `TECH_STACK_TEST_COMMAND_REQUIRED_BY_ED-001` 与 `MODULE_BOUNDARY_CHECK_REQUIRED_BY_ED-001` 两个占位命令；技术栈为空不能解释为“无约束”，至少需要部署环境、操作系统、数据库许可、身份系统和对象存储约束。
- 变更前门禁基线有效：37 个规格版本、384 个 PRD 来源条目，来源无漂移，影响图为空。
- 旧租户语义分布在 PRD 的组织模型、业务域、权限、AI 检索、验收、风险、发布门禁和 OD-002；也分布在 SEC-AUTH-001、OPS-RECEIPT-002、AC-REC-001 及 ATC-REC-001/002/003。
- 旧 ATC 中 `tenantId`、`tenantScopedSequence`、“租户内唯一”和“跨租户访问”把共享数据平面隔离假设带入了数据/API/日志/测试契约，不能只改产品说明。
- OD-020 把“共享SaaS”与“集团多机构”并列作为未决部署选项；应由新增的已批准 OD-002 排除共享 SaaS，再让 OD-020 只决定单集团部署内部的站点拓扑、容量、并发、RPO/RTO 和恢复责任。
- ED-001、Release 1 候选基线及首批 Story 应精确依赖 OD-002；否则实现任务仍可能在技术选型时重新引入共享多租户。
- PRD 的 `AC-SEC-001` 当前把“客户甲/客户乙”误写成跨租户边界，混淆送检客户与租户；应改为集团内部跨客户、跨法人、跨实验室对象越权，同时另设集团外隔离由独立部署/数据平面验证。
- 规格 Schema 允许 decision 使用 `status=approved` 与 `decision_state=decided`，并支持自定义 `decision/options/exit_criteria` 字段；OD-002 可在用户明确批准的产品方向范围内创建为 1.0.0。
- 当前仓库契约测试尚无架构模式断言；应在 `tests/test_repository_contract.py` 增加 PRD/活跃规格/发布基线/Story 字段级检查，防止 tenantId、共享 SaaS 选项或跨租户测试再次进入机器源。
- 决策校验规则要求 `decision_state=decided` 与 `status=approved` 成对出现；OD-002 可合法形成 approved/decided，且其依赖必须全部是 approved/deprecated/retired，因此 OD-002 本身不应依赖仍在 proposed 的 OD-020。
- `source-accept` 对已变化且已有映射的 PRD 条目要求关联规格同时变化；新增 PRD 条目可在新增规格后统一以显式 reviewer/reason/approved acknowledgement 接受，不能使用 bootstrap/force。
- 产品定位段目前没有任何部署模式约束；应在 2.1 明确单集团独立部署、集团内多机构、客户非租户，并在 4.2 将共享 SaaS 多租户列入明确排除项。
- Release 0 当前只写“多实验室兼容”，但未固定集团/法人/实验室层级；应改为从底座即支持 OrganizationGroup→LegalEntity→Laboratory→Department/WorkCenter，并明确首个实施只激活一个法人/实验室不等于退化为单机构模型。
- 配套分析文档和 AI 开发手册仍有 SaaS/租户措辞，应同步改为集团独立部署、集团内组织维度授权及越权测试，避免实施团队从旁路文档恢复旧假设。
- 修改后的非生成源码中已不再出现 `tenantId`、`tenantScopedSequence`、共享 SaaS 候选项或把客户当租户的测试；剩余“租户/多租户”文本均是明确禁止说明，另有尚待 source-accept 更新的旧来源基线标题。
- ATC-REC-004/005/006 不直接包含旧租户字段，但仍应精确依赖 `OD-002@1.0.0`，使全部首批任务在架构方向上闭包；其具体业务契约无需机械加入无关的集团字段。
- 最终影响图归零；除契约测试中的禁止性断言外，docs/spec/generated 中不存在 `tenantId`、`tenantScopedSequence`、旧“跨租户”验收标题、共享 SaaS 候选项或旧 Tenant 实体列表。
- 生成后的 ATC-REC-001 明确区分服务端 `serverContext.organizationGroupId` 与客户端 required 字段，并包含跨组织拒绝、集团上下文不可覆盖、显式跨实验室协作测试。
- 最终规模为 43 个规格版本、389 个 PRD 来源条目、28 个生成文件、10 个 Gherkin 特性文件、6 张任务卡和 23 个自动化测试。
- 项目根目录已有一轮完成的规划记录，内容显示 PRD 已经形成较完整的业务规则、需求 ID、验收 ID、行业包和发布路线。
- 本轮关键不是继续扩写“产品应该有什么”，而是把产品规格翻译为 AI 可独立执行、可测试、上下文闭合的小工作包。
- PRD 共 1,491 行，覆盖定位、范围、角色、术语、权威矩阵、数据模型、端到端流程、11 类状态机、详细功能、AI 治理、权限、集成、UX、非功能、业务规则、验收、发布计划、风险和待决策项。
- 表格中约有 333 条带 ID 的定义/需求/风险/决策行；需求本身已经较细，但一条需求经常同时包含数据、规则、权限、状态和 UI 行为，尚不能直接等同于单个编码任务。
- 文档已经指定首期采用模块化单体、模块私有表隔离、RPO/RTO 等架构原则，但没有形成可执行的模块目录、端口/适配器契约、API/OpenAPI、DDL、事件协议或错误码。
- 验收标准以高质量 Given/When/Then 语义写成，但未进一步落为测试文件、固定测试数据、预期 API 响应、并发/权限组合和自动化测试层级。
- 当前工作区只有文档和演示材料，没有应用代码、构建脚本、技术栈或工程骨架；在技术选型和 34 个 OD 待决策项收敛前，AI 无法安全进入大规模功能编码。
- Release 0 已明确应与 Release 1 纵向生产切片共同开发，这支持“薄底座 + 纵向闭环”拆解，而不适合先生成完整通用平台。
- Release 1 默认建议纺织常规检测，但 `OD-001` 仍未正式锁定唯一试点；其余关键开发前决策包括部署/租户模式、范围行粒度、认可范围粒度、方法执行边界、首批条码/仪器接口和结论语义。
- 核心模型实体数量很大且关联复杂；直接按实体逐表生成 CRUD 会失去业务门禁。更合适的拆法是以用户可观察的状态转换为切片，再反推该切片最小的数据、接口、页面、权限、审计和测试。
- 可用 `AC-REC-001`（隔离控制）作为首批 AI 任务卡示例：它同时关联 `OPS-RECEIPT-001..003`、收到实物状态机、服务端授权、追加审计和阻断错误反馈，能示范完整纵向切片。
- 角色达到 20 余类且权限是“角色 + 对象 + 客户 + 法人 + 实验室 + 金额 + 有效期”的属性约束；仅写页面按钮权限不足以满足 PRD。
- NFR 已提供目标，但性能测试容量基线、部署拓扑、加密实现、对象存储和容灾运行手册仍需工程决策包补齐。
- 本机提供 Python 3.13.9 与 PowerShell 5.1；工作区仍无 `.git`。需求编译器可采用 Python 标准库实现，避免在尚无工程依赖管理的文档仓库引入第三方运行依赖。
- 收样首批切片有清晰的直接来源：`OPS-RECEIPT-001..003`、`OPS-IDENTITY-001..003`、`OPS-EXC-001..002`、`RULE-004`、`AC-REC-001` 与 `AC-ID-001`，适合用于端到端演示源漂移、影响分析和派生文件同步。
- `RULE-026` 明确历史和在制对象必须按批准迁移/沿用策略处理；这要求生成器保留版本化源指纹和基线，不能让新规范静默覆盖历史证据。
- 领域复核确认 PRD 当前为“待联合评审”，首批结构化对象必须使用 `proposed/in_review`，不得伪装为已批准。
- 规格模型必须把业务优先级（Must/Should/Could）与启用适用性（Core/Enabled-Pack/Conditional/BusinessOps）分离；适用性为 `UNKNOWN` 时默认阻断生产启用。
- 已批准规格需要“一版本一文件、历史不可变”；被替代关系通过追加式生命周期记录或索引表达，不能原地改写旧版本。
- 运行中委托必须绑定发布基线/requirements lock，不得查询 `latest`，否则规范更新会静默改变在制业务语义。
- Backlog 复核建议任务稳定 ID 不含 Release；采用 `ATC-*`，将 `target_release`、`epic_id`、`feature_id` 独立保存。
- 生成器复核确认 CI 中不得调用 AI；严格 JSON 应拒绝重复键、NaN/Infinity、浮点 number、BOM、非 NFC 和 CR 字符，输出不得含动态时间、绝对路径或未排序集合。
- 已发布历史控制需要完整对象哈希发现同版本篡改、行为哈希检查 PATCH 是否改变业务语义，并用 exclusive-create Seal 和前驱哈希链保护发布基线。
- 生成目录的 lock 文件兼任所有权 sentinel；未知文件必须使生成失败，只允许删除上一锁文件明确拥有的旧派生物。
- 当前实际输出为 26 个派生文件、42 个 `spec/**/*.json`（含配置、Schema 和来源基线）及 12 个 AI 开发手册/模板文件；6 张任务卡均明确显示 target release、Epic、Feature、完整来源/依赖和阻塞原因。
- 就绪报告正确显示 0 个 PRD 来源漂移和 6 个 BLOCKED Story；阻塞来自 proposed/in_review 状态、ED/OD 未决及依赖未批准，未出现“生成即批准”的错误提升。
- 最终交付含 15 个工具模块、37 个结构化规格对象、26 个派生文件、6 个测试模块（20 个测试用例）和 12 个中文手册/模板文件。
- 当前工作区仍未初始化 Git，因此 GitHub Actions 仅作为已准备好的 CI 配置文件；建立仓库并推送到受保护分支后才会实际执行。
- 候选发布保持 `proposed`，Seal 负向测试按设计失败；这证明工具没有为了演示而伪造批准状态。

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| 先抽取 PRD 标题、ID、流程和验收结构，再设计拆解层级 | 可避免仅凭通用模板给出与原文不匹配的建议 |
| 使用 Release → Domain/Epic → Feature → Vertical Story → AI Task Card 五层模型 | 既保留产品追踪，又把最小交付单元收敛为可独立验证的改动 |
| AI Task Card 必须同时包含数据、接口、状态、权限、审计、UI、测试与非目标 | 让 AI 不需要自行猜测跨层行为和验收边界 |
| 需求源采用 JSON + JSON Schema，生成器仅依赖 Python 标准库 | 在 Windows 当前环境可立即运行，避免 YAML 解析依赖；未来可选接入 YAML 适配器 |
| 自由文本 PRD 变化先触发 source-drift 门禁，再由人工批准结构化语义版本 | 自动传播与合规审批并存，避免错别字或模糊描述直接改写代码契约 |
| 将 priority 与 activation/applicability 分字段建模 | `Must` 不代表所有部署无条件启用；PRD 已明确 Core、行业包、条件接口和 BusinessOps 的不同适用范围 |
| 当前保留 PRD 来源扫描，但自由文本变化只触发 source-drift，不直接生成新语义 | 兼顾现阶段 PRD 追踪和确定性编译；人工完成结构化版本变更与来源确认后才重新生成 |

## Recommended Decomposition
- Layer 0/Release：冻结唯一试点、启用包、排除项、技术栈和部署基线。
- Layer 1/Domain Epic：治理与身份、要求/方法、询价/范围/报价、收样/谱系、执行/QC/结果、报告、计费交接、扩展包与 AI 治理。
- Layer 2/Feature：用一个有业务结果的能力组织，例如“完成身份评估并解除隔离”。
- Layer 3/Vertical Story：一个主要参与者、一个主要状态转换、明确前置和失败分支，可独立演示。
- Layer 4/AI Task Card：指定源需求、设计契约、允许修改范围、依赖、测试命令、证据和完成定义。

## Required Engineering Artifacts
- 决策包：Release 1 唯一切片、适用性矩阵、关键 OD 结论和假设失效条件。
- ADR：技术栈、模块化单体边界、事务/发件箱、权限、对象存储、集成和部署。
- 领域契约：聚合、实体、状态机、不变量、跨模块端口和事件。
- 机器可读契约：OpenAPI/JSON Schema、事件模式、错误码和幂等语义。
- UX 流程：页面状态、字段/校验、空态/错误态、权限态和关键线框。
- 测试资产：Gherkin 场景、固定数据、权限/并发/恢复矩阵和未来行业契约样例。
- AI 开发指南：仓库地图、编码规范、允许修改范围、生成/迁移规则、构建测试命令和 DoD。

## Suggested Delivery Order
1. 决策与工程骨架：锁定试点、ADR、仓库、CI、模块边界、身份/租户/审计/发件箱最小骨架。
2. 黄金主链：主数据 → 询价/能力评审 → TestScopeMatrix/报价 → 收样/身份 → 计划/执行/QC → 报告 → 计费证据导出。
3. 异常闭环：范围变更、数量不足、身份冲突、QC 失败、复测、报告更正、接口失败和人工降级。
4. 真实接入与上线门禁：条码、首类仪器、条件 ERP 接口、迁移、性能/安全/恢复、影子运行。
5. P0 AI 场景：作为可关闭、非生产硬依赖的独立切片，并带输入输出模式、评估集、停止条件和人工复核。

## Ready / Done Gates
- Ready：无阻塞 OD；源 ID/AC 明确；输入输出、状态、权限、错误码、UI 行为、测试数据和依赖齐全；非目标清楚。
- Done：代码/迁移/契约/文档完成；正向、反向、边界、权限、并发和恢复测试通过；追踪矩阵更新；无跨模块私表访问和循环依赖；可演示且可回滚。

## Example AI Task
- `R1-REC-003 隔离门禁`：来源 `OPS-RECEIPT-003`、11.4 状态机、`AC-REC-001`、`SEC-AUD-001`、`SEC-AUTH-001`。
- 目标：身份评估未完成或处于隔离/待定/拒收/安全封存的实物，不能进入拆解、制样或检测分配；失败时不改变业务状态并记录追加式审计事件。
- 前置：`OD-005` 已批准，收到实物状态/错误码/跨模块查询契约已冻结。
- 测试：各阻断状态、允许状态、跨租户、无权限、并发状态变化、重复请求、审计字段和无副作用。
- 非目标：本卡不实现身份评估 UI、条件接收审批、标签打印或完整收样流程。

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| PowerShell 控制台默认编码曾导致旧规划文件中文乱码显示 | 后续读取 PRD 时显式使用 UTF-8，并优先做结构化提取 |
| session catch-up 报告包含上一轮尚未写入规划文件的同步控制设计 | 已将该设计和用户的实施请求纳入 Phase 5—8；由于已知当前目录不是 Git 仓库，不重复执行此前失败的 `git diff --stat` |

## Resources
- `D:\FHJTFS\OpenLIMS\docs\AI原生第三方产品检测LIMS产品需求文档.md`
- `D:\FHJTFS\OpenLIMS\docs\ai-development\README.md`
- `D:\FHJTFS\OpenLIMS\generated\spec\readiness-report.md`
- `D:\FHJTFS\OpenLIMS\generated\spec\tasks\ATC-REC-003__v0.1.0.md`

## DEV-001B 基础依赖与登录集成（2026-07-24）
- 当前分支为 `codex/dev-001-engineering-skeleton`，DEV-001 工程骨架尚未提交，需作为本包实现基线保留。
- 前置规格门禁通过：59个规格版本、389个来源条目有效，来源无漂移，影响图为空。
- `ATC-PLT-000@1.0.0` 仍因自身状态及ED/SEC/NFR/AC依赖返回BLOCKED；用户已明确批准继续下一个基础设施集成任务包，因此本轮只能作为Spike推进，不得宣称Story Ready或生产可用。
- 本包目标限定为PostgreSQL、Keycloak、MinIO真实适配、外部readiness、OIDC登录和基础持久化，不实现LIMS业务模块。
- `ATC-PLT-000@1.0.0` 的allowed_paths已覆盖Host、building-blocks、平台契约、Web、平台测试、Compose/配置、验证脚本、应用CI和必要工程说明；本包无需修改规格扩大路径。
- 现有集中包版本已包含EF Core、Npgsql和OpenTelemetry，但尚无JWT Bearer、OIDC前端客户端或S3 SDK；新增依赖必须精确锁定并重新生成锁文件。
- Compose已有固定digest的PostgreSQL 18.4、Keycloak 26.4.1和MinIO，Keycloak realm当前只有公共Web客户端，尚缺API audience/claims、合成用户及MinIO初始化。
- 本机锁定工具链可用：.NET SDK 10.0.302 / runtime 10.0.10、Node 24.14.1、pnpm 10.34.5。
- NuGet官方索引核对后固定`Microsoft.AspNetCore.Authentication.JwtBearer 10.0.10`和`AWSSDK.S3 4.0.101.4`；版本写入中央清单，后续用锁定恢复和漏洞审计验证。
- 本机没有Docker/Podman Engine，也没有Java、PostgreSQL或MinIO本地服务；依赖真实启动无法在当前机器直接执行，必须通过可复现Compose/CI路径或安装新的外部运行时后验证，不能把静态配置检查描述成已启动验证。
- 前端代理已完成`oidc-client-ts 3.5.0`的Authorization Code + PKCE会话壳、回调、登出、Bearer调用和9项测试；根pnpm锁仍由主代理统一更新。
- 前端初版OIDC authority只允许HTTPS，和本地Keycloak的`http://localhost:8080`冲突；集成时需采用“生产HTTPS，开发仅允许loopback HTTP”的确定性校验，不能放宽到任意明文IdP。
- OIDC回调初版安全忽略外部returnTo但总是回首页；可在不信任URL的前提下恢复经过校验的本地returnTo，提升登录后回到原技术页面的体验。
- 后端初版已加入JWT、三依赖探测、S3端口和显式迁移入口，但迁移只创建history表，`IOutboxWriter`/`IInboxDeduplicator`/`IAuditIntentWriter`仍无生产适配；必须补实际PostgreSQL表与实现，不能用测试夹具冒充完成。
- 后端初版OIDC discovery使用可能缺尾斜杠的BaseAddress加相对路径，存在解析到错误realm路径的风险；应显式构造`{authority}/.well-known/openid-configuration`。
- 后端readiness缺独立总超时/分依赖超时；配置模板已有超时字段但代码未绑定。需确保每次探测失败关闭且不无限挂起。
- Web状态页初版仍请求匿名`/health/ready`，因此不会真实验证登录令牌、401/403或集团claim；应改为请求受保护且复用依赖探测的`/system/status`。
- 运行配置模板使用`*SecretRef`/`*BucketRef`字段，而Host初版绑定实际ConnectionString/Bucket/AccessKey/SecretKey，当前文档命令无法启动；集成阶段必须统一真实绑定名并继续禁止把Secret写入仓库。
- CI已增加三依赖启动smoke和镜像digest审计，但尚未启动应用、执行迁移或验证JWT/对象存储/API readiness；当前只能证明依赖容器配置，不能证明端到端集成。
- 主集成新增`tests/e2e/smoke`独立可执行探针，由CI在真实三依赖启动后验证迁移、readiness、Outbox/Audit原子提交与回滚、Inbox并发去重及MinIO上传/读取/删除；普通单元/契约测试不因本机缺Docker而静默跳过。
- 首次前端整体验证12项测试全绿，但pnpm明确提示esbuild构建脚本未获批准且单块产物约550KB；将显式只批准锁定的esbuild脚本，并把身份客户端/UI依赖拆为独立chunk后复验。
- NuGet全解决方案（含传递依赖）与pnpm高危门槛审计均为0个已知漏洞；Keycloak realm静态语义确认Web仅PKCE S256且禁Direct Grant，CI服务账户只使用合成Secret并携带单集团claim。
- CI的镜像锁审计初版只要求“至少一行匹配digest”，已收紧为逐镜像校验；新增NuGet JSON递归漏洞门禁，避免只打印清单却不失败。
- 真实浏览器验证通过：`/`呈现未登录状态和唯一登录入口；通过可见系统状态链接进入`/system/status`后收到后端真实401并显示`AUTH.AUTHENTICATION_REQUIRED`与关联ID；页面无集团选择器、无密码输入、无console warning/error。
- 由于本机无Keycloak，未点击登录向不可用IdP发起跳转，也未宣称浏览器回调完成；真实PKCE回调仍由可启动Compose环境执行。
- Keycloak 26健康端点默认位于容器内部management端口9000，CI不能从映射的8080主端口请求`/health/ready`；外部Smoke已改为请求realm discovery，容器就绪仍由内部9000 healthcheck负责。
- DEV-001B终审结论：不修改任何spec/generated/PRD/decision-packets，不创建业务模块；本机可验证范围全部通过。唯一环境性未执行项是三依赖真实容器启动和PKCE完整回调，已在应用CI中形成确定性执行链，但需分支提交推送后由GitHub runner实际产生证据。

## DEV-001/DEV-001B GitHub发布（2026-07-24）
- 用户接受建议，明确授权提交并推送当前`codex/dev-001-engineering-skeleton`分支，以触发GitHub Actions真实依赖Smoke。
- 远端为`https://github.com/garyyue2019/OpenLIMS.git`；发布目标是远端同名开发分支，不直接合并`main`。
- 提交前前置门禁再次通过：59个规格版本、389个来源，source current、impact为空；`ATC-PLT-000@1.0.0`仍按预期以退出码4返回BLOCKED，发布不提升任何规格状态。
- 发布前完整规格与应用验证再次全绿；当前可以进入范围审计、暂存和提交，不需要修改任何实现或门禁。
