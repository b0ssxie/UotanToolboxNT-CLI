using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

internal static class FileCommands
{
    public static async Task<int> FileAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                文件管理:
                  utoolbox file ls <路径>                 列出目录内容
                  utoolbox file push <本地> <设备路径>    上传文件
                  utoolbox file pull <设备路径> <本地目录>  下载文件
                  utoolbox file chmod <模式> <路径>       修改权限 (如 755, 777)
                  utoolbox file rm <路径> [-r]            删除（-r 递归）
                  utoolbox file mkdir <路径>              新建目录
                  utoolbox file touch <路径>              新建文件
                  utoolbox file mv <源> <目标>            移动/重命名
                  utoolbox file cp <源> <目标> [-r]       复制（-r 递归）
                """);
            return 0;
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;

        return args[0].ToLowerInvariant() switch
        {
            "ls" => await LsAsync(args.Skip(1).ToArray(), device, isHdc),
            "push" => await PushAsync(args.Skip(1).ToArray(), device, isHdc),
            "pull" => await PullAsync(args.Skip(1).ToArray(), device, isHdc),
            "chmod" => await ChmodAsync(args.Skip(1).ToArray(), device, isHdc),
            "rm" => await RmAsync(args.Skip(1).ToArray(), device, isHdc),
            "mkdir" => await MkdirAsync(args.Skip(1).ToArray(), device, isHdc),
            "touch" => await TouchAsync(args.Skip(1).ToArray(), device, isHdc),
            "mv" => await MvAsync(args.Skip(1).ToArray(), device, isHdc),
            "cp" => await CpAsync(args.Skip(1).ToArray(), device, isHdc),
            _ => CommandUtil.Unknown("file", args[0]),
        };
    }

    private static async Task<int> LsAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        string path = args.Length > 0 ? args[0] : "/sdcard";
        if (!path.EndsWith("/")) path += "/";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, $"shell ls -la \"{path}\"")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell ls -la \"{path}\"");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> PushAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox file push <本地文件> <设备目标路径>");
            return 1;
        }
        if (!File.Exists(args[0]))
        {
            Console.Error.WriteLine($"本地文件不存在: {args[0]}");
            return 1;
        }
        string cmd = isHdc
            ? $"file send \"{args[0]}\" \"{args[1]}\""
            : $"push \"{args[0]}\" \"{args[1]}\"";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, cmd)
            : await FeaturesHelper.AdbCmd(device.Id, cmd);
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> PullAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox file pull <设备路径> <本地目录>");
            return 1;
        }
        string cmd = isHdc
            ? $"file recv \"{args[0]}\" \"{args[1]}\""
            : $"pull \"{args[0]}\" \"{args[1]}\"";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, cmd)
            : await FeaturesHelper.AdbCmd(device.Id, cmd);
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> ChmodAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox file chmod <模式> <路径>");
            return 1;
        }
        string shell = $"chmod {args[0]} \"{args[1]}\"";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, $"shell {shell}")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell {shell}");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> RmAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox file rm <路径> [-r]");
            return 1;
        }
        bool recursive = args.Contains("-r") || args.Contains("--recursive");
        string target = args[0];
        string shell = $"rm {(recursive ? "-rf " : "")}\"{target}\"";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, $"shell {shell}")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell {shell}");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> MkdirAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox file mkdir <路径>");
            return 1;
        }
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, $"shell mkdir \"{args[0]}\"")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell mkdir \"{args[0]}\"");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> TouchAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox file touch <路径>");
            return 1;
        }
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, $"shell touch \"{args[0]}\"")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell touch \"{args[0]}\"");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> MvAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox file mv <源> <目标>");
            return 1;
        }
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, $"shell mv \"{args[0]}\" \"{args[1]}\"")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell mv \"{args[0]}\" \"{args[1]}\"");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> CpAsync(string[] args, DeviceInfo device, bool isHdc)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox file cp <源> <目标> [-r]");
            return 1;
        }
        bool recursive = args.Contains("-r") || args.Contains("--recursive");
        string shell = $"cp {(recursive ? "-r " : "")}\"{args[0]}\" \"{args[1]}\"";
        string output = isHdc
            ? await FeaturesHelper.HdcCmd(device.Id, $"shell {shell}")
            : await FeaturesHelper.AdbCmd(device.Id, $"shell {shell}");
        Console.WriteLine(output);
        return 0;
    }
}
