using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

internal static class AppCommands
{
    public static async Task<int> AppAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                应用管理:
                  utoolbox app list                    列出已安装应用
                  utoolbox app install <apk>           安装 APK
                  utoolbox app run <包名>              运行应用
                  utoolbox app disable <包名>          停用应用
                  utoolbox app enable <包名>           启用应用
                  utoolbox app uninstall <包名> [--keep]  卸载（--keep 保留数据）
                  utoolbox app extract <包名> [-o 目录]   提取安装包
                  utoolbox app clear <包名>            清除应用数据
                  utoolbox app stop <包名>             强制停止
                """);
            return 0;
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;

        return args[0].ToLowerInvariant() switch
        {
            "list" => await ListAsync(device, isHdc),
            "install" => await InstallAsync(args.Skip(1).ToArray(), device, isHdc),
            "run" => await RunAsync(args.Skip(1).ToArray(), device, isHdc),
            "disable" => await ToggleAsync(args.Skip(1).ToArray(), device, isHdc, false),
            "enable" => await ToggleAsync(args.Skip(1).ToArray(), device, isHdc, true),
            "uninstall" => await UninstallAsync(args.Skip(1).ToArray(), device, isHdc),
            "extract" => await ExtractAsync(args.Skip(1).ToArray(), device, isHdc),
            "clear" => await ClearAsync(args.Skip(1).ToArray(), device, isHdc),
            "stop" => await StopAsync(args.Skip(1).ToArray(), device, isHdc),
            _ => CommandUtil.Unknown("app", args[0]),
        };
    }

    private static async Task<int> ListAsync(DeviceInfo device, bool isHdc)
    {
        string output;
        if (isHdc)
        {
            output = await FeaturesHelper.HdcCmd(device.Id, "shell bm dump -a");
            Console.WriteLine(output);
        }
        else
        {
            output = await FeaturesHelper.AdbCmd(device.Id, "shell pm list packages");
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var name = line.Replace("package:", "").Trim();
                if (name.Length > 0)
                    Console.WriteLine(name);
            }
        }
        return 0;
    }

    private static async Task<int> InstallAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox app install <apk文件> [更多apk...]");
            return 1;
        }
        foreach (var apk in args)
        {
            if (!File.Exists(apk))
            {
                Console.Error.WriteLine($"文件不存在: {apk}");
                continue;
            }
            string output = isHdc
                ? await FeaturesHelper.HdcCmd(device.Id, $"install \"{apk}\"")
                : await FeaturesHelper.AdbCmd(device.Id, $"install -r \"{apk}\"");
            Console.WriteLine(output);
            bool ok = isHdc
                ? output.Contains("successfully")
                : output.Contains("Success");
            Console.WriteLine(ok ? $"安装成功: {apk}" : $"安装失败: {apk}");
        }
        return 0;
    }

    private static async Task<int> RunAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox app run <包名>");
            return 1;
        }
        string output = await FeaturesHelper.AdbCmd(device.Id, $"shell monkey -p {args[0]} 1");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> ToggleAsync(string[] args, DeviceInfo device, bool isHdc, bool enable)
    {
        if (args.Length == 0)
        {
            Console.WriteLine($"用法: utoolbox app {(enable ? "enable" : "disable")} <包名>");
            return 1;
        }
        string cmd = isHdc
            ? $"shell bm {(enable ? "enable" : "disable")} -n {args[0]}"
            : $"shell pm {(enable ? "enable" : "disable-user --user 0")} {args[0]}";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, cmd)
            : await FeaturesHelper.AdbCmd(device.Id, cmd);
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> UninstallAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox app uninstall <包名> [--keep]");
            return 1;
        }
        string package = args[0];
        bool keep = args.Contains("--keep");
        string cmd = isHdc
            ? $"app uninstall {package}"
            : $"shell pm uninstall {(keep ? "-k " : "")}{package}";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, cmd)
            : await FeaturesHelper.AdbCmd(device.Id, cmd);
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> ExtractAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox app extract <包名> [-o 目录]");
            return 1;
        }
        string package = args[0];
        string outputDir = Directory.GetCurrentDirectory();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
                outputDir = args[++i];
        }
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        string output = await FeaturesHelper.AdbCmd(device.Id, $"shell pm path {package}");
        Console.WriteLine($"pm path 输出:\n{output}");
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("package:")) continue;
            string remotePath = line.Substring("package:".Length).Trim();
            if (remotePath.Length == 0) continue;
            string localName = Path.GetFileName(remotePath);
            Console.WriteLine($"拉取 {remotePath} -> {Path.Combine(outputDir, localName)}");
            string pull = await FeaturesHelper.AdbCmd(device.Id, $"pull \"{remotePath}\" \"{Path.Combine(outputDir, localName)}\"");
            Console.WriteLine(pull);
        }
        return 0;
    }

    private static async Task<int> ClearAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox app clear <包名>");
            return 1;
        }
        string cmd = isHdc
            ? $"shell bm clean -n {args[0]} -d"
            : $"shell pm clear {args[0]}";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, cmd)
            : await FeaturesHelper.AdbCmd(device.Id, cmd);
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> StopAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox app stop <包名>");
            return 1;
        }
        string cmd = isHdc
            ? $"shell aa force-stop {args[0]}"
            : $"shell am force-stop {args[0]}";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, cmd)
            : await FeaturesHelper.AdbCmd(device.Id, cmd);
        Console.WriteLine(output);
        return 0;
    }
}
