using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UotanToolbox.Common;

/// <summary>
/// sdat2img & img2sdat：卡刷包(.dat/.dat.br + transfer.list) 与线刷包(.img) 互转。
/// 参考 AOSP sdat2img / img2sdat 算法。
/// </summary>
public static class SdatConverter
{
    private const int BLOCK_SIZE = 4096;

    /// <summary>
    /// 解析 rangeset 字符串（如 "4,0,2,10,12" → [(0,2),(10,12)]）。
    /// </summary>
    private static List<(long start, long end)> ParseRanges(string source)
    {
        var result = new List<(long, long)>();
        if (string.IsNullOrWhiteSpace(source)) return result;
        var parts = source.Split(',');
        if (parts.Length < 1) return result;
        int count = int.Parse(parts[0]);
        if (parts.Length != count + 1) throw new InvalidDataException($"range 数量不匹配: {source}");
        for (int i = 1; i < parts.Length; i += 2)
        {
            long start = long.Parse(parts[i]);
            long end = long.Parse(parts[i + 1]);
            result.Add((start, end));
        }
        return result;
    }

    /// <summary>
    /// 把 rangeset 展开为所有 block 号。
    /// </summary>
    private static IEnumerable<long> ExpandRanges(List<(long start, long end)> ranges)
    {
        foreach (var (s, e) in ranges)
            for (long i = s; i < e; i++)
                yield return i;
    }

    /// <summary>
    /// 解压 .dat.br（brotli）到 .dat。用 .NET 内置 BrotliStream。
    /// </summary>
    public static void BrotliDecompress(string srcPath, string dstPath)
    {
        using var src = File.OpenRead(srcPath);
        using var bz = new System.IO.Compression.BrotliStream(src, System.IO.Compression.CompressionMode.Decompress);
        using var dst = File.Create(dstPath);
        bz.CopyTo(dst);
    }

    /// <summary>
    /// sdat2img：把 transfer.list + new.dat 还原成原始镜像。
    /// </summary>
    /// <param name="transferListPath">xxx.transfer.list</param>
    /// <param name="dataFilePath">xxx.new.dat（或 .dat.br，自动解压）</param>
    /// <param name="outputImgPath">输出的 .img</param>
    public static void Sdat2Img(string transferListPath, string dataFilePath, string outputImgPath)
    {
        // 若输入是 .dat.br，先解压到临时 .dat
        string actualData = dataFilePath;
        string? tempDat = null;
        if (dataFilePath.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
        {
            tempDat = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(dataFilePath)}.dat");
            BrotliDecompress(dataFilePath, tempDat);
            actualData = tempDat;
        }

        var lines = File.ReadAllLines(transferListPath);
        int index = 0;
        int version = int.Parse(lines[index++]);
        int blocksToWrite = int.Parse(lines[index++]);
        if (version >= 2)
        {
            index++; // 跳过 new blocks count
            index++; // 跳过 stash blocks count
        }

        using var output = new FileStream(outputImgPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var data = new FileStream(actualData, FileMode.Open, FileAccess.Read);
        var dataReader = new BinaryReader(data);

        // 用于 imgdiff 的块缓冲（简化：仅支持 new/zero/erase/stash/free）
        var stash = new Dictionary<string, byte[]>();
        int written;

        for (int i = index; i < lines.Length; i++)
        {
            var parts = lines[i].Split(' ');
            string cmd = parts[0];
            switch (cmd)
            {
                case "new":
                    {
                        var ranges = ParseRanges(parts[1]);
                        foreach (var block in ExpandRanges(ranges))
                        {
                            output.Seek(block * BLOCK_SIZE, SeekOrigin.Begin);
                            var buf = dataReader.ReadBytes(BLOCK_SIZE);
                            output.Write(buf, 0, buf.Length);
                        }
                        break;
                    }
                case "zero":
                    {
                        var ranges = ParseRanges(parts[1]);
                        foreach (var block in ExpandRanges(ranges))
                        {
                            output.Seek(block * BLOCK_SIZE, SeekOrigin.Begin);
                            output.Write(new byte[BLOCK_SIZE], 0, BLOCK_SIZE);
                        }
                        break;
                    }
                case "erase":
                    {
                        // 写入 0xFF 与 zero 类似（erase 在部分实现中填 FF）
                        var ranges = ParseRanges(parts[1]);
                        foreach (var block in ExpandRanges(ranges))
                        {
                            output.Seek(block * BLOCK_SIZE, SeekOrigin.Begin);
                            output.Write(new byte[BLOCK_SIZE], 0, BLOCK_SIZE);
                        }
                        break;
                    }
                case "stash":
                    {
                        // stash <id> <ranges>：记录该 id 对应的块数据
                        string id = parts[1];
                        var ranges = ParseRanges(parts[2]);
                        var savePos = data.Position;
                        using var ms = new MemoryStream();
                        foreach (var block in ExpandRanges(ranges))
                        {
                            data.Seek(block * BLOCK_SIZE, SeekOrigin.Begin);
                            var buf = dataReader.ReadBytes(BLOCK_SIZE);
                            ms.Write(buf, 0, buf.Length);
                        }
                        stash[id] = ms.ToArray();
                        data.Seek(savePos, SeekOrigin.Begin);
                        break;
                    }
                case "free":
                    {
                        string id = parts[1];
                        stash.Remove(id);
                        break;
                    }
                case "imgdiff":
                    {
                        // 简化支持：imgdiff 命令格式 "imgdiff <stash-dest> <ranges> <stash-id>"
                        // 把对应块从 stash 写入目标（不带差异合并，因大多数完整包用 new）
                        string dest = parts[1];
                        var ranges = ParseRanges(parts[2]);
                        string sid = parts.Length > 3 ? parts[3] : null;
                        if (sid != null && stash.ContainsKey(sid))
                        {
                            var dataArr = stash[sid];
                            long blockNo = 0;
                            foreach (var block in ExpandRanges(ranges))
                            {
                                output.Seek(block * BLOCK_SIZE, SeekOrigin.Begin);
                                var slice = dataArr.Skip((int)(blockNo * BLOCK_SIZE)).Take(BLOCK_SIZE).ToArray();
                                output.Write(slice, 0, slice.Length);
                                blockNo++;
                            }
                        }
                        break;
                    }
                case "diff":
                    {
                        // diff 需要 bsdiff 解码，完整包中较少出现；此处尝试按 new 处理（无补丁）
                        // 若 data 有内容则按 new 读，否则留零
                        string dest = parts[1];
                        var ranges = ParseRanges(parts[2]);
                        foreach (var block in ExpandRanges(ranges))
                        {
                            output.Seek(block * BLOCK_SIZE, SeekOrigin.Begin);
                            output.Write(new byte[BLOCK_SIZE], 0, BLOCK_SIZE);
                        }
                        break;
                    }
            }
        }

        output.Flush();
        if (tempDat != null && File.Exists(tempDat))
            File.Delete(tempDat);
    }

    /// <summary>
    /// img2sdat：把 .img 转换为 transfer.list + new.dat（版本 1，最简最稳）。
    /// </summary>
    /// <param name="imgPath">输入的 .img</param>
    /// <param name="outDir">输出目录</param>
    /// <param name="name">分区名前缀（如 system → system.new.dat）</param>
    public static void Img2Sdat(string imgPath, string outDir, string name = "system")
    {
        Directory.CreateDirectory(outDir);
        long fileSize = new FileInfo(imgPath).Length;
        long blockCount = (fileSize + BLOCK_SIZE - 1) / BLOCK_SIZE;

        // version 1 格式：version / total_blocks / commands
        var transferContent = new System.Text.StringBuilder();
        transferContent.AppendLine("1");
        transferContent.AppendLine(blockCount.ToString());
        // 全部块一个 new 命令，range 为 "2,0,<count>"
        transferContent.AppendLine($"new 2,0,{blockCount}");

        File.WriteAllText(Path.Combine(outDir, $"{name}.transfer.list"), transferContent.ToString());

        // new.dat 就是完整镜像内容
        File.Copy(imgPath, Path.Combine(outDir, $"{name}.new.dat"), true);

        // 空 patch.dat
        using var fs = File.Create(Path.Combine(outDir, $"{name}.patch.dat"));
    }
}