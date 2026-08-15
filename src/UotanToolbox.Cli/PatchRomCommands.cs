using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.PatchHelper;

namespace UotanToolbox.Cli;

/// <summary>
/// 从刷机包提取 boot 并修补 Root（Magisk/KernelSU）。组合 firmware 提取 + patch-boot。
/// </summary>
internal static class PatchRomCommands
{
    public static async Task<int> PatchRomAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                从刷机包提取并修补 Root:
                  utoolbox patch-rom <刷机包> --zip <Root包> [-o 输出目录] [--part boot|init_boot|vendor_boot] [--list]
                  utoolbox patch-rom <刷机包> --auto magisk|kernelsu [--mirror <镜像>] [--kernel <版本>] [-o 输出目录]

                参数:
                  <刷机包>   固件文件：payload / super.img / .ntpi / .nb0 / .ozip / .ops / .ofp / 线刷zip
                  --zip       Magisk.apk 或 KernelSU zip（自动识别 Magisk / GKI / LKM）
                  --auto      自动从 GitHub 下载最新 Magisk / KernelSU
                  --mirror    GitHub 镜像加速（如 https://ghfast.top/；patch-boot --list-mirrors 查看）
                  --kernel    KernelSU 内核版本筛选（如 android15-6.6）
                  --part      要修补的分区（默认自动选择 boot；可用 --list 查看可用分区）
                  --list      只列出固件中可修补的分区，不执行
                  -o          输出目录（默认当前目录）
                """);
            return 0;
        }

        string romFile = args.FirstOrDefault(a => !a.StartsWith("-")) ?? "";
        if (!File.Exists(romFile))
        {
            Console.Error.WriteLine($"刷机包不存在: {romFile}");
            return 1;
        }

        string? zipFile = null;
        string outputDir = Directory.GetCurrentDirectory();
        string? part = null;
        string? auto = null;
        string mirror = "";
        string? kernel = null;
        bool listOnly = args.Contains("--list");
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--zip" or "-z" when i + 1 < args.Length: zipFile = args[++i]; break;
                case "--auto" when i + 1 < args.Length: auto = args[++i]; break;
                case "--mirror" when i + 1 < args.Length: mirror = args[++i]; break;
                case "--kernel" when i + 1 < args.Length: kernel = args[++i]; break;
                case "--part" when i + 1 < args.Length: part = args[++i]; break;
                case "-o" when i + 1 < args.Length: outputDir = args[++i]; break;
            }
        }

        // 1. 列出固件中可修补的分区
        var service = new FirmwareExtractService();
        var kind = await service.DetectAsync(romFile);
        Console.WriteLine($"固件类型: {DescribeKind(kind)}");
        List<FirmwareExtractService.Partition> parts;
        if (kind == FirmwareExtractService.FileKind.Unknown)
        {
            // 回退：普通 zip 直接含 img，扫描 zip 内条目
            parts = await ListImagesFromZipAsync(romFile);
        }
        else
        {
            parts = await service.ListPartitionsAsync(romFile);
        }
        if (parts.Count == 0)
        {
            Console.Error.WriteLine("无法读取固件分区列表。");
            return 1;
        }

        var patchableParts = parts.Select(p => p.Name.ToLowerInvariant())
            .Where(n => n is "boot" or "init_boot" or "vendor_boot" or "boot_a" or "init_boot_a" or "vendor_boot_a" or "boot_b" or "init_boot_b" or "vendor_boot_b")
            .ToList();

        if (listOnly || patchableParts.Count == 0)
        {
            Console.WriteLine($"可修补的分区: {string.Join(", ", patchableParts.Count > 0 ? patchableParts : new List<string> { "(无)" })}");
            if (!listOnly) Console.WriteLine("可用 --part 指定分区后重试。");
            return 0;
        }

        // 2. 确定要修补的分区
        string target = part?.ToLowerInvariant() ?? "boot";
        if (!patchableParts.Contains(target))
        {
            Console.Error.WriteLine($"分区 {target} 不在固件中。可修补: {string.Join(", ", patchableParts)}");
            return 1;
        }
        Console.WriteLine($"选择分区: {target}");

        // 3. 提取该分区到临时目录
        string work = Path.Combine(Path.GetTempPath(), $"utoolbox_patchrom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        string extracted = Path.Combine(work, $"{target}.img");
        Console.WriteLine($"正在从固件提取 {target} ...");
        if (kind == FirmwareExtractService.FileKind.Unknown)
        {
            // 普通 zip 直接含 img
            extracted = ExtractImgFromZip(romFile, target, work);
        }
        else
        {
            int extractResult = await service.ExtractAsync(romFile, work, new[] { target });
            _ = extractResult;
        }
        if (!File.Exists(extracted))
        {
            // 搜索所有 .img（可能 _a/_b 命名不同）
            var candidates = Directory.GetFiles(work, "*.img", SearchOption.AllDirectories);
            if (candidates.Length == 0)
            {
                Console.Error.WriteLine("提取失败：未找到镜像。");
                Directory.Delete(work, true);
                return 1;
            }
            extracted = candidates[0];
        }

        // 4. 修补
        // --auto：自动下载 Root 包
        if (auto != null)
        {
            zipFile = await UotanToolbox.Cli.Program.AutoDownloadRootAsync(auto, mirror, kernel);
            if (zipFile == null)
            {
                Directory.Delete(work, true);
                return 1;
            }
        }
        if (zipFile == null)
        {
            Console.Error.WriteLine("缺少 --zip Root 包（Magisk.apk 或 KernelSU zip）。");
            Directory.Delete(work, true);
            return 1;
        }
        if (!File.Exists(zipFile))
        {
            Console.Error.WriteLine($"Root 包不存在: {zipFile}");
            Directory.Delete(work, true);
            return 1;
        }

        string patched = await PatchBootFileAsync(extracted, zipFile);
        if (patched == null || !File.Exists(patched))
        {
            Console.Error.WriteLine("修补失败。");
            Directory.Delete(work, true);
            return 1;
        }

        // 5. 输出
        string finalName = $"{target}-rooted.img";
        string outPath = Path.Combine(outputDir, finalName);
        Directory.CreateDirectory(outputDir);
        File.Copy(patched, outPath, true);
        Console.WriteLine($"修补完成: {outPath}");
        try { Directory.Delete(work, true); } catch { }
        return 0;
    }

    internal static string DescribeKind(FirmwareExtractService.FileKind kind)
    {
        return kind switch
        {
            FirmwareExtractService.FileKind.Payload => "Payload",
            FirmwareExtractService.FileKind.Super => "Super (LP)",
            FirmwareExtractService.FileKind.Ntpi => "NTPi",
            FirmwareExtractService.FileKind.Nb0 => "NB0",
            FirmwareExtractService.FileKind.Ozip => "OZIP (OPPO)",
            FirmwareExtractService.FileKind.Ops => "OPS (OPPO)",
            FirmwareExtractService.FileKind.Ofp => "OFP (OPPO)",
            _ => "未知",
        };
    }

    /// <summary>
    /// 从普通 zip（直接含 .img 的线刷包）列出镜像分区。
    /// </summary>
    private static Task<List<FirmwareExtractService.Partition>> ListImagesFromZipAsync(string zipPath)
    {
        var result = new List<FirmwareExtractService.Partition>();
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileNameWithoutExtension(e.Name);
                    if (name.Length > 0)
                        result.Add(new FirmwareExtractService.Partition(name.ToLowerInvariant(), e.Length));
                }
            }
        }
        catch
        {
        }
        return Task.FromResult(result);
    }

    /// <summary>
    /// 从普通 zip 提取指定分区 img 到输出目录。
    /// </summary>
    private static string ExtractImgFromZip(string zipPath, string partName, string outDir)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries)
            {
                var name = Path.GetFileNameWithoutExtension(e.Name);
                if (name.Equals(partName, StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                {
                    string dst = Path.Combine(outDir, $"{partName}.img");
                    e.ExtractToFile(dst, true);
                    return dst;
                }
            }
        }
        catch
        {
        }
        return "";
    }

    /// <summary>
    /// 用 Root 包修补 boot 镜像，返回修补后的路径。
    /// </summary>
    internal static async Task<string?> PatchBootFileAsync(string bootFile, string zipFile)
    {
        // 环境变量默认值
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
            return null;
        }
        Console.WriteLine($"boot: 版本={bootinfo.Version} KMI={bootinfo.KMI} 架构={bootinfo.Arch} 压缩={bootinfo.Compress}");

        Console.WriteLine("正在检测 Root 包...");
        var zipinfo = await PatchDetect.Patch_Detect(zipFile);
        if (!zipinfo.IsUseful || zipinfo.Mode == PatchMode.None)
        {
            Console.Error.WriteLine("无法识别的 Root 包类型。");
            return null;
        }
        Console.WriteLine($"Root 类型: {zipinfo.Mode}");

        Console.WriteLine("正在修补，这可能需要一些时间...");
        return zipinfo.Mode switch
        {
            PatchMode.Magisk => await MagiskPatch.Magisk_Patch_Mouzei(zipinfo, bootinfo),
            PatchMode.GKI => await KernelSUPatch.GKI_Patch(zipinfo, bootinfo),
            PatchMode.LKM => await KernelSUPatch.LKM_Patch(zipinfo, bootinfo),
            _ => null,
        };
    }
}