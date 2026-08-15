using System;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

internal static class WirelessCommands
{
    public static async Task<int> WirelessAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                无线 ADB:
                  utoolbox wireless pair <ip:端口> <配对码>   手动配对 (adb pair)
                  utoolbox wireless connect <ip:端口>         连接设备 (adb connect)
                  utoolbox wireless tcpip <端口>              在设备上开启无线调试 (默认 5555)
                """);
            return 0;
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;

        return args[0].ToLowerInvariant() switch
        {
            "pair" => await PairAsync(args.Skip(1).ToArray(), device, isHdc),
            "connect" => await ConnectAsync(args.Skip(1).ToArray(), device, isHdc),
            "tcpip" => await TcpipAsync(args.Skip(1).ToArray(), device, isHdc),
            _ => CommandUtil.Unknown("wireless", args[0]),
        };
    }

    private static async Task<int> PairAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox wireless pair <ip:端口> <配对码>");
            return 1;
        }
        bool ok = await ADBPairHelper.Pair(args[0], args[1]);
        if (ok)
            Console.WriteLine("配对成功。可用 connect 连接。");
        else
            Console.Error.WriteLine("配对失败。请检查配对码和地址。");
        return ok ? 0 : 1;
    }

    private static async Task<int> ConnectAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox wireless connect <ip:端口>");
            return 1;
        }
        string cmd = isHdc
            ? $"tconn {args[0]}"
            : $"connect {args[0]}";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, cmd)
            : await FeaturesHelper.AdbCmd(device.Id, cmd);
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> TcpipAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        string port = args.Length > 0 ? args[0] : "5555";
        if (isHdc)
        {
            Console.Error.WriteLine("OpenHarmony 设备请使用 utoolbox wireless connect 连接。");
            return 1;
        }
        Console.WriteLine($"在设备上开启 tcpip 端口 {port}...");
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"tcpip {port}"));
        return 0;
    }
}
