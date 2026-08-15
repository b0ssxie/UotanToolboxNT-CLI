using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

/// <summary>
/// 系统杂项命令：破解X、状态栏定制、辅助应用激活、版本检查。
/// </summary>
internal static class SystemCommands
{
    public static async Task<int> SystemAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                系统杂项:
                  utoolbox xda [--off]            破解 X（配置 captive portal / 时间 / NTP）
                  utoolbox statusbar <项,...>     状态栏图标黑名单（如 wifi,bluetooth,nfc；--show 显示信息）
                  utoolbox clock-seconds [--off]  状态栏显示秒
                  utoolbox rotation-suggest [--off]  旋转建议（默认开启=写0）
                  utoolbox active-app <辅助应用>   激活辅助应用（Shizuku/Dhizuku/Brevent/IceBox/Greenify/StopApp/PermissionDog）
                  utoolbox version                检查版本
                """);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "xda" => await XdaAsync(args.Skip(1).ToArray()),
            "statusbar" => await StatusbarAsync(args.Skip(1).ToArray()),
            "clock-seconds" => await ClockSecondsAsync(args.Skip(1).ToArray()),
            "rotation-suggest" => await RotationSuggestAsync(args.Skip(1).ToArray()),
            "active-app" => await ActiveAppAsync(args.Skip(1).ToArray()),
            "version" => await VersionAsync(),
            _ => CommandUtil.Unknown("system", args[0]),
        };
    }

    private static async Task<int> XdaAsync(string[] args)
    {
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }

        if (args.Contains("--off"))
        {
            Console.WriteLine("恢复默认网络检测...");
            string url = "http://connectivitycheck.gstatic.com/generate_204";
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell settings put global captive_portal_http_url \"{url}\""));
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell settings put global captive_portal_https_url \"{url}\""));
            return 0;
        }

        Console.WriteLine("破解 X（国内网络检测）...");
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell settings put global captive_portal_http_url \"http://connect.rom.miui.com/generate_204\""));
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell settings put global captive_portal_https_url \"https://connect.rom.miui.com/generate_204\""));
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell settings put global time_zone Asia/Shanghai"));
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell settings put global ntp_server ntp1.aliyun.com"));
        Console.WriteLine("完成。");
        return 0;
    }

    private static async Task<int> StatusbarAsync(string[] args)
    {
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }

        if (args.Contains("--show"))
        {
            Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell settings get secure icon_blacklist"));
            return 0;
        }
        string items = string.Join(",", args.Where(a => !a.StartsWith("--")));
        if (items == "")
        {
            Console.WriteLine("用法: utoolbox statusbar <项,...>   例如: statusbar wifi,bluetooth,nfc");
            return 1;
        }
        // 固定前缀 rotate,ime,
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell settings put secure icon_blacklist rotate,ime,{items},"));
        return 0;
    }

    private static async Task<int> ClockSecondsAsync(string[] args)
    {
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }
        string value = args.Contains("--off") ? "0" : "1";
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell settings put secure clock_seconds {value}"));
        return 0;
    }

    private static async Task<int> RotationSuggestAsync(string[] args)
    {
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }
        // 注意取反：默认开旋转建议=写 0
        string value = args.Contains("--off") ? "1" : "0";
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell settings put secure show_rotation_suggestions {value}"));
        return 0;
    }

    private static async Task<int> ActiveAppAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox active-app <Shizuku|Dhizuku|Brevent|IceBox|Greenify|StopApp|PermissionDog>");
            return 1;
        }
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc) { Console.Error.WriteLine("OpenHarmony 设备不支持此操作。"); return 1; }

        // 先获取前台应用
        string focus = await FeaturesHelper.AdbCmd(device.Id, "shell dumpsys window | grep mCurrentFocus");
        string name = args[0].ToLowerInvariant();
        string appPackage = name switch
        {
            "shizuku" => "moe.shizuku.privileged.api",
            "dhizuku" => "com.rosan.dhizuku",
            "greenify" => "com.oasisfeng.greenify",
            "island" => "com.oasisfeng.island",
            "brevent" => "me.piebridge.brevent",
            "icebox" => "com.catchingnow.icebox",
            "stopapp" => "web1n.stopapp",
            "permissiondog" => "com.web1n.permissiondog",
            _ => null,
        };
        if (appPackage == null)
        {
            Console.Error.WriteLine($"不支持的辅助应用: {args[0]}");
            return 1;
        }

        if (!focus.Contains(appPackage))
        {
            Console.WriteLine($"当前前台应用不是 {appPackage}，请在设备上打开该应用后重试。");
            return 1;
        }

        string output = appPackage switch
        {
            "moe.shizuku.privileged.api" =>
                await FeaturesHelper.AdbCmd(device.Id, "shell sh /storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh"),
            "com.rosan.dhizuku" =>
                await FeaturesHelper.AdbCmd(device.Id, "shell dpm set-device-owner com.rosan.dhizuku/.server.DhizukuDAReceiver"),
            "com.oasisfeng.greenify" =>
                await FeaturesHelper.AdbCmd(device.Id, "shell pm grant com.oasisfeng.greenify android.permission.WRITE_SECURE_SETTINGS")
                + "\n" + await FeaturesHelper.AdbCmd(device.Id, "shell pm grant com.oasisfeng.greenify android.permission.DUMP")
                + "\n" + await FeaturesHelper.AdbCmd(device.Id, "shell pm grant com.oasisfeng.greenify android.permission.READ_LOGS"),
            "com.oasisfeng.island" =>
                await FeaturesHelper.AdbCmd(device.Id, "shell pm grant com.oasisfeng.island android.permission.INTERACT_ACROSS_USERS"),
            "me.piebridge.brevent" =>
                await FeaturesHelper.AdbCmd(device.Id, "shell sh /data/data/me.piebridge.brevent/brevent.sh"),
            "com.catchingnow.icebox" =>
                await FeaturesHelper.AdbCmd(device.Id, "shell sh /sdcard/Android/data/com.catchingnow.icebox/files/start.sh"),
            "web1n.stopapp" =>
                await FeaturesHelper.AdbCmd(device.Id, "shell sh /storage/emulated/0/Android/data/web1n.stopapp/files/starter.sh"),
            "com.web1n.permissiondog" =>
                await FeaturesHelper.AdbCmd(device.Id, "shell sh /storage/emulated/0/Android/data/com.web1n.permissiondog/files/starter.sh"),
            _ => "",
        };
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> VersionAsync()
    {
        Console.WriteLine($"本机版本: {Global.currentVersion}");
        // 检查远程最新版
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var req = new HttpRequestMessage(HttpMethod.Post, "https://toolbox.uotan.cn/api/list");
            var resp = await client.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            // 简单解析版本号（GUI 用复杂 JSON 解析，这里仅作演示）
            Console.WriteLine($"远程检查返回: {(resp.IsSuccessStatusCode ? "正常" : $"HTTP {(int)resp.StatusCode}")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"版本检查失败: {ex.Message}");
        }
        return 0;
    }
}