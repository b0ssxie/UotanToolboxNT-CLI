using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

/// <summary>
/// 刷机恢复类命令：刷入 Magisk、关闭自动还原、同步 A/B 分区、高级重启。
/// </summary>
internal static class FlashCommands
{
    public static async Task<int> FlashAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                刷机恢复工具:
                  utoolbox install-magisk <apk>                   刷入 Magisk（Recovery/Sideload/Android 系统）
                  utoolbox disable-autorecovery                   关闭自动还原 Recovery
                  utoolbox sync-ab                                同步 A/B 分区
                  utoolbox advanced-reboot <模式>                 高级重启

                安装方式选项（跟在命令后）:
                  --twrp       通过 TWRP 安装（需 Recovery）
                  --sideload   通过 ADB Sideload 安装（Recovery/Sideload）

                高级重启模式:
                  twrp-sideload | sideload | autodloader | zygote | safe-mode | muc | factory | admin
                """);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "install-magisk" => await InstallMagiskAsync(args.Skip(1).ToArray()),
            "disable-autorecovery" => await DisableAutorecoveryAsync(args.Skip(1).ToArray()),
            "sync-ab" => await SyncAbAsync(args.Skip(1).ToArray()),
            "advanced-reboot" => await AdvancedRebootAsync(args.Skip(1).ToArray()),
            _ => CommandUtil.Unknown("flash", args[0]),
        };
    }

    /// <summary>
    /// 解析 sed 方式参数（--twrop / --sideload）。
    /// </summary>
    private static string InstallMethod(string[] args, string defaultMethod = "sideload")
    {
        var a = args.Where(x => x is "--twrp" or "--sideload").ToArray();
        return a.Length > 0 ? a[0].TrimStart('-') : defaultMethod;
    }

    /// <summary>
    /// 判断设备是否为 Sideload 状态；不是则尝试从 Recovery 进入 sideload。
    /// </summary>
    private static async Task<bool> EnsureSideloadAsync(DeviceInfo device)
    {
        // 先检查当前状态
        string lst = await FeaturesHelper.AdbCmd(device.Id, "shell getprop ro.boot.mode");
        if (lst.Contains("sideload")) return true;

        // 尝试 twrp sideload
        string output = await FeaturesHelper.AdbCmd(device.Id, "shell twrp sideload");
        if (output.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            await FeaturesHelper.AdbCmd(device.Id, "reboot sideload");
        }
        await Task.Delay(2000);
        Console.WriteLine("等待设备进入 Sideload 模式...");
        return true;
    }

    /// <summary>
    /// 刷入 Magisk / APK 到设备。
    /// </summary>
    private static async Task<int> InstallMagiskAsync(string[] args)
    {
        string apkPath = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrEmpty(apkPath) || !File.Exists(apkPath))
        {
            Console.Error.WriteLine("用法: utoolbox install-magisk <apk文件> [--twrp | --sideload]");
            return 1;
        }
        string method = InstallMethod(args);

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc)
        {
            Console.Error.WriteLine("OpenHarmony 设备暂不支持刷入 Magisk。");
            return 1;
        }

        // 判断设备状态
        string status = await GetDeviceStatusAsync(device);

        if (method == "twrp")
        {
            if (status.Contains("Recovery"))
            {
                Console.WriteLine($"推送 {apkPath} 到 /tmp/magisk.apk ...");
                await FeaturesHelper.AdbCmd(device.Id, $"push \"{apkPath}\" /tmp/magisk.apk");
                Console.WriteLine("通过 TWRP 安装...");
                Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell twrp install /tmp/magisk.apk"));
            }
            else
            {
                Console.Error.WriteLine("--twrp 需要设备处于 Recovery 模式。");
                return 1;
            }
        }
        else if (method == "sideload")
        {
            if (status.Contains("Android"))
            {
                // Android 系统下只能推送 APK 到 /sdcard 手动安装
                Console.WriteLine($"Android 系统下将 Magisk 推送到 /sdcard/magisk.apk 供手动安装...");
                await FeaturesHelper.AdbCmd(device.Id, $"push \"{apkPath}\" /sdcard/magisk.apk");
                Console.WriteLine("已推送，请在设备上手动点击安装 /sdcard/magisk.apk");
            }
            else
            {
                if (!await EnsureSideloadAsync(device)) return 1;
                Console.WriteLine("通过 ADB Sideload 安装...");
                Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"sideload \"{apkPath}\""));
            }
        }
        else
        {
            Console.Error.WriteLine("未知安装方式。");
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// 关闭自动还原 Recovery：安装 DisableAutoRecovery.zip。
    /// </summary>
    private static async Task<int> DisableAutorecoveryAsync(string[] args)
    {
        string method = InstallMethod(args);
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc)
        {
            Console.Error.WriteLine("OpenHarmony 设备不支持此操作。");
            return 1;
        }

        string zip = Path.Combine(Global.runpath, "ZIP", "DisableAutoRecovery.zip");
        if (!File.Exists(zip))
        {
            Console.Error.WriteLine($"未找到资源文件: {zip}");
            return 1;
        }

        string status = await GetDeviceStatusAsync(device);
        if (method == "twrp")
        {
            if (!status.Contains("Recovery"))
            {
                Console.Error.WriteLine("--twrp 需要设备处于 Recovery 模式。");
                return 1;
            }
            Console.WriteLine("推送 DisableAutoRecovery.zip ...");
            await FeaturesHelper.AdbCmd(device.Id, $"push \"{zip}\" /tmp/");
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell twrp install /tmp/DisableAutoRecovery.zip"));
        }
        else
        {
            await EnsureSideloadAsync(device);
            Console.WriteLine("通过 ADB Sideload 安装 DisableAutoRecovery.zip ...");
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"sideload \"{zip}\""));
        }
        return 0;
    }

    /// <summary>
    /// 同步 A/B 分区：安装 copy-partitions.zip。
    /// </summary>
    private static async Task<int> SyncAbAsync(string[] args)
    {
        string method = InstallMethod(args);
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc)
        {
            Console.Error.WriteLine("OpenHarmony 设备不支持此操作。");
            return 1;
        }

        string zip = Path.Combine(Global.runpath, "ZIP", "copy-partitions.zip");
        if (!File.Exists(zip))
        {
            Console.Error.WriteLine($"未找到资源文件: {zip}");
            return 1;
        }

        string status = await GetDeviceStatusAsync(device);
        if (method == "twrp")
        {
            if (!status.Contains("Recovery"))
            {
                Console.Error.WriteLine("--twrp 需要设备处于 Recovery 模式。");
                return 1;
            }
            Console.WriteLine("推送 copy-partitions.zip ...");
            await FeaturesHelper.AdbCmd(device.Id, $"push \"{zip}\" /tmp/");
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell twrp install /tmp/copy-partitions.zip"));
        }
        else
        {
            await EnsureSideloadAsync(device);
            Console.WriteLine("通过 ADB Sideload 安装 copy-partitions.zip ...");
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"sideload \"{zip}\""));
        }
        return 0;
    }

    /// <summary>
    /// 高级重启。
    /// </summary>
    private static async Task<int> AdvancedRebootAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox advanced-reboot <模式>");
            Console.WriteLine("模式: twrp-sideload | sideload | autodloader | zygote | safe-mode | muc | factory | admin");
            return 1;
        }
        string mode = args[0].ToLowerInvariant();
        string cmd = mode switch
        {
            "twrp-sideload" => "shell twrp sideload",
            "sideload" => "reboot sideload",
            "autodloader" => "reboot autodloader",
            "zygote" => "shell su -c \"setprop ctl.restart zygote\"",
            "safe-mode" => "reboot safe-mode",
            "muc" => "reboot muc",
            "factory" => "reboot factory",
            "admin" => "reboot admin",
            _ => null,
        };
        if (cmd == null)
        {
            Console.Error.WriteLine($"未知模式: {mode}");
            return 1;
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        Console.WriteLine($"执行: {cmd}");
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, cmd));
        return 0;
    }

    /// <summary>
    /// 获取设备当前状态（Android/Recovery/Sideload/Fastboot 等）。
    /// </summary>
    private static async Task<string> GetDeviceStatusAsync(DeviceInfo device)
    {
        try
        {
            var info = await GetDevicesInfo.DevicesInfoLittle(device.Id);
            return info.TryGetValue("Status", out var s) ? s : "";
        }
        catch
        {
            return "";
        }
    }
}