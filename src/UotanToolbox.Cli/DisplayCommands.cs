using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

internal static class DisplayCommands
{
    public static async Task<int> DisplayAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                显示与系统设置 (Android):
                  utoolbox display info                   显示当前分辨率/DPI/超时等
                  utoolbox display size <宽x高>           修改分辨率
                  utoolbox display density <dpi|dp值> [--dp]  修改 DPI（--dp 表示输入的是 DP）
                  utoolbox display reset                  恢复默认分辨率/DPI
                  utoolbox battery set-temp <摄氏温度>    修改电池温度
                  utoolbox battery set-level <电量>       修改电池电量
                  utoolbox battery reset                  重置电池
                  utoolbox battery no-charge              禁止充电
                  utoolbox lock-time <秒>                 设置锁屏超时
                  utoolbox scale font|window|transition|anim <倍数>  设置缩放
                """);
            return 0;
        }

        var (device, _) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;

        return args[0].ToLowerInvariant() switch
        {
            "info" => await InfoAsync(device),
            "size" => await SizeAsync(args.Skip(1).ToArray(), device),
            "density" => await DensityAsync(args.Skip(1).ToArray(), device),
            "reset" => await ResetAsync(device),
            "battery" => await BatteryAsync(args.Skip(1).ToArray(), device),
            "lock-time" => await LockTimeAsync(args.Skip(1).ToArray(), device),
            "scale" => await ScaleAsync(args.Skip(1).ToArray(), device),
            _ => CommandUtil.Unknown("display", args[0]),
        };
    }

    private static async Task<int> InfoAsync(DeviceInfo device)
    {
        Console.WriteLine("wm size:   " + await FeaturesHelper.AdbCmd(device.Id, "shell wm size"));
        Console.WriteLine("wm density:" + await FeaturesHelper.AdbCmd(device.Id, "shell wm density"));
        Console.WriteLine("screen_off_timeout: " + await FeaturesHelper.AdbCmd(device.Id, "shell settings get system screen_off_timeout"));
        Console.WriteLine("font_scale: " + await FeaturesHelper.AdbCmd(device.Id, "shell settings get system font_scale"));
        Console.WriteLine("window_animation_scale: " + await FeaturesHelper.AdbCmd(device.Id, "shell settings get global window_animation_scale"));
        Console.WriteLine("transition_animation_scale: " + await FeaturesHelper.AdbCmd(device.Id, "shell settings get global transition_animation_scale"));
        Console.WriteLine("animator_duration_scale: " + await FeaturesHelper.AdbCmd(device.Id, "shell settings get global animator_duration_scale"));
        return 0;
    }

    private static async Task<int> SizeAsync(string[] args, DeviceInfo device)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox display size <宽x高>   例: 1080x2340");
            return 1;
        }
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell wm size {args[0]}"));
        return 0;
    }

    private static async Task<int> DensityAsync(string[] args, DeviceInfo device)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox display density <dpi> [--dp]");
            return 1;
        }
        string value = args[0];
        bool isDp = args.Contains("--dp");
        if (isDp)
        {
            // DP 转 DPI: dpi = 宽像素 * 160 / dp
            string wmSize = await FeaturesHelper.AdbCmd(device.Id, "shell wm size");
            // 形如 Physical size: 1080x2340
            Match m = Regex.Match(wmSize, @"(\d+)[xX](\d+)");
            if (!m.Success || !double.TryParse(value, out double dp))
            {
                Console.Error.WriteLine("无法解析分辨率或 DP 值。");
                return 1;
            }
            int width = int.Parse(m.Groups[1].Value);
            int dpi = (int)(width * 160.0 / dp);
            Console.WriteLine($"换算: {width}px * 160 / {dp}dp = {dpi} dpi");
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell wm density {dpi}"));
        }
        else
        {
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell wm density {value}"));
        }
        return 0;
    }

    private static async Task<int> ResetAsync(DeviceInfo device)
    {
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell wm size reset"));
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell wm density reset"));
        return 0;
    }

    private static async Task<int> BatteryAsync(string[] args, DeviceInfo device)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox display battery set-temp|set-level|reset|no-charge [...]");
            return 1;
        }
        return args[0].ToLowerInvariant() switch
        {
            "set-temp" => await SetTempAsync(args.Skip(1).ToArray(), device),
            "set-level" => await SetLevelAsync(args.Skip(1).ToArray(), device),
            "reset" => await ExecAsync(device, "dumpsys battery reset"),
            "no-charge" => await ExecAsync(device, "dumpsys battery set status 1"),
            _ => CommandUtil.Unknown("display battery", args[0]),
        };
    }

    private static async Task<int> SetTempAsync(string[] args, DeviceInfo device)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox display battery set-temp <摄氏温度>");
            return 1;
        }
        if (!double.TryParse(args[0], out double temp))
        {
            Console.Error.WriteLine("温度格式错误。");
            return 1;
        }
        if (temp >= 100)
        {
            Console.Error.WriteLine("温度过高（≥100℃），已拒绝执行！");
            return 1;
        }
        if (temp < -273.15)
        {
            Console.Error.WriteLine("温度低于绝对零度，已拒绝执行！");
            return 1;
        }
        int value = (int)(temp * 10);
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell dumpsys battery set temp {value}"));
        return 0;
    }

    private static async Task<int> SetLevelAsync(string[] args, DeviceInfo device)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox display battery set-level <电量>");
            return 1;
        }
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell dumpsys battery set level {args[0]}"));
        return 0;
    }

    private static async Task<int> LockTimeAsync(string[] args, DeviceInfo device)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox display lock-time <秒>");
            return 1;
        }
        if (!double.TryParse(args[0], out double seconds))
        {
            Console.Error.WriteLine("秒数格式错误。");
            return 1;
        }
        int ms = (int)(seconds * 1000);
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell settings put system screen_off_timeout {ms}"));
        return 0;
    }

    private static async Task<int> ScaleAsync(string[] args, DeviceInfo device)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox display scale font|window|transition|anim <倍数>");
            return 1;
        }
        string target = args[0];
        string value = args[1];
        string setting = target switch
        {
            "font" => "settings put system font_scale",
            "window" => "settings put global window_animation_scale",
            "transition" => "settings put global transition_animation_scale",
            "anim" => "settings put global animator_duration_scale",
            _ => null,
        };
        if (setting == null)
        {
            Console.Error.WriteLine("未知设置项，应为 font|window|transition|anim");
            return 1;
        }
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell {setting} {value}"));
        return 0;
    }

    private static async Task<int> ExecAsync(DeviceInfo device, string shellCmd)
    {
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell {shellCmd}"));
        return 0;
    }
}
