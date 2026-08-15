using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.Devices;
using UotanToolbox.Common.PatchHelper;
using UotanToolbox.Common.ROMHelper;

namespace UotanToolbox.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            // 初始化全局路径
            InitializeGlobal();

            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "help" or "-h" or "--help" => PrintHelp(),
                "devices" => await DevicesAsync(),
                "info" => await InfoAsync(),
                "reboot" => await RebootAsync(args.Skip(1).ToArray()),
                "adb" => await AdbAsync(args.Skip(1).ToArray()),
                "exec" => await ExecAsync(args.Skip(1).ToArray()),
                "extract" => await ExtractAsync(args.Skip(1).ToArray()),
                "payload-parts" => await PayloadPartsAsync(args.Skip(1).ToArray()),
                "patch-boot" => await PatchBootAsync(args.Skip(1).ToArray()),
                "flash" => await FlashAsync(args.Skip(1).ToArray()),
                "unlock" => await UnlockAsync(args.Skip(1).ToArray()),
                "lock" => await LockAsync(args.Skip(1).ToArray()),
                "erase" => await EraseAsync(args.Skip(1).ToArray()),
                "set-active" => await SetActiveAsync(args.Skip(1).ToArray()),
                "wipe-super" => await WipeSuperAsync(),
                "flash-all" => await FlashAllAsync(args.Skip(1).ToArray()),
                "app" => await AppCommands.AppAsync(args.Skip(1).ToArray()),
                "file" => await FileCommands.FileAsync(args.Skip(1).ToArray()),
                "display" => await DisplayCommands.DisplayAsync(args.Skip(1).ToArray()),
                "scrcpy" => await ScrcpyCommands.ScrcpyAsync(args.Skip(1).ToArray()),
                "partition" => await PartitionCommands.PartitionAsync(args.Skip(1).ToArray()),
                "qcn" => await QcnCommands.QcnAsync(args.Skip(1).ToArray()),
                "wireless" => await WirelessCommands.WirelessAsync(args.Skip(1).ToArray()),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    private static void InitializeGlobal()
    {
        Global.runpath = AppContext.BaseDirectory;
        Global.tmp_path = Path.GetTempPath();
        if (OperatingSystem.IsLinux())
        {
            if (RuntimeInformation.OSArchitecture == Architecture.X64)
                Global.System = "Linux_AMD64";
            else if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                Global.System = "Linux_AArch64";
            else if (RuntimeInformation.OSArchitecture == Architecture.LoongArch64)
                Global.System = "Linux_LoongArch64";
            Global.log_path = Global.tmp_path;
        }
        else if (OperatingSystem.IsMacOS())
        {
            Global.System = "macOS";
            Global.log_path = Path.Combine(Global.runpath, "Log");
        }
        else if (OperatingSystem.IsWindows())
        {
            Global.System = "Windows";
            Global.log_path = Path.Combine(Global.runpath, "Log");
        }
        if (!Directory.Exists(Global.log_path))
            Directory.CreateDirectory(Global.log_path);

        Global.bin_path = Path.Combine(Global.runpath, "Bin");
        Global.backup_path = Path.Combine(Global.runpath, "Backup");
        if (!Directory.Exists(Global.backup_path))
            Directory.CreateDirectory(Global.backup_path);

        Global.serviceID = "uotan-" + StringHelper.RandomString(8);
        Global.password = StringHelper.RandomString(8);

        if (File.Exists(Path.Combine(Global.bin_path, "MagiskPatcher.csv")))
            Global.BootPatchPath = Path.Combine(Global.bin_path, "MagiskPatcher.csv");
        if (File.Exists(Path.Combine(Global.runpath, "APK", "Magisk-v30.6.apk")))
            Global.MagiskAPKPath = Path.Combine(Global.runpath, "APK", "Magisk-v30.6.apk");

        // 初始化设备管理器（adb / fastboot / hdc / edl）
        Global.DeviceManager = new DeviceManager(new IDeviceTransport[]
        {
            new AdbTransport(),
            new FastbootTransport(),
            new HdcTransport(),
            new EdlTransport(),
        });
    }

    private static DeviceInfo? FindDevice(string? deviceId, TransportType? type = null)
    {
        if (Global.DeviceManager == null) return null;
        var devices = Global.DeviceManager.Devices;
        if (!string.IsNullOrWhiteSpace(deviceId))
            return devices.FirstOrDefault(d => d.Id == deviceId && (type == null || d.Transport == type));
        if (type != null)
            return devices.FirstOrDefault(d => d.Transport == type);
        return devices.FirstOrDefault();
    }

    private static async Task<int> DevicesAsync()
    {
        await Global.DeviceManager!.ScanAsync();
        var devices = Global.DeviceManager.Devices;
        if (devices.Count == 0)
        {
            Console.WriteLine("未发现设备。");
            return 0;
        }
        Console.WriteLine("设备列表:");
        foreach (var d in devices)
        {
            string extra = d.Properties.TryGetValue("State", out var s) ? $" [{s}]" : "";
            Console.WriteLine($"  {d.Id,-30} {d.Transport}{extra}");
        }
        return 0;
    }

    private static async Task<int> InfoAsync()
    {
        if (Global.DeviceManager == null) return 1;
        await Global.DeviceManager.ScanAsync();

        // 简化参数：info [设备ID]
        // 未来可加 --json 输出
        var devices = Global.DeviceManager.Devices;
        if (devices.Count == 0)
        {
            Console.WriteLine("未发现设备。");
            return 0;
        }
        foreach (var d in devices)
        {
            Console.WriteLine($"===== 设备 {d.Id} ({d.Transport}) =====");
            try
            {
                var info = await GetDevicesInfo.DevicesInfo(d.Id);
                foreach (var kv in info)
                    Console.WriteLine($"  {kv.Key}: {kv.Value}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  获取信息失败: {ex.Message}");
            }
        }
        return 0;
    }

    private static async Task<int> RebootAsync(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "system";
        await Global.DeviceManager!.ScanAsync();
        var device = FindDevice(Global.thisdevice);
        if (device == null)
        {
            Console.Error.WriteLine("未发现设备，请先连接。");
            return 1;
        }

        var (t, cmd) = device.Transport switch
        {
            TransportType.Adb => mode switch
            {
                "system" => ("adb", "reboot"),
                "recovery" => ("adb", "reboot recovery"),
                "bootloader" => ("adb", "reboot bootloader"),
                "fastboot" => ("adb", "reboot fastboot"),
                "edl" => ("adb", "reboot edl"),
                "poweroff" => ("adb", "reboot -p"),
                _ => ("adb", $"reboot {mode}"),
            },
            TransportType.Fastboot => mode switch
            {
                "system" => ("fastboot", "reboot"),
                "recovery" => ("fastboot", "oem reboot-recovery"),
                "bootloader" => ("fastboot", "reboot-bootloader"),
                "fastboot" => ("fastboot", "reboot-fastboot"),
                "edl" => ("fastboot", "oem edl"),
                "poweroff" => ("fastboot", "oem poweroff"),
                _ => ("fastboot", $"reboot {mode}"),
            },
            TransportType.Hdc => mode switch
            {
                "system" => ("hdc", "target boot"),
                "recovery" => ("hdc", "target boot -recovery"),
                "bootloader" => ("hdc", "target boot -bootloader"),
                "fastboot" => ("hdc", "target boot -fastboot"),
                "edl" => ("hdc", "target boot -edl"),
                "poweroff" => ("hdc", "target boot shutdown"),
                _ => ("hdc", $"target boot {mode}"),
            },
            _ => (null, null),
        };

        if (cmd == null)
        {
            Console.Error.WriteLine($"当前设备模式 {device.Transport} 不支持此操作。");
            return 1;
        }

        Console.WriteLine($"[{t}] {cmd}");
        string output = t switch
        {
            "adb" => await FeaturesHelper.AdbCmd(device.Id, cmd),
            "fastboot" => await FeaturesHelper.FastbootCmd(device.Id, cmd),
            "hdc" => await FeaturesHelper.HdcCmd(device.Id, cmd),
            _ => string.Empty,
        };
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> AdbAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox adb <adb参数...>  （例如 utoolbox adb shell getprop ro.product.device）");
            return 1;
        }
        string output = await FeaturesHelper.AdbCmd("", string.Join(" ", args));
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> ExecAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox exec <设备命令>");
            return 1;
        }
        string deviceId = args[0];
        string cmd = string.Join(" ", args.Skip(1));
        Console.WriteLine(await FeaturesHelper.AdbCmd(deviceId, cmd));
        return 0;
    }

    private static async Task<int> PayloadPartsAsync(string[] args)
    {
        // 用法: payload-parts <payload.bin>
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox payload-parts <payload.bin 或含 payload 的 zip>");
            return 1;
        }
        string file = args[0];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"文件不存在: {file}");
            return 1;
        }
        var parts = await PayloadParser.GetPartitionInfoAsync(file);
        Console.WriteLine($"共 {parts.Count} 个分区:");
        foreach (var p in parts)
            Console.WriteLine($"  {p.Name,-32} {p.SizeReadable}");
        return 0;
    }

    private static async Task<int> ExtractAsync(string[] args)
    {
        // 用法: extract <payload.bin> [-o 输出目录] [分区名...]
        // 不带分区名则全部提取
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox extract <payload.bin 或含 payload 的 zip> [-o 输出目录] [分区名...]");
            return 1;
        }
        string file = args[0];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"文件不存在: {file}");
            return 1;
        }
        string outputDir = Directory.GetCurrentDirectory();
        var partitionNames = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-o" || args[i] == "--output")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("缺少 -o 后的输出目录");
                    return 1;
                }
                outputDir = args[++i];
            }
            else
            {
                partitionNames.Add(args[i]);
            }
        }
        Console.WriteLine($"开始提取，输出目录: {outputDir}");
        await PayloadParser.ExtractSelectedPartitionsAsync(file, partitionNames.Count > 0 ? [.. partitionNames] : null, outputDir);
        Console.WriteLine("提取完成。");
        return 0;
    }

    private static async Task<int> PatchBootAsync(string[] args)
    {
        // 用法: patch-boot <boot.img> --zip <Magisk/GKI/LKM zip> [-o 输出文件]
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox patch-boot <boot.img> --zip <Magisk/GKI/LKM 包> [-o 输出文件]");
            return 1;
        }
        string bootFile = args[0];
        string? zipFile = null;
        string? output = null;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--zip" or "-z":
                    if (i + 1 < args.Length) zipFile = args[++i];
                    break;
                case "--out" or "-o":
                    if (i + 1 < args.Length) output = args[++i];
                    break;
            }
        }
        if (!File.Exists(bootFile))
        {
            Console.Error.WriteLine($"boot 文件不存在: {bootFile}");
            return 1;
        }
        if (zipFile == null || !File.Exists(zipFile))
        {
            Console.Error.WriteLine($"缺少有效的 --zip 参数（Magisk/GKI/LKM 包）");
            return 1;
        }

        // 环境变量（来自 GUI 的默认值）
        EnvironmentVariable.KEEPVERITY = true;
        EnvironmentVariable.KEEPFORCEENCRYPT = true;
        EnvironmentVariable.PATCHVBMETAFLAG = false;
        EnvironmentVariable.RECOVERYMODE = false;
        EnvironmentVariable.LEGACYSAR = true;
        EnvironmentVariable.PREINITDEVICE = "";

        Console.WriteLine("正在检测 boot 镜像...");
        var bootinfo = await ImageDetect.Boot_Detect(bootFile);
        if (bootinfo.IsUseful != true)
        {
            Console.Error.WriteLine("boot 镜像无效或解析失败。");
            return 1;
        }
        Console.WriteLine($"boot 信息: 版本={bootinfo.Version} KMI={bootinfo.KMI} 架构={bootinfo.Arch} 压缩={bootinfo.Compress}");

        Console.WriteLine("正在检测修补包...");
        var zipinfo = await PatchDetect.Patch_Detect(zipFile);
        if (!zipinfo.IsUseful || zipinfo.Mode == PatchMode.None)
        {
            Console.Error.WriteLine("无法识别的修补包类型。");
            return 1;
        }
        Console.WriteLine($"修补类型: {zipinfo.Mode}");

        Console.WriteLine("正在修补 boot 镜像，这可能需要一些时间...");
        string newboot = zipinfo.Mode switch
        {
            PatchMode.Magisk => await MagiskPatch.Magisk_Patch_Mouzei(zipinfo, bootinfo),
            PatchMode.GKI => await KernelSUPatch.GKI_Patch(zipinfo, bootinfo),
            PatchMode.LKM => await KernelSUPatch.LKM_Patch(zipinfo, bootinfo),
            _ => throw new InvalidOperationException($"不支持的修补类型: {zipinfo.Mode}"),
        };

        if (output != null)
        {
            File.Copy(newboot, output, true);
            Console.WriteLine($"修补完成，已保存到: {output}");
        }
        else
        {
            Console.WriteLine($"修补完成: {newboot}");
        }
        return 0;
    }

    // ---- 刷机相关命令（Fastboot）----

    private static async Task<DeviceInfo?> RequireFastbootAsync()
    {
        await Global.DeviceManager!.ScanAsync();
        var fb = FindDevice(Global.thisdevice, TransportType.Fastboot);
        if (fb == null)
        {
            // 尝试在没有 -s 的情况下调用 fastboot devices 确认
            Console.Error.WriteLine("未发现 Fastboot 设备，请先进入 Fastboot 模式并连接。");
        }
        return fb;
    }

    private static async Task<int> FlashAsync(string[] args)
    {
        // 用法: flash <分区> <文件> [--device <id>]
        if (args.Length < 2)
        {
            Console.WriteLine("用法: utoolbox flash <分区名> <镜像文件> [--device <id>]");
            Console.WriteLine("示例: utoolbox flash boot boot-patched.img");
            return 1;
        }
        string partition = args[0];
        string file = args[1];
        string? device = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--device" && i + 1 < args.Length)
                device = args[++i];
        }
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"文件不存在: {file}");
            return 1;
        }
        var fb = await RequireFastbootAsync();
        if (fb == null) return 1;
        string id = device ?? fb.Id;

        Console.WriteLine($"刷入 {partition} <- {file}");
        string output = await FeaturesHelper.FastbootCmd(id, $"flash {partition} \"{file}\"");
        Console.WriteLine(output);
        if (output.Contains("FAILED") || output.Contains("error"))
        {
            Console.Error.WriteLine("刷入失败。");
            return 1;
        }
        Console.WriteLine("刷入成功。");
        return 0;
    }

    private static async Task<int> UnlockAsync(string[] args)
    {
        // 用法: unlock [--file <unlock文件>] [--code <解锁码>]
        // 无参数时依次尝试通用解锁命令
        string? file = null, code = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--file" when i + 1 < args.Length: file = args[++i]; break;
                case "--code" when i + 1 < args.Length: code = args[++i]; break;
            }
        }
        var fb = await RequireFastbootAsync();
        if (fb == null) return 1;
        string id = fb.Id;

        if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(code))
        {
            Console.WriteLine($"刷入解锁文件并用解锁码解锁...");
            string o = await FeaturesHelper.FastbootCmd(id, $"flash unlock \"{file}\"");
            Console.WriteLine(o);
        }
        else if (!string.IsNullOrEmpty(file))
        {
            Console.WriteLine($"刷入解锁文件并执行 oem unlock-go...");
            string o = await FeaturesHelper.FastbootCmd(id, $"flash unlock \"{file}\"");
            Console.WriteLine(o);
            o = await FeaturesHelper.FastbootCmd(id, "oem unlock-go");
            Console.WriteLine(o);
        }
        else if (!string.IsNullOrEmpty(code))
        {
            Console.WriteLine($"使用解锁码 oem unlock {code} ...");
            string o = await FeaturesHelper.FastbootCmd(id, $"oem unlock {code}");
            Console.WriteLine(o);
        }
        else
        {
            string[] simple = ["oem unlock", "oem unlock-go", "flashing unlock", "flashing unlock_critical"];
            bool success = false;
            foreach (var cmd in simple)
            {
                Console.WriteLine($"[尝试] {cmd}");
                string o = await FeaturesHelper.FastbootCmd(id, cmd);
                Console.WriteLine(o);
                if (!o.Contains("FAILED") && !o.Contains("error") && !o.Contains("unknown command"))
                {
                    success = true;
                    break;
                }
            }
            if (!success)
            {
                Console.Error.WriteLine("所有通用解锁命令均失败，可能需要解锁文件或解锁码。");
                Console.Error.WriteLine("用法: utoolbox unlock --file <文件>  或  utoolbox unlock --code <解锁码>");
                return 1;
            }
        }
        Console.WriteLine("解锁完成。");
        return 0;
    }

    private static async Task<int> LockAsync(string[] args)
    {
        var fb = await RequireFastbootAsync();
        if (fb == null) return 1;
        string id = fb.Id;
        Console.WriteLine("上锁会清除设备数据，请谨慎操作！");
        string o = await FeaturesHelper.FastbootCmd(id, "oem lock-go");
        Console.WriteLine(o);
        if (o.Contains("FAILED") || o.Contains("error") || o.Contains("unknown command"))
        {
            Console.WriteLine("oem lock-go 失败，尝试 flashing lock...");
            o = await FeaturesHelper.FastbootCmd(id, "flashing lock");
            Console.WriteLine(o);
        }
        Console.WriteLine("上锁完成。");
        return 0;
    }

    private static async Task<int> EraseAsync(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("用法: utoolbox erase <分区名> [--device <id>]");
            return 1;
        }
        string partition = args[0];
        string? device = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--device" && i + 1 < args.Length)
                device = args[++i];
        }
        var fb = await RequireFastbootAsync();
        if (fb == null) return 1;
        string id = device ?? fb.Id;
        Console.WriteLine($"擦除 {partition} ...");
        string output = await FeaturesHelper.FastbootCmd(id, $"erase {partition}");
        Console.WriteLine(output);
        if (output.Contains("FAILED") || output.Contains("error"))
        {
            Console.Error.WriteLine("擦除失败。");
            return 1;
        }
        Console.WriteLine("擦除完成。");
        return 0;
    }

    private static async Task<int> SetActiveAsync(string[] args)
    {
        if (args.Length < 1 || (args[0] != "a" && args[0] != "b" && args[0] != "other"))
        {
            Console.WriteLine("用法: utoolbox set-active <a|b|other>");
            return 1;
        }
        var fb = await RequireFastbootAsync();
        if (fb == null) return 1;
        Console.WriteLine($"切换活动槽位到 {args[0]} ...");
        string output = await FeaturesHelper.FastbootCmd(fb.Id, $"set_active {args[0]}");
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> WipeSuperAsync()
    {
        var fb = await RequireFastbootAsync();
        if (fb == null) return 1;
        Console.WriteLine("刷入 super_empty 镜像（wipe-super）...");
        string output = await FeaturesHelper.FastbootCmd(fb.Id, "wipe-super");
        Console.WriteLine(output);
        if (output.Contains("FAILED") || output.Contains("error"))
        {
            Console.Error.WriteLine("操作失败。");
            return 1;
        }
        Console.WriteLine("完成。");
        return 0;
    }

    private static async Task<int> FlashAllAsync(string[] args)
    {
        // 用法: flash-all [--fastboot <fastboot.txt>] [--fastbootd <fastbootd.txt>] [--skip-model] [--add-root] [--disable-vbmeta] [--erase-data]
        string? fastbootTxt = null, fastbootdTxt = null;
        bool skipModel = false, addRoot = false, disableVbmeta = false, eraseData = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--fastboot" when i + 1 < args.Length: fastbootTxt = args[++i]; break;
                case "--fastbootd" when i + 1 < args.Length: fastbootdTxt = args[++i]; break;
                case "--skip-model": skipModel = true; break;
                case "--add-root": addRoot = true; break;
                case "--disable-vbmeta": disableVbmeta = true; break;
                case "--erase-data": eraseData = true; break;
            }
        }

        if (fastbootTxt == null && fastbootdTxt == null)
        {
            Console.WriteLine("""
                用法: utoolbox flash-all --fastboot <fastboot.txt> --fastbootd <fastbootd.txt> [选项]

                选项:
                  --skip-model          跳过机型校验
                  --add-root            对 boot 分区注入 Root (Magisk/KSU)
                  --disable-vbmeta      刷 vbmeta 时附带禁用参数
                  --erase-data          完成后擦除 metadata 和 userdata
                  --fastboot <txt>      fastboot 阶段脚本
                  --fastbootd <txt>     fastbootd 阶段脚本
                """);
            return 1;
        }
        if (fastbootTxt != null && !File.Exists(fastbootTxt))
        {
            Console.Error.WriteLine($"文件不存在: {fastbootTxt}");
            return 1;
        }
        if (fastbootdTxt != null && !File.Exists(fastbootdTxt))
        {
            Console.Error.WriteLine($"文件不存在: {fastbootdTxt}");
            return 1;
        }

        var fb = await RequireFastbootAsync();
        if (fb == null) return 1;

        // 获取设备机型用于校验
        string? codename = null;
        if (!skipModel)
        {
            try
            {
                var info = await GetDevicesInfo.DevicesInfoLittle(fb.Id);
                codename = info.TryGetValue("CodeName", out var v) ? v : null;
                Console.WriteLine($"设备机型: {codename}");
            }
            catch
            {
                Console.Error.WriteLine("无法获取设备机型，请用 --skip-model 跳过校验。");
                return 1;
            }
        }

        Console.WriteLine("开始 TXT 双包刷机...");
        var service = new WiredFlashService(
            fb.Id,
            addRoot: addRoot,
            disableVbmeta: disableVbmeta,
            eraseData: eraseData,
            setBoot: "boot",
            magiskApkPath: Global.MagiskAPKPath,
            log: s => Console.WriteLine(s));

        bool success = await service.FlashAsync(fastbootTxt, fastbootdTxt, codename);
        if (success)
        {
            Console.WriteLine("刷机成功！");
            return 0;
        }
        Console.Error.WriteLine("刷机失败，请检查上方日志。");
        return 1;
    }


    private static int PrintHelp()
    {
        Console.WriteLine("""
            UotanToolbox CLI — Android & OpenHarmony 设备命令行工具箱

            用法: utoolbox <命令> [参数]

            设备:
              devices                       列出已连接设备
              info                          显示设备详细信息

            重启:
              reboot [mode]                 重启设备 (mode: system|recovery|bootloader|fastboot|edl|poweroff)

            固件解包:
              payload-parts <payload.bin>   列出 payload 中的分区
              extract <payload.bin> [-o 目录] [分区名...]   提取分区（不带分区名则全部）

            Root 修补:
              patch-boot <boot.img> --zip <包> [-o 输出]    用 Magisk/GKI/LKM 修补 boot

            刷机 (需进入 Fastboot 模式):
              flash <分区> <镜像>          刷入单个分区
              erase <分区>                 擦除分区
              wipe-super                   刷入 super_empty
              set-active <a|b|other>       切换活动槽位
              unlock [--file <文件>] [--code <解锁码>]   解锁 Bootloader
              lock                         上锁 Bootloader
              flash-all --fastboot <txt> [--fastbootd <txt>] [选项]   TXT 双包刷机

            通用:
              adb <adb参数...>              直接执行 adb 命令
              exec <设备ID> <命令>          对指定 adb 设备执行命令

            """);
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"未知命令: {cmd}");
        PrintHelp();
        return 1;
    }
}

