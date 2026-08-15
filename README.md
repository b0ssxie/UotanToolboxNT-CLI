# UotanToolbox CLI

柚坛工具箱（UotanToolboxNT）的命令行版本。用命令行完成原 GUI 的全部设备操作。

## 环境要求

- Windows x64
- .NET 10 SDK（仅编译需要；发布版可直接运行）

## 快速开始

```
utoolbox help                查看全部命令
utoolbox devices             列出已连接设备
utoolbox info                查看设备详细信息
```

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
刷机脚本格式与 GUI 版一致：首行可为 `codename:<机型>` 用于机型校验，其余行每行一个分区名（自动拼接 `images/分区.img`），`分区 路径` 形式可指定镜像，`分区 create 分区` 标记创建逻辑分区。

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

### 通用
| 命令 | 说明 |
|---|---|
| `utoolbox adb <adb参数...>` | 直接执行 adb 命令 |
| `utoolbox exec <设备ID> <命令>` | 对指定设备执行命令 |

## 构建

```
dotnet build UotanToolbox.Cli.slnx
```

发布：
```
dotnet publish src\UotanToolbox.Cli\UotanToolbox.Cli.csproj -c Release -r win-x64 -o publish\utoolbox-win-x64
```

二进制资源（adb/fastboot/magiskboot 等）需放在发布目录下的 `Bin\` 中，
Magisk APK 放 `APK\`、脚本资源放 `ZIP\` 与 `Image\`。可从
[UotanToolboxNT.Binary](https://github.com/Uotan-Dev/UotanToolboxNT.Binary) 获取，
或直接下载 [UotanToolboxNT 发布包](https://github.com/Uotan-Dev/UotanToolboxNT/releases) 解压后复制对应目录。

## 项目结构

```
src/
├── UotanToolbox.Core/   核心逻辑类库（设备管理、解包、修补、adb 封装）
└── UotanToolbox.Cli/    命令行入口与子命令
```

## 注意事项

- 刷机、解锁、格式化、分区操作有风险，请先备份数据并确认理解后再执行。
- `--dry-run` 功能正在开发中；当前执行前会打印将要运行的命令。
- 部分功能依赖对应设备状态（如解锁需 Fastboot、QCN 需 901D/9091 端口）。
