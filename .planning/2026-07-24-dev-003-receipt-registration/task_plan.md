# DEV-003 到货、包装与实物登记

## Goal

在单集团独立部署、集团多法人多实验室且禁止共享 SaaS 多租户的边界内，交付可运行的到货登记纵向切片：区分 Receipt、Container、ReceivedItem，完成权限、幂等、隔离初始状态、审计、事务发件箱、API、最小页面、迁移和自动化测试。

## Current Phase

Phase 5: 测试与硬化

## Phases

### Phase 1: 将人工批准转成 READY 规格

- [x] 盘点 DEV-001/DEV-002 已交付平台能力及 ATC-REC-001 的精确依赖闭包
- [x] 只依据用户明确批准的八项业务基线创建新的 SemVer 规格版本
- [x] 创建 DEV-003 READY 任务卡，冻结 allowed paths、非目标和验收命令
- [x] 运行 validate、source-status、impact、generate、check 和 ready
- **Status:** complete

### Phase 2: 模块设计与契约冻结

- [x] 设计 receiving 模块边界、公共端口、聚合、不变量和错误码
- [x] 冻结 RegisterReceipt API、幂等语义、授权上下文和输出契约
- [x] 冻结数据库迁移、审计与事务发件箱的原子性边界
- **Status:** complete

### Phase 3: 后端与持久化实现

- [x] 实现 Receipt、Container、ReceivedItem、编号和版本并发控制
- [x] 实现授权、服务委托可收样校验、幂等和事务原子性
- [x] 实现受控迁移、API 端点、审计证据和事务发件箱
- **Status:** complete

### Phase 4: Web 最小纵向切片

- [x] 接入 receiving Web feature、路由和导航
- [x] 实现包装/实物分层录入、提交中、成功、错误和只读状态
- [x] 保证客户端不能提交或覆盖集团上下文
- **Status:** complete

### Phase 5: 测试与硬化

- [ ] 覆盖正向、反向、边界、权限、并发、恢复和审计测试
- [ ] 覆盖幂等相同载荷、幂等冲突、跨组织拒绝和跨实验室显式授权
- [ ] 覆盖迁移、模块边界、Web 行为与可访问性
- **Status:** in_progress

### Phase 6: 完整门禁与交付

- [ ] 执行仓库完成门禁和确定性二次生成
- [ ] 审计全部变更是否位于 DEV-003 allowed paths
- [ ] 提交、推送并创建 PR，等待全部 CI 通过
- **Status:** pending

## Approved Scope Decisions

| Decision | Approval evidence |
|---|---|
| 单集团独立部署、集团多法人多实验室；禁止共享 SaaS 多租户 | 用户明确回复“批准 DEV-003 业务基线” |
| Receipt 是一次物流到货，Container 是实际包装，ReceivedItem 是一个完整销售玩具或玩具套装 | 同上 |
| 同一包装内多个完整玩具或套装逐个登记；零部件、材料和颜色不在本任务拆分 | 同上 |
| 型号、批次、序列号、颜色、包装状态、封识或实物状态不同必须拆分 ReceivedItem | 同上 |
| 登记成功自动隔离，登记本身不得解除隔离 | 同上 |
| 权限按集团、法人、实验室、客户和委托控制；管理员默认无业务权限；跨实验室需显式授权 | 同上 |
| 业务写入、审计和事务发件箱原子；支持幂等重试 | 同上 |
| DEV-001、DEV-002 已交付平台能力作为技术基线，并允许创建新 READY 任务卡版本 | 同上 |

## Guardrails

- 不修改产品来源文档。
- 不直接编辑 `generated/spec/`。
- 不引入共享 SaaS 多租户或运行时插件发现。
- 不实现身份评估结论、异常审批、条件接收、解除隔离、拆解、制样或检测任务。
- 不把包装数量自动解释成实物数量。
- 不以系统管理员身份绕过业务授权。
- 不在 READY 任务卡 allowed paths 外修改实现。

## Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| `ATC-REC-001@1.0.0` 为 proposed/blocked，且存在 11 个未批准依赖 | 1 | 停止编码；获得用户对 DEV-003 八项业务基线的明确批准，准备创建新版本闭包 |
| 读取 `apps/web/pnpm-lock.yaml` 失败，锁文件不在应用子目录 | 1 | 不重复原命令；改用 `rg --files` 定位仓库级锁文件 |
| Windows 下直接把 `*.spec.ts` 和 `**/*.spec.ts` 作为 rg 路径导致路径语法错误 | 1 | 不重复通配路径；改用 `rg -g '*.spec.ts'` 文件过滤 |
| 前端首次 lint 因 18 个 HTML void `<input />` 自闭合警告而失败 | 1 | 按仓库 Vue 规则将 input 改为非自闭合写法后重新运行完整前端门禁 |
| 前端第二次门禁的 typecheck 无法从零参数 mock 推断 fetch 调用元组 | 1 | 为 mock 显式声明 `RequestInfo/URL` 与 `RequestInit` 参数，保持生产代码不变 |
| 显式 fetch mock 参数触发测试 lint 未使用变量规则 | 1 | 在 mock 中显式消费标准参数后返回响应；不关闭 lint 规则 |
| 组件测试的 `a-alert` stub 未渲染 description prop，导致只读说明断言失败 | 1 | 让 stub 显式接收并渲染 message/description，验证真实传参语义 |
| 本地环境没有 `docker` 命令，无法启动仓库 PostgreSQL Compose | 1 | 不伪造集成结果；检查本机 PostgreSQL 可用性，并保留 Linux CI 的真实 PostgreSQL service 验证 |
| Python 仓库回归 40 项中 3 项仍固定 DEV-002 的 60 规格、任务清单和“所有新版本均未批准”假设 | 1 | 仅更新受 DEV-003 新版本直接影响的仓库契约断言，不删除历史草案保护测试 |
| 新授权单测 4 处未传 xUnit 测试取消令牌，被 `-warnaserror` 阻断 | 1 | 为每次 AuthorizeAsync 调用传 `TestContext.Current.CancellationToken`，不压制分析器 |
| `dotnet format --verify-no-changes` 报告 5 个本任务文件 import 顺序和 1 个未改动平台测试的既存 import 顺序 | 1 | 手工修正 allowed paths 内的 5 个文件；对未改动且不在 allowed paths 的既存文件不越界改写，改用限定文件格式验证 |
| GitHub PR 描述首次填充返回浏览器内部错误 | 1 | 保留已打开页面，不重复相同定位方式；重新读取页面状态并使用当前 textarea 定位 |
| 首轮 Linux PostgreSQL CI 6 项中 1 项失败：record 内嵌 `List<T>` 的 `Assert.Equal` 使用引用相等 | 1 | 数据库实际重放 ID 和字段一致；将测试改为严格深层 `Assert.Equivalent`，不修改生产幂等逻辑 |
