using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FirmwareKit.Lp;
using FirmwareKit.Nb0;
using FirmwareKit.NTPi;
using FirmwareKit.Oppo;
using FirmwareKit.Oppo.Crypto;
using FirmwareKit.Oppo.Models;
using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Streams;

namespace UotanToolbox.Common;

/// <summary>
/// 固件分区解析与提取服务（super.img / .ntpi / .nb0 / .ozip / .ops / .ofp）。
/// 从 Advancedflash 提炼，无 UI 依赖。
/// </summary>
public class FirmwareExtractService
{
    public enum FileKind { Unknown, Payload, Super, Ntpi, Nb0, Ozip, Ops, Ofp }

    public record Partition(string Name, long Size);

    /// <summary>
    /// 根据扩展名 + 内容识别固件类型。
    /// </summary>
    public async Task<FileKind> DetectAsync(string path)
    {
        // 先按扩展名分发
        string ext = Path.GetExtension(path);
        switch (ext.ToLowerInvariant())
        {
            case ".ntpi":
                return await TryParseNtpiAsync(path) ? FileKind.Ntpi : FileKind.Unknown;
            case ".nb0":
                return await TryParseNb0Async(path) ? FileKind.Nb0 : FileKind.Unknown;
            case ".ozip":
            case ".ops":
            case ".ofp":
                var oppoKind = await DetectOppoAsync(path);
                return oppoKind;
        }

        // 兜底：payload / super
        if (await IsPayloadAsync(path)) return FileKind.Payload;
        if (ext.Equals(".img", StringComparison.OrdinalIgnoreCase) && await IsSuperAsync(path)) return FileKind.Super;
        return FileKind.Unknown;
    }

    private static async Task<bool> IsPayloadAsync(string path)
    {
        try
        {
            var parts = await ROMHelper.PayloadParser.GetPartitionInfoAsync(path);
            return parts.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsSuperAsync(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var magic = new byte[4];
            if (fs.Read(magic, 0, 4) == 4)
            {
                uint v = BitConverter.ToUInt32(magic, 0);
                // sparse header magic
                if (v == (uint)0xED26FF3A || v == SparseFormat.SparseHeaderMagic)
                    return true;
            }
            // 尝试 raw LP metadata
            fs.Seek(0, SeekOrigin.Begin);
            var reader = new MetadataReader();
            var meta = reader.ReadFromImageStream(fs);
            return meta?.Partitions?.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 列出固件中的分区。
    /// </summary>
    public async Task<List<Partition>> ListPartitionsAsync(string path)
    {
        string ext = Path.GetExtension(path);
        switch (ext.ToLowerInvariant())
        {
            case ".ntpi":
                return ListNtpi(path);
            case ".nb0":
                return ListNb0(path);
            case ".ozip":
            case ".ops":
            case ".ofp":
                return ListOppo(path);
        }
        if (Path.GetExtension(path).Equals(".img", StringComparison.OrdinalIgnoreCase))
            return await ListSuperAsync(path);
        // payload
        try
        {
            var parts = await ROMHelper.PayloadParser.GetPartitionInfoAsync(path);
            return parts.Select(p => new Partition(p.Name, ParseSize(p.SizeReadable))).ToList();
        }
        catch
        {
            return new List<Partition>();
        }
    }

    private static long ParseSize(string readable)
    {
        if (string.IsNullOrEmpty(readable)) return 0;
        // "1.5GB" / "500MB" 转为字节
        double val;
        double mult;
        var s = readable.Trim();
        if (s.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
        {
            val = double.TryParse(s[..^2], out val) ? val : 0;
            mult = 1024L * 1024 * 1024;
        }
        else if (s.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
        {
            val = double.TryParse(s[..^2], out val) ? val : 0;
            mult = 1024L * 1024;
        }
        else if (s.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
        {
            val = double.TryParse(s[..^2], out val) ? val : 0;
            mult = 1024L;
        }
        else
        {
            long.TryParse(s, out long raw);
            return raw;
        }
        return (long)(val * mult);
    }

    private static async Task<List<Partition>> ListSuperAsync(string path)
    {
        var result = new List<Partition>();
        using var fs = File.OpenRead(path);
        var magic = new byte[4];
        bool isSparse = fs.Read(magic, 0, 4) == 4 &&
                        BitConverter.ToUInt32(magic, 0) == SparseFormat.SparseHeaderMagic;
        var reader = new MetadataReader();
        LpMetadata? meta;
        if (isSparse)
        {
            using var sparseFile = SparseFile.FromImageFile(path);
            using var stream = new SparseStream(sparseFile);
            meta = reader.ReadFromImageStream(stream);
        }
        else
        {
            fs.Seek(0, SeekOrigin.Begin);
            meta = reader.ReadFromImageStream(fs);
        }
        if (meta?.Partitions == null) return result;
        foreach (var p in meta.Partitions)
        {
            ulong total = 0;
            for (uint i = 0; i < p.NumExtents; i++)
                total += meta.Extents[(int)(p.FirstExtentIndex + i)].NumSectors * 512;
            result.Add(new Partition(p.GetName(), (long)total));
        }
        return result;
    }

    private static List<Partition> ListNtpi(string path)
    {
        var reader = new NtpiReader(
            new FirmwareKit.NTPi.Crypto.AesCbcCryptoProvider(),
            new FirmwareKit.NTPi.Compression.Lzma2Compressor());
        var fileInfo = reader.ReadInfo(path);
        return fileInfo?.FileEntries?
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => new Partition(e.Name, e.OriginalLength))
            .ToList() ?? [];
    }

    private static List<Partition> ListNb0(string path)
    {
        var info = Nb0Parser.Parse(path);
        return info?.Entries?
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => new Partition(e.Name, e.Size))
            .ToList() ?? [];
    }

    private static OppCryptoProvider? _oppProvider;
    private static OppCryptoProvider GetOppoProvider()
    {
        if (_oppProvider != null) return _oppProvider;
        _oppProvider = new OppCryptoProvider(null);
        FirmwareKit.OzipReader.OzipPackageInitializer.Initialize(_oppProvider);
        FirmwareKit.OpsReader.OpsPackageInitializer.Register(_oppProvider);
        FirmwareKit.OfpReader.OfpPackageInitializer.Initialize(_oppProvider, null);
        return _oppProvider;
    }

    private static List<Partition> ListOppo(string path)
    {
        var reader = new OppReader(GetOppoProvider());
        var archive = reader.Parse(path);
        return archive?.Entries?.Select(e => new Partition(e.Name, e.Size)).ToList() ?? [];
    }

    private static async Task<FileKind> DetectOppoAsync(string path)
    {
        try
        {
            var reader = new OppReader(GetOppoProvider());
            var archive = reader.Parse(path);
            if (archive == null || archive.Entries.Count == 0) return FileKind.Unknown;
            return archive.Metadata?.Format switch
            {
                OppFormat.Ozip => FileKind.Ozip,
                OppFormat.Ops => FileKind.Ops,
                OppFormat.OfpQc or OppFormat.OfpMtk => FileKind.Ofp,
                _ => FileKind.Unknown,
            };
        }
        catch
        {
            return FileKind.Unknown;
        }
    }

    private static async Task<bool> TryParseNtpiAsync(string path)
    {
        try { return ListNtpi(path).Count > 0; }
        catch { return false; }
    }

    private static async Task<bool> TryParseNb0Async(string path)
    {
        try { return ListNb0(path).Count > 0; }
        catch { return false; }
    }

    /// <summary>
    /// 提取选中分区到输出目录。
    /// </summary>
    public async Task<int> ExtractAsync(string path, string outputDir, IReadOnlyList<string>? partitionNames = null)
    {
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var names = partitionNames?.Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        FileKind kind = await DetectAsync(path);
        switch (kind)
        {
            case FileKind.Payload:
                await ROMHelper.PayloadParser.ExtractSelectedPartitionsAsync(path, names, outputDir);
                return 0;
            case FileKind.Super:
                await Task.Run(() => ExtractSuperPartitions(path, outputDir, names));
                return 0;
            case FileKind.Ntpi:
                ExtractNtpi(path, outputDir, names);
                return 0;
            case FileKind.Nb0:
                ExtractNb0(path, outputDir, names);
                return 0;
            case FileKind.Ozip:
            case FileKind.Ops:
            case FileKind.Ofp:
                ExtractOppo(path, outputDir, names);
                return 0;
            default:
                return -1;
        }
    }

    private static void ExtractSuperPartitions(string sourcePath, string outputDir, string[]? names)
    {
        var selected = new HashSet<string>(names ?? [], StringComparer.OrdinalIgnoreCase);
        using var fs = File.OpenRead(sourcePath);
        var magic = new byte[4];
        bool isSparse = fs.Read(magic, 0, 4) == 4 &&
                        BitConverter.ToUInt32(magic, 0) == SparseFormat.SparseHeaderMagic;
        if (isSparse)
        {
            using var sparseFile = SparseFile.FromImageFile(sourcePath);
            using var stream = new SparseStream(sparseFile);
            ExtractSuperFromStream(stream, outputDir, selected);
        }
        else
        {
            fs.Seek(0, SeekOrigin.Begin);
            ExtractSuperFromStream(fs, outputDir, selected);
        }
    }

    private static void ExtractSuperFromStream(Stream stream, string outputDir, HashSet<string> selected)
    {
        var reader = new MetadataReader();
        var meta = reader.ReadFromImageStream(stream);
        if (meta?.Partitions == null) return;

        foreach (var partition in meta.Partitions)
        {
            var name = partition.GetName();
            if (selected.Count > 0 && !selected.Contains(name)) continue;

            ulong totalSectors = 0;
            for (uint i = 0; i < partition.NumExtents; i++)
                totalSectors += meta.Extents[(int)(partition.FirstExtentIndex + i)].NumSectors;
            long totalSize = (long)totalSectors * MetadataFormat.LP_SECTOR_SIZE;

            var outPath = Path.Combine(outputDir, $"{name}.img");
            using var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
            outFs.SetLength(totalSize);

            long outOffset = 0;
            for (uint i = 0; i < partition.NumExtents; i++)
            {
                var extent = meta.Extents[(int)(partition.FirstExtentIndex + i)];
                long size = (long)extent.NumSectors * MetadataFormat.LP_SECTOR_SIZE;
                if (extent.TargetType == MetadataFormat.LP_TARGET_TYPE_LINEAR)
                {
                    long srcOffset = (long)extent.TargetData * MetadataFormat.LP_SECTOR_SIZE;
                    stream.Seek(srcOffset, SeekOrigin.Begin);
                    outFs.Seek(outOffset, SeekOrigin.Begin);
                    CopyStream(stream, outFs, size);
                }
                outOffset += size;
            }
        }
    }

    private static void CopyStream(Stream src, Stream dst, long count)
    {
        var buffer = new byte[1024 * 1024];
        long remaining = count;
        while (remaining > 0)
        {
            int read = src.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) break;
            dst.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ExtractNtpi(string path, string outputDir, string[]? names)
    {
        var reader = new NtpiReader(
            new FirmwareKit.NTPi.Crypto.AesCbcCryptoProvider(),
            new FirmwareKit.NTPi.Compression.Lzma2Compressor());
        reader.Unpack(path, outputDir);
        if (names != null && names.Length > 0)
        {
            foreach (var f in Directory.GetFiles(outputDir, "*.img"))
            {
                string baseName = Path.GetFileNameWithoutExtension(f);
                if (!names.Contains(baseName, StringComparer.OrdinalIgnoreCase) &&
                    !names.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                    File.Delete(f);
            }
        }
    }

    private static void ExtractNb0(string path, string outputDir, string[]? names)
    {
        var extractor = new Nb0Extractor();
        extractor.Extract(path, outputDir);
        if (names != null && names.Length > 0)
        {
            foreach (var f in Directory.GetFiles(outputDir, "*.img"))
            {
                string baseName = Path.GetFileNameWithoutExtension(f);
                if (!names.Contains(baseName, StringComparer.OrdinalIgnoreCase) &&
                    !names.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                    File.Delete(f);
            }
        }
    }

    private static void ExtractOppo(string path, string outputDir, string[]? names)
    {
        var reader = new OppReader(GetOppoProvider());
        reader.Extract(path, outputDir);
        if (names != null && names.Length > 0)
        {
            foreach (var f in Directory.GetFiles(outputDir, "*.img"))
            {
                string baseName = Path.GetFileNameWithoutExtension(f);
                string full = Path.GetFileName(f);
                if (!names.Contains(baseName, StringComparer.OrdinalIgnoreCase) &&
                    !names.Contains(full, StringComparer.OrdinalIgnoreCase))
                    File.Delete(f);
            }
        }
    }
}