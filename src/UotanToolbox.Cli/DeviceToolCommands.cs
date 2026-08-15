using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

/// <summary>
/// 设备工具：logcat、bugreport、端口转发、诊断。
/// </summary>
internal static class DeviceToolCommands
{
    public static async Task<int> DeviceToolAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                设备工具:
                  utoolbox logcat [标签] [-l]           实时查看日志（Ctrl+C 停止；-l 打印现有后退出）
                  utoolbox logcat -o <文件> [标签]      保存日志到文件
                  utoolbox bugreport -o <文件>          生成 bugreport 诊断包
                  utoolbox forward [list|add <本地> <设备>|remove <本地>]  端口转发
                  utoolbox reverse [list|add <设备> <本地>|remove <设备>]  反向转发
                  utoolbox diagnose                       常用设备诊断（电量/网络/进程/电池健康）

                端口转发示例:
                  utoolbox forward add tcp:8080 tcp:8080
                  utoolbox reverse add tcp:8080 tcp:5000
                """);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "logcat" => await LogcatAsync(args.Skip(1).ToArray()),
            "bugreport" => await BugreportAsync(args.Skip(1).ToArray()),
            "forward" => await ForwardAsync("forward", args.Skip(1).ToArray()),
            "reverse" => await ForwardAsync("reverse", args.Skip(1).ToArray()),
            "diagnose" => await DiagnoseAsync(),
            _ => CommandUtil.Unknown("devicetool", args[0]),
        };
    }

    private static bool IsHdcDevice(DeviceInfo d) => d.Transport == TransportType.Hdc;

    private static async Task<int> LogcatAsync(string[] args)
    {
        string? outputFile = null;
        bool listOnly = false;
        var tags = new System.Collections.Generic.List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length) outputFile = args[++i];
            else if (args[i] == "-l" || args[i] == "--list") listOnly = true;
            else tags.Add(args[i]);
        }

        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;

        // 构造 logcat 参数：-s TAG:PRIORITY 等
        string filter = "";
        if (tags.Count > 0)
        {
            // 支持 标签:级别 或直接 标签
            filter = " -s " + string.Join(" ", tags.Select(t => t.Contains(':') ? t : t + ":*"));
        }

        string cmd = isHdc
            ? $"shell hilog {filter}"
            : $"logcat {filter}";
        string fullCmd = isHdc
            ? $"shell hilog"
            : $"logcat";

        Console.WriteLine($"{(isHdc ? "hdc (hilog)" : "adb logcat")} 实时日志，Ctrl+C 停止...");

        if (listOnly)
        {
            // 打印现有日志后退出（-d）
            string output = isHdc
                ? await FeaturesHelper.HdcCmd(device.Id, "shell hilog")
                : await FeaturesHelper.AdbCmd(device.Id, "logcat -d -v brief");
            if (outputFile != null)
            {
                File.WriteAllText(outputFile, output);
                Console.WriteLine($"已保存到: {outputFile}");
            }
            else
                Console.WriteLine(output);
            return 0;
        }

        if (outputFile != null)
        {
            // 重定向到文件，跑固定时长或 Ctrl+C
            using var outStream = new StreamWriter(outputFile, append: false);
            Console.WriteLine($"日志将写入: {outputFile}");
            try
            {
                await StreamAdbAsync(device, isHdc, fullCmd, outStream);
            }
            catch (OperationCanceledException)
            {
            }
            Console.WriteLine("已停止。");
        }
        else
        {
            try
            {
                await StreamAdbAsync(device, isHdc, fullCmd, null);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n已停止。");
            }
        }
        return 0;
    }

    /// <summary>
    /// 流式执行 adb 命令输出到控制台或文件。
    /// </summary>
    private static Task StreamAdbAsync(DeviceInfo device, bool isHdc, string cmd, TextWriter? writer)
    {
        return Task.Run(() =>
        {
            string exe = isHdc
                ? Path.Combine(Global.bin_path, "toolchains", "hdc" + (Global.System == "Windows" ? ".exe" : ""))
                : Path.Combine(Global.bin_path, "platform-tools", "adb" + (Global.System == "Windows" ? ".exe" : ""));
            string prefix = isHdc ? $"-t {device.Id} " : $"-s {device.Id} ";
            string args = prefix + cmd;
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi)!;
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    if (writer != null) writer.WriteLine(e.Data);
                    else Console.WriteLine(e.Data);
                }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    if (writer != null) writer.WriteLine(e.Data);
                    else Console.Error.WriteLine(e.Data);
                }
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            // 等待用户 Ctrl+C：读到 stdin EOF 表示用户按了 Ctrl+C，终止进程
            while (!proc.HasExited)
            {
                if (Console.IsInputRedirected) Thread.Sleep(200);
                else
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.C)
                    {
                        proc.Kill();
                        break;
                    }
                    Thread.Sleep(200);
                }
            }
            proc.WaitForExit();
            writer?.Flush();
        });
    }

    private static async Task<int> BugreportAsync(string[] args)
    {
        string? outputFile = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length) outputFile = args[++i];
        }
        if (outputFile == null)
        {
            // 默认生成到当前目录
            outputFile = Path.Combine(Directory.GetCurrentDirectory(), $"bugreport_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        }
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc)
        {
            Console.Error.WriteLine("OpenHarmony 设备不支持 bugreport，可用 diagnose 查看诊断。");
            return 1;
        }
        Console.WriteLine($"生成 bugreport 到 {outputFile} ...（可能需要几分钟）");
        string output = await FeaturesHelper.AdbCmd(device.Id, $"bugreport \"{outputFile}\"");
        Console.WriteLine(output);
        if (File.Exists(outputFile))
            Console.WriteLine($"完成: {outputFile} ({(new FileInfo(outputFile).Length / 1024)} KB)");
        return 0;
    }

    private static async Task<int> ForwardAsync(string mode, string[] args)
    {
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;
        if (isHdc)
        {
            // hdc 用 fport/rport
            string hcmd = mode == "forward" ? "fport" : "rport";
            if (args.Length == 0 || args[0] == "list")
            {
                string o = await FeaturesHelper.HdcCmd(device.Id, $"fport ls".Replace("fport", hcmd));
                Console.WriteLine(o);
                return 0;
            }
            // hdc fport 本地 设备
            if (args.Length >= 3 && args[0] == "add")
            {
                string local = args[1], remote = args[2];
                string o = await FeaturesHelper.HdcCmd(device.Id, $"{hcmd} {local} {remote}");
                Console.WriteLine(o);
                return 0;
            }
            Console.Error.WriteLine("hdc 转发用法: utoolbox forward add <本地端口> <设备端口>");
            return 1;
        }

        if (args.Length == 0 || args[0] == "list")
        {
            string o = await FeaturesHelper.AdbCmd(device.Id, $"{mode} --list");
            Console.WriteLine(o);
            return 0;
        }
        if (args.Length >= 1 && args[0] == "add" && args.Length >= 3)
        {
            string o = await FeaturesHelper.AdbCmd(device.Id, $"{mode} {args[1]} {args[2]}");
            Console.WriteLine(o);
            return 0;
        }
        if (args.Length >= 2 && args[0] == "remove")
        {
            string o = await FeaturesHelper.AdbCmd(device.Id, $"{mode} --remove {args[1]}");
            Console.WriteLine(o);
            return 0;
        }
        Console.Error.WriteLine($"用法: utoolbox {mode} add <本地> <设备> | list | remove <端口>");
        return 1;
    }

    private static async Task<int> DiagnoseAsync()
    {
        var (device, isHdc) = await CommandUtil.RequireAdbOrHdcAsync();
        if (device == null) return 1;

        Console.WriteLine("===== 设备诊断 =====");
        try
        {
            var info = await GetDevicesInfo.DevicesInfoLittle(device.Id);
            Console.WriteLine($"  设备: {device.Id} ({device.Transport})");
            Console.WriteLine($"  状态: {info.GetValueOrDefault("Status")}");
            Console.WriteLine($"  机型: {info.GetValueOrDefault("CodeName")}");
            Console.WriteLine($"  BL状态: {info.GetValueOrDefault("BLStatus")}");
        }
        catch { }

        if (isHdc)
        {
            Console.WriteLine($"  电池: {await FeaturesHelper.HdcCmd(device.Id, "shell hidumper -s BatteryService -a -i")}");
        }
        else
        {
            try { Console.WriteLine($"  电池: {await FeaturesHelper.AdbCmd(device.Id, "shell dumpsys battery")}"); } catch { }
            try { Console.WriteLine($"  存储: {await FeaturesHelper.AdbCmd(device.Id, "shell df -h /data")}"); } catch { }
            try { Console.WriteLine($"  网络: {await FeaturesHelper.AdbCmd(device.Id, "shell ping -c 2 223.5.5.5")}"); } catch { }
            try { Console.WriteLine($"  运行中进程数: {await FeaturesHelper.AdbCmd(device.Id, "shell ps | wc -l")}"); } catch { }
        }
        return 0;
    }
}