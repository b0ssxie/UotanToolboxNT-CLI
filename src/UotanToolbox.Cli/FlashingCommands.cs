using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Cli;

/// <summary>
/// 厂商线刷：小米线刷包、三星 Heimdall、MTK SP Flash Tool、高通 EDL(edl.py)。
/// 依赖外部二进制（heimdall / flash_tool / edl），需用户自备。
/// </summary>
internal static class FlashingCommands
{
    public static async Task<int> FlashAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                厂商线刷工具:

                [小米] utoolbox xiaomi <线刷包.tgz或目录> [--keep-data | --lock]
                      刷小米官方线刷包（解压后执行 flash_all*.bat）
                      --keep-data  保留数据（flash_all_except_storage）
                      --lock       刷完回锁 bootloader（flash_all_lock）

                [三星] utoolbox heimdall detect | print-pit | download-pit <文件> | flash [选项]
                      需安装 Heimdall（https://gitlab.com/BenjaminDobell/Heimdall）
                      flash 示例: utoolbox heimdall flash --BOOT boot.img --RECOVERY recovery.img

                [MTK] utoolbox spflash <scatter.txt> [--da <DA文件>] [--mode download|firmware-upgrade|format-download] [--com <端口>] [-b]
                      需安装 SP Flash Tool（https://spflashtool.com）

                [高通EDL] utoolbox edl printgpt [--loader <firehose.elf>]
                          utoolbox edl qfil <rawprogram.xml> <patch.xml> <images目录> [--loader <firehose.elf>]
                      需要 python + pip install edl（https://github.com/bkerler/edl）
                """);
            return 0;
        }

        string sub = args[0].ToLowerInvariant();
        return sub switch
        {
            "xiaomi" => await XiaomiAsync(args.Skip(1).ToArray()),
            "heimdall" => await HeimdallAsync(args.Skip(1).ToArray()),
            "spflash" => await SpFlashAsync(args.Skip(1).ToArray()),
            "edl" => await EdlAsync(args.Skip(1).ToArray()),
            _ => CommandUtil.Unknown("flash", args[0]),
        };
    }

    /// <summary>
    /// 小米线刷包：解压 tgz，执行 flash_all*.bat。
    /// </summary>
    private static async Task<int> XiaomiAsync(string[] args)
    {
        string src = args.FirstOrDefault(a => !a.StartsWith("-")) ?? "";
        if (!Directory.Exists(src) && !File.Exists(src))
        {
            Console.Error.WriteLine("线刷包路径不存在（需 .tgz 或已解压目录）。");
            return 1;
        }
        bool keepData = args.Contains("--keep-data");
        bool lockBl = args.Contains("--lock");

        // 解压目录
        string workDir;
        if (File.Exists(src) && src.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            workDir = Path.Combine(Path.GetTempPath(), $"utoolbox_xiaomi_{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDir);
            Console.WriteLine($"解压 {src} ...");
            // 用 7z 解 tgz（7z 在 Bin\7z）
            string sevenZip = Path.Combine(Global.bin_path, "7z", "7za.exe");
            if (!File.Exists(sevenZip))
            {
                Console.Error.WriteLine("未找到 7za.exe，无法解压 .tgz（也可先手动解压后传入目录）。");
                return 1;
            }
            string output = await CallExternalProgram.SevenZip($"x \"{src}\" -o\"{workDir}\" -y");
            // 找到 .bat 所在根目录
            workDir = FindXiaomiRoot(workDir, out _);
        }
        else
        {
            workDir = src;
        }

        // 选择脚本
        string scriptName = lockBl ? "flash_all_lock.bat"
            : keepData ? "flash_all_except_storage.bat"
            : "flash_all.bat";
        // 可能不存在，回退
        string script = FindScript(workDir, scriptName);
        if (script == null)
        {
            // 尝试 sh 变体
            string shName = lockBl ? "flash_all_lock.bat"
                : keepData ? "flash_all_except_storage.bat"
                : "flash_all.bat";
            var candidates = Directory.GetFiles(workDir, "flash_all*.bat", SearchOption.AllDirectories);
            if (candidates.Length == 0)
            {
                Console.Error.WriteLine($"未找到刷机脚本 (flash_all*.bat)，目录: {workDir}");
                return 1;
            }
            script = candidates.FirstOrDefault(c => Path.GetFileName(c).Contains(keepData ? "except_storage" : "")) ?? candidates[0];
        }

        string scriptDir = Path.GetDirectoryName(script)!;
        Console.WriteLine($"执行: {script}");
        Console.WriteLine("提示: 设备需进入 Fastboot (音量- + 电源) 并已解锁 BL。按 Enter 开始...");
        Console.ReadLine();
        return RunBatch(script, scriptDir);
    }

    private static string FindXiaomiRoot(string dir, out string root)
    {
        // tgz 解压后可能有一层目录，找到含 images + flash_all*.bat 的目录
        var candidates = Directory.GetDirectories(dir);
        foreach (var c in candidates)
        {
            if (Directory.Exists(Path.Combine(c, "images")) &&
                Directory.GetFiles(c, "flash_all*.bat").Length > 0)
            {
                root = c;
                return c;
            }
        }
        root = dir;
        return dir;
    }

    private static string? FindScript(string dir, string name)
    {
        var c = Directory.GetFiles(dir, name, SearchOption.AllDirectories);
        if (c.Length > 0) return c[0];
        return null;
    }

    private static int RunBatch(string script, string workDir)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();
        return proc.ExitCode;
    }

    /// <summary>
    /// 三星 Heimdall。
    /// </summary>
    private static async Task<int> HeimdallAsync(string[] args)
    {
        string binary = FindBinary("heimdall");
        if (binary == null)
        {
            Console.Error.WriteLine("未找到 heimdall。请从 https://gitlab.com/BenjaminDobell/Heimdall 下载并加入 PATH，或放到 Bin 目录。");
            return 1;
        }
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox heimdall detect | print-pit | download-pit <文件> | flash [--分区 文件 ...] [--repartition]");
            return 1;
        }
        string action = args[0];
        switch (action)
        {
            case "detect":
                return RunExe(binary, "detect");
            case "print-pit":
                return RunExe(binary, "print-pit");
            case "download-pit":
                if (args.Length < 2) { Console.Error.WriteLine("用法: heimdall download-pit <输出.pit>"); return 1; }
                return RunExe(binary, $"download-pit --output \"{args[1]}\"");
            case "flash":
                {
                    // 拼装 flash 参数: --分区 文件 ...
                    var parts = args.Skip(1).ToArray();
                    bool repartition = parts.Contains("--repartition");
                    var flashArgs = new System.Collections.Generic.List<string> { "flash" };
                    if (repartition) flashArgs.Add("--repartition");
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].StartsWith("--"))
                        {
                            if (parts[i] == "--repartition" || parts[i] == "--no-reboot" || parts[i] == "--tflash") continue;
                            string part = parts[i].TrimStart('-');
                            if (i + 1 < parts.Length && !parts[i + 1].StartsWith("--"))
                            {
                                flashArgs.Add($"--{part}");
                                flashArgs.Add($"\"{parts[i + 1]}\"");
                                i++;
                            }
                        }
                    }
                    return RunExe(binary, string.Join(" ", flashArgs));
                }
            default:
                Console.Error.WriteLine("未知 heimdall 子命令。");
                return 1;
        }
    }

    /// <summary>
    /// MTK SP Flash Tool。
    /// </summary>
    private static async Task<int> SpFlashAsync(string[] args)
    {
        string scatter = args.FirstOrDefault(a => !a.EndsWith(".txt") && !a.EndsWith(".TXT")) ?? "";
        string? da = null, com = null;
        string mode = "download";
        bool reboot = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--da" when i + 1 < args.Length: da = args[++i]; break;
                case "--mode" when i + 1 < args.Length: mode = args[++i]; break;
                case "--com" when i + 1 < args.Length: com = args[++i]; break;
                case "-b": reboot = true; break;
            }
        }
        // 重新找 scatter（可能是第一个参数）
        if (scatter == "")
        {
            var f = args.FirstOrDefault(a => a.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
            scatter = f ?? "";
        }
        if (scatter == "" || !File.Exists(scatter))
        {
            Console.Error.WriteLine("用法: utoolbox spflash <scatter.txt> [--da DA] [--mode download|firmware-upgrade|format-download] [--com COM] [-b]");
            return 1;
        }

        string binary = FindBinary("flash_tool");
        if (binary == null)
        {
            Console.Error.WriteLine("未找到 flash_tool。请从 https://spflashtool.com 下载 SP Flash Tool 并加入 PATH 或放入 Bin 目录。");
            return 1;
        }

        var cmdParts = new System.Collections.Generic.List<string>
        {
            $"-s \"{scatter}\"", $"-c {mode}"
        };
        if (da != null) cmdParts.Add($"-d \"{da}\"");
        if (com != null) cmdParts.Add($"-p {com}");
        if (reboot) cmdParts.Add("-b");
        return RunExe(binary, string.Join(" ", cmdParts));
    }

    /// <summary>
    /// 高通 EDL (edl.py)。
    /// </summary>
    private static async Task<int> EdlAsync(string[] args)
    {
        string action = args.FirstOrDefault(a => !a.StartsWith("-") && a is "printgpt" or "reset" or "qfil") ?? (args.Length > 0 ? args[0] : "");
        if (action is "" or "help")
        {
            Console.WriteLine("""
                用法:
                  utoolbox edl printgpt [--loader <firehose.elf>]
                  utoolbox edl reset [--loader <firehose.elf>]
                  utoolbox edl qfil <rawprogram.xml> <patch.xml> <images目录> [--loader <firehose.elf>]

                需要: python + pip install edl（https://github.com/bkerler/edl）
                设备需处于 9008 EDL 模式（utoolbox reboot edl）。
                """);
            return 1;
        }

        string? loader = null;
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--loader" && i + 1 < args.Length) loader = args[i + 1];

        string cmd;
        if (action == "printgpt" || action == "reset")
        {
            cmd = $"{action}" + (loader != null ? $" --loader \"{loader}\"" : "");
        }
        else if (action == "qfil")
        {
            // qfil rawprogram patch images
            var nonFlag = args.Skip(1).Where(a => !a.StartsWith("-")).ToArray();
            if (nonFlag.Length < 3)
            {
                Console.Error.WriteLine("用法: edl qfil <rawprogram.xml> <patch.xml> <images目录> [--loader firehose]");
                return 1;
            }
            cmd = $"qfil \"{nonFlag[0]}\" \"{nonFlag[1]}\" \"{nonFlag[2]}\"" + (loader != null ? $" --loader \"{loader}\"" : "");
        }
        else
        {
            Console.Error.WriteLine($"未知 edl 子命令: {action}");
            return 1;
        }

        string binary = FindBinary("edl");
        if (binary == null)
        {
            Console.Error.WriteLine("未找到 edl。请安装: pip install edl，确认 edl 命令可用后重试。");
            return 1;
        }
        Console.WriteLine($"执行: edl {cmd}");
        return RunExe(binary, cmd);
    }

    /// <summary>
    /// 查找外部二进制：PATH 或 Bin 目录。
    /// </summary>
    private static string? FindBinary(string name)
    {
        string exe = name + ".exe";
        // 检查 Bin 目录
        string binPaths = Path.Combine(Global.bin_path, name);
        if (File.Exists(binPaths)) return binPaths;
        if (File.Exists(binPaths + ".exe")) return binPaths + ".exe";
        // PATH
        try
        {
            var psi = new ProcessStartInfo("where.exe", exe)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            if (o.Length > 0) return o.Split('\n')[0].Trim();
        }
        catch { }
        return null;
    }

    private static int RunExe(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();
        return proc.ExitCode;
    }
}