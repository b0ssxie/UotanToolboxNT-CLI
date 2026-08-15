using ChromeosUpdateEngine;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace UotanToolbox.Common.ROMHelper
{
    public class PayloadParser
    {
        private const string ZipMagic = "PK";
        private const string PayloadMagic = "CrAU";
        private const ulong BrilloMajorPayloadVersion = 2;
        private const string PayloadFilename = "payload.bin";

        /// <summary>
        /// 从 payload 文件中解压分区（可按分区名过滤）。
        /// </summary>
        public static void Extracet(string filepath, string[]? extractFiles = null)
        {
            if (extractFiles == null)
            {
                extractFiles = Array.Empty<string>();
            }
            FileStream f = File.OpenRead(filepath);
            {
                if (IsZip(f))
                {
                    f.Close();

                    using (ZipArchive zr = ZipFile.OpenRead(filepath))
                    {
                        ZipArchiveEntry zf = FindPayload(zr) ?? throw new Exception($"{PayloadFilename} not found in the zip file");
                        using FileStream outFile = File.Create(PayloadFilename);
                        using Stream zfStream = zf.Open();
                        zfStream.CopyTo(outFile);
                    }

                    f = File.OpenRead(PayloadFilename);
                }
                PayloadParser parser = new();
                parser.ParsePayload(f, extractFiles);
            }
        }

        /// <summary>
        /// 异步从 payload 文件中解压分区（可按分区名过滤）。
        /// </summary>
        public static async Task ExtracetAsync(string filepath, string[]? extractFiles = null)
        {
            if (extractFiles == null)
            {
                extractFiles = Array.Empty<string>();
            }
            FileStream f = File.OpenRead(filepath);
            {
                if (IsZip(f))
                {
                    f.Close();

                    using (ZipArchive zr = ZipFile.OpenRead(filepath))
                    {
                        ZipArchiveEntry zf = FindPayload(zr) ?? throw new Exception($"{PayloadFilename} not found in the zip file");
                        using FileStream outFile = File.Create(PayloadFilename);
                        using Stream zfStream = zf.Open();
                        await zfStream.CopyToAsync(outFile);
                    }

                    f = File.OpenRead(PayloadFilename);
                }
                PayloadParser parser = new();
                await parser.ParsePayloadAsync(f, extractFiles);

            }
        }

        /// <summary>
        /// 通过魔数判断流是否为 zip 文件。
        /// </summary>
        private static bool IsZip(FileStream f)
        {
            byte[] header = new byte[ZipMagic.Length];
            int bytesRead = f.Read(header, 0, header.Length);
            f.Seek(0, SeekOrigin.Begin);
            return bytesRead == header.Length && Encoding.ASCII.GetString(header) == ZipMagic;
        }

        /// <summary>
        /// 在 zip 包中查找 payload 条目。
        /// </summary>
        private static ZipArchiveEntry? FindPayload(ZipArchive zr)
        {
            foreach (ZipArchiveEntry entry in zr.Entries)
            {
                if (entry.Name == PayloadFilename)
                {
                    return entry;
                }
            }
            return null;
        }

        public class PayloadMetadata
        {
            public uint BlockSize { get; set; }
            public List<string> PartitionNames { get; } = new List<string>();
            public ulong ManifestLength { get; set; }
            public uint MetadataSignatureLength { get; set; }
            public ulong BaseOffset { get; set; }
            public required DeltaArchiveManifest Manifest { get; set; }
        }

        /// <summary>
        /// 详细的分区信息，包括大小和可读格式。
        /// </summary>
        public class PartitionInfoData
        {
            public string Name { get; set; } = string.Empty;
            public ulong SizeBytes { get; set; }
            public string SizeReadable { get; set; } = string.Empty;
        }

        /// <summary>
        /// 不解压分区数据，读取 payload 元信息与分区列表。
        /// </summary>
        public static PayloadMetadata GetPayloadMetadata(string filepath)
        {
            PayloadParser parser = new();
            using FileStream f = File.OpenRead(filepath);
            if (IsZip(f))
            {
                using ZipArchive zr = ZipFile.OpenRead(filepath);
                ZipArchiveEntry zf = FindPayload(zr) ?? throw new FileNotFoundException($"{PayloadFilename} not found in the zip file");
                using Stream zfStream = zf.Open();
                return BuildMetadata(parser.ReadManifest(zfStream));
            }

            f.Seek(0, SeekOrigin.Begin);
            return BuildMetadata(parser.ReadManifest(f));
        }

        /// <summary>
        /// 异步不解压分区数据，读取 payload 元信息与分区列表。
        /// </summary>
        public static async Task<PayloadMetadata> GetPayloadMetadataAsync(string filepath)
        {
            PayloadParser parser = new();
            using FileStream f = File.OpenRead(filepath);
            if (IsZip(f))
            {
                using ZipArchive zr = ZipFile.OpenRead(filepath);
                ZipArchiveEntry zf = FindPayload(zr) ?? throw new FileNotFoundException($"{PayloadFilename} not found in the zip file");
                using Stream zfStream = zf.Open();
                return BuildMetadata(await parser.ReadManifestAsync(zfStream));
            }

            f.Seek(0, SeekOrigin.Begin);
            return BuildMetadata(await parser.ReadManifestAsync(f));
        }

        /// <summary>
        /// 仅解压指定分区。
        /// </summary>
        public static void ExtractSelectedPartitions(string filepath, string[]? partitionNames = null)
        {
            partitionNames ??= Array.Empty<string>();

            string payloadPath = filepath;
            string? tempPath = null;

            using (FileStream fcheck = File.OpenRead(filepath))
            {
                if (IsZip(fcheck))
                {
                    using ZipArchive zr = ZipFile.OpenRead(filepath);
                    ZipArchiveEntry zf = FindPayload(zr) ?? throw new FileNotFoundException($"{PayloadFilename} not found in the zip file");
                    tempPath = Path.GetTempFileName();
                    using (FileStream outFile = File.Create(tempPath))
                    using (Stream zfStream = zf.Open())
                    {
                        zfStream.CopyTo(outFile);
                    }
                    payloadPath = tempPath;
                }
            }

            try
            {
                using FileStream stream = File.OpenRead(payloadPath);
                PayloadParser parser = new();
                var (manifest, manifestLen, metadataSigLen, baseOffset) = parser.ReadManifest(stream);

                IEnumerable<PartitionUpdate> partitions = manifest.Partitions.Where(p => p.PartitionName != null);
                if (partitionNames.Length > 0)
                {
                    partitions = partitions.Where(p => partitionNames.Contains(p.PartitionName));
                }

                foreach (var partition in partitions)
                {
                    string outFilename = $"{partition.PartitionName}.img";
                    parser.ExtractPartition(partition, outFilename, stream, baseOffset, manifest.BlockSize);
                }
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        /// <summary>
        /// 异步仅解压指定分区。
        /// </summary>
        public static async Task ExtractSelectedPartitionsAsync(string filepath, string[]? partitionNames = null, string? outputDir = null)
        {
            partitionNames ??= Array.Empty<string>();
            outputDir ??= Directory.GetCurrentDirectory();
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string payloadPath = filepath;
            string? tempPath = null;

            using (FileStream fcheck = File.OpenRead(filepath))
            {
                if (IsZip(fcheck))
                {
                    using ZipArchive zr = ZipFile.OpenRead(filepath);
                    ZipArchiveEntry zf = FindPayload(zr) ?? throw new FileNotFoundException($"{PayloadFilename} not found in the zip file");
                    tempPath = Path.GetTempFileName();
                    using (FileStream outFile = File.Create(tempPath))
                    using (Stream zfStream = zf.Open())
                    {
                        await zfStream.CopyToAsync(outFile);
                    }
                    payloadPath = tempPath;
                }
            }

            try
            {
                using FileStream stream = File.OpenRead(payloadPath);
                PayloadParser parser = new();
                var (manifest, manifestLen, metadataSigLen, baseOffset) = await parser.ReadManifestAsync(stream);

                IEnumerable<PartitionUpdate> partitions = manifest.Partitions.Where(p => p.PartitionName != null);
                if (partitionNames.Length > 0)
                {
                    partitions = partitions.Where(p => partitionNames.Contains(p.PartitionName));
                }

                var tasks = new List<Task>();
                foreach (var partition in partitions)
                {
                    string outFilename = Path.Combine(outputDir, $"{partition.PartitionName}.img");
                    tasks.Add(Task.Run(() =>
                    {
                        using FileStream readStream = File.OpenRead(payloadPath);
                        parser.ExtractPartition(partition, outFilename, readStream, baseOffset, manifest.BlockSize);
                    }));
                }
                await Task.WhenAll(tasks);
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        /// <summary>
        /// 由解析后的 manifest 元组构建元信息对象。
        /// </summary>
        private static PayloadMetadata BuildMetadata((DeltaArchiveManifest manifest, ulong manifestLen, uint metadataSigLen, ulong baseOffset) data)
        {
            var (manifest, manifestLen, metadataSigLen, baseOffset) = data;
            var meta = new PayloadMetadata
            {
                BlockSize = manifest.BlockSize,
                ManifestLength = manifestLen,
                MetadataSignatureLength = metadataSigLen,
                BaseOffset = baseOffset,
                Manifest = manifest
            };

            foreach (var p in manifest.Partitions)
            {
                if (!string.IsNullOrEmpty(p.PartitionName))
                {
                    meta.PartitionNames.Add(p.PartitionName);
                }
            }

            return meta;
        }

        /// <summary>
        /// 计算分区列表及其大小信息。
        /// </summary>
        public static List<PartitionInfoData> GetPartitionInfo(string filepath)
        {
            PayloadParser parser = new();
            using FileStream f = File.OpenRead(filepath);
            if (IsZip(f))
            {
                using ZipArchive zr = ZipFile.OpenRead(filepath);
                ZipArchiveEntry zf = FindPayload(zr) ?? throw new FileNotFoundException($"{PayloadFilename} not found in the zip file");
                using Stream zfStream = zf.Open();
                var data = parser.ReadManifest(zfStream);
                return ComputePartitionInfo(data.manifest, data.baseOffset);
            }

            f.Seek(0, SeekOrigin.Begin);
            var result = parser.ReadManifest(f);
            return ComputePartitionInfo(result.manifest, result.baseOffset);
        }

        /// <summary>
        /// 异步版本的 GetPartitionInfo。
        /// </summary>
        public static async Task<List<PartitionInfoData>> GetPartitionInfoAsync(string filepath)
        {
            PayloadParser parser = new();
            using FileStream f = File.OpenRead(filepath);
            if (IsZip(f))
            {
                using ZipArchive zr = ZipFile.OpenRead(filepath);
                ZipArchiveEntry zf = FindPayload(zr) ?? throw new FileNotFoundException($"{PayloadFilename} not found in the zip file");
                using Stream zfStream = zf.Open();
                var data = await parser.ReadManifestAsync(zfStream);
                return ComputePartitionInfo(data.manifest, data.baseOffset);
            }

            f.Seek(0, SeekOrigin.Begin);
            var result = await parser.ReadManifestAsync(f);
            return ComputePartitionInfo(result.manifest, result.baseOffset);
        }

        private static List<PartitionInfoData> ComputePartitionInfo(DeltaArchiveManifest manifest, ulong baseOffset)
        {
            var list = new List<PartitionInfoData>();
            foreach (var part in manifest.Partitions)
            {
                if (string.IsNullOrEmpty(part.PartitionName))
                    continue;
                ulong blocks = 0;
                foreach (var op in part.Operations)
                {
                    foreach (var ext in op.DstExtents)
                    {
                        blocks += ext.NumBlocks;
                    }
                }
                ulong bytes = blocks * manifest.BlockSize;
                string readable;
                if (bytes >= 1024UL * 1024 * 1024)
                    readable = $"{bytes / (1024.0 * 1024 * 1024):F1}GB";
                else if (bytes >= 1024UL * 1024)
                    readable = $"{bytes / (1024.0 * 1024):F1}MB";
                else
                    readable = $"{bytes / 1024.0:F1}KB";
                list.Add(new PartitionInfoData { Name = part.PartitionName, SizeBytes = bytes, SizeReadable = readable });
            }
            return list;
        }

        /// <summary>
        /// 从 zip/payload 文件中提取 "META-INF/com/android/metadata" 内容并保存到指定路径。
        /// 如果 outputPath 为 null，则返回字符串。
        /// </summary>
        public static string? ExtractAndroidMetadata(string filepath, string? outputPath = null)
        {
            const string metaPath = "META-INF/com/android/metadata";
            using FileStream f = File.OpenRead(filepath);
            if (IsZip(f))
            {
                using ZipArchive zr = ZipFile.OpenRead(filepath);
                ZipArchiveEntry zf = FindPayload(zr) ?? throw new FileNotFoundException($"{PayloadFilename} not found in the zip file");
                using var zip = new ZipArchive(f);
                var entry = zip.GetEntry(metaPath) ?? throw new FileNotFoundException(metaPath);
                using var s = entry.Open();
                using var reader = new StreamReader(s, Encoding.UTF8);
                var content = reader.ReadToEnd();
                if (outputPath != null)
                    File.WriteAllText(outputPath, content);
                return content;
            }
            else
            {
                // not a zip, cannot contain metadata
                throw new InvalidOperationException("Not a zip file, no android metadata present");
            }
        }

        /// <summary>
        /// 使用 HttpClient 和范围请求提供一个可寻址的远程流。
        /// </summary>
        public sealed class HttpRangeStream : Stream
        {
            private readonly HttpClient _client;
            private readonly string _url;
            private readonly long _length;
            private long _position;

            public HttpRangeStream(string url, HttpClient? client = null)
            {
                _url = url;
                _client = client ?? new HttpClient();
                var head = _client.Send(new HttpRequestMessage(HttpMethod.Head, url));
                head.EnsureSuccessStatusCode();
                if (!head.Content.Headers.ContentLength.HasValue)
                    throw new IOException("unable to determine remote length");
                _length = head.Content.Headers.ContentLength.Value;
                _position = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _length)
                    return 0;
                long end = Math.Min(_length - 1, _position + count - 1);
                var req = new HttpRequestMessage(HttpMethod.Get, _url);
                req.Headers.Range = new RangeHeaderValue(_position, end);
                var resp = _client.Send(req);
                resp.EnsureSuccessStatusCode();
                byte[] data = resp.Content.ReadAsByteArrayAsync().Result;
                int read = data.Length;
                Array.Copy(data, 0, buffer, offset, read);
                _position += read;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long newPos = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => _length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };
                if (newPos < 0 || newPos > _length)
                    throw new IOException("Attempt to seek outside of stream");
                _position = newPos;
                return _position;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>
        /// 根据远程 payload url 获取分区信息。
        /// 支持直接访问 ZIP 包（通过 HTTP range 请求映射 payload entry）以及
        /// 传统的直接下载方式（raw payload 或不易范围请求的服务器）。
        /// </summary>
        public static async Task<List<PartitionInfoData>> GetPartitionInfoFromUrlAsync(string url)
        {
            using var client = new HttpClient();
            // try to read manifest by mapping the payload entry inside a zip
            try
            {
                var (dataOffset, compSize) = await GetZipEntryInfoAsync(client, url, PayloadFilename);
                using var stream = new HttpOffsetStream(url, dataOffset, compSize, client);
                PayloadParser parser = new();
                var (manifest, _, _, baseOffset) = await parser.ReadManifestAsync(stream);
                return ComputePartitionInfo(manifest, baseOffset);
            }
            catch (Exception)
            {
                // fallback to legacy metadata retrieval
                PayloadMetadata meta = await GetPayloadMetadataFromUrlAsync(url);
                return ComputePartitionInfo(meta.Manifest, meta.BaseOffset);
            }
        }

        /// <summary>
        /// 从远端 payload URL 下载并解压指定分区。
        /// <paramref name="partitionNames"/> 为 null 或长度为 0 表示解压全部。
        /// 该方法支持 ZIP 包（使用 range 请求定位 payload.bin）和普通 payload，两者均
        /// 会在本地临时文件基础上调用 <see cref="ExtractSelectedPartitions(string,string[]?)"/> 进行真正的解压。
        /// 如果要提取特定分区，可传入其名称数组；否则传 null 表示全盘提取。
        /// </summary>
        public static async Task ExtractSelectedPartitionsFromUrlV2Async(string url, string? outputDir = null, string[]? partitionNames = null)
        {
            outputDir ??= Directory.GetCurrentDirectory();

            // normalize filter set for case-insensitive comparison
            HashSet<string> nameSet = partitionNames == null || partitionNames.Length == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(partitionNames, StringComparer.OrdinalIgnoreCase);

            using var client = new HttpClient();
            // attempt streaming extraction from zip payload entry
            try
            {
                var (dataOffset, compSize) = await GetZipEntryInfoAsync(client, url, PayloadFilename);
                using var stream = new HttpOffsetStream(url, dataOffset, compSize, client);
                PayloadParser parser = new();
                var (manifest, _, _, baseOffset) = await parser.ReadManifestAsync(stream);

                var parts = manifest.Partitions
                    .Where(p => p.PartitionName != null &&
                                (nameSet.Count == 0 || nameSet.Contains(p.PartitionName)))
                    .ToList();

                foreach (var part in parts)
                {
                    string outPath = Path.Combine(outputDir, part.PartitionName + ".img");
                    Console.WriteLine($"Extracting {part.PartitionName} to {outPath}...");
                    parser.ExtractPartition(part, outPath, stream, baseOffset, manifest.BlockSize);
                }

                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is FileNotFoundException)
            {
                // fall back to legacy download flow
            }

            bool contains = await RemoteUrlContainsPayloadAsync(url);
            if (!contains)
                throw new FileNotFoundException("remote URL does not contain a recognized payload");

            string tempPayload = Path.GetTempFileName();
            try
            {
                bool isZip;
                var headReq = new HttpRequestMessage(HttpMethod.Get, url);
                headReq.Headers.Range = new RangeHeaderValue(0, 3);
                using var headResp = await client.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead);
                byte[] header = await headResp.Content.ReadAsByteArrayAsync();
                isZip = header.Length >= 2 && Encoding.ASCII.GetString(header, 0, 2) == ZipMagic;

                if (isZip)
                {
                    await DownloadEntryFromZipAsync(client, url, PayloadFilename, tempPayload);
                }
                else
                {
                    using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();
                    using var fs = File.Create(tempPayload);
                    await resp.Content.CopyToAsync(fs);
                }

                // reuse existing helper which honors partition filter
                ExtractSelectedPartitions(tempPayload, nameSet.Count == 0 ? null : nameSet.ToArray());

                if (outputDir != Directory.GetCurrentDirectory())
                {
                    var extracted = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.img", SearchOption.TopDirectoryOnly)
                        .Where(f => nameSet.Count == 0 || nameSet.Contains(Path.GetFileNameWithoutExtension(f)))
                        .ToArray();
                    foreach (var src in extracted)
                    {
                        string dest = Path.Combine(outputDir, Path.GetFileName(src));
                        File.Move(src, dest, true);
                    }
                }
            }
            finally
            {
                try { File.Delete(tempPayload); } catch { }
            }
        }

        /// <summary>
        /// 从远端 zip/url 提取 android 元数据并可选保存到文件
        /// </summary>
        public static async Task<string> ExtractAndroidMetadataFromUrlAsync(string url, string? outputPath = null)
        {
            using HttpClient client = new();
            // determine if zip or raw payload
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 3);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            byte[] header = await resp.Content.ReadAsByteArrayAsync();
            const string metaPath = "META-INF/com/android/metadata";
            if (header.Length >= 2 && Encoding.ASCII.GetString(header, 0, 2) == ZipMagic)
            {
                bool has = await RemoteZipHasEntryAsync(client, url, metaPath);
                if (!has)
                    throw new FileNotFoundException(metaPath);
                string tmp = Path.GetTempFileName();
                try
                {
                    await DownloadEntryFromZipAsync(client, url, metaPath, tmp);
                    string content = File.ReadAllText(tmp, Encoding.UTF8);
                    if (outputPath != null) File.WriteAllText(outputPath, content);
                    return content;
                }
                finally { try { File.Delete(tmp); } catch { } }
            }
            else
            {
                throw new InvalidOperationException("remote url does not point to a zip containing android metadata");
            }
        }

        /// <summary>
        /// 从 payload 流读取并解析 manifest。
        /// </summary>
        private (DeltaArchiveManifest manifest, ulong manifestLen, uint metadataSigLen, ulong baseOffset) ReadManifest(Stream stream)
        {
            byte[] magic = new byte[PayloadMagic.Length];
            int bytesRead = stream.Read(magic, 0, magic.Length);
            if (bytesRead != magic.Length || Encoding.ASCII.GetString(magic) != PayloadMagic)
            {
                throw new InvalidOperationException($"Incorrect magic ({Encoding.ASCII.GetString(magic)})");
            }

            ulong version = ReadUInt64BigEndian(stream);
            if (version != BrilloMajorPayloadVersion)
            {
                throw new InvalidOperationException($"Unsupported payload version ({version}). This tool only supports version {BrilloMajorPayloadVersion}");
            }

            ulong manifestLen = ReadUInt64BigEndian(stream);
            if (manifestLen <= 0)
            {
                throw new InvalidOperationException($"Incorrect manifest length ({manifestLen})");
            }

            uint metadataSigLen = ReadUInt32BigEndian(stream);
            if (metadataSigLen <= 0)
            {
                throw new InvalidOperationException($"Incorrect metadata signature length ({metadataSigLen})");
            }

            byte[] manifestRaw = new byte[manifestLen];
            bytesRead = stream.Read(manifestRaw, 0, manifestRaw.Length);
            if ((ulong)bytesRead != manifestLen)
            {
                throw new InvalidOperationException($"Failed to read the manifest ({manifestLen})");
            }

            DeltaArchiveManifest manifest = DeltaArchiveManifest.Parser.ParseFrom(manifestRaw);
            // do not reject nonzero minor version; delta payloads are allowed but may
            // contain operations we don't support when extracting later.
            if (manifest.MinorVersion != 0)
            {
                // Debug.WriteLine($"delta payload minor version {manifest.MinorVersion}");
            }

            ulong baseOffset = 24 + manifestLen + metadataSigLen;
            return (manifest, manifestLen, metadataSigLen, baseOffset);
        }

        /// <summary>
        /// 异步从 payload 流读取并解析 manifest。
        /// </summary>
        private async Task<(DeltaArchiveManifest manifest, ulong manifestLen, uint metadataSigLen, ulong baseOffset)> ReadManifestAsync(Stream stream)
        {
            byte[] magic = new byte[PayloadMagic.Length];
            int bytesRead = await stream.ReadAsync(magic, 0, magic.Length);
            if (bytesRead != magic.Length || Encoding.ASCII.GetString(magic) != PayloadMagic)
            {
                throw new InvalidOperationException($"Incorrect magic ({Encoding.ASCII.GetString(magic)})");
            }

            ulong version = await ReadUInt64BigEndianAsync(stream);
            if (version != BrilloMajorPayloadVersion)
            {
                throw new InvalidOperationException($"Unsupported payload version ({version}). This tool only supports version {BrilloMajorPayloadVersion}");
            }

            ulong manifestLen = await ReadUInt64BigEndianAsync(stream);
            if (manifestLen <= 0)
            {
                throw new InvalidOperationException($"Incorrect manifest length ({manifestLen})");
            }

            uint metadataSigLen = await ReadUInt32BigEndianAsync(stream);
            if (metadataSigLen <= 0)
            {
                throw new InvalidOperationException($"Incorrect metadata signature length ({metadataSigLen})");
            }

            byte[] manifestRaw = new byte[manifestLen];
            bytesRead = await stream.ReadAsync(manifestRaw, 0, manifestRaw.Length);
            if ((ulong)bytesRead != manifestLen)
            {
                throw new InvalidOperationException($"Failed to read the manifest ({manifestLen})");
            }

            DeltaArchiveManifest manifest = DeltaArchiveManifest.Parser.ParseFrom(manifestRaw);
            if (manifest.MinorVersion != 0)
            {
                // allow delta manifests, no exception
                // Debug.WriteLine($"delta payload minor version {manifest.MinorVersion}");
            }

            ulong baseOffset = 24 + manifestLen + metadataSigLen;
            return (manifest, manifestLen, metadataSigLen, baseOffset);
        }

        /// <summary>
        /// 解析 payload 并解压分区为镜像文件。
        /// </summary>
        private void ParsePayload(Stream stream, string[] extractFiles)
        {

            // Magic
            byte[] magic = new byte[PayloadMagic.Length];
            int bytesRead = stream.Read(magic, 0, magic.Length);
            if (bytesRead != magic.Length || Encoding.ASCII.GetString(magic) != PayloadMagic)
            {
                throw new InvalidOperationException($"Incorrect magic ({Encoding.ASCII.GetString(magic)})");
            }

            // Version & lengths
            ulong version = ReadUInt64BigEndian(stream);
            if (version != BrilloMajorPayloadVersion)
            {
                throw new InvalidOperationException($"Unsupported payload version ({version}). This tool only supports version {BrilloMajorPayloadVersion}");
            }

            ulong manifestLen = ReadUInt64BigEndian(stream);
            if (manifestLen <= 0)
            {
                throw new InvalidOperationException($"Incorrect manifest length ({manifestLen})");
            }

            uint metadataSigLen = ReadUInt32BigEndian(stream);
            if (metadataSigLen <= 0)
            {
                throw new InvalidOperationException($"Incorrect metadata signature length ({metadataSigLen})");
            }

            // Manifest
            byte[] manifestRaw = new byte[manifestLen];
            bytesRead = stream.Read(manifestRaw, 0, manifestRaw.Length);
            if ((ulong)bytesRead != manifestLen)
            {
                throw new InvalidOperationException($"Failed to read the manifest ({manifestLen})");
            }

            DeltaArchiveManifest manifest = DeltaArchiveManifest.Parser.ParseFrom(manifestRaw);
            if (manifest.MinorVersion != 0)
            {
                throw new InvalidOperationException("Delta payloads are not supported, please use a full payload file");
            }

            // Print manifest info
            //$"Block size: {manifest.BlockSize}, Partition count: {manifest.Partitions.Count}"

            // Extract partitions
            ExtractPartitions(manifest, stream, 24 + manifestLen + metadataSigLen, extractFiles);

            // Done
            //"Done!"
        }

        /// <summary>
        /// 从流读取大端 UInt64。
        /// </summary>
        private ulong ReadUInt64BigEndian(Stream stream)
        {
            byte[] buffer = new byte[8];
            if (stream.Read(buffer, 0, buffer.Length) != buffer.Length)
            {
                throw new EndOfStreamException();
            }
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt64(buffer, 0);
        }

        /// <summary>
        /// 从流读取大端 UInt32。
        /// </summary>
        private uint ReadUInt32BigEndian(Stream stream)
        {
            byte[] buffer = new byte[4];
            if (stream.Read(buffer, 0, buffer.Length) != buffer.Length)
            {
                throw new EndOfStreamException();
            }
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt32(buffer, 0);
        }
        /// <summary>
        /// 解压 manifest 中描述的分区。
        /// </summary>
        private void ExtractPartitions(DeltaArchiveManifest manifest, Stream stream, ulong baseOffset, string[] extractFiles)
        {
            foreach (var partition in manifest.Partitions)
            {
                if (partition.PartitionName == null || extractFiles.Length > 0 && !extractFiles.Contains(partition.PartitionName))
                {
                    continue;
                }
                //$"Extracting {partition.PartitionName} ({partition.Operations.Count} ops) ..."
                string outFilename = $"{partition.PartitionName}.img";
                ExtractPartition(partition, outFilename, stream, baseOffset, manifest.BlockSize);
            }
        }
        /// <summary>
        /// 异步解析 payload 并解压分区为镜像文件。
        /// </summary>
        public async Task ParsePayloadAsync(Stream stream, string[] extractFiles)
        {
            //"Parsing payload..."

            // Magic
            byte[] magic = new byte[PayloadMagic.Length];
            int bytesRead = await stream.ReadAsync(magic, 0, magic.Length);
            if (bytesRead != magic.Length || Encoding.ASCII.GetString(magic) != PayloadMagic)
            {
                throw new InvalidOperationException($"Incorrect magic ({Encoding.ASCII.GetString(magic)})");
            }

            // Version & lengths
            ulong version = await ReadUInt64BigEndianAsync(stream);
            if (version != BrilloMajorPayloadVersion)
            {
                throw new InvalidOperationException($"Unsupported payload version ({version}). This tool only supports version {BrilloMajorPayloadVersion}");
            }

            ulong manifestLen = await ReadUInt64BigEndianAsync(stream);
            if (manifestLen <= 0)
            {
                throw new InvalidOperationException($"Incorrect manifest length ({manifestLen})");
            }

            uint metadataSigLen = await ReadUInt32BigEndianAsync(stream);
            if (metadataSigLen <= 0)
            {
                throw new InvalidOperationException($"Incorrect metadata signature length ({metadataSigLen})");
            }

            // Manifest
            byte[] manifestRaw = new byte[manifestLen];
            bytesRead = await stream.ReadAsync(manifestRaw, 0, manifestRaw.Length);
            if ((ulong)bytesRead != manifestLen)
            {
                throw new InvalidOperationException($"Failed to read the manifest ({manifestLen})");
            }

            DeltaArchiveManifest manifest = DeltaArchiveManifest.Parser.ParseFrom(manifestRaw);
            if (manifest.MinorVersion != 0)
            {
                throw new InvalidOperationException("Delta payloads are not supported, please use a full payload file");
            }

            // Print manifest info
            //$"Block size: {manifest.BlockSize}, Partition count: {manifest.Partitions.Count}"

            // Extract partitions
            await ExtractPartitionsAsync(manifest, stream, 24 + manifestLen + metadataSigLen, extractFiles);

            // Done
        }

        /// <summary>
        /// 异步从流读取大端 UInt64。
        /// </summary>
        private async Task<ulong> ReadUInt64BigEndianAsync(Stream stream)
        {
            byte[] buffer = new byte[8];
            if (await stream.ReadAsync(buffer, 0, buffer.Length) != buffer.Length)
            {
                throw new EndOfStreamException();
            }
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt64(buffer, 0);
        }

        /// <summary>
        /// 异步从流读取大端 UInt32。
        /// </summary>
        private async Task<uint> ReadUInt32BigEndianAsync(Stream stream)
        {
            byte[] buffer = new byte[4];
            if (await stream.ReadAsync(buffer, 0, buffer.Length) != buffer.Length)
            {
                throw new EndOfStreamException();
            }
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt32(buffer, 0);
        }
        /// <summary>
        /// 异步解压 manifest 中描述的分区。
        /// </summary>
        public async Task ExtractPartitionsAsync(DeltaArchiveManifest manifest, Stream stream, ulong baseOffset, string[] extractFiles)
        {
            var tasks = new List<Task>();

            foreach (var partition in manifest.Partitions)
            {
                if (partition.PartitionName == null || extractFiles.Length > 0 && !extractFiles.Contains(partition.PartitionName))
                {
                    continue;
                }

                //$"Extracting {partition.PartitionName} ({partition.Operations.Count} ops) ..."
                string outFilename = $"{partition.PartitionName}.img";
                tasks.Add(Task.Run(() => ExtractPartition(partition, outFilename, stream, baseOffset, manifest.BlockSize)));
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 解压单个分区到镜像文件。
        /// <para>当前实现支持以下操作类型：</para>
        /// <list type="bullet">
        ///   <item><description>Replace / ReplaceBz / ReplaceXz / ReplaceZstd</description></item>
        ///   <item><description>Zero</description></item>
        ///   <item><description>Discard (跳过，不写入数据)</description></item>
        ///   <item><description>SourceCopy（顺序执行）</description></item>
        /// </list>
        /// 其他类型（SourceBsdiff、BrotliBsdiff、Puffdiff 等）仍未实现，
        /// 遇到时会抛出 <see cref="NotSupportedException"/> 。
        /// </summary>
        private void ExtractPartition(PartitionUpdate partition, string outFilename, Stream stream, ulong baseOffset, uint blockSize)
        {
            using var outFile = new FileStream(outFilename, FileMode.Create, FileAccess.ReadWrite);
            // calculate approximate total size based on dst extents
            long totalSize = 0;
            foreach (var op in partition.Operations)
            {
                foreach (var ext in op.DstExtents)
                {
                    totalSize += (long)ext.NumBlocks * blockSize;
                }
            }
            if (totalSize > 0)
                outFile.SetLength(totalSize);

            object writeLock = new object();
            object streamLock = new object();

            var parallelOps = new List<InstallOperation>();
            var sequentialOps = new List<InstallOperation>();
            foreach (var op in partition.Operations)
            {
                // operations that rely on reading data already written to the destination
                // (source copy/diff) must be executed sequentially, otherwise we risk races.
                if (op.Type == InstallOperation.Types.Type.SourceCopy ||
                    op.Type == InstallOperation.Types.Type.SourceBsdiff ||
                    op.Type == InstallOperation.Types.Type.BrotliBsdiff ||
                    op.Type == InstallOperation.Types.Type.Puffdiff)
                {
                    sequentialOps.Add(op);
                }
                else
                {
                    parallelOps.Add(op);
                }
            }

            void ProcessOperation(InstallOperation op)
            {
                byte[] data = new byte[op.DataLength];
                long dataPos = (long)(baseOffset + op.DataOffset);
                lock (streamLock)
                {
                    stream.Seek(dataPos, SeekOrigin.Begin);
                    int r = stream.Read(data, 0, data.Length);
                    if (r != data.Length)
                        throw new InvalidOperationException($"Failed to read enough data from partition {outFilename}");
                }
                long outSeekPos = (long)(op.DstExtents[0].StartBlock * blockSize);
                lock (writeLock)
                {
                    outFile.Seek(outSeekPos, SeekOrigin.Begin);
                    switch (op.Type)
                    {
                        case InstallOperation.Types.Type.Replace:
                            outFile.Write(data, 0, data.Length);
                            break;
                        case InstallOperation.Types.Type.ReplaceBz:
                            using (var bzr = new BZip2Stream(new MemoryStream(data), SharpCompress.Compressors.CompressionMode.Decompress, false))
                            {
                                bzr.CopyTo(outFile);
                            }
                            break;
                        case InstallOperation.Types.Type.ReplaceXz:
                            using (var xzr = new XZStream(new MemoryStream(data)))
                            {
                                xzr.CopyTo(outFile);
                            }
                            break;
                        case InstallOperation.Types.Type.ReplaceZstd:
                            using (var d = new ZstdSharp.Decompressor())
                            {
                                byte[] decomp = d.Unwrap(data).ToArray();
                                outFile.Write(decomp, 0, decomp.Length);
                            }
                            break;
                        case InstallOperation.Types.Type.Zero:
                            foreach (var ext in op.DstExtents)
                            {
                                long pos = (long)(ext.StartBlock * blockSize);
                                outFile.Seek(pos, SeekOrigin.Begin);
                                byte[] zeros = new byte[ext.NumBlocks * blockSize];
                                outFile.Write(zeros, 0, zeros.Length);
                            }
                            break;
                        case InstallOperation.Types.Type.Discard:
                            // nothing needs to be written; the destination remains unchanged
                            // (treated as undefined).
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported parallel operation type: {op.Type}");
                    }
                }
            }

            Parallel.ForEach(parallelOps.OrderBy(o => o.DataOffset), ProcessOperation);

            foreach (var op in sequentialOps)
            {
                if (op.Type == InstallOperation.Types.Type.SourceCopy)
                {
                    foreach (var srcExt in op.SrcExtents)
                    {
                        long srcPos = (long)(srcExt.StartBlock * blockSize);
                        int length = (int)(srcExt.NumBlocks * blockSize);
                        byte[] buf = new byte[length];
                        outFile.Seek(srcPos, SeekOrigin.Begin);
                        int r = outFile.Read(buf, 0, length);
                        if (r != length)
                            throw new InvalidOperationException("Failed to read source data for SOURCE_COPY");
                        long dstPos = (long)(op.DstExtents[0].StartBlock * blockSize);
                        outFile.Seek(dstPos, SeekOrigin.Begin);
                        outFile.Write(buf, 0, r);
                    }
                }
                else if (op.Type == InstallOperation.Types.Type.SourceBsdiff ||
                         op.Type == InstallOperation.Types.Type.BrotliBsdiff ||
                         op.Type == InstallOperation.Types.Type.Puffdiff)
                {
                    // diff-based operations require applying patch algorithms; not yet
                    // implemented.  leave a clear message rather than falling through to
                    // a generic error.
                    throw new NotSupportedException($"{op.Type} operations are not supported by this library");
                }
                else
                {
                    // This should not happen because we filtered the op types above,
                    // but guard anyway.
                    throw new InvalidOperationException($"Unexpected sequential operation type: {op.Type}");
                }
            }
        }

        /// <summary>
        /// <summary>
        /// 检查远程 URL 是否包含 payload 内容。
        /// - ZIP 包：会尝试查找 payload.bin 条目
        /// - 直接的 payload.bin：只检查文件头魔数
        /// 方法不会下载整个文件，只使用范围请求读取必要字节。
        /// </summary>
        public static async Task<bool> RemoteUrlContainsPayloadAsync(string url)
        {
            using HttpClient client = new();
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 3);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
                return false;
            byte[] header = await resp.Content.ReadAsByteArrayAsync();
            if (header.Length >= 2 && Encoding.ASCII.GetString(header, 0, 2) == ZipMagic)
            {
                // zip file, look for payload entry
                return await RemoteZipHasEntryAsync(client, url, PayloadFilename);
            }
            if (header.Length >= PayloadMagic.Length && Encoding.ASCII.GetString(header, 0, PayloadMagic.Length) == PayloadMagic)
            {
                // raw payload
                return true;
            }
            return false;
        }

        /// <summary>
        /// 从远程 URL 下载并解压 boot 分区镜像。
        /// 支持两种模式：
        /// 1. ZIP 包：使用 HTTP range 请求映射 payload.bin entry，避免下载整个文件。
        /// 2. 传统模式：下载整个 payload（zip 或 raw）并使用现有解析逻辑。
        /// 方法会根据元数据自动处理 A/B 多槽位，所有名称以 "boot" 开头的分区都会输出。
        /// </summary>
        public static Task ExtractBootFromUrlAsync(string url, string? outputDir = null)
        {
            // simply delegate to the general extractor with a filter for boot variants
            string[] bootNames = new[] { "boot", "boot_a", "boot_b" };
            return ExtractSelectedPartitionsFromUrlV2Async(url, outputDir, bootNames);
        }

        private static async Task<bool> RemoteZipHasEntryAsync(HttpClient client, string url, string entryName)
        {
            try
            {
                await GetZipEntryInfoAsync(client, url, entryName);
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch { return false; }
        }

        private static async Task<(long dataOffset, long compSize)> GetZipEntryInfoAsync(HttpClient client, string url, string entryName)
        {
            long fileSize = await GetRemoteFileSizeAsync(client, url);
            long toFetch = Math.Min(66000, fileSize);
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(fileSize - toFetch, fileSize - 1);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            byte[] tail = await resp.Content.ReadAsByteArrayAsync();

            int eocdOffset = FindEOCDOffset(tail);
            if (eocdOffset < 0) throw new InvalidOperationException("EOCD not found");

            using var ms = new MemoryStream(tail, eocdOffset, tail.Length - eocdOffset);
            using var br = new BinaryReader(ms);
            br.ReadUInt32();
            br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt16();
            uint cSize = br.ReadUInt32();
            uint cOffset = br.ReadUInt32();
            long centralSize = cSize;
            long centralOffset = cOffset;

            if (cSize == 0xFFFFFFFF || cOffset == 0xFFFFFFFF)
            {
                int locatorPos = -1;
                for (int i = eocdOffset - 20; i >= 0 && i >= eocdOffset - 100; i--)
                {
                    if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x06 && tail[i + 3] == 0x07)
                    {
                        locatorPos = i;
                        break;
                    }
                }
                if (locatorPos != -1)
                {
                    long zip64EocdOffset = BitConverter.ToInt64(tail, locatorPos + 8);
                    req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new RangeHeaderValue(zip64EocdOffset, zip64EocdOffset + 56 - 1);
                    using var resp64 = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    byte[] eocd64 = await resp64.Content.ReadAsByteArrayAsync();
                    centralSize = BitConverter.ToInt64(eocd64, 40);
                    centralOffset = BitConverter.ToInt64(eocd64, 48);
                }
            }

            req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(centralOffset, centralOffset + centralSize - 1);
            using var cr = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            byte[] central = await cr.Content.ReadAsByteArrayAsync();

            int offset = 0;
            long localHeaderOffset = -1;
            long compSize = 0;
            while (offset + 46 <= central.Length)
            {
                uint sig = BitConverter.ToUInt32(central, offset);
                if (sig != 0x02014B50) { offset++; continue; }
                ushort nameLen = BitConverter.ToUInt16(central, offset + 28);
                ushort extraLen = BitConverter.ToUInt16(central, offset + 30);
                ushort commentLen = BitConverter.ToUInt16(central, offset + 32);
                string name = Encoding.UTF8.GetString(central, offset + 46, nameLen);
                if (name == entryName)
                {
                    compSize = (uint)BitConverter.ToUInt32(central, offset + 20);
                    localHeaderOffset = (uint)BitConverter.ToUInt32(central, offset + 42);
                    if (compSize == 0xFFFFFFFF || localHeaderOffset == 0xFFFFFFFF)
                    {
                        int extraOffset = offset + 46 + nameLen;
                        int extraEnd = extraOffset + extraLen;
                        while (extraOffset + 4 <= extraEnd)
                        {
                            ushort tag = BitConverter.ToUInt16(central, extraOffset);
                            ushort size = BitConverter.ToUInt16(central, extraOffset + 2);
                            if (tag == 0x0001)
                            {
                                int zip64DataOffset = extraOffset + 4;
                                if (BitConverter.ToUInt32(central, offset + 24) == 0xFFFFFFFF) zip64DataOffset += 8;
                                if (compSize == 0xFFFFFFFF) { compSize = BitConverter.ToInt64(central, zip64DataOffset); zip64DataOffset += 8; }
                                if (localHeaderOffset == 0xFFFFFFFF) localHeaderOffset = BitConverter.ToInt64(central, zip64DataOffset);
                                break;
                            }
                            extraOffset += 4 + size;
                        }
                    }
                    break;
                }
                offset += 46 + nameLen + extraLen + commentLen;
            }
            if (localHeaderOffset == -1) throw new FileNotFoundException(entryName);

            req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(localHeaderOffset, localHeaderOffset + 30 + entryName.Length + 1024); // assumption
            using var lr = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            byte[] localChunk = await lr.Content.ReadAsByteArrayAsync();
            using var lms = new MemoryStream(localChunk);
            using var lbr = new BinaryReader(lms);
            if (lbr.ReadUInt32() != 0x04034B50) throw new InvalidOperationException("Invalid local header");
            lbr.BaseStream.Seek(26, SeekOrigin.Begin);
            ushort nLen = lbr.ReadUInt16();
            ushort eLen = lbr.ReadUInt16();
            return (localHeaderOffset + 30 + nLen + eLen, compSize);
        }

        public sealed class HttpOffsetStream : Stream
        {
            private readonly HttpClient _client;
            private readonly string _url;
            private readonly long _baseOffset;
            private readonly long _length;
            private long _position;

            public HttpOffsetStream(string url, long offset, long length, HttpClient? client = null)
            {
                _url = url;
                _baseOffset = offset;
                _length = length;
                _client = client ?? new HttpClient();
                _position = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _length) return 0;
                int toRead = (int)Math.Min(count, _length - _position);
                long start = _baseOffset + _position;
                long end = start + toRead - 1;
                var req = new HttpRequestMessage(HttpMethod.Get, _url);
                req.Headers.Range = new RangeHeaderValue(start, end);
                var resp = _client.Send(req);
                resp.EnsureSuccessStatusCode();
                byte[] data = resp.Content.ReadAsByteArrayAsync().Result;
                Array.Copy(data, 0, buffer, offset, data.Length);
                _position += data.Length;
                return data.Length;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long newPos = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => _length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };
                if (newPos < 0 || newPos > _length) throw new IOException("Seek out of range");
                _position = newPos;
                return _position;
            }
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }



        private static int FindEOCDOffset(byte[] data)
        {
            for (int i = data.Length - 22; i >= 0; i--)
            {
                if (data[i] == 0x50 && data[i + 1] == 0x4b && data[i + 2] == 0x05 && data[i + 3] == 0x06)
                    return i;
            }
            return -1;
        }

        private static bool CentralDirectoryContains(byte[] central, string entryName)
        {
            int offset = 0;
            while (offset + 46 <= central.Length)
            {
                uint sig = BitConverter.ToUInt32(central, offset);
                if (sig != 0x02014B50)
                {
                    offset += 1;
                    continue;
                }

                ushort nameLen = BitConverter.ToUInt16(central, offset + 28);
                ushort extraLen = BitConverter.ToUInt16(central, offset + 30);
                ushort commentLen = BitConverter.ToUInt16(central, offset + 32);

                if (offset + 46 + nameLen > central.Length) break;

                string name = Encoding.UTF8.GetString(central, offset + 46, nameLen);
                if (name == entryName)
                    return true;
                offset += 46 + nameLen + extraLen + commentLen;
            }
            return false;
        }

        private static async Task DownloadEntryFromZipAsync(HttpClient client, string url, string entryName, string outputPath)
        {
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            }

            long fileSize = await GetRemoteFileSizeAsync(client, url);
            long toFetch = Math.Min(66000, fileSize);
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(fileSize - toFetch, fileSize - 1);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            byte[] tail = await resp.Content.ReadAsByteArrayAsync();
            int eocdOffset = FindEOCDOffset(tail);
            if (eocdOffset < 0) throw new InvalidOperationException("EOCD not found");
            using var ms = new MemoryStream(tail, eocdOffset, tail.Length - eocdOffset);
            using var br = new BinaryReader(ms);
            br.ReadUInt32(); br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt16();
            long centralSize = br.ReadUInt32();
            long centralOffset = br.ReadUInt32();

            if (centralOffset == 0xFFFFFFFF || centralSize == 0xFFFFFFFF)
            {
                int locatorIdx = -1;
                for (int i = eocdOffset - 20; i >= 0 && i >= eocdOffset - 100; i--)
                {
                    if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 0x06 && tail[i + 3] == 0x07)
                    {
                        locatorIdx = i;
                        break;
                    }
                }

                if (locatorIdx != -1)
                {
                    long zip64EocdOffset = BitConverter.ToInt64(tail, locatorIdx + 8);

                    req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new RangeHeaderValue(zip64EocdOffset, zip64EocdOffset + 56 - 1);
                    using var zip64Resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    byte[] zip64Eocd = await zip64Resp.Content.ReadAsByteArrayAsync();

                    centralSize = BitConverter.ToInt64(zip64Eocd, 40);
                    centralOffset = BitConverter.ToInt64(zip64Eocd, 48);
                }
            }

            long start = centralOffset;
            long end = centralOffset + centralSize - 1;
            if (end < start) end = start;
            req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(start, end);
            using var cr = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            byte[] central = await cr.Content.ReadAsByteArrayAsync();

            int offset = 0;
            long localHeaderOffset = -1;
            ushort compMethod = 0;
            long compSize = 0;
            while (offset + 46 <= central.Length)
            {
                uint sig = BitConverter.ToUInt32(central, offset);
                if (sig != 0x02014B50)
                {
                    offset++;
                    continue;
                }

                ushort nameLen = BitConverter.ToUInt16(central, offset + 28);
                ushort extraLen = BitConverter.ToUInt16(central, offset + 30);
                ushort commentLen = BitConverter.ToUInt16(central, offset + 32);

                string name = Encoding.UTF8.GetString(central, offset + 46, nameLen);
                if (name == entryName)
                {
                    compMethod = BitConverter.ToUInt16(central, offset + 10);
                    compSize = (uint)BitConverter.ToUInt32(central, offset + 20);
                    localHeaderOffset = (uint)BitConverter.ToUInt32(central, offset + 42);

                    if (compSize == 0xFFFFFFFF || localHeaderOffset == 0xFFFFFFFF)
                    {
                        int extraOffset = offset + 46 + nameLen;
                        int extraEnd = extraOffset + extraLen;
                        while (extraOffset + 4 <= extraEnd)
                        {
                            ushort tag = BitConverter.ToUInt16(central, extraOffset);
                            ushort size = BitConverter.ToUInt16(central, extraOffset + 2);
                            if (tag == 0x0001) // Zip64 tag
                            {
                                int zip64DataOffset = extraOffset + 4;
                                if (BitConverter.ToUInt32(central, offset + 24) == 0xFFFFFFFF)
                                {
                                    zip64DataOffset += 8;
                                }
                                if (compSize == 0xFFFFFFFF)
                                {
                                    compSize = BitConverter.ToInt64(central, zip64DataOffset);
                                    zip64DataOffset += 8;
                                }
                                if (localHeaderOffset == 0xFFFFFFFF)
                                {
                                    localHeaderOffset = BitConverter.ToInt64(central, zip64DataOffset);
                                }
                                break;
                            }
                            extraOffset += 4 + size;
                        }
                    }
                    break;
                }
                offset += 46 + nameLen + extraLen + commentLen;
            }
            if (localHeaderOffset == -1) throw new FileNotFoundException(entryName);

            req = new HttpRequestMessage(HttpMethod.Get, url);
            long headerEnd = localHeaderOffset + 64 * 1024 - 1;
            req.Headers.Range = new RangeHeaderValue(localHeaderOffset, headerEnd);
            using var lr = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            byte[] localChunk = await lr.Content.ReadAsByteArrayAsync();
            using var lms = new MemoryStream(localChunk);
            using var lbr = new BinaryReader(lms);
            uint lhSig = lbr.ReadUInt32();
            if (lhSig != 0x04034b50) throw new InvalidOperationException("Invalid local header signature");
            lbr.ReadUInt16();
            lbr.ReadUInt16();
            ushort lhCompMethod = lbr.ReadUInt16();
            lbr.ReadUInt16(); lbr.ReadUInt16();
            lbr.ReadUInt32(); lbr.ReadUInt32(); lbr.ReadUInt32();
            ushort fhNameLen = lbr.ReadUInt16();
            ushort fhExtraLen = lbr.ReadUInt16();
            long headerSize = 30 + fhNameLen + fhExtraLen;
            long dataOffset = (long)localHeaderOffset + headerSize;

            req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(dataOffset, dataOffset + compSize - 1);
            using var dr = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            using var compressedStream = await dr.Content.ReadAsStreamAsync();

            using var outFs = File.Create(outputPath);
            Stream decompressStream;
            switch (compMethod)
            {
                case 0:
                    await compressedStream.CopyToAsync(outFs);
                    return;
                case 8:
                    decompressStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported compression method {compMethod}");
            }
            await decompressStream.CopyToAsync(outFs);
        }

        private static async Task<long> GetRemoteFileSizeAsync(HttpClient client, string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Head, url);
            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength.HasValue)
                return resp.Content.Headers.ContentLength.Value;
            throw new InvalidOperationException("Unable to determine remote file size");
        }

        /// <summary>
        /// 从远程地址获取 payload 元数据。
        /// 用于查询分区表，不会下载整个 payload 文件。
        /// </summary>
        public static async Task<PayloadMetadata> GetPayloadMetadataFromUrlAsync(string url)
        {
            using HttpClient client = new();
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 3);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            byte[] header = await resp.Content.ReadAsByteArrayAsync();

            if (header.Length >= 2 && Encoding.ASCII.GetString(header, 0, 2) == ZipMagic)
            {
                bool has = await RemoteZipHasEntryAsync(client, url, PayloadFilename);
                if (!has)
                    throw new FileNotFoundException("payload.bin not found in remote zip");
                string tmp = Path.GetTempFileName();
                try
                {
                    await DownloadEntryFromZipAsync(client, url, PayloadFilename, tmp);
                    return GetPayloadMetadata(tmp);
                }
                finally { try { File.Delete(tmp); } catch { } }
            }
            else
            {
                req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(0, 23);
                using var hdrResp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                hdrResp.EnsureSuccessStatusCode();
                byte[] hdr = await hdrResp.Content.ReadAsByteArrayAsync();
                if (hdr.Length < 24 || Encoding.ASCII.GetString(hdr, 0, PayloadMagic.Length) != PayloadMagic)
                    throw new InvalidOperationException("not a payload file");
                ulong version = ReadUInt64BigEndian(hdr, 4);
                if (version != BrilloMajorPayloadVersion)
                    throw new InvalidOperationException($"unsupported payload version {version}");
                ulong manifestLen = ReadUInt64BigEndian(hdr, 12);
                uint sigLen = ReadUInt32BigEndian(hdr, 20);
                long manifestStart = 24;
                long manifestEnd = manifestStart + (long)manifestLen - 1;
                req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(manifestStart, manifestEnd);
                using var manResp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                manResp.EnsureSuccessStatusCode();
                byte[] manifestRaw = await manResp.Content.ReadAsByteArrayAsync();
                DeltaArchiveManifest manifest = DeltaArchiveManifest.Parser.ParseFrom(manifestRaw);
                if (manifest.MinorVersion != 0)
                    throw new InvalidOperationException("Delta payloads are not supported, please use a full payload file");
                return BuildMetadata((manifest, manifestLen, sigLen, (ulong)(24 + manifestLen + sigLen)));
            }
        }

        private static ulong ReadUInt64BigEndian(byte[] buf, int offset)
        {
            byte[] tmp = new byte[8];
            Array.Copy(buf, offset, tmp, 0, 8);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);
            return BitConverter.ToUInt64(tmp, 0);
        }
        private static uint ReadUInt32BigEndian(byte[] buf, int offset)
        {
            byte[] tmp = new byte[4];
            Array.Copy(buf, offset, tmp, 0, 4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);
            return BitConverter.ToUInt32(tmp, 0);
        }
    }
}
