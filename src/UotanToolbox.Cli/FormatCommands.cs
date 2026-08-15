using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

/// <summary>
/// 格式化与备份命令：格式化分区、提取分区镜像、全量备份、清 data。
/// </summary>
internal static class FormatCommands
{
    public static async Task<int> FormatAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                格式化与备份:
                  utoolbox format <分区名> [--fs ext4|f2fs|fat32|exfat|ntfs]   格式化分区（Recovery）
                  utoolbox format-fastboot <分区名>                             Fastboot 擦除分区
                  utoolbox wipe-data                                           清 data（recovery --wipe_data）
                  utoolbox twrp-wipe-data                                      清 data（twrp format data）
                  utoolbox extract-part <分区名> [-o 目录] [--mode recovery|root|debug]   提取分区镜像
                  utoolbox extract-vpart <分区名> [-o 目录] [--mode ...]       提取逻辑分区（mapper）
                  utoolbox full-backup [-o 目录] [--mode ...]   全量备份（所有分区 + 生成刷机脚本）

                模式:
                  --mode recovery    Recovery 模式（默认）
                  --mode root        Android + su root
                  --mode debug       Android + adb root (调试)
                """);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "format" => await FormatPartAsync(args.Skip(1).ToArray()),
            "format-fastboot" => await FormatFastbootAsync(args.Skip(1).ToArray()),
            "wipe-data" => await WipeDataAsync("recovery --wipe_data"),
            "twrp-wipe-data" => await WipeDataAsync("twrp format data"),
            "extract-part" => await ExtractPartAsync(args.Skip(1).ToArray()),
            "extract-vpart" => await ExtractVPartAsync(args.Skip(1).ToArray()),
            "full-backup" => await FullBackupAsync(args.Skip(1).ToArray()),
            _ => await FormatPartAsync(args), // 如 format boot，直接把剩余参数当分区名
        };
    }

    private static string FileSystemTool(string fs)
    {
        return fs.ToLowerInvariant() switch
        {
            "ext4" => "mke2fs -t ext4",
            "f2fs" => "/tmp/mkfs.f2fs",
            "fat32" => "mkfs.fat -F32 -s1",
            "exfat" => "mkexfatfs -n exfat",
            "ntfs" => "/tmp/mkntfs -f",
            _ => throw new ArgumentException($"不支持的文件系统: {fs}（可选 ext4|f2fs|fat32|exfat|ntfs）"),
        };
    }

    private static string ParseOption(string[] args, string key, string def)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == key && i + 1 < args.Length)
                return args[i + 1];
        }
        return def;
    }

    private static string BackupMode(string[] args) => ParseOption(args, "--mode", "recovery");

    /// <summary>
    /// 获取分区所在盘（FindDisk 的逻辑，遍历 9 张分区表）。
    /// </summary>
    private static string FindDiskName(string partname)
    {
        string[] tables = [Global.sdatable, Global.sdetable, Global.sdbtable, Global.sdctable, Global.sddtable, Global.sdftable, Global.sdgtable, Global.sdhtable, Global.emmcrom];
        string[] names = ["sda", "sde", "sdb", "sdc", "sdd", "sdf", "sdg", "sdh", "mmcblk0p"];
        for (int i = 0; i < tables.Length; i++)
        {
            if (tables[i].Contains(partname) && StringHelper.Partno(tables[i], partname) != null)
                return names[i];
        }
        return "";
    }

    /// <summary>
    /// 按模式执行 shell 命令（recovery 直接执行；root 用 su -c；debug 先 adb root）。
    /// </summary>
    private static async Task<string> ExecuteShellAsync(DeviceInfo device, string mode, string shellCmd)
    {
        return mode.ToLowerInvariant() switch
        {
            "root" => await FeaturesHelper.AdbCmd(device.Id, $"shell su -c \"{shellCmd}\""),
            "debug" => await FeaturesHelper.AdbCmd(device.Id, $"shell {shellCmd}"),
            _ => await FeaturesHelper.AdbCmd(device.Id, $"shell {shellCmd}"),
        };
    }

    /// <summary>
    /// 推送 parted 并按模式读取分区表。
    /// </summary>
    private static async Task GetPartTableAsync(DeviceInfo device, string mode)
    {
        string remotePath = mode.ToLowerInvariant() == "recovery" ? "/tmp/" : "/data/local/tmp/";
        string push = await FeaturesHelper.AdbCmd(device.Id, $"push \"{Path.Combine(Global.runpath, "Push", "parted")}\" {remotePath}");
        Console.WriteLine(push.Trim());
        string chmod = mode.ToLowerInvariant() == "root"
            ? await FeaturesHelper.AdbCmd(device.Id, $"shell su -c \"chmod +x {remotePath}parted\"")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell chmod +x {remotePath}parted");
        Console.WriteLine(chmod.Trim());

        string partedCmd = mode.ToLowerInvariant() == "root"
            ? $"su -c \"{remotePath}parted /dev/block/{{0}} print\""
            : $"{remotePath}parted /dev/block/{{0}} print";

        string[] disks = ["sda", "sdb", "sdc", "sdd", "sde", "sdf", "sdg", "sdh", "mmcblk0"];
        string[] tables = ["sda", "sdb", "sdc", "sdd", "sde", "sdf", "sdg", "sdh", "mmc"];
        var outputs = await Task.WhenAll(disks.Select(d =>
        {
            string cmd = string.Format(partedCmd, d);
            return ExecuteShellAsync(device, mode, cmd);
        }));
        Global.sdatable = outputs[0];
        Global.sdbtable = outputs[1];
        Global.sdctable = outputs[2];
        Global.sddtable = outputs[3];
        Global.sdetable = outputs[4];
        Global.sdftable = outputs[5];
        Global.sdgtable = outputs[6];
        Global.sdhtable = outputs[7];
        Global.emmcrom = outputs[8];
    }

    /// <summary>
    /// 格式化分区（ADB/Recovery 方式）。
    /// </summary>
    private static async Task<int> FormatPartAsync(string[] args)
    {
        string partname = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrEmpty(partname))
        {
            Console.Error.WriteLine("用法: utoolbox format <分区名> [--fs ext4|f2fs|fat32|exfat|ntfs]");
            return 1;
        }
        string fs = ParseOption(args, "--fs", "ext4");
        string tool;
        try
        {
            tool = FileSystemTool(fs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }

        await GetPartTableAsync(device, "recovery");
        string sdxx = FindDiskName(partname);
        if (sdxx == "")
        {
            Console.Error.WriteLine($"未找到分区 {partname}。");
            return 1;
        }
        string partnum = StringHelper.Partno(FeaturesHelper.FindPart(partname), partname);
        Console.WriteLine($"格式化 /dev/block/{sdxx}{partnum} 为 {fs} ...");
        string output = await ExecuteShellAsync(device, "recovery", $"{tool} /dev/block/{sdxx}{partnum}");
        Console.WriteLine(output);
        return 0;
    }

    /// <summary>
    /// Fastboot 方式格式化（擦除分区）。
    /// </summary>
    private static async Task<int> FormatFastbootAsync(string[] args)
    {
        string partname = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrEmpty(partname))
        {
            Console.Error.WriteLine("用法: utoolbox format-fastboot <分区名>");
            return 1;
        }
        await Global.DeviceManager!.ScanAsync();
        var fb = Global.DeviceManager.Devices.FirstOrDefault(d => d.Transport == TransportType.Fastboot);
        if (fb == null)
        {
            Console.Error.WriteLine("未发现 Fastboot 设备。");
            return 1;
        }
        Console.WriteLine($"擦除 {partname} ...");
        Console.WriteLine(await FeaturesHelper.FastbootCmd(fb.Id, $"erase {partname}"));
        return 0;
    }

    private static async Task<int> WipeDataAsync(string cmd)
    {
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }
        Console.WriteLine($"执行: {cmd}");
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell {cmd}"));
        return 0;
    }

    /// <summary>
    /// 提取物理分区镜像。
    /// </summary>
    private static async Task<int> ExtractPartAsync(string[] args)
    {
        string partname = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrEmpty(partname))
        {
            Console.Error.WriteLine("用法: utoolbox extract-part <分区名> [-o 目录] [--mode recovery|root|debug]");
            return 1;
        }
        string mode = BackupMode(args);
        string outputDir = ParseOption(args, "-o", Global.backup_path);
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
        {
            Console.Error.WriteLine($"备份目录不存在: {outputDir ?? "(未设置)"}。用 -o 指定目录。");
            return 1;
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }

        if (mode == "debug")
            await FeaturesHelper.AdbCmd(device.Id, "root");
        await GetPartTableAsync(device, mode);

        string sdxx = FindDiskName(partname);
        if (sdxx == "")
        {
            Console.Error.WriteLine($"未找到分区 {partname}。");
            return 1;
        }
        string partnum = StringHelper.Partno(FeaturesHelper.FindPart(partname), partname);
        string devicePath = $"/dev/block/{sdxx}{partnum}";

        // 根据模式确定写目录
        string targetDir = mode switch
        {
            "debug" or "root" => "/sdcard",
            _ => "",
        };
        string remoteImg = "/" + (targetDir.Length > 0 ? $"{targetDir}/" : "") + partname + ".img";

        // 如果根目录写了残留先清理
        if (mode == "recovery")
        {
            Console.WriteLine(await ExecuteShellAsync(device, mode, $"rm /{partname}.img"));
        }

        Console.WriteLine($"提取 {devicePath} -> {remoteImg} ...");
        string ddOut = await ExecuteShellAsync(device, mode, $"dd if={devicePath} of={remoteImg}");
        Console.WriteLine(ddOut.Trim());

        Console.WriteLine($"拉取到本地 {outputDir}");
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"pull \"{remoteImg}\" \"{outputDir}\""));
        Console.WriteLine(await ExecuteShellAsync(device, mode, $"rm {remoteImg}"));
        return 0;
    }

    /// <summary>
    /// 提取逻辑分区（mapper）镜像。
    /// </summary>
    private static async Task<int> ExtractVPartAsync(string[] args)
    {
        string partname = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrEmpty(partname))
        {
            Console.Error.WriteLine("用法: utoolbox extract-vpart <分区名> [-o 目录] [--mode recovery|root|debug]");
            return 1;
        }
        string mode = BackupMode(args);
        string outputDir = ParseOption(args, "-o", Global.backup_path);
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
        {
            Console.Error.WriteLine($"备份目录不存在: {outputDir ?? "(未设置)"}。用 -o 指定目录。");
            return 1;
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }

        if (mode == "debug")
            await FeaturesHelper.AdbCmd(device.Id, "root");

        // 解析 mapper 路径
        string lsOut = await ExecuteShellAsync(device, mode, $"ls -l /dev/block/mapper/{partname}");
        if (lsOut.Contains("No such file"))
        {
            Console.Error.WriteLine($"找不到逻辑分区 {partname}。");
            return 1;
        }
        var words = lsOut.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
        string devicepoint = words[^1];
        Console.WriteLine($"映射设备: {devicepoint}");

        string targetDir = mode == "recovery" ? "" : "/sdcard";
        string remoteImg = "/" + (targetDir.Length > 0 ? $"{targetDir}/" : "") + partname + ".img";
        if (mode == "recovery")
            await ExecuteShellAsync(device, mode, $"rm /{partname}.img");

        Console.WriteLine($"提取 {devicepoint} -> {remoteImg} ...");
        string ddOut = await ExecuteShellAsync(device, mode, $"dd if={devicepoint} of={remoteImg}");
        Console.WriteLine(ddOut.Trim());

        Console.WriteLine($"拉取到本地 {outputDir}");
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"pull \"{remoteImg}\" \"{outputDir}\""));
        Console.WriteLine(await ExecuteShellAsync(device, mode, $"rm {remoteImg}"));
        return 0;
    }

    /// <summary>
    /// 全量备份：遍历 9 盘分区表逐分区备份 + 生成刷机脚本。
    /// </summary>
    private static async Task<int> FullBackupAsync(string[] args)
    {
        string mode = BackupMode(args);
        string outputDir = ParseOption(args, "-o", Global.backup_path);
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
        {
            Console.Error.WriteLine($"备份目录不存在: {outputDir ?? "(未设置)"}。用 -o 指定目录。");
            return 1;
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }

        if (mode == "debug")
            await FeaturesHelper.AdbCmd(device.Id, "root");
        await GetPartTableAsync(device, mode);

        string backupName = $"UotanToolbox_FullBackup_{DateTime.Now:yyyyMMddHHmmss}";
        string backupFolder = Path.Combine(outputDir, backupName);
        string imagesFolder = Path.Combine(backupFolder, "images");
        Directory.CreateDirectory(imagesFolder);
        Console.WriteLine($"备份目录: {backupFolder}");

        string[] diskTables = [Global.sdatable, Global.sdetable, Global.sdbtable, Global.sdctable, Global.sddtable, Global.sdftable, Global.sdgtable, Global.sdhtable, Global.emmcrom];
        string[] diskNames = ["sda", "sde", "sdb", "sdc", "sdd", "sdf", "sdg", "sdh", "mmcblk0p"];
        var partNames = new List<string>();
        int desk = 0;

        for (int i = 0; i < diskTables.Length; i++)
        {
            string[] parts = diskTables[i].Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 6) { desk++; continue; }
            for (int n = 6; n < parts.Length; n++)
            {
                string[] items = StringHelper.Items(parts[n].ToCharArray());
                if (items.Length < 6) continue;
                string partName = items[5];
                if (partName == "userdata") continue;

                // 重名处理
                string finalName = partNames.Contains(partName) ? $"{partName}_{diskNames[i]}" : partName;
                string diskSrc = $"/dev/block/{diskNames[i]}{items[0]}";

                Console.WriteLine($"备份 {diskSrc} ({partName}) ...");
                string ddOut = await ExecuteShellAsync(device, mode, $"dd if={diskSrc} of=/sdcard/{finalName}.img");
                if (ddOut.Contains("No space left") || ddOut.Contains("not found") || ddOut.Contains("unknown command"))
                {
                    Console.Error.WriteLine($"备份 {partName} 失败，终止。");
                    return 1;
                }
                Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"pull /sdcard/{finalName}.img \"{imagesFolder}\""));
                await ExecuteShellAsync(device, mode, $"rm /sdcard/{finalName}.img");

                // 构造刷机脚本条目
                partNames.Add(finalName);
                if (finalName != partName)
                    partNames.Add($"{partName}       /images/{finalName}.img");
                else
                    partNames.Add(partName);
            }
        }

        // 特殊分区（spl / preloader 等 by-name）
        string[] specialParts = ["spl", "spl_a", "spl_b", "preloader_raw", "perloader_raw_a", "preloader_raw_b"];
        foreach (var p in specialParts)
        {
            string byName = await ExecuteShellAsync(device, mode, $"ls -l /dev/block/by-name/{p}");
            string src = byName.Contains("No such file") ? "" : "/dev/block/by-name/" + p;
            if (src == "")
            {
                string bootdevice = await ExecuteShellAsync(device, mode, $"ls -l /dev/block/bootdevice/by-name/{p}");
                src = bootdevice.Contains("No such file") ? "" : "/dev/block/bootdevice/by-name/" + p;
            }
            if (src == "") continue;

            Console.WriteLine($"备份特殊分区 {p} ...");
            string ddOut = await ExecuteShellAsync(device, mode, $"dd if={src} of=/sdcard/{p}.img");
            if (ddOut.Contains("No space left") || ddOut.Contains("not found") || ddOut.Contains("unknown command"))
            {
                Console.Error.WriteLine($"备份 {p} 失败，终止。");
                return 1;
            }
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"pull /sdcard/{p}.img \"{imagesFolder}\""));
            await ExecuteShellAsync(device, mode, $"rm /sdcard/{p}.img");
            partNames.Add(p);
        }

        // 生成 flashall_fastboot.txt（分区名清单，唯一名只写名，重名写 分区+路径）
        var flashList = new List<string>();
        for (int i = 0; i < partNames.Count; i++)
        {
            if (partNames[i].Contains("       /images/"))
                flashList.Add(partNames[i]);
            else if (!partNames.Contains($"{partNames[i]}       /images/{partNames[i]}.img"))
                flashList.Add(partNames[i]);
        }
        var ordered = flashList.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Distinct();
        string txt = string.Join(Environment.NewLine, ordered);
        string txtPath = Path.Combine(backupFolder, "flashall_fastboot.txt");
        File.WriteAllText(txtPath, txt);
        Console.WriteLine($"备份完成。镜像: {imagesFolder}");
        Console.WriteLine($"刷机脚本: {txtPath}");
        return 0;
    }
}