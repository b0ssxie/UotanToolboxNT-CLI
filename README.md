<div align="center">

# UotanToolbox CLI

### 柚坛工具箱 · 命令行版 | Android & OpenHarmony 设备管理

在终端里完成刷机、解锁、应用管理、文件管理、投屏、固件解包等全部操作，
无需图形界面，适合脚本化、批量化和远程操作。

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/platform-Windows-blue)](#)
[![License](https://img.shields.io/badge/license-GPL--3.0-green)](LICENSE)
[![GitHub](https://img.shields.io/badge/github-UotanToolboxNT--CLI-181717?logo=github)](https://github.com/b0ssxie/UotanToolboxNT-CLI)

</div>

---

## 简介

UotanToolbox CLI 是 [柚坛工具箱 NT](https://github.com/Uotan-Dev/UotanToolboxNT) 的命令行版本。
它复用原项目全部核心逻辑（设备管理、固件解包、Magisk 修补、刷机），
把图形界面操作变成一个个终端命令，例如：

```powershell
utoolbox devices                      # 查看已连接设备
utoolbox info                         # 查看设备详情
utoolbox reboot recovery              # 重启到 Recovery
utoolbox app install app.apk          # 安装应用
utoolbox file push boot.img /sdcard   # 上传文件
utoolbox scrcpy screenshot            # 截屏
```

## 特性

- 🚀 **命令即操作**：全部功能通过 `utoolbox <命令>` 完成，可脚本化、可批处理
- 📱 **Android & OpenHarmony**：同时支持 adb 与 hdc 设备
- 🧩 **固件工具箱**：payload 解包、分区提取、Magisk/KernelSU 修补
- 🔐 **刷机全套**：单分区刷入、解锁/上锁、TXT 双包刷机（完整 VAB 流程）
- 🧰 **日常管理**：应用、文件、显示/电池、投屏、无线 ADB、分区、QCN
- 🤖 **AI 友好**：内置 [opencode](https://opencode.ai) MCP server 与 Skill，可直接让 AI 操作设备

## 快速开始

### 获取 utoolbox

1. 从 [Releases](https://github.com/b0ssxie/UotanToolboxNT-CLI/releases) 下载最新版
2. 解压后把 `utoolbox.exe` 所在目录加入 PATH，或直接使用完整路径
3. 二进制资源（adb / fastboot / magiskboot 等）已在发布包中，开箱即用

> 也可以自行构建，见[构建](#构建)。

### 连接设备

```powershell
utoolbox devices          # 确认设备在线
utoolbox info             # 查看设备信息
```

- Android 设备：开启「USB 调试」后连接，或先用 `utoolbox wireless connect <ip:端口>`
- MuMu 模拟器：`utoolbox adb connect 127.0.0.1:16384`
- 刷机操作需将设备重启到 Fastboot：`utoolbox reboot bootloader`

## 命令速查

| 类别 | 常用命令 |
|---|---|
| **设备** | `devices` 列出设备 · `info` 查看详情 |
| **重启** | `reboot system / recovery / bootloader / fastboot / edl / poweroff` |
| **解包** | `payload-parts <payload.bin>` · `extract <payload.bin> [-o 目录] [分区...]` |
| **Root** | `patch-boot <boot.img> --zip <Magisk包>` |
| **刷机** | `flash <分区> <镜像>` · `unlock` · `lock` · `flash-all --fastboot <txt> --fastbootd <txt>` |
| **应用** | `app list / install / run / disable / enable / uninstall / extract / clear / stop` |
| **文件** | `file ls / push / pull / chmod / rm / mkdir / touch / mv / cp` |
| **显示** | `display info / size / density / reset / battery ... / lock-time / scale` |
| **投屏** | `scrcpy start / key / screenshot` |
| **分区** | `partition show / rm / mkpart / esp / resize-table` |
| **QCN** | `qcn backup / write / diag-901d / diag-9091` |
| **无线** | `wireless pair / connect / tcpip` |
| **通用** | `adb <参数>` · `exec <设备ID> <命令>` |

完整命令与参数说明见 [命令一览](#命令一览)。

---

## 安装

### 方式一：下载发布包（推荐）

前往 [Releases](https://github.com/b0ssxie/UotanToolboxNT-CLI/releases) 下载 `utoolbox-win-x64`，
解压后运行 `utoolbox.exe`。发布包已包含全部二进制资源（adb、fastboot、magiskboot、scrcpy 等）。

### 方式二：自行构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```powershell
# 还原并构建
dotnet build UotanToolbox.Cli.slnx

# 发布（Windows x64）
dotnet publish src\UotanToolbox.Cli\UotanToolbox.Cli.csproj -c Release -r win-x64 -o publish\utoolbox-win-x64
```

发布后还需把二进制资源放入发布目录：

- `Bin\`：adb / fastboot / hdc / magiskboot / 7za / file 等（来自 [UotanToolboxNT.Binary](https://github.com/Uotan-Dev/UotanToolboxNT.Binary)）
- `APK\`：Magisk 安装包
- `ZIP\`、`Image\`、`Push\`：刷机与设备端工具资源

> 最省事的方式：从 [柚坛工具箱 NT Release](https://github.com/Uotan-Dev/UotanToolboxNT/releases) 下载 Windows 版解压，
> 把其中的 `Bin`、`APK`、`Drive`、`Image`、`Push`、`ZIP` 目录复制到发布目录。

---

## 命令一览

### 设备

| 命令 | 说明 |
|---|---|
| `utoolbox devices` | 列出所有已连接设备（adb / fastboot / hdc / EDL） |
| `utoolbox info` | 显示设备详细信息（系统版本、电池、存储等） |

### 重启

| 命令 | 说明 |
|---|---|
| `utoolbox reboot system` | 重启到系统 |
| `utoolbox reboot recovery` | 重启到 Recovery |
| `utoolbox reboot bootloader` | 重启到 Bootloader |
| `utoolbox reboot fastboot` | 重启到 Fastboot |
| `utoolbox reboot edl` | 重启到 EDL（9008） |
| `utoolbox reboot poweroff` | 关机 |

### 固件解包

| 命令 | 说明 |
|---|---|
| `utoolbox payload-parts <payload.bin>` | 列出 payload 中的分区 |
| `utoolbox extract <payload.bin> [-o 目录] [分区名...]` | 提取分区（不带分区名则全部） |
| `utoolbox firmware detect <文件>` | 识别固件类型（super/ntpi/nb0/ozip/ops/ofp） |
| `utoolbox firmware parts <文件>` | 列出固件分区 |
| `utoolbox firmware extract <文件> [-o 目录] [分区...]` | 提取固件分区 |
| `utoolbox firmware extract-url <url> [-o 目录] [分区...]` | 在线解包 payload URL |

### Root 修补

| 命令 | 说明 |
|---|---|
| `utoolbox patch-boot <boot.img> --zip <Magisk/GKI/LKM包> [-o 输出]` | 用 Magisk / KernelSU 修补 boot 镜像 |

### 刷机（需进入 Fastboot 模式）

| 命令 | 说明 |
|---|---|
| `utoolbox flash <分区> <镜像>` | 刷入单个分区（如 `flash boot boot-patched.img`） |
| `utoolbox erase <分区>` | 擦除分区 |
| `utoolbox wipe-super` | 刷入 super_empty |
| `utoolbox set-active <a\|b\|other>` | 切换活动槽位 |
| `utoolbox unlock [--file 文件] [--code 解锁码]` | 解锁 Bootloader |
| `utoolbox lock` | 上锁 Bootloader |
| `utoolbox flash-all --fastboot <txt> --fastbootd <txt> [选项]` | TXT 双包刷机（完整 VAB 流程） |

`flash-all` 选项：

| 选项 | 说明 |
|---|---|
| `--skip-model` | 跳过机型校验 |
| `--add-root` | 对 boot 分区注入 Root（Magisk/KSU，需 APK\Magisk 包） |
| `--disable-vbmeta` | 刷 vbmeta 时附带禁用参数 |
| `--erase-data` | 完成后擦除 metadata 和 userdata |
| `--fastboot <txt>` | fastboot 阶段脚本路径 |
| `--fastbootd <txt>` | fastbootd 阶段脚本路径 |

示例：
```
utoolbox flash-all --fastboot C:\fw\fastboot.txt --fastbootd C:\fw\fastbootd.txt
utoolbox flash-all --fastboot C:\fw\fastboot.txt --fastbootd C:\fw\fastbootd.txt --erase-data
```

刷机脚本格式与 GUI 版一致：首行可为 `codename:<机型>` 用于机型校验，
其余行每行一个分区名（自动拼接 `images/分区.img`），`分区 路径` 形式可指定镜像，
`分区 create 分区` 标记创建逻辑分区。

### 刷机恢复工具

| 命令 | 说明 |
|---|---|
| `utoolbox install-magisk <apk> [--twrp\|--sideload]` | 刷入 Magisk / APK 到设备 |
| `utoolbox disable-autorecovery [--twrp\|--sideload]` | 关闭自动还原 Recovery |
| `utoolbox sync-ab [--twrp\|--sideload]` | 同步 A/B 分区 |
| `utoolbox advanced-reboot <模式>` | 高级重启（twrp-sideload / sideload / autodloader / zygote / safe-mode / muc / factory / admin） |

> 设备处于 Android 系统时 `install-magisk` 会推送到 `/sdcard/magisk.apk` 手动安装；
> Recovery 模式下用 `--twrp`（TWRP 安装）或 `--sideload`（ADB Sideload）自动安装。

### 格式化与备份

| 命令 | 说明 |
|---|---|
| `utoolbox format <分区名> [--fs ext4\|f2fs\|fat32\|exfat\|ntfs]` | 格式化分区（Recovery） |
| `utoolbox format format-fastboot <分区名>` | Fastboot 方式擦除分区 |
| `utoolbox format wipe-data` | 清 data（`recovery --wipe_data`） |
| `utoolbox format twrp-wipe-data` | 清 data（`twrp format data`） |
| `utoolbox format extract-part <分区名> [-o 目录] [--mode ...]` | 提取物理分区镜像 |
| `utoolbox format extract-vpart <分区名> [-o 目录] [--mode ...]` | 提取逻辑分区（mapper）镜像 |
| `utoolbox format full-backup [-o 目录] [--mode ...]` | 全量备份（所有分区 + 生成刷机脚本） |

`--mode` 可选：`recovery`（默认）/ `root`（Android + su）/ `debug`（Android + adb root）。

`full-backup` 会遍历 9 个磁盘分区表，逐个备份分区镜像（跳过 userdata），
备份特殊分区（spl / preloader 等），并生成 `flashall_fastboot.txt` 刷机清单。

### 应用管理

| 命令 | 说明 |
|---|---|
| `utoolbox app list` | 列出已安装应用 |
| `utoolbox app install <apk>` | 安装 APK |
| `utoolbox app run <包名>` | 运行应用 |
| `utoolbox app disable/enable <包名>` | 停用 / 启用应用 |
| `utoolbox app uninstall <包名> [--keep]` | 卸载（--keep 保留数据） |
| `utoolbox app extract <包名> [-o 目录]` | 提取安装包 |
| `utoolbox app clear <包名>` | 清除应用数据 |
| `utoolbox app stop <包名>` | 强制停止 |

### 文件管理

| 命令 | 说明 |
|---|---|
| `utoolbox file ls <路径>` | 列出目录 |
| `utoolbox file push <本地> <设备路径>` | 上传文件 |
| `utoolbox file pull <设备路径> <本地目录>` | 下载文件 |
| `utoolbox file chmod <模式> <路径>` | 修改权限 |
| `utoolbox file rm <路径> [-r]` | 删除 |
| `utoolbox file mkdir <路径>` | 新建目录 |
| `utoolbox file touch <路径>` | 新建文件 |
| `utoolbox file mv <源> <目标>` | 移动 / 重命名 |
| `utoolbox file cp <源> <目标> [-r]` | 复制 |

### 显示与电池（Android）

| 命令 | 说明 |
|---|---|
| `utoolbox display info` | 当前显示信息 |
| `utoolbox display size <宽x高>` | 修改分辨率 |
| `utoolbox display density <dpi> [--dp]` | 修改 DPI（--dp 表示输入 DP 值） |
| `utoolbox display reset` | 恢复默认显示 |
| `utoolbox display battery set-temp <℃>` | 修改电池温度 |
| `utoolbox display battery set-level <电量>` | 修改电池电量 |
| `utoolbox display battery reset` | 重置电池 |
| `utoolbox display battery no-charge` | 禁止充电 |
| `utoolbox display lock-time <秒>` | 锁屏超时 |
| `utoolbox display scale font\|window\|transition\|anim <倍数>` | 缩放 |

### 投屏（scrcpy）

| 命令 | 说明 |
|---|---|
| `utoolbox scrcpy start [参数...]` | 启动投屏（参数直传 scrcpy，如 `--record 1.mp4`） |
| `utoolbox scrcpy key <back\|home\|recent\|lock\|volup\|voldown\|mute>` | 发送按键 |
| `utoolbox scrcpy screenshot` | 截图 |

### 分区管理

| 命令 | 说明 |
|---|---|
| `utoolbox partition show` | 读取分区表 |
| `utoolbox partition rm <盘> <分区号>` | 删除分区 |
| `utoolbox partition mkpart <盘> <名称> <fs> <起点> <终点>` | 创建分区 |
| `utoolbox partition esp <盘> <分区号>` | 设置 ESP 标志 |
| `utoolbox partition resize-table <盘>` | 扩展分区表到 128 |

### QCN（Qualcomm，Windows）

| 命令 | 说明 |
|---|---|
| `utoolbox qcn backup <com口>` | 备份 QCN |
| `utoolbox qcn write <com口> <文件>` | 写入 QCN |
| `utoolbox qcn diag-901d` | 开启 901D 诊断口 |
| `utoolbox qcn diag-9091` | 开启 9091 诊断口（小米） |

### 无线调试

| 命令 | 说明 |
|---|---|
| `utoolbox wireless pair <ip:端口> <配对码>` | 无线配对 |
| `utoolbox wireless connect <ip:端口>` | 连接设备 |
| `utoolbox wireless tcpip <端口>` | 开启无线调试（默认 5555） |

### 系统杂项

| 命令 | 说明 |
|---|---|
| `utoolbox xda [--off]` | 破解 X（配置国内网络检测 / 时间 / NTP） |
| `utoolbox statusbar <项,...>` | 状态栏图标黑名单（如 wifi,bluetooth,nfc） |
| `utoolbox clock-seconds [--off]` | 状态栏显示秒 |
| `utoolbox rotation-suggest [--off]` | 旋转建议 |
| `utoolbox active-app <辅助应用>` | 激活辅助应用（Shizuku/Dhizuku/Brevent/IceBox/Greenify/StopApp/PermissionDog） |
| `utoolbox system-version` | 检查版本 |

### 通用

| 命令 | 说明 |
|---|---|
| `utoolbox adb <adb参数...>` | 直接执行 adb 命令 |
| `utoolbox exec <设备ID> <命令>` | 对指定设备执行命令 |

---

## 与 AI 一起用（opencode 集成）

本项目内置 opencode MCP server 与 Skill，可以直接用对话操作设备：

- **MCP server**：`.opencode/mcp/utoolbox-mcp.mjs`，暴露 19 个工具
  （devices / info / reboot / flash / app / file / scrcpy 等）
- **Skill**：`.opencode/skills/utoolbox/SKILL.md`，指导 AI 何时用、怎么用
- **配置**：`opencode.json` 注册 MCP server 与 skill 路径

启用步骤：

1. 确认 `utoolbox.exe` 存在，或在 `opencode.json` 中把 `UTOOLBOX_BIN` 指向实际路径
2. 重启 opencode 使配置生效
3. 直接对话，例如：
   - "看看有哪些设备" → 调用 `devices`
   - "帮我重启到 recovery" → 调用 `reboot`
   - "把 app.apk 装到手机上" → 调用 `app install`
   - "帮我刷 boot 镜像" → 调用 `flash`

### 项目结构

```
src/
├── UotanToolbox.Core/   核心逻辑类库（设备管理、解包、修补、adb 封装）
└── UotanToolbox.Cli/    命令行入口与子命令

.opencode/
├── mcp/utoolbox-mcp.mjs        MCP server
└── skills/utoolbox/SKILL.md    Skill
```

## 致谢

- [柚坛工具箱 NT](https://github.com/Uotan-Dev/UotanToolboxNT) — 核心逻辑来源
- [UotanToolboxNT.Binary](https://github.com/Uotan-Dev/UotanToolboxNT.Binary) — 二进制资源
- 本项目基于 [GPL-3.0](LICENSE) 协议开源

## 注意事项

- 刷机、解锁、格式化、分区操作有风险，请先备份数据并确认理解后再执行
- 部分功能依赖对应设备状态（如解锁需 Fastboot、QCN 需 901D/9091 端口）
- 反馈问题请提交 [Issue](https://github.com/b0ssxie/UotanToolboxNT-CLI/issues)
