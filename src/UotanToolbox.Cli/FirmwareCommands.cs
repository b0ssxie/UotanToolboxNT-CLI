using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common;
using UotanToolbox.Common.ROMHelper;

namespace UotanToolbox.Cli;

/// <summary>
/// 高级固件命令：识别/列出/提取 super.img、.ntpi、.nb0、.ozip、.ops、.ofp，以及 URL 在线解包。
/// </summary>
internal static class FirmwareCommands
{
    public static async Task<int> FirmwareAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                固件工具（super/ntpi/nb0/oppo/payload）:
                  utoolbox firmware detect <文件>            识别固件类型
                  utoolbox firmware parts <文件>             列出固件分区
                  utoolbox firmware extract <文件> [-o 目录] [分区...]   提取分区
                  utoolbox firmware extract-url <url> [-o 目录] [分区...]   在线解包 payload URL
                """);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "detect" => await DetectAsync(args.Skip(1).ToArray()),
            "parts" => await PartsAsync(args.Skip(1).ToArray()),
            "extract" => await ExtractAsync(args.Skip(1).ToArray()),
            "extract-url" => await ExtractUrlAsync(args.Skip(1).ToArray()),
            _ => CommandUtil.Unknown("firmware", args[0]),
        };
    }

    private static async Task<int> DetectAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox firmware detect <文件>");
            return 1;
        }
        string file = args[0];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"文件不存在: {file}");
            return 1;
        }
        var service = new FirmwareExtractService();
        var kind = await service.DetectAsync(file);
        Console.WriteLine($"固件类型: {Describe(kind)}");
        return 0;
    }

    private static string Describe(FirmwareExtractService.FileKind kind) => kind switch
    {
        FirmwareExtractService.FileKind.Payload => "Payload (payload.bin)",
        FirmwareExtractService.FileKind.Super => "Super (LP 动态分区镜像)",
        FirmwareExtractService.FileKind.Ntpi => "NTPi",
        FirmwareExtractService.FileKind.Nb0 => "NB0",
        FirmwareExtractService.FileKind.Ozip => "OZIP (OPPO)",
        FirmwareExtractService.FileKind.Ops => "OPS (OPPO)",
        FirmwareExtractService.FileKind.Ofp => "OFP (OPPO)",
        _ => "未知格式",
    };

    private static async Task<int> PartsAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox firmware parts <文件>");
            return 1;
        }
        string file = args[0];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"文件不存在: {file}");
            return 1;
        }
        var service = new FirmwareExtractService();
        var kind = await service.DetectAsync(file);
        Console.WriteLine($"固件类型: {Describe(kind)}");
        var parts = await service.ListPartitionsAsync(file);
        if (parts.Count == 0)
        {
            Console.WriteLine("未解析到分区。");
            return 1;
        }
        Console.WriteLine($"共 {parts.Count} 个分区:");
        foreach (var p in parts)
            Console.WriteLine($"  {p.Name,-32} {FormatSize(p.Size)}");
        return 0;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:0.##} {units[i]}";
    }

    private static async Task<int> ExtractAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox firmware extract <文件> [-o 输出目录] [分区名...]");
            return 1;
        }
        string file = args[0];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"文件不存在: {file}");
            return 1;
        }
        string outputDir = Directory.GetCurrentDirectory();
        var names = new System.Collections.Generic.List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
                outputDir = args[++i];
            else
                names.Add(args[i]);
        }

        var service = new FirmwareExtractService();
        var kind = await service.DetectAsync(file);
        Console.WriteLine($"固件类型: {Describe(kind)}");
        int result = await service.ExtractAsync(file, outputDir, names);
        if (result == 0)
            Console.WriteLine($"提取完成，输出目录: {outputDir}");
        else
        {
            Console.Error.WriteLine("提取失败：无法识别的固件格式。");
            return 1;
        }
        return 0;
    }

    private static async Task<int> ExtractUrlAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: utoolbox firmware extract-url <payload url> [-o 输出目录] [分区名...]");
            return 1;
        }
        string url = args[0];
        string outputDir = Directory.GetCurrentDirectory();
        var names = new System.Collections.Generic.List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
                outputDir = args[++i];
            else
                names.Add(args[i]);
        }
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        Console.WriteLine($"在线解析: {url}");
        // 先列出分区
        var parts = await PayloadParser.GetPartitionInfoFromUrlAsync(url);
        Console.WriteLine($"共 {parts.Count} 个分区");
        string[]? namesArr = names.Count > 0 ? [.. names] : null;
        await PayloadParser.ExtractSelectedPartitionsFromUrlV2Async(url, outputDir, namesArr);
        Console.WriteLine($"提取完成，输出目录: {outputDir}");
        return 0;
    }
}