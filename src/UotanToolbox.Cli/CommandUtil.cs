using System;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

internal static class CommandUtil
{
    /// <summary>
    /// 查找 adb 或 hdc 设备。返回 (设备, 是否Hdc)。未连接返回 (null, false)。
    /// </summary>
    public static async Task<(DeviceInfo? device, bool isHdc)> RequireAdbOrHdcAsync()
    {
        await Global.DeviceManager!.ScanAsync();
        var adb = Global.DeviceManager.Devices.FirstOrDefault(d => d.Transport == TransportType.Adb && d.Properties.TryGetValue("State", out var s) && s == "device");
        if (adb != null)
            return (adb, false);
        var hdc = Global.DeviceManager.Devices.FirstOrDefault(d => d.Transport == TransportType.Hdc);
        if (hdc != null)
            return (hdc, true);
        Console.Error.WriteLine("未发现可用的 Android/OpenHarmony 设备（需要 adb 设备或 hdc 设备）。");
        return (null, false);
    }

    /// <summary>
    /// 在 adb 设备上执行 shell 命令（自动适配 su root 模式）。
    /// </summary>
    public static async Task<string> AdbShell(DeviceInfo device, string cmd, string? rootMode = null)
    {
        return rootMode switch
        {
            "root" => await FeaturesHelper.AdbCmd(device.Id, $"shell su -c \"{cmd}\""),
            "debug" => await FeaturesHelper.AdbCmd(device.Id, $"shell {cmd}"),
            _ => await FeaturesHelper.AdbCmd(device.Id, $"shell {cmd}"),
        };
    }

    public static int Unknown(string group, string cmd)
    {
        Console.Error.WriteLine($"未知命令: utoolbox {group} {cmd}");
        return 1;
    }
}
