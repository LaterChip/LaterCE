---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: b24dc39192336bf7b807e81b52c48f4e_96402509d27e11f1a301525400cd780f
    ReservedCode1: vN1QoZHTXOJQLMRJ4/yrSFCUi9Uppt4p7GMQwKW1ZxoPwca4UMrx2y6TgsMAZ3jUdbKKm+NdDX5WfSzcem6KWEUS0xaFMoi9xk+jKMppUsMCrPHSbsPwMvusV+Yzv9HZB0GhlPnzaoIwiknh5quP+R4U6ksnHvqGWKsUT0rdIqr6bB10cGzRt0c7G3I=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: b24dc39192336bf7b807e81b52c48f4e_96402509d27e11f1a301525400cd780f
    ReservedCode2: vN1QoZHTXOJQLMRJ4/yrSFCUi9Uppt4p7GMQwKW1ZxoPwca4UMrx2y6TgsMAZ3jUdbKKm+NdDX5WfSzcem6KWEUS0xaFMoi9xk+jKMppUsMCrPHSbsPwMvusV+Yzv9HZB0GhlPnzaoIwiknh5quP+R4U6ksnHvqGWKsUT0rdIqr6bB10cGzRt0c7G3I=
---

# Later CE

Later CE 是一个面向 Windows 的 **Minecraft Java 版启动器**，使用 **WinUI 3 / .NET 8 / Windows App SDK 1.8** 开发，界面采用浅色主题，支持版本与加载器安装、微软正版 / LittleSkin / 离线多账号登录、Modrinth 与 CurseForge 内容下载，以及 Java 运行时的自动检测与一键安装。

仓库地址：<https://github.com/LaterChip/LaterCE>

> 本项目为个人开发的非官方启动器，与 Mojang Studios、Microsoft 无任何关联。请使用已购买的 Minecraft 正版账号登录游戏。

---

## 目录

- [界面预览](#界面预览)
- [功能特性](#功能特性)
  - [版本管理](#版本管理)
  - [加载器支持](#加载器支持)
  - [账户与登录](#账户与登录)
  - [模组与内容市场](#模组与内容市场)
  - [Java 运行时](#java-运行时)
  - [下载与镜像加速](#下载与镜像加速)
  - [启动参数与实例隔离](#启动参数与实例隔离)
  - [界面与主题](#界面与主题)
- [技术栈](#技术栈)
- [系统要求](#系统要求)
- [从源码构建](#从源码构建)
- [配置微软正版登录](#配置微软正版登录)
- [配置文件与日志路径](#配置文件与日志路径)
- [目录结构](#目录结构)
- [常见问题](#常见问题)
- [免责声明](#免责声明)
- [许可证](#许可证)

---

## 界面预览

> **说明：仓库当前尚未包含界面截图。** 为保证文档内容真实，此处不放置任何占位图片或外部图片链接。
>
> 如需查看界面，请按下文 [从源码构建](#从源码构建) 自行编译运行；截图可放入 `docs/screenshots/` 目录后在此处补充，例如：
>
> ```
> docs/screenshots/home.png        主页
> docs/screenshots/versions.png    版本管理
> docs/screenshots/mods.png        模组市场
> docs/screenshots/accounts.png    账户
> docs/screenshots/settings.png    设置
> ```
>
> 上述文件目前尚不存在，属于后续待补充内容。

主窗口左侧导航包含：**主页、版本管理、模组市场、资源包、光影包、整合包、账户、设置**；左下角固定显示当前账号与「启动游戏」按钮，窗口顶部为自绘标题栏（应用图标 + 名称 + 副标题）。

---

## 功能特性

### 版本管理

- **版本清单来源**：官方版本清单（Mojang `version_manifest`），支持手动「刷新清单」。
- **分类筛选**：正式版、全部版本、快照、愚人节版本、远古版本（`release` / `all` / `snapshot` / `fools` / `old`）。
- **版本搜索**：按版本号关键词过滤，例如 `1.20.1`。
- **安装**：逐个版本下载，带进度显示；下载内容包括客户端 jar、依赖库（libraries）、资源索引与资源对象（assets）。
- **已安装管理**：独立的「已安装」页签，支持重新扫描、**设为当前**、**打开目录**、**删除**。
- **版本继承**：启动时按 `inheritsFrom` 链合并版本信息（加载器版本继承原版），并对 classpath 依赖按 `group:artifact:classifier` 去重。

### 加载器支持

安装版本时可选加载器与具体加载器版本：

| 加载器 | 说明 | 实现方式 |
| --- | --- | --- |
| Vanilla（原版） | 仅安装官方原版 | 直接下载官方版本文件 |
| Fabric | 可选具体 loader 版本 | 通过 Fabric 官方 meta（`meta.fabricmc.net`）生成继承原版的版本 JSON，并补齐依赖库 |
| Quilt | 可选具体 loader 版本 | 通过 Quilt 官方 meta（`meta.quiltmc.org`）生成继承原版的版本 JSON，并补齐依赖库 |
| Forge | 可选具体版本 | 读取 Forge promotions / `maven-metadata.xml` 版本列表，下载 installer 并执行 `--installClient` |
| NeoForge | 可选具体版本 | 读取 NeoForge maven 版本列表，下载 installer 并执行 `--installClient` |

- 加载器版本列表获取失败时会在界面提示并记录日志，不会中断其余功能。
- Forge / NeoForge 安装需要本机可用 Java；安装器日志会写入 Later CE 日志文件。

### 账户与登录

| 账户类型 | 登录方式 | 说明 |
| --- | --- | --- |
| 微软正版 | OAuth 2.0 **设备码流程**（Device Code） | 显示用户码与授权地址，浏览器完成授权后自动轮询获取令牌；需 **自行配置 Azure 应用 Client ID**（见下文） |
| LittleSkin | Yggdrasil 外置登录（账号 + 密码） | 服务端固定为 `https://littleskin.cn/api/yggdrasil`；要求该账号已创建角色 |
| 离线账户 | 本地创建用户名 | UUID 由用户名生成，无需联网验证，仅能进入离线服务器 |

- **令牌自动刷新**：微软账户启动前检测过期并使用 `refresh_token` 重新换取 Minecraft 令牌；LittleSkin 账户过期后调用 `authserver/refresh` 刷新。
- **多账号管理**：可保存多个账户并切换当前账户；「移除」仅删除本启动器中保存的登录信息，不影响账号本身。
- 微软登录流程中若用户未拥有 Minecraft Java 版，会给出明确提示；Xbox Live 常见错误码（未注册 Xbox、地区不支持、未成年需家庭组等）会转换为中文提示。

### 模组与内容市场

| 内容类型 | 来源 | 安装位置 |
| --- | --- | --- |
| 模组（mod） | Modrinth（无需配置）；CurseForge（需 API Key） | 当前实例的 `mods` |
| 资源包（resourcepack） | Modrinth | 当前实例的 `resourcepacks` |
| 整合包（modpack，`.mrpack`） | Modrinth | 按整合包清单安装，含 `overrides` / `client-overrides` 覆盖 |
| 光影包（shader） | Modrinth | 当前实例的 `shaderpacks`（支持启用 / 禁用 / 删除管理） |

- 「模组市场」打开即加载热门内容，支持关键词搜索、加载器筛选、游戏版本筛选，并记住上次选择的加载器。
- CurseForge 需要在「设置 → API 与来源」中填写个人 API Key（`console.curseforge.com` 免费申请）；未填写时会提示并可切回 Modrinth。
- 安装前会要求选择目标游戏版本，文件统一写入该版本的实例目录，避免写入不存在的目录。
- 市场图标采用惰性加载，加载失败时保留占位图标，不会导致页面崩溃。
- 「光影包」页面分两个页签：**光影市场**支持按游戏版本 / 加载器（Iris、OptiFine、Canvas、Vanilla）筛选与关键词搜索，下载时自动选择或手动指定光影版本，并在列表中提示「获取版本 / 下载中 / 已安装 / 失败」状态；**已安装光影**列出实例 `shaderpacks` 内的 `.zip` / `.jar` 与文件夹，支持按名称筛选、启用 / 禁用（通过 `.disabled` 后缀）、删除（移入回收站）与一键全部启用。

### Java 运行时

- **自动检测**：扫描 `PATH`、`JAVA_HOME`、注册表常见位置、`%LOCALAPPDATA%\Programs`、`.minecraft\runtime` 等位置的 `java.exe`，并读取其大版本。
- **严格版本匹配**：读取版本 JSON 中的 `javaVersion.majorVersion`，只选用 **大版本 ≥ 要求** 的运行时，绝不把低版本 Java 交给高版本游戏（否则会抛 `UnsupportedClassVersionError`）。
- **自动安装**：本地缺少匹配运行时时，启动前自动安装对应大版本 JRE；设置页另提供「一键安装 JRE 21」。
- **下载源**：优先清华大学 TUNA 的 Adoptium 镜像，失败后回退 Adoptium 官方 API；安装目录为 `%APPDATA%\LaterCE\java`。

### 下载与镜像加速

- **BMCLAPI 镜像加速**（默认开启）：将 Mojang、Forge、Fabric、Quilt、NeoForge 等官方地址改写为 BMCLAPI 镜像地址。
- **多源候选链**：每个文件按「镜像 → 官方 → 备用源」的顺序逐个尝试，单个源失败自动切换下一个。
- **完整性校验**：下载后校验文件大小，异常则换源重试；并发下载线程数可配置（默认 16，范围 1–64）。
- **启动前依赖兜底**：启动游戏前检查缺失的依赖库并按「BMCLAPI → 官方」补下载，降低因 `libraries.minecraft.net` 返回 503 导致的 `NoClassDefFoundError`。
- **日志配置容错**：`log_configs` 文件缺失（404）时跳过该 JVM 参数继续启动，不阻断游戏。

### 启动参数与实例隔离

- 内存：最小内存（`-Xms`）与最大内存（`-Xmx`）可视化配置，启动时自动剔除版本 JSON 中的内存参数避免冲突。
- 附加 JVM 参数、附加游戏参数均支持自定义（按命令行规则拆分）。
- **版本隔离**（默认开启）：每个版本使用 `versions/<版本号>/game` 作为独立工作目录，`mods`、`saves`、`config` 等互不干扰；关闭后统一使用游戏根目录。
- 启动完成后可实时查看游戏标准输出日志，进程退出后记录退出码。

### 界面与主题

- **浅色主题**：`Application RequestedTheme="Light"`，自定义浅灰背景、白色卡片、绿色强调色（`#0EA472`）与渐变按钮样式。
- 自绘标题栏，显示应用图标与应用名称。
- 主页展示已安装版本数、检测到的 Java 数、当前版本模组数与光影数，以及实时运行日志。
- 采用非打包（unpackaged）方式运行，无需 MSIX 安装包。

---

## 技术栈

| 项目 | 版本 / 说明 |
| --- | --- |
| UI 框架 | WinUI 3（`UseWinUI=true`） |
| 运行时 | .NET 8（`net8.0-windows10.0.19041.0`） |
| Windows App SDK | 1.8.260921001 |
| 打包方式 | 非打包（`WindowsPackageType=None`，`EnableMsixTooling=false`） |
| 目标平台 | x64（`win-x64`，`RuntimeIdentifier=win-x64`） |
| 语言 | C#（`LangVersion=latest`，启用可空引用类型） |
| 版本号 | 2.0.0.0 |

---

## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 1809（build 17763）或更高；Windows 11 推荐 |
| 架构 | x64 |
| 运行时 | Windows App SDK 1.8 运行时（未随程序打包）；.NET 8 桌面运行时 |
| 磁盘 | 视所安装的游戏版本与资源而定（原版约数百 MB，整合包可能数 GB） |
| 网络 | 首次安装版本、登录与访问市场需要联网 |

> 项目为 `WindowsAppSDKSelfContained=false`、`SelfContained=false`，运行前需已安装 Windows App SDK 1.8 运行时与 .NET 8 运行时，否则会提示缺少依赖。

---

## 从源码构建

### 前置条件

1. **.NET SDK 8.0** 或更高（`dotnet --version` 应输出 8.x 及以上）。
2. **Visual Studio 2022 / 2026**（可选，用于可视化调试），需勾选「.NET 桌面开发」与「Windows 应用开发 / WinUI」相关工作负载。
3. **Windows App SDK 1.8 运行时**（运行非打包应用需要）。

### 命令行构建

```powershell
# 进入仓库根目录
cd C:\path\to\LaterCE

# 还原依赖并编译
dotnet build .\LaterCE\LaterCE.csproj -c Debug

# 运行
dotnet run --project .\LaterCE\LaterCE.csproj
```

生成的程序位于：

```
LaterCE\bin\Debug\net8.0-windows10.0.19041.0\win-x64\LaterCE.exe
```

### 发布构建

```powershell
dotnet publish .\LaterCE\LaterCE.csproj -c Release -r win-x64 --self-contained false
```

> 说明：项目为**非打包**应用，直接运行 `LaterCE.exe` 即可，无需安装 MSIX；但依赖系统已安装 Windows App SDK 1.8 与 .NET 8 运行时。若需分发免依赖版本，可自行改为自包含发布（`--self-contained true`），该配置不在本项目默认设置中。

---

## 配置微软正版登录

> **重要**：微软已停用旧版公共 Client ID（`00000000402b5328`），继续使用会返回 `AADSTS700016`。因此 Later CE **不内置任何 Client ID**，必须由使用者自行注册 Azure 应用。

### 注册步骤

1. 打开 [Azure 应用注册](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade) 并登录 Microsoft 账号。
2. 选择「新注册」，名称可任意填写（例如 `Later CE`）。
3. **受支持的账户类型**选择「**仅个人 Microsoft 账户**」（设备码流程走 `consumers` 端点）。
4. 注册完成后，在应用概览页复制「**应用程序(客户端) ID**」。
5. 进入「身份验证」，启用「**允许公共客户端流**」（设备码流程属于公共客户端，无需配置重定向 URI）。
6. 打开 Later CE →「设置 → 微软正版登录」，将 Client ID 粘贴到「Microsoft 应用 Client ID」并保存。
7. 回到「账户」页点击微软登录，按提示打开授权页并输入设备码中的用户码完成登录。

设置页同时提供「打开 Azure 应用注册」与「打开微软账号授权页」两个快捷入口。

### 令牌刷新

微软令牌过期后，启动游戏时会自动使用刷新令牌重新获取；若刷新令牌失效，会提示重新登录。刷新令牌保存在 `%APPDATA%\LaterCE\config.json` 的账户列表中，请注意该文件的隐私安全。

---

## 配置文件与日志路径

所有启动器自身数据位于 `%APPDATA%\LaterCE`（即 `C:\Users\<用户名>\AppData\Roaming\LaterCE`）。

| 路径 | 内容 |
| --- | --- |
| `%APPDATA%\LaterCE\config.json` | 全局配置：游戏目录、Java 路径、内存、附加参数、镜像开关、线程数、微软 Client ID、CurseForge API Key、版本隔离、当前版本、账户列表等 |
| `%APPDATA%\LaterCE\logs\laterce-YYYYMMDD.log` | 运行日志（按日期分文件），包含下载、登录、启动与游戏输出 |
| `%APPDATA%\LaterCE\java\jre-<大版本>` | 通过启动器一键安装的 JRE |
| `%APPDATA%\LaterCE\installers` | Forge / NeoForge 安装器缓存 |
| `%APPDATA%\.minecraft`（默认） | 游戏根目录，可在设置中修改；包含 `versions`、`libraries`、`assets` |

版本隔离开启时，单个版本的游戏工作目录为：

```
<游戏目录>\versions\<版本号>\game
```

其中包含该版本独立的 `mods`、`resourcepacks`、`shaderpacks`、`saves`、`config`。

---

## 目录结构

```
LaterCE/
├─ LaterCE.sln                    解决方案文件
├─ README.md                      本文档
├─ RELEASE_NOTES.md               版本更新说明
└─ LaterCE/
   ├─ LaterCE.csproj              项目文件（WinUI 3 / .NET 8 / Windows App SDK 1.8）
   ├─ app.manifest                应用清单
   ├─ App.xaml / App.xaml.cs      应用入口、浅色主题与全局样式
   ├─ MainWindow.xaml(.cs)        主窗口、导航、启动流程（含令牌刷新）
   ├─ Assets/                    应用图标（ico / png 多尺寸）
   ├─ Models/
   │  ├─ Account.cs              账户模型（类型、UUID、令牌、过期判断）
   │  ├─ AppConfig.cs            全局配置模型
   │  ├─ MinecraftModels.cs      版本清单 / 版本详情 / 资源索引等模型
   │  ├─ ModrinthModels.cs       Modrinth 数据模型
   │  └─ CurseForgeModels.cs     CurseForge 数据模型
   ├─ Pages/
   │  ├─ HomePage.xaml(.cs)      主页（统计信息与运行日志）
   │  ├─ VersionsPage.xaml(.cs)  版本管理（下载 / 已安装）
   │  ├─ ModsPage.xaml(.cs)      模组市场（Modrinth / CurseForge）
   │  ├─ ResourcePacksPage.xaml(.cs)  资源包市场（Modrinth）
   │  ├─ ShaderPacksPage.xaml(.cs)  光影市场与已安装光影管理（Modrinth）
   │  ├─ ModPacksPage.xaml(.cs)  整合包市场（Modrinth）
   │  ├─ MarketSupport.cs        市场公共逻辑（基础版本解析、图标批量加载）
   │  ├─ AccountsPage.xaml(.cs)  账户（微软 / LittleSkin / 离线）
   │  └─ SettingsPage.xaml(.cs)  设置
   └─ Services/
      ├─ AppPaths.cs             路径常量（配置、日志、版本、库、资源、实例）
      ├─ AppState.cs             全局状态（账户、服务实例、事件）
      ├─ ConfigService.cs        配置读写
      ├─ Log.cs                  日志写入
      ├─ Http.cs                 HTTP 客户端与镜像地址改写 / 候选链
      ├─ Downloader.cs           多线程下载、多源回退、大小校验
      ├─ MinecraftVersionService.cs  版本清单与版本详情
      ├─ VersionInstaller.cs     原版安装（客户端、库、资源）
      ├─ LoaderInstaller.cs      Fabric / Quilt / Forge / NeoForge 安装
      ├─ JavaFinder.cs           Java 检测、严格匹配、JRE 自动安装
      ├─ AuthService.cs          微软设备码 / LittleSkin / 离线登录与刷新
      ├─ ModrinthService.cs      Modrinth 搜索与下载
      ├─ CurseForgeService.cs    CurseForge 搜索与下载
      ├─ ModManager.cs           内容安装（mods / resourcepacks / shaderpacks / mrpack）
      └─ GameLauncher.cs         版本合并、依赖兜底、参数组装与进程启动
```

---

## 常见问题

### 1. 微软登录报 `AADSTS700016` / `unauthorized_client`

**原因**：使用了已停用的旧公共 Client ID，或 Client ID 填写错误、应用类型不支持个人账户。

**处理**：按 [配置微软正版登录](#配置微软正版登录) 自行注册 Azure 应用，账户类型选择「仅个人 Microsoft 账户」，开启「允许公共客户端流」，并将 Client ID 填入设置页后重启登录流程。

### 2. 启动时报 `UnsupportedClassVersionError`

**原因**：Java 大版本低于版本要求。例如新版本快照需要 Java 25，本机仅有 Java 21 时无法运行。

**处理**：Later CE 会在启动前严格校验 `javaVersion.majorVersion`，缺失时自动安装对应 JRE。若自动安装失败，可在「设置」中一键安装 JRE 21 或手动指定更高版本 `java.exe`；也可查看日志确认实际使用的 Java 路径与大版本。

### 3. 启动时报 `NoClassDefFoundError` 或缺少 natives

**原因**：依赖库下载不完整（官方 `libraries.minecraft.net` 曾出现持续 503）。

**处理**：保持「使用 BMCLAPI 镜像加速下载」开启，重新安装该版本；启动前启动器会再次检查并补下载缺失库。若仍失败，请删除 `versions\<版本号>` 后重装。

### 4. 微软账号无法登录 / 提示未拥有游戏

- 提示「该微软账号未拥有 Minecraft Java 版」：该账号未购买 Java 版或尚未创建角色名。
- Xbox 错误 `2148916233`：账号尚未注册 Xbox，请先到 xbox.com 创建。
- `2148916235`：账号所在地区不支持 Xbox Live。
- `2148916238`：未成年账号需先加入家庭组并由成年人批准。

### 5. 模组市场（CurseForge）列表为空

CurseForge 必须填写个人 API Key：`console.curseforge.com` 免费申请后填入「设置 → API 与来源」。Modrinth 无需任何配置。

### 6. 网络被 hosts 屏蔽导致无法下载 / 登录

若本机 hosts 文件屏蔽了 `mojang.com`、`minecraft.net`、`libraries.minecraft.net` 等域名，官方源会直接失败。可开启 BMCLAPI 加速，或移除相关 hosts 记录；微软登录与 Xbox 认证域名（`login.microsoftonline.com`、`*.xboxlive.com`、`api.minecraftservices.com`）同样需要可访问。

### 7. 模组安装了但游戏里不生效

- 确认模组支持的**游戏版本**与**加载器类型**（Fabric / Forge / NeoForge / Quilt）与当前实例一致。
- 开启版本隔离时，模组必须装在当前版本的 `versions\<版本号>\game\mods` 下；若手动复制，请注意目标目录。
- Fabric / Quilt 需要 Fabric API 等前置模组，按模组页面说明安装。

### 8. 日志出现「日志配置文件缺失，已跳过」

这是可忽略的警告：版本 JSON 引用的 `log_configs` 文件下载失败（常见为 404）时，启动器会跳过该 JVM 参数继续启动，不影响游戏运行。

### 9. 整合包安装后如何启动

整合包按清单安装到所选版本实例中（含 `overrides` 覆盖文件）。安装完成后到「版本管理 → 已安装」将对应版本「设为当前」，再点击「启动游戏」；首次启动建议检查该整合包要求的 Java 版本与内存。

---

## 免责声明

- Later CE 是**非官方**的第三方 Minecraft 启动器，与 Mojang Studios、Microsoft 及其关联公司**无任何隶属或合作关系**。
- Minecraft 是 Mojang Studios 的商标。使用本软件下载、启动游戏前，请确保你已通过官方渠道购买并拥有合法的 Minecraft Java 版账号。
- 本软件按「现状」提供，不附带任何明示或暗示的担保。因使用本软件导致的账号异常、存档损坏、数据丢失等问题，作者不承担责任。
- 第三方内容（Modrinth、CurseForge、LittleSkin、BMCLAPI、Adoptium 等）由其各自运营方提供并受其服务条款约束，本软件仅作为客户端进行访问。
- 请勿将本软件用于任何违反 Minecraft 最终用户许可协议（EULA）或相关法律法规的用途。

---

## 许可证

本项目**尚未选择开源许可证**（仓库根目录当前不存在 `LICENSE` 文件）。

在补充许可证之前，默认保留所有权利；如需转载、分发或二次开发，请先通过 Issue 与作者联系。计划后续补充许可证文件，届时以仓库中的 `LICENSE` 为准。

---

## 反馈

问题反馈与功能建议请提交至：<https://github.com/LaterChip/LaterCE/issues>

提交问题时请附上：

- Later CE 版本（当前为 2.0.0）
- Windows 版本
- 日志文件 `%APPDATA%\LaterCE\logs\laterce-YYYYMMDD.log` 的相关片段（请先移除账号令牌等敏感信息）
*（内容由AI生成，仅供参考）*
