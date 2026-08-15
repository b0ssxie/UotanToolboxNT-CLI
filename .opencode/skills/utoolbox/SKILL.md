---
name: utoolbox
description: Use when the user wants to manage Android or OpenHarmony devices via the UotanToolbox CLI — checking connected devices, reading device info, rebooting, flashing partitions, unlocking the bootloader, installing/managing apps, managing files, adjusting display/battery settings, screen mirroring with scrcpy, wireless ADB, partition editing, or extracting/patching firmware images. Trigger keywords: adb, fastboot, 刷机, 刷入, 解锁, 重启, 设备, device, flash, reboot, unlock, scrcpy, payload, magisk, muMu, 模拟器, root.
---

# UotanToolbox 设备管理

本技能帮助用户通过 **utoolbox CLI**（通过 MCP 工具暴露）管理连接的 Android / OpenHarmony 设备。

## 何时使用

- 用户提到设备管理、刷机、解锁、重启、应用管理、文件传输等操作
- 用户说出 adb / fastboot / hdc / scrcpy / Magisk / payload 等关键词
- 用户的 MuMu 模拟器或真机需要操作

## 工作流程

### 1. 先确认设备在线

对设备做任何操作前，**总是先调用 `devices` 工具**确认设备状态：

```
devices
```

- 返回空 → 提示用户连接设备 / 打开 USB 调试 / 检查 adb 连接
- 有设备 → 记下设备 ID（如 `emulator-5554`）用于后续操作

### 2. 根据需求选择工具

| 需求 | 工具 | 示例 |
|---|---|---|
| 查看设备详情 | `info` | `info` |
| 重启 | `reboot` | `reboot` mode=`recovery` |
| 执行 adb 命令 | `adb` | `adb` args=`shell getprop ro.product.device` |
| 应用管理 | `app` | `app` subcommand=`list`；`app` subcommand=`install` args=[apk路径] |
| 文件管理 | `file` | `file` subcommand=`ls` args=[`/sdcard`] |
| 投屏/截图 | `scrcpy` | `scrcpy` subcommand=`screenshot` |
| 显示/电池 | `display` | `display` subcommand=`battery set-level 80` |
| 无线连接 | `wireless` | `wireless` subcommand=`connect 192.168.1.5:5555` |
| 固件分区列表 | `payload_parts` | `payload_parts` file=`C:\fw\payload.bin` |
| 提取固件分区 | `extract` | `extract` file=... partitions=[`boot`,`system`] |
| Magisk 修补 | `patch_boot` | `patch_boot` boot=`boot.img` zip=`Magisk.apk`；或 `patch_boot` boot=`boot.img` auto=`magisk` mirror=`https://ghfast.top/` 自动下载 |
| 从刷机包修补 | `patch_rom` | `patch_rom` rom=`payload.bin` zip=`Magisk.apk`；或 auto=`magisk` 自动下载；用 `listOnly`=true 查看可修补分区 |
| 单分区刷入 | `flash` | `flash` partition=`boot` image=`boot-patched.img` |
| TXT 双包刷机 | `flash_all` | `flash_all` fastboot=`a.txt` fastbootd=`b.txt` |
| 解锁 Bootloader | `unlock` | `unlock` |
| 未封装命令 | `raw` | `raw` command=`erase userdata` |

### 3. 常见命令参数速查

- **file**: `ls <路径>` / `push <本地> <设备>` / `pull <设备> <本地目录>` / `chmod <模式> <路径>` / `rm <路径> [-r]` / `mkdir` / `touch` / `mv` / `cp`
- **app**: `list` / `install <apk>` / `run <pkg>` / `disable|enable <pkg>` / `uninstall <pkg> [--keep]` / `extract <pkg>` / `clear <pkg>` / `stop <pkg>`
- **display**: `info` / `size <宽x高>` / `density <dpi> [--dp]` / `reset` / `battery set-temp|set-level|reset|no-charge` / `lock-time <秒>` / `scale font|window|transition|anim <倍数>`
- **partition**: `show` / `rm <盘> <分区号>` / `mkpart <盘> <名称> <fs> <起点> <终点>` / `esp <盘> <分区号>` / `resize-table <盘>`

## 安全注意事项（重要）

1. **危险操作先确认**：刷机（`flash`、`flash_all`）、解锁（`unlock`）、上锁（`raw lock`）、格式化（`raw erase`）、分区操作（`partition`）都会改变设备，**执行前向用户说明风险并确认**。
2. **刷机前提醒备份数据**。
3. **Fastboot 模式前提**：`flash`、`flash_all`、`unlock`、`erase`、`set-active`、`wipe-super` 需要设备处于 Fastboot 模式（用 `reboot` mode=`bootloader` 或 `fastboot` 进入）。
4. **路径用 Windows 反斜杠**：工具运行在 Windows，文件路径用 `C:\...` 或绝对路径，注意 JSON 中转义 `\\`。
5. **上锁会清数据**：`lock` 会清除设备数据，除非用户明确要求，否则不要执行。
6. **MuMu 模拟器**：MuMu 的 adb 端口通常是 `127.0.0.1:16384`，如果 `devices` 返回空，可尝试 `adb` args=`connect 127.0.0.1:16384`。

## 工具失败排查

- 提示"未发现设备" → 先运行 `devices`，必要时 `wireless` subcommand=`connect ...` 或让用户检查 USB 调试
- 提示"需要 Fastboot" → 用 `reboot` mode=`fastboot` 或 `bootloader` 让设备进入对应模式，稍等再试
- 输出乱码 → 不影响，工具输出是 UTF-8 文本
