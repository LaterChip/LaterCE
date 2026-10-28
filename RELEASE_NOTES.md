---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: b24dc39192336bf7b807e81b52c48f4e_97816151d27e11f18068525400de85a5
    ReservedCode1: onS1w3YI0GBxQGruN5Ruy9BtsoNu1NGCxMKIX7WDL1hbUo7ZF6LfXjYUGI5OgYHE1cuMIBnNR5QVVvFVev2rH+ammu8N4AcsWa3z3RfcOEmjSOaXGcmnncJGxpmLr4YO0JEgO/s1opcCIqrcGwbsoz6f5eSuX4h2YoBwphuLdeOpv6ALztXU7nWBpGE=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: b24dc39192336bf7b807e81b52c48f4e_97816151d27e11f18068525400de85a5
    ReservedCode2: onS1w3YI0GBxQGruN5Ruy9BtsoNu1NGCxMKIX7WDL1hbUo7ZF6LfXjYUGI5OgYHE1cuMIBnNR5QVVvFVev2rH+ammu8N4AcsWa3z3RfcOEmjSOaXGcmnncJGxpmLr4YO0JEgO/s1opcCIqrcGwbsoz6f5eSuX4h2YoBwphuLdeOpv6ALztXU7nWBpGE=
---

# Later CE 更新说明

## v2.0.0 — 光影支持与管理增强

**发布日期**：2026-10-28
**适用平台**：Windows 10 1809+ / Windows 11，x64
**运行环境**：.NET 8 桌面运行时 + Windows App SDK 1.8 运行时

本次版本补齐了 1.0 版本缺失的**光影（Shader）下载能力**，并把「光影包」提升为独立市场页面，同时新增已安装光影的启用 / 禁用 / 删除管理与若干体验改进。

---

### 新增功能

#### 光影市场（新页面）

- 左侧导航新增「光影包」页签，接入 Modrinth `project_type=shader`，与模组 / 资源包市场共用同一套市场公共逻辑。
- 打开页面即加载热门光影，支持关键词搜索（如 Complementary、BSL、Sildur）、按**游戏版本**筛选、按**加载器**（Iris / OptiFine / Canvas / Vanilla）筛选。
- 当前实例对应的游戏版本会出现在版本下拉的首项并标注「当前实例」；指定版本无结果时自动放宽为全部版本，页面不会空白。
- 下载时按项目 + 游戏版本拉取版本清单，只保留含主文件的版本；仅有一个可用版本时直接下载，多个版本时弹窗选择。
- 列表项显示下载状态：`获取版本…` / `下载中…` / `已安装` / `失败`，状态栏同步显示搜索结果数量与实际安装路径。
- 顶部展示当前实例名与 `shaderpacks` 目录真实路径，并提供「打开 shaderpacks 目录」按钮。

#### 已安装光影管理

- 新增「已安装光影」页签，扫描当前实例 `shaderpacks` 目录下的 `.zip` / `.jar` 光影包与光影文件夹，展示启用状态、大小与占用合计。
- 支持**启用 / 禁用**：禁用时将文件（或文件夹）重命名为 `*.disabled`，游戏不再加载，随时可恢复。
- 支持**删除**：确认后移入回收站，可还原，不做永久删除。
- 支持按名称筛选、单项切换、一键「全部启用」与手动刷新。

#### 其它改进

- 主页新增「当前版本光影」统计卡，显示已启用数量与总数 / 已禁用数量，未安装时给出前往「光影包」页面的引导。
- 版本号升级至 `2.0.0.0`（`AssemblyName`、`Version`、`FileVersion` 同步）。
- 修复 1.0 版本「光影包只能手动放入目录、没有下载界面」的功能缺口。

### 说明与已知限制

- **光影渲染仍需游戏侧安装对应加载器**：光影本身依赖 Iris / OptiFine 等加载器运行，启动器只负责下载与启停文件，不替代加载器。
- CurseForge 依旧只提供模组来源，光影市场仅接入 Modrinth。

---

## v1.0.0 — 首次发布

**发布日期**：2026-10-26
**适用平台**：Windows 10 1809+ / Windows 11，x64
**运行环境**：.NET 8 桌面运行时 + Windows App SDK 1.8 运行时

首个公开版本，围绕「装得上、登得进、跑得起」完成了版本安装、加载器、多账号登录、内容市场与 Java 运行时管理的主干功能。

---

### 新增功能

#### 界面与主框架

- 全新的 WinUI 3 浅色界面：浅灰背景、白色卡片、绿色强调色与渐变主按钮，自绘标题栏并接入应用图标。
- 左侧导航：主页、版本管理、模组市场、资源包、整合包、账户、设置；左下角固定展示当前账户与「启动游戏」按钮。
- 主页概览：已安装版本数、检测到的 Java 运行时数量、当前版本模组数，以及实时运行日志面板。

#### 版本管理

- 从官方清单获取全部版本，支持按 **正式版 / 全部 / 快照 / 愚人节版本 / 远古版本** 筛选，并支持版本号关键词搜索与手动刷新清单。
- 单版本安装带进度显示，下载客户端 jar、依赖库、资源索引与资源文件。
- 「已安装」页签支持重新扫描、设为当前、打开目录、删除。
- 启动时按 `inheritsFrom` 合并版本信息，兼容加载器版本继承原版的结构。

#### 加载器支持

- 支持 **Vanilla / Fabric / Quilt / Forge / NeoForge** 五类加载器，并可选择具体加载器版本。
- Fabric、Quilt 通过官方 meta 生成继承原版的版本 JSON 并补齐依赖；Forge、NeoForge 下载 installer 后以 `--installClient` 完成安装，支持版本列表获取失败时的 maven 元数据回退。

#### 账户系统

- **微软正版登录**：OAuth 2.0 设备码流程，展示用户码与授权地址，自动轮询授权结果；完成 Xbox Live → XSTS → Minecraft Services → 玩家档案的完整换票链路。
- **LittleSkin 外置登录**：Yggdrasil 认证，校验是否已创建角色。
- **离线账户**：本地生成 UUID，仅用于离线服务器。
- 多账户保存与切换；移除账户仅清理本地登录信息。
- 令牌自动刷新：微软账户启动前按过期时间刷新，LittleSkin 账户过期后调用 refresh 接口。

#### 内容市场

- 模组市场：Modrinth 与 CurseForge 双来源，打开即加载热门内容，支持搜索、加载器筛选、游戏版本筛选，并记住首选加载器。
- 资源包页面：Modrinth 资源包浏览与一键安装至实例 `resourcepacks`。
- 整合包页面：Modrinth 整合包浏览与 `.mrpack` 安装，支持 `overrides` / `client-overrides` 覆盖。
- 安装前选择目标游戏版本，文件统一写入该版本实例目录，避免写错位置。

#### Java 运行时

- 自动扫描 PATH、JAVA_HOME、注册表、`%LOCALAPPDATA%\Programs`、`.minecraft\runtime` 等位置的 Java 并识别大版本。
- 按版本 JSON 的 `javaVersion.majorVersion` **严格匹配**，只使用大版本不低于要求的运行时。
- 缺少匹配运行时时自动下载安装对应 JRE：优先清华 TUNA 的 Adoptium 镜像，失败回退 Adoptium 官方 API；设置页提供「一键安装 JRE 21」。

#### 下载与启动

- BMCLAPI 镜像加速（默认开启），官方地址自动改写为镜像地址；每个文件按「镜像 → 官方 → 备用」候选链重试。
- 下载后校验文件大小，异常自动换源；并发下载线程数可调（默认 16）。
- 启动前补齐缺失依赖库，避免因官方库源 503 导致的启动失败；缺失 `log_configs` 时跳过对应参数继续启动。
- 可配置最小/最大内存、附加 JVM 参数、附加游戏参数；启动时剔除版本 JSON 自带内存参数避免冲突。
- **版本隔离**默认开启：每个版本独立 `game` 目录，`mods` / `saves` / `config` 互不影响。
- 游戏标准输出实时写入运行日志，进程退出后记录退出码。

---

### 修复项

| 问题 | 修复方式 |
| --- | --- |
| 微软登录返回 `AADSTS700016`（旧公共 Client ID `00000000402b5328` 已被微软停用） | 移除内置公共 ID，改为读取用户在「设置 → 微软正版登录」中自行注册的 Azure 应用 Client ID；未配置时给出明确中文指引，不再直接抛出原始错误 |
| 正版令牌过期后每次启动都需要重新登录 | 启动前检测账户过期状态，自动使用刷新令牌换取新的 Minecraft 令牌并回写账户；刷新令牌失效时才提示重新登录 |
| Java 版本低于版本要求导致 `UnsupportedClassVersionError` | 启动前按 `javaVersion.majorVersion` 严格校验，缺失时自动安装对应 JRE 并重试，校验不通过时阻止启动并说明原因 |
| 官方库源 `libraries.minecraft.net` 返回 503 导致 `NoClassDefFoundError` | 下载改为多源候选链，启动前再对缺失库做镜像补下载 |
| 版本 JSON 引用的日志配置 404 导致启动中断 | 日志配置缺失时仅记录警告并跳过该 JVM 参数，继续启动 |
| 加载器版本继承原版后 classpath 出现重复依赖 | 按 `group:artifact:classifier` 去重，保留 natives classifier，避免误删 |
| 市场图标加载导致页面崩溃或空白 | 图标改为惰性加载，加载失败保留占位图标，单张失败不影响列表 |
| 在导航栏切换页面时偶发异常导致崩溃 | 修正页面导航与选择变更处理，切换页面时不再重复触发加载逻辑 |
| 应用窗口/任务栏不显示图标 | 补齐 `ApplicationIcon` 与多尺寸 PNG 资源，标题栏显式引用应用图标 |
| 应用若干处界面文案与状态显示不准确 | 统一账户状态、过期提示、安装进度与错误提示文案 |

---

### 已知限制

- **微软正版登录必须自备 Azure 应用 Client ID**：本版本不内置任何 Client ID，未配置时无法使用正版登录（离线账户与 LittleSkin 不受影响）。
- **整合包安装尚未经过大规模实机验证**：`.mrpack` 解析与覆盖文件复制已实现，但不同整合包（尤其是含自定义启动参数、Forge 版本组合、服务端专属文件）可能存在兼容性问题，建议先在测试目录安装验证。
- ~~**光影包暂无独立市场页面**~~：已在 v2.0.0 中补齐独立光影市场页面与已安装光影管理。
- **CurseForge 仅支持模组**：资源包与整合包市场目前仅接入 Modrinth；CurseForge 需要在设置中填写个人 API Key，未填写时列表为空。
- **仅支持 Windows x64**：项目固定 `win-x64`，未提供 arm64 或 32 位构建。
- **非打包应用**：需预先安装 .NET 8 与 Windows App SDK 1.8 运行时，首次运行不会自动部署运行时。
- **无自动更新**：当前版本不包含启动器自更新功能，需手动获取新版本。
- **未选择开源许可证**：仓库暂无 `LICENSE` 文件，默认保留所有权利。

---

### 使用提醒

1. **首次使用请先完成两项配置**：在「设置」中确认游戏目录，并填写微软应用 Client ID（如需正版登录）。
2. **正版登录前**：确认 Azure 应用的受支持账户类型为「仅个人 Microsoft 账户」，并已启用「允许公共客户端流」。
3. **安装版本时**：若使用 Fabric / Quilt / Forge / NeoForge，请选择与目标游戏版本兼容的加载器版本；加载器版本列表获取失败时可点击刷新重试。
4. **启动失败时**：优先查看 `%APPDATA%\LaterCE\logs\laterce-YYYYMMDD.log`，日志中包含实际使用的 Java 路径、游戏目录、缺失依赖与完整错误堆栈。
5. **安全提示**：`%APPDATA%\LaterCE\config.json` 保存账户令牌与 CurseForge API Key，请勿随意分享；提交 Issue 前请先移除敏感信息。
6. **存档安全**：升级或重装前建议备份游戏目录；删除版本会移除该版本目录（版本隔离开启时包含其独立存档）。

---

### 发布说明

- 本版本以**源码形式**发布，仓库地址：<https://github.com/LaterChip/LaterCE>
- 构建方式见 [README.md](README.md) 的「从源码构建」章节，当前版本号 `2.0.0`。
- 本版本**未提供**预编译安装包或下载链接；请勿从非官方渠道获取可执行文件。
- 反馈问题请提交至：<https://github.com/LaterChip/LaterCE/issues>
*（内容由AI生成，仅供参考）*
