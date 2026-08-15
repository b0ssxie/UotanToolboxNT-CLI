using System;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

internal static class ScrcpyCommands
{
    public static async Task<int> ScrcpyAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                投屏 (scrcpy):
                  utoolbox scrcpy start [scrcpy参数...]   启动投屏（参数直接传给 scrcpy）
                  utoolbox scrcpy key <back|home|recent|lock|volup|voldown|mute>   发送按键
                  utoolbox scrcpy screenshot               截图到 /sdcard
                """);
            return 0;
        }

        var (device, _) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;

        return args[0].ToLowerInvariant() switch
        {
            "start" => await StartAsync(args.Skip(1).ToArray(), device),
            "key" => await KeyAsync(args.Skip(1).ToArray(), device),
            "screenshot" => await ScreenshotAsync(device),
            _ => CommandUtil.Unknown("scrcpy", args[0]),
        };
    }

    private static async Task<int> StartAsync(string[] args, DeviceInfo device)
    {
        // 直接拼装 scrcpy 参数
        string arg = $"-s \"{device.Id}\" ";
        foreach (var a in args)
            arg += a + " ";
        Console.WriteLine($"启动 scrcpy: {arg}");
        string output = await CallExternalProgram.Scrcpy(arg.Trim());
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> KeyAsync(string[] args, DeviceInfo device)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox scrcpy key <back|home|recent|lock|volup|voldown|mute>");
            return 1;
        }
        int keycode = args[0].ToLowerInvariant() switch
        {
            "back" => 4,
            "home" => 3,
            "recent" or "mul" => 187,
            "lock" => 26,
            "volup" => 24,
            "voldown" => 25,
            "mute" => 164,
            _ => -1,
        };
        if (keycode == -1)
        {
            Console.Error.WriteLine($"未知按键: {args[0]}");
            return 1;
        }
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell input keyevent {keycode}"));
        return 0;
    }

    private static async Task<int> ScreenshotAsync(DeviceInfo device)
    {
        string file = $"/sdcard/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"shell /system/bin/screencap -p {file}"));
        Console.WriteLine($"截图已保存到设备: {file} （可用 utoolbox file pull {file} . 拉取到本地）");
        return 0;
    }
}
