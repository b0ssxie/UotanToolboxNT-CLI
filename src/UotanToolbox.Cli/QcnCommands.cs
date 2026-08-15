using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

internal static class QcnCommands
{
    public static async Task<int> QcnAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                QCN 备份/写入 (Qualcomm, Windows 专用):
                  utoolbox qcn backup <com口>              备份 QCN（设备处于 901D/9091 模式）
                  utoolbox qcn write <com口> <QCN文件>     写入 QCN
                  utoolbox qcn diag-901d                  开启 901D 诊断口 (adb)
                  utoolbox qcn diag-9091                  开启 9091 诊断口 (小米)
                """);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "backup" => await BackupAsync(args.Skip(1).ToArray()),
            "write" => await WriteAsync(args.Skip(1).ToArray()),
            "diag-901d" => await Enable901dAsync(),
            "diag-9091" => await Enable9091Async(),
            _ => CommandUtil.Unknown("qcn", args[0]),
        };
    }

    private static int ExtractComNumber(string com)
    {
        Match m = Regex.Match(com, @"\d+");
        return m.Success ? int.Parse(m.Value) : 0;
    }

    private static async Task<int> BackupAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox qcn backup <com口>   例: utoolbox qcn backup COM3");
            return 1;
        }
        int com = ExtractComNumber(args[0]);
        if (com == 0)
        {
            Console.Error.WriteLine("无效的 COM 口号。");
            return 1;
        }
        string output = await CallExternalProgram.QCNTool($"-r -p {com}");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> WriteAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox qcn write <com口> <QCN文件>");
            return 1;
        }
        int com = ExtractComNumber(args[0]);
        if (com == 0)
        {
            Console.Error.WriteLine("无效的 COM 口号。");
            return 1;
        }
        string file = args[1];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"文件不存在: {file}");
            return 1;
        }
        string output = await CallExternalProgram.QCNTool($"-w -p {com} -f \"{file}\"");
        Console.WriteLine(output);
        if (output.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("写入失败。");
            return 1;
        }
        Console.WriteLine("写入完成。");
        return 0;
    }

    private static async Task<int> Enable901dAsync()
    {
        var (device, _) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        Console.WriteLine("开启 901D 诊断口...");
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "root"));
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell setprop sys.usb.config diag,adb"));
        Console.WriteLine("已开启。设备将重新枚举为 901D 端口。");
        return 0;
    }

    private static async Task<int> Enable9091Async()
    {
        var (device, _) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        string apk = Path.Combine(Global.runpath, "APK", "mi_diag.apk");
        if (!File.Exists(apk))
        {
            Console.Error.WriteLine($"未找到 mi_diag.apk: {apk}");
            return 1;
        }
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, $"push \"{apk}\" /sdcard"));
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell am start -a miui.intent.action.OPEN"));
        Console.WriteLine(await FeaturesHelper.AdbCmd(device.Id, "shell am start -n com.longcheertel.midtest/com.longcheertel.midtest.Diag"));
        Console.WriteLine("已启动小米诊断应用。");
        return 0;
    }
}
