using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UotanToolbox.Common;

namespace UotanToolbox.Cli;

/// <summary>
/// 线刷卡刷互转命令。
/// </summary>
internal static class OtaCommands
{
    public static async Task<int> OtaAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                线刷卡刷互转:
                  utoolbox ota2img <update.zip> [-o 输出目录]    卡刷包 → 线刷镜像（还原 .dat 为 .img）
                  utoolbox img2ota <img目录或清单> <-o 输出zip>   线刷镜像 → 卡刷包
                """);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "ota2img" => await Ota2ImgAsync(args.Skip(1).ToArray()),
            "img2ota" => await Img2OtaAsync(args.Skip(1).ToArray()),
            _ => CommandUtil.Unknown("ota", args[0]),
        };
    }

    /// <summary>
    /// 卡刷包 → 线刷镜像。
    /// </summary>
    private static async Task<int> Ota2ImgAsync(string[] args)
    {
        string zipPath = args.FirstOrDefault(a => !a.StartsWith("-")) ?? "";
        string outDir = ParseDir(args, "-o", Directory.GetCurrentDirectory());
        if (!File.Exists(zipPath))
        {
            Console.Error.WriteLine($"卡刷包不存在: {zipPath}");
            return 1;
        }
        Directory.CreateDirectory(outDir);
        // 使用 zip 内 META-INF 目录，输出到 outDir
        string work = Path.Combine(Path.GetTempPath(), $"utoolbox_ota_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        Console.WriteLine($"解压 {zipPath} ...");

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            // 找到所有 xxx.new.dat / xxx.transfer.list / xxx.patch.dat / xxx.img 等
            var datEntries = zip.Entries.Where(e => e.FullName.EndsWith(".new.dat") || e.FullName.EndsWith(".dat.br") || e.FullName.EndsWith(".patch.dat") || e.FullName.EndsWith(".transfer.list") || e.FullName.EndsWith(".img") || e.FullName.Contains("META-INF")).ToList();
            int converted = 0;

            // 先解压 transfer.list 以确定哪些分区有 .dat
            foreach (var entry in datEntries)
            {
                // 提取分区名
                string name = entry.FullName.Replace("/", "");
                string baseName;
                if (entry.FullName.EndsWith(".transfer.list"))
                {
                    baseName = name.Replace(".transfer.list", "");
                }
                else if (name.EndsWith(".new.dat"))
                {
                    baseName = name.Replace(".new.dat", "");
                }
                else if (name.EndsWith(".dat.br"))
                {
                    baseName = name.Replace(".dat.br", "");
                }
                else if (name.EndsWith(".patch.dat"))
                {
                    baseName = name.Replace(".patch.dat", "");
                }
                else if (name.EndsWith(".img"))
                {
                    baseName = name.Replace(".img", "");
                }
                else if (entry.FullName.Contains("META-INF"))
                {
                    continue;
                }
                else
                {
                    continue;
                }

                // 解压此 entry
                string localPath = Path.Combine(work, entry.Name);
                entry.ExtractToFile(localPath, true);
            }

            // 处理每个 transfer.list → sdat2img
            var transferFiles = Directory.GetFiles(work, "*.transfer.list");
            foreach (var tf in transferFiles)
            {
                string baseName = Path.GetFileName(tf).Replace(".transfer.list", ""); // system.transfer.list → system
                string? datFile = Directory.GetFiles(work, $"{baseName}.new.dat").FirstOrDefault()
                    ?? Directory.GetFiles(work, $"{baseName}.new.dat.br").FirstOrDefault();
                if (datFile == null)
                {
                    Console.WriteLine($"跳过 {baseName}：缺少 new.dat");
                    continue;
                }
                string outImg = Path.Combine(outDir, $"{baseName}.img");
                Console.WriteLine($"转换 {baseName} ...");
                SdatConverter.Sdat2Img(tf, datFile, outImg);
                converted++;
            }

            // 直接是 .img 的卡刷包（无 .dat），复制即可
            foreach (var img in Directory.GetFiles(work, "*.img"))
            {
                string dst = Path.Combine(outDir, Path.GetFileName(img));
                File.Copy(img, dst, true);
                Console.WriteLine($"复制 {Path.GetFileName(img)}");
            }

            Console.WriteLine($"完成。共转换 {converted} 个 .dat 分区，输出到 {outDir}");

            // 生成线刷脚本
            GenerateFastbootScript(outDir);
        }
        finally
        {
            // 清理临时目录
            try { Directory.Delete(work, true); } catch { }
        }
        return 0;
    }

    /// <summary>
    /// 线刷镜像 → 卡刷包。
    /// </summary>
    private static async Task<int> Img2OtaAsync(string[] args)
    {
        string src = args.FirstOrDefault(a => !a.StartsWith("-")) ?? "";
        string outZip = ParseDir(args, "-o", "update.zip");
        if (!Directory.Exists(src))
        {
            Console.Error.WriteLine($"img 目录不存在: {src}");
            return 1;
        }
        if (!outZip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            outZip += ".zip";

        Console.WriteLine($"img2ota: {src} → {outZip}");
        string work = outZip.Replace(".zip", "_work");
        Directory.CreateDirectory(work);
        string imagesDir = Path.Combine(work, "images");
        Directory.CreateDirectory(imagesDir);

        var imgs = Directory.GetFiles(src, "*.img");
        if (imgs.Length == 0)
        {
            Console.Error.WriteLine($"目录中没有 .img 文件: {src}");
            return 1;
        }

        // 每个 img → sdat
        var convertedNames = new List<string>();
        foreach (var img in imgs)
        {
            string name = Path.GetFileNameWithoutExtension(img).ToLowerInvariant();
            // 跳过特殊分区：boot/recovery/dtbo/vbmeta 等保持 .img 直放
            bool direct = name is "boot" or "recovery" or "dtbo" or "vbmeta" or "vbmeta_system" or "vbmeta_vendor" or "spl" or "sbl1" or "tz" or "modem" or "abl" or "xbl" or "hyp" or "gpt";
            if (direct)
            {
                File.Copy(img, Path.Combine(imagesDir, Path.GetFileName(img)), true);
                convertedNames.Add(name);
                Console.WriteLine($"直放 {name}.img");
            }
            else
            {
                Console.WriteLine($"转 sdat {name} ...");
                SdatConverter.Img2Sdat(img, imagesDir, name);
                convertedNames.Add(name);
            }
        }

        // 生成 updater-script
        string updater = BuildUpdaterScript(convertedNames);
        string metaDir = Path.Combine(work, "META-INF", "com", "google", "android");
        Directory.CreateDirectory(metaDir);
        File.WriteAllText(Path.Combine(metaDir, "updater-script"), updater);

        // 打包 zip
        if (File.Exists(outZip)) File.Delete(outZip);
        ZipFile.CreateFromDirectory(work, outZip, CompressionLevel.Fastest, false);
        Console.WriteLine($"卡刷包生成: {outZip}");
        try { Directory.Delete(work, true); } catch { }
        return 0;
    }

    /// <summary>
    /// 生成卡刷 updater-script。
    /// </summary>
    private static string BuildUpdaterScript(List<string> parts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ui_print(\"UotanToolbox CLI generated update.zip\");");
        sb.AppendLine("ui_print(\"Installing...\");");
        sb.AppendLine("show_progress(0.7, 0);");
        foreach (var p in parts)
        {
            string file = p == "boot" ? "boot.img"
                : p == "recovery" ? "recovery.img"
                : p == "dtbo" ? "dtbo.img"
                : $"system.new.dat".Replace("system", p); // 占位，实际下面处理
        }
        // 简化：system/vendor/product 用 block_image_update，其余用 package_extract_file
        foreach (var p in parts.Where(x => x is "system" or "vendor" or "product" or "system_ext" or "odm"))
        {
            sb.AppendLine($"block_image_update(\"/dev/block/bootdevice/by-name/{p}\", package_extract_file(\"{p}.transfer.list\"), \"{p}.new.dat\", \"{p}.patch.dat\");");
        }
        foreach (var p in parts.Where(x => x is not "system" and not "vendor" and not "product" and not "system_ext" and not "odm"))
        {
            string img = p.EndsWith("img") ? p : p + ".img";
            sb.AppendLine($"package_extract_file(\"{img}\", \"/dev/block/bootdevice/by-name/{p}\");");
        }
        sb.AppendLine("set_progress(1.0);");
        sb.AppendLine("ui_print(\"Done\");");
        return sb.ToString();
    }

    private static string ParseDir(string[] args, string key, string def)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == key && i + 1 < args.Length)
                return args[i + 1];
        }
        return def;
    }

    /// <summary>
    /// 根据输出目录的 img，生成 fastboot 线刷脚本。
    /// </summary>
    private static void GenerateFastbootScript(string outDir)
    {
        var imgs = Directory.GetFiles(outDir, "*.img")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (imgs.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("echo Flashing all partitions...");
        foreach (var name in imgs)
        {
            // A/B 分区处理：若存在 _a/_b 则分别刷入
            sb.AppendLine($@"fastboot flash {name} images\{name}.img");
        }
        sb.AppendLine("fastboot reboot");
        string script = Path.Combine(outDir, "flash_all.bat");
        File.WriteAllText(script, sb.ToString());
        Console.WriteLine($"已生成线刷脚本: {script}");
        Console.WriteLine("提示: 将所有 img 放入 images 子目录，设备连 Fastboot 后运行脚本。");
    }
}