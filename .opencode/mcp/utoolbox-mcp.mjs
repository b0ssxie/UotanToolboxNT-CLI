#!/usr/bin/env node
/**
 * UotanToolbox MCP Server
 *
 * 把 utoolbox CLI 暴露为 MCP 工具，让 AI 能直接操作 Android/OpenHarmony 设备。
 *
 * 手写 MCP stdio 协议（JSON-RPC 2.0 over stdio），无第三方依赖。
 * 用 node 运行:  node .opencode/mcp/utoolbox-mcp.mjs
 */

import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import path from "node:path";
import fs from "node:fs";

// ---------- 工具路径解析 ----------
// 优先级: 环境变量 UTOOLBOX_BIN > 相对脚本位置查找
function findUtoolbox() {
  if (process.env.UTOOLBOX_BIN && fs.existsSync(process.env.UTOOLBOX_BIN)) {
    return process.env.UTOOLBOX_BIN;
  }
  const candidates = [
    path.resolve(import.meta.dirname, "..", "..", "publish", "utoolbox-win-x64", "utoolbox.exe"),
    path.resolve(import.meta.dirname, "..", "..", "publish", "utoolbox-win-x64", "utoolbox"),
    path.resolve(import.meta.dirname, "..", "..", "src", "UotanToolbox.Cli", "bin", "Debug", "net10.0", "utoolbox.exe"),
  ];
  for (const c of candidates) {
    if (fs.existsSync(c)) return c;
  }
  // 最后尝试 PATH 中的 utoolbox
  return "utoolbox";
}

const UTOOLBOX = findUtoolbox();

// ---------- 工具定义 ----------
const tools = [
  {
    name: "devices",
    description:
      "列出所有已连接的设备（adb / fastboot / hdc / EDL）。返回设备 ID、传输类型和状态。在操作任何设备前先调用此工具确认设备在线。",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "info",
    description:
      "显示设备详细信息（系统版本、分辨率、电池、内存、存储、机型等）。可指定设备 ID，不指定则显示所有设备。",
    inputSchema: {
      type: "object",
      properties: {
        deviceId: { type: "string", description: "设备 ID（可选，从 devices 获取）" },
      },
    },
  },
  {
    name: "reboot",
    description:
      "重启设备到指定模式。mode: system | recovery | bootloader | fastboot | edl | poweroff。",
    inputSchema: {
      type: "object",
      properties: {
        mode: {
          type: "string",
          enum: ["system", "recovery", "bootloader", "fastboot", "edl", "poweroff"],
          description: "重启目标模式",
          default: "system",
        },
      },
      required: [],
    },
  },
  {
    name: "adb",
    description:
      "直接执行 adb 命令（如 'shell getprop ro.product.device'、'tcpip 5555'）。注意不要重复加 adb 前缀。",
    inputSchema: {
      type: "object",
      properties: {
        args: { type: "string", description: "adb 参数，如: shell getprop ro.build.version.release" },
      },
      required: ["args"],
    },
  },
  {
    name: "flash",
    description: "刷入单个分区镜像。需要设备处于 Fastboot 模式。例: flash boot boot-patched.img",
    inputSchema: {
      type: "object",
      properties: {
        partition: { type: "string", description: "分区名，如 boot、recovery、system、vbmeta" },
        image: { type: "string", description: "镜像文件路径" },
        deviceId: { type: "string", description: "fastboot 设备 ID（可选）" },
      },
      required: ["partition", "image"],
    },
  },
  {
    name: "flash_all",
    description:
      "TXT 双包刷机（完整 VAB 流程）。需要设备处于 Fastboot 模式。选项: --fastboot <txt> --fastbootd <txt> [--skip-model] [--add-root] [--disable-vbmeta] [--erase-data]",
    inputSchema: {
      type: "object",
      properties: {
        fastboot: { type: "string", description: "fastboot.txt 脚本路径" },
        fastbootd: { type: "string", description: "fastbootd.txt 脚本路径" },
        skipModel: { type: "boolean", description: "跳过机型校验" },
        addRoot: { type: "boolean", description: "boot 分区注入 Root" },
        disableVbmeta: { type: "boolean", description: "刷 vbmeta 时禁用验证" },
        eraseData: { type: "boolean", description: "完成后擦除数据" },
      },
      required: [],
    },
  },
  {
    name: "unlock",
    description:
      "解锁 Bootloader。需要 Fastboot 模式。可选 --file 解锁文件 或 --code 解锁码。",
    inputSchema: {
      type: "object",
      properties: {
        file: { type: "string", description: "解锁文件路径" },
        code: { type: "string", description: "解锁码" },
      },
      required: [],
    },
  },
  {
    name: "app",
    description:
      "应用管理。子命令: list | install <apk> | run <pkg> | disable <pkg> | enable <pkg> | uninstall <pkg> [--keep] | extract <pkg> [-o 目录] | clear <pkg> | stop <pkg>",
    inputSchema: {
      type: "object",
      properties: {
        subcommand: {
          type: "string",
          enum: ["list", "install", "run", "disable", "enable", "uninstall", "extract", "clear", "stop"],
          description: "操作类型",
        },
        args: { type: "array", items: { type: "string" }, description: "子命令参数" },
      },
      required: ["subcommand"],
    },
  },
  {
    name: "file",
    description:
      "文件管理。子命令: ls <路径> | push <本地> <设备路径> | pull <设备路径> <本地目录> | chmod <模式> <路径> | rm <路径> [-r] | mkdir <路径> | touch <路径> | mv <源> <目标> | cp <源> <目标> [-r]",
    inputSchema: {
      type: "object",
      properties: {
        subcommand: {
          type: "string",
          enum: ["ls", "push", "pull", "chmod", "rm", "mkdir", "touch", "mv", "cp"],
          description: "操作类型",
        },
        args: { type: "array", items: { type: "string" }, description: "子命令参数" },
      },
      required: ["subcommand"],
    },
  },
  {
    name: "display",
    description:
      "显示/电池设置 (Android)。子命令: info | size <宽x高> | density <dpi> [--dp] | reset | battery set-temp <℃> | battery set-level <电量> | battery reset | battery no-charge | lock-time <秒> | scale font|window|transition|anim <倍数>",
    inputSchema: {
      type: "object",
      properties: {
        subcommand: { type: "string", description: "display 子命令及参数，如: battery set-level 80" },
      },
      required: ["subcommand"],
    },
  },
  {
    name: "scrcpy",
    description:
      "投屏 (scrcpy)。子命令: start [scrcpy参数...] | key <back|home|recent|lock|volup|voldown|mute> | screenshot",
    inputSchema: {
      type: "object",
      properties: {
        subcommand: { type: "string", description: "scrcpy 子命令及参数" },
      },
      required: ["subcommand"],
    },
  },
  {
    name: "wireless",
    description:
      "无线 ADB。子命令: pair <ip:端口> <配对码> | connect <ip:端口> | tcpip <端口>",
    inputSchema: {
      type: "object",
      properties: {
        subcommand: { type: "string", description: "wireless 子命令及参数" },
      },
      required: ["subcommand"],
    },
  },
  {
    name: "partition",
    description:
      "分区管理。子命令: show | rm <盘> <分区号> | mkpart <盘> <名称> <fs> <起点> <终点> | esp <盘> <分区号> | resize-table <盘>",
    inputSchema: {
      type: "object",
      properties: {
        subcommand: { type: "string", description: "partition 子命令及参数" },
      },
      required: ["subcommand"],
    },
  },
  {
    name: "payload_parts",
    description: "列出 payload.bin 固件中的分区。",
    inputSchema: {
      type: "object",
      properties: {
        file: { type: "string", description: "payload.bin 或含 payload 的 zip 路径" },
      },
      required: ["file"],
    },
  },
  {
    name: "extract",
    description: "从 payload.bin 提取分区镜像。例: extract <file> [-o 输出目录] [分区名...]",
    inputSchema: {
      type: "object",
      properties: {
        file: { type: "string", description: "payload.bin 或含 payload 的 zip 路径" },
        outputDir: { type: "string", description: "输出目录（可选，默认当前目录）" },
        partitions: { type: "array", items: { type: "string" }, description: "要提取的分区名列表（可选，默认全部）" },
      },
      required: ["file"],
    },
  },
  {
    name: "patch_boot",
    description:
      "用 Magisk/GKI/LKM 修补 boot 镜像。可用本地包 (zip=<路径>) 或自动下载最新版 (auto=magisk|kernelsu, 可选 mirror=镜像 加速)。例: patch_boot <boot.img> zip=<Magisk包>  或  patch_boot <boot.img> auto=magisk mirror=https://ghfast.top/",
    inputSchema: {
      type: "object",
      properties: {
        boot: { type: "string", description: "boot.img 路径" },
        zip: { type: "string", description: "Magisk/GKI/LKM 包路径（与 auto 二选一）" },
        auto: { type: "string", enum: ["magisk", "kernelsu", "ksu", "kernelsu-lkm"], description: "自动从 GitHub 下载最新 Root 方案" },
        mirror: { type: "string", description: "GitHub 镜像加速前缀（可选，如 https://ghfast.top/）" },
        part: { type: "string", description: "分区（可选，配合 auto）" },
        output: { type: "string", description: "输出文件路径（可选）" },
      },
      required: ["boot", "zip"],
    },
  },
  {
    name: "patch_rom",
    description:
      "从刷机包（payload/super/zip固件等）提取 boot/init_boot/vendor_boot 分区并自动用 Magisk/GKI/LKM 修补 Root。可用本地包 zip= 或 auto=magisk|kernelsu 自动下载（可选 mirror=镜像）。例: patch_rom <刷机包> zip=<Root包> 或  patch_rom <刷机包> auto=magisk mirror=https://ghfast.top/",
    inputSchema: {
      type: "object",
      properties: {
        rom: { type: "string", description: "刷机包路径（payload/super.img/.ntpi/.nb0/.ozip/.ops/.ofp/含img的zip）" },
        zip: { type: "string", description: "Magisk.apk 或 KernelSU zip 路径（与 auto 二选一）" },
        auto: { type: "string", enum: ["magisk", "kernelsu", "ksu", "kernelsu-lkm"], description: "自动从 GitHub 下载最新 Root" },
        mirror: { type: "string", description: "GitHub 镜像加速前缀（可选）" },
        kernel: { type: "string", description: "KernelSU 内核版本筛选（可选，如 android15-6.6）" },
        part: { type: "string", description: "要修补的分区（默认 boot，可选 init_boot/vendor_boot）" },
        outputDir: { type: "string", description: "输出目录（可选）" },
        listOnly: { type: "boolean", description: "只列出可修补分区（true 时 zip/part 可省略）" },
      },
      required: ["rom"],
    },
  },
  {
    name: "exec",
    description: "执行 utoolbox 尚未封装的原始命令。例: exec <设备ID> <adb命令>",
    inputSchema: {
      type: "object",
      properties: {
        deviceId: { type: "string", description: "设备 ID" },
        command: { type: "string", description: "要执行的 adb 命令" },
      },
      required: ["deviceId", "command"],
    },
  },
  {
    name: "raw",
    description:
      "执行任意 utoolbox 命令，参数原样透传。用于调用未单独封装的命令（如 qcn、set-active、wipe-super、erase 等）。例: raw erase userdata",
    inputSchema: {
      type: "object",
      properties: {
        command: { type: "string", description: "完整 utoolbox 命令及参数，如 'erase userdata' 或 'qcn backup COM3'" },
      },
      required: ["command"],
    },
  },
];

// ---------- 命令执行 ----------
function runUtoolbox(args, timeoutMs = 120000) {
  return new Promise((resolve) => {
    const child = spawn(UTOOLBOX, args, { windowsHide: true, encoding: "utf8" });
    let stdout = "";
    let stderr = "";
    const timer = setTimeout(() => {
      child.kill();
      resolve({ stdout, stderr, exitCode: 124, timedOut: true });
    }, timeoutMs);

    child.stdout.on("data", (d) => { stdout += d.toString(); });
    child.stderr.on("data", (d) => { stderr += d.toString(); });
    child.on("error", (err) => {
      clearTimeout(timer);
      resolve({ stdout, stderr: "启动失败: " + err.message, exitCode: 1, timedOut: false });
    });
    child.on("close", (code) => {
      clearTimeout(timer);
      resolve({ stdout, stderr, exitCode: code ?? -1, timedOut: false });
    });
  });
}

function buildArgs(name, input) {
  switch (name) {
    case "devices": return ["devices"];
    case "info": return input.deviceId ? ["info", "--device", String(input.deviceId)] : ["info"];
    case "reboot": return ["reboot", input.mode || "system"];
    case "adb": return ["adb", ...String(input.args).split(" ")];
    case "flash": {
      const a = ["flash", String(input.partition), String(input.image)];
      if (input.deviceId) a.push("--device", String(input.deviceId));
      return a;
    }
    case "flash_all": {
      const a = ["flash-all"];
      if (input.fastboot) a.push("--fastboot", String(input.fastboot));
      if (input.fastbootd) a.push("--fastbootd", String(input.fastbootd));
      if (input.skipModel) a.push("--skip-model");
      if (input.addRoot) a.push("--add-root");
      if (input.disableVbmeta) a.push("--disable-vbmeta");
      if (input.eraseData) a.push("--erase-data");
      return a;
    }
    case "unlock": {
      const a = ["unlock"];
      if (input.file) a.push("--file", String(input.file));
      if (input.code) a.push("--code", String(input.code));
      return a;
    }
    case "app": {
      const args = [String(input.subcommand)];
      for (const x of input.args ?? []) args.push(String(x));
      return ["app", ...args];
    }
    case "file": {
      const args = [String(input.subcommand)];
      for (const x of input.args ?? []) args.push(String(x));
      return ["file", ...args];
    }
    case "display": return ["display", ...String(input.subcommand).split(" ")];
    case "scrcpy": return ["scrcpy", ...String(input.subcommand).split(" ")];
    case "wireless": return ["wireless", ...String(input.subcommand).split(" ")];
    case "partition": return ["partition", ...String(input.subcommand).split(" ")];
    case "payload_parts": return ["payload-parts", String(input.file)];
    case "extract": {
      const a = ["extract", String(input.file)];
      if (input.outputDir) a.push("-o", String(input.outputDir));
      for (const p of input.partitions ?? []) a.push(String(p));
      return a;
    }
    case "patch_boot": {
      const a = ["patch-boot", String(input.boot)];
      if (input.auto) a.push("--auto", String(input.auto));
      else if (input.zip) a.push("--zip", String(input.zip));
      if (input.mirror) a.push("--mirror", String(input.mirror));
      if (input.part) a.push("--kernel", String(input.part));
      if (input.output) a.push("-o", String(input.output));
      return a;
    }
    case "patch_rom": {
      const a = ["patch-rom", String(input.rom)];
      if (input.auto) a.push("--auto", String(input.auto));
      else if (input.zip) a.push("--zip", String(input.zip));
      if (input.mirror) a.push("--mirror", String(input.mirror));
      if (input.kernel) a.push("--kernel", String(input.kernel));
      if (input.part) a.push("--part", String(input.part));
      if (input.outputDir) a.push("-o", String(input.outputDir));
      if (input.listOnly) a.push("--list");
      return a;
    }
    case "exec": return ["exec", String(input.deviceId), String(input.command)];
    case "raw": return String(input.command).split(" ").filter((s) => s.length > 0);
    default: return [];
  }
}

// ---------- MCP stdio 协议 ----------
const rl = createInterface({ input: process.stdin, terminal: false });

function send(message) {
  process.stdout.write(JSON.stringify(message) + "\n");
}

async function handleMessage(line) {
  let msg;
  try {
    msg = JSON.parse(line);
  } catch {
    return;
  }
  const { id, method, params } = msg;

  // 通知类消息无 id，不回复
  if (!id) return;

  if (method === "initialize") {
    return send({
      jsonrpc: "2.0",
      id,
      result: {
        protocolVersion: "2024-11-05",
        capabilities: { tools: { listChanged: false } },
        serverInfo: { name: "utoolbox", version: "0.1.0" },
      },
    });
  }

  if (method === "tools/list") {
    return send({ jsonrpc: "2.0", id, result: { tools } });
  }

  if (method === "tools/call") {
    const { name, arguments: input } = params;
    const tool = tools.find((t) => t.name === name);
    if (!tool) {
      return send({
        jsonrpc: "2.0",
        id,
        result: { content: [{ type: "text", text: `未知工具: ${name}` }], isError: true },
      });
    }
    const args = buildArgs(name, input ?? {});
    const { stdout, stderr, exitCode, timedOut } = await runUtoolbox(args);
    const text = [stdout.trim(), stderr.trim()].filter((s) => s.length > 0).join("\n");
    return send({
      jsonrpc: "2.0",
      id,
      result: {
        content: [{ type: "text", text: text || "(无输出)" }],
        isError: exitCode !== 0 || timedOut,
      },
    });
  }

  if (method === "ping") {
    return send({ jsonrpc: "2.0", id, result: {} });
  }

  return send({ jsonrpc: "2.0", id, result: {} });
}

rl.on("line", (line) => {
  if (line.trim()) handleMessage(line);
});
