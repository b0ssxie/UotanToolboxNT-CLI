using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

internal static class PartitionCommands
{
    public static async Task<int> PartitionAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                分区管理:
                  utoolbox partition show               读取分区表（fastboot getvar all / parted）
                  utoolbox partition rm <盘> <分区号>   删除物理分区 (如 sda 5)
                  utoolbox partition mkpart <盘> <名称> <fs> <起点> <终点>  创建分区
                  utoolbox partition esp <盘> <分区号>   设置 ESP 标志
                  utoolbox partition resize-table <盘>   用 sgdisk 扩展分区表到 128
                """);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "show" => await ShowAsync(args.Skip(1).ToArray()),
            "rm" => await RmAsync(args.Skip(1).ToArray()),
            "mkpart" => await MkPartAsync(args.Skip(1).ToArray()),
            "esp" => await EspAsync(args.Skip(1).ToArray()),
            "resize-table" => await ResizeTableAsync(args.Skip(1).ToArray()),
            _ => CommandUtil.Unknown("partition", args[0]),
        };
    }

    /// <summary>
    /// 优先尝试 fastboot 设备；否则找 adb 设备用 parted 读表。
    /// </summary>
    private static async Task<(DeviceInfo? device, TransportType type)> RequireFastbootOrAdbAsync()
    {
        await Global.DeviceManager!.ScanAsync();
        var fb = Global.DeviceManager.Devices.FirstOrDefault(d => d.Transport == TransportType.Fastboot);
        if (fb != null)
            return (fb, TransportType.Fastboot);
        var adb = Global.DeviceManager.Devices.FirstOrDefault(d => d.Transport == TransportType.Adb);
        if (adb != null)
            return (adb, TransportType.Adb);
        Console.Error.WriteLine("未发现 Fastboot 或 adb 设备。");
        return (null, TransportType.Adb);
    }

    private static async Task<int> ShowAsync(string[] args)
    {
        var (device, type) = await RequireFastbootOrAdbAsync();
        if (device == null) return 1;
        if (type == TransportType.Fastboot)
        {
            string output = await FeaturesHelper.FastbootCmd(device.Id, "getvar all");
            Console.WriteLine(output);
        }
        else
        {
            // 推送 parted 到 /tmp 并读取分区表
            string push = await FeaturesHelper.AdbCmd(device.Id, $"push \"{Path.Combine(Global.runpath, "Push", "parted")}\" /tmp/");
            Console.WriteLine(push);
            await FeaturesHelper.AdbCmd(device.Id, "shell chmod +x /tmp/parted");
            foreach (var disk in new[] { "sda", "sdb", "sdc", "sdd", "sde", "sdf", "sdg", "sdh", "mmcblk0" })
            {
                Console.WriteLine($"===== /dev/block/{disk} =====");
                Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell /tmp/parted /dev/block/{disk} print"));
            }
        }
        return 0;
    }

    private static async Task<int> RmAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox partition rm <盘> <分区号>   （或 Fastbootd: rm-logical <名称>）");
            return 1;
        }
        var (device, type) = await RequireFastbootOrAdbAsync();
        if (device == null) return 1;
        string output = type == TransportType.Fastboot
            ? await FeaturesHelper.FastbootCmd(device.Id, $"delete-logical-partition {args[0]}")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell /tmp/parted /dev/block/{args[0]} rm {args[1]}");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> MkPartAsync(string[] args)
    {
        if (args.Length < 5)
        {
            Console.WriteLine("用法: utoolbox partition mkpart <盘> <名称> <文件系统> <起点> <终点>");
            Console.WriteLine("示例: utoolbox partition mkpart sda test ext4 500MB 1000MB");
            return 1;
        }
        var (device, type) = await RequireFastbootOrAdbAsync();
        if (device == null) return 1;
        string output = type == TransportType.Fastboot
            ? await FeaturesHelper.FastbootCmd(device.Id, $"create-logical-partition {args[1]} 00")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell /tmp/parted /dev/block/{args[0]} mkpart {args[1]} {args[2]} {args[3]} {args[4]}");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> EspAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox partition esp <盘> <分区号>");
            return 1;
        }
        var (device, _) = await RequireFastbootOrAdbAsync();
        if (device == null) return 1;
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell /tmp/parted /dev/block/{args[0]} set {args[1]} esp on"));
        return 0;
    }

    private static async Task<int> ResizeTableAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox partition resize-table <盘>");
            return 1;
        }
        var (device, _) = await RequireFastbootOrAdbAsync();
        if (device == null) return 1;
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"push \"{Path.Combine(Global.runpath, "Push", "sgdisk")}\" /tmp/"));
        await FeaturesHelper.AdbCmd(device.Id, "shell chmod +x /tmp/sgdisk");
        string output = await FeaturesHelper.AdbCmd(device.Id, $"shell /tmp/sgdisk --resize-table=128 /dev/block/{args[0]}");
        Console.WriteLine(output);
        if (output.Contains("completed successfully"))
        {
            Console.WriteLine("分区表扩展成功，重启进入 Recovery...");
            await FeaturesHelper.AdbCmd(device.Id, "reboot recovery");
        }
        return 0;
    }
}
