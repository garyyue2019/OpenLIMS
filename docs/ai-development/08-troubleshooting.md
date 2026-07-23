# 08 故障排查

## `VALID` 失败

常见原因：

- 文件名与 `id/version` 不一致；
- 依赖没有 `@x.y.z`；
- 引用对象不存在；
- 依赖成环；
- Decision 状态与批准状态矛盾；
- Story 缺少 body 字段；
- release 选择不存在或未固定版本；
- JSON 重复 key、BOM、浮点、NaN、非 NFC。

先修权威 `spec/`，不要编辑 generated。

## `SOURCE DRIFT`

说明 PRD 与来源基线不同。运行：

```powershell
python -m tools.specgen impact --json
```

逐条判断是否影响结构化规格。完成规格评审后使用精确 `source-accept`。不要用 bootstrap/force 处理日常变化。

## `生成文件已过期或被手改`

原因可能是：

- 修改了 spec 但没有 generate；
- 直接编辑 generated；
- renderer 变化后未更新输出；
- 换行或编码被外部工具改写。

正确处理：修改源或 renderer，重新 generate，再 check。

## `生成目录存在未知文件`

生成目录不能放人工笔记。如果文件确实是新派生物，应让 renderer 生成并进入 lock；否则移到 `docs/` 或其他人工目录。生成器不会自动删除未知文件。

## `source-accept` 拒绝“规格未变化”

PRD 语义变化，但当前结构化规格与旧 lock 相同。应先：

1. 判断是否需要新版本；
2. 更新直接和传递依赖；
3. 运行 impact；
4. 再确认来源。

确属无工程语义变化时，经书面审阅使用 `--waive-spec-change`。

## `ready` 返回 BLOCKED

查看阻塞原因：

- Story 未 approved；
- readiness 不是 ready/in_progress/done；
- Decision 仍 open；
- 依赖未批准；
- 有 PRD 来源漂移。

不要让 AI 自行关闭阻塞项。当前样例预期全部 BLOCKED。

## `review-status` 返回 BLOCKED

这是评审证据尚未闭合的正常门禁。文本输出会逐条列出角色记录和技术锁；JSON输出可供工作台解析：

```powershell
python -m tools.specgen review-status `
  --change-set CHANGE-PLT-NEXT-VERSIONS-001 `
  --json
```

常见阻塞包括：

- 角色记录仍为`PENDING/DRAFT`；
- 身份、授权范围、授权证据、时间或签名缺失；
- `ACCEPT_WITH_CONDITIONS`尚未转化为条件闭合后的明确`ACCEPT`证据；
- 存在`REJECT`、`ABSTAIN`或阻塞性反对意见；
- 技术锁缺少精确版本、`VERIFIED`状态或实际证据引用。

退出码`2`表示输入本身无效，例如正文被修改但SHA侧车/记录未同步、同一角色槽有多条活动记录、CSV缺列或时间没有时区。修复证据源，不能放宽门禁或删除失败记录。

## `seal` 被拒绝

可能是：

- 发布仍 proposed/in_review；
- 选择规格未 approved；
- 依赖没有包含在发布选择中；
- generated/check 不一致；
- 来源存在漂移；
- 同版本 Seal 已存在。

Seal 是批准结果，不应通过命令行绕过。

## `verify-history` 失败

优先恢复被修改/删除的封存版本。若业务确实变化，应建立新版本，不能“修复”旧 Seal 的哈希。

## `PATCH 版本改变行为`

说明行为哈希变了但只提升 PATCH。判断变化是否兼容：

- 兼容新增通常使用 MINOR；
- 状态、权限、接口、数据或义务变化通常使用 MAJOR；
- 仅标题、负责人、解释文字变化才适合 PATCH。

## Windows 编码问题

权威 JSON 必须 UTF-8 无 BOM。不要使用会默认写 BOM 的旧版 PowerShell `Out-File -Encoding utf8` 生成权威 JSON。优先使用编辑器、apply_patch 或 specgen 自身写入。
