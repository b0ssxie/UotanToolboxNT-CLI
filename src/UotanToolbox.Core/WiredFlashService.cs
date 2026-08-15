using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common.PatchHelper;
using UotanToolbox.Common.ROMHelper;
using ZstdSharp;

namespace UotanToolbox.Common;

/// <summary>
/// TXT 双包刷机（VAB 双槽位动态分区刷机流程），从 GUI 的 WiredflashView 提炼而来，无 UI 依赖。
/// </summary>
public class WiredFlashService
{
    private readonly string _deviceId;
    private readonly bool _addRoot;
    private readonly bool _disableVbmeta;
    private readonly bool _eraseData;
    private readonly string _setBoot;
    private readonly string _magiskApkPath;
    private readonly Action<string>? _log;

    public WiredFlashService(
        string deviceId,
        bool addRoot = false,
        bool disableVbmeta = false,
        bool eraseData = false,
        string setBoot = "boot",
        string magiskApkPath = "",
        Action<string>? log = null)
    {
        _deviceId = deviceId;
        _addRoot = addRoot;
        _disableVbmeta = disableVbmeta;
        _eraseData = eraseData;
        _setBoot = setBoot;
        _magiskApkPath = magiskApkPath;
        _log = log;
    }

    private void Log(string message) => _log?.Invoke(message);

    private async Task<string> Fastboot(string cmd) => await FeaturesHelper.FastbootCmd(_deviceId, cmd);

    private async Task<(bool ok, int c)> CheckModel(string[] flashparts, string codename)
    {
        int c = 0;
        if (flashparts.Length == 0) return (true, c);
        if (flashparts[c].Contains("codename"))
        {
            string[] lines = flashparts[c].Split(':');
            if (lines.Length < 2)
            {
                Log("机型识别行格式错误");
                return (false, c);
            }
            string devicename = lines[1].Trim();
            c = 1;
            if (codename != devicename)
            {
                Log($"机型不匹配！固件机型: {devicename}，设备机型: {codename}，已停止刷机。");
                return (false, c);
            }
            Log($"机型校验通过: {devicename}");
        }
        return (true, c);
    }

    /// <summary>
    /// 解压 .zst 镜像文件，返回解压后的路径。
    /// </summary>
    private static async Task<string> DecompressZst(string filepath)
    {
        var zstfile = File.OpenRead(filepath);
        string zstname = Path.GetFileNameWithoutExtension(filepath);
        if (!zstname.Contains(".img"))
            zstname += ".img";
        string outfile = Path.Combine(Path.GetDirectoryName(filepath) ?? ".", zstname);
        var zstout = File.OpenWrite(outfile);
        var decompress = new DecompressionStream(zstfile);
        await decompress.CopyToAsync(zstout);
        decompress.Close();
        zstout.Close();
        zstfile.Close();
        return outfile;
    }

    private bool IsFailed(string output) =>
        output.Contains("FAILED") || output.Contains("error");

    private async Task<bool> FlashPartitionWithRoot(string part, string filepath)
    {
        if (part == _setBoot && _addRoot && !string.IsNullOrEmpty(_magiskApkPath))
        {
            Log($"检测到 {part}，正在注入 Root (Magisk/KSU)...");
            Global.Bootinfo = await ImageDetect.Boot_Detect(filepath);
            Global.Zipinfo = await PatchDetect.Patch_Detect(_magiskApkPath);
            string newboot;
            switch (Global.Zipinfo.Mode)
            {
                case PatchMode.Magisk:
                    newboot = await MagiskPatch.Magisk_Patch_Mouzei(Global.Zipinfo, Global.Bootinfo);
                    break;
                case PatchMode.GKI:
                    newboot = await KernelSUPatch.GKI_Patch(Global.Zipinfo, Global.Bootinfo);
                    break;
                case PatchMode.LKM:
                    newboot = await KernelSUPatch.LKM_Patch(Global.Zipinfo, Global.Bootinfo);
                    break;
                default:
                    throw new InvalidOperationException($"不支持的修补类型: {Global.Zipinfo.Mode}");
            }
            string cmd = _disableVbmeta && part.Contains("vbmeta")
                ? $"{Global.VbmetaCommand} flash {part} \"{newboot}\""
                : $"flash {part} \"{newboot}\"";
            string output = await Fastboot(cmd);
            Log(output);
            return !IsFailed(output);
        }

        string flashCmd = _disableVbmeta && part.Contains("vbmeta")
            ? $"{Global.VbmetaCommand} flash {part} \"{filepath}\""
            : $"flash {part} \"{filepath}\"";
        string flashOut = await Fastboot(flashCmd);
        Log(flashOut);
        return !IsFailed(flashOut);
    }

    /// <summary>
    /// 执行完整 TXT 双包刷机流程。
    /// </summary>
    /// <param name="fastbootTxt">fastboot.txt 路径（可空）</param>
    /// <param name="fastbootdTxt">fastbootd.txt 路径（可空）</param>
    /// <param name="codename">设备机型（用于校验）；为 null 或空时跳过校验</param>
    /// <returns>是否成功</returns>
    public async Task<bool> FlashAsync(string? fastbootTxt, string? fastbootdTxt, string? codename = null)
    {
        if (string.IsNullOrEmpty(fastbootTxt) && string.IsNullOrEmpty(fastbootdTxt))
        {
            Log("未提供任何刷机脚本。");
            return false;
        }

        bool skipModel = string.IsNullOrEmpty(codename);
        bool succ = true;

        // ===== 阶段 1: 刷 fastboot 分区 =====
        if (!string.IsNullOrEmpty(fastbootTxt))
        {
            string fbtxt = fastbootTxt;
            string imgpath = Path.Combine(Path.GetDirectoryName(fbtxt) ?? ".", "images");
            string[] fbflashparts = File.ReadAllText(fbtxt).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            int c = 0;
            if (!skipModel)
            {
                (bool okModel, c) = await CheckModel(fbflashparts, codename ?? "");
                if (!okModel)
                {
                    Log("机型校验失败，已停止刷机。");
                    return false;
                }
            }
            else if (fbflashparts.Length > 0 && fbflashparts[0].Contains("codename"))
            {
                c = 1; // 跳过机型识别行
            }

            for (int i = c; i < fbflashparts.Length; i++)
            {
                if (fbflashparts[i].Contains(' '))
                {
                    string[] partandpath = fbflashparts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string filepath = Path.Combine(Path.GetDirectoryName(fbtxt) ?? ".", partandpath[1]);
                    if (Path.GetExtension(filepath) == ".zst")
                    {
                        Log($"解压 .zst: {filepath}");
                        filepath = await DecompressZst(filepath);
                    }
                    if (!await FlashPartitionWithRoot(partandpath[0], filepath))
                    {
                        succ = false;
                        break;
                    }
                }
                else
                {
                    if (!await FlashPartitionWithRoot(fbflashparts[i], Path.Combine(imgpath, $"{fbflashparts[i]}.img")))
                    {
                        succ = false;
                        break;
                    }
                }
            }
        }

        // ===== 阶段 2: 刷 fastbootd（动态分区）=====
        if (!string.IsNullOrEmpty(fastbootdTxt) && succ)
        {
            string fbdtxt = fastbootdTxt;
            string fbdDir = Path.GetDirectoryName(fbdtxt) ?? ".";
            string imgpath = Path.Combine(fbdDir, "images");
            string[] fbdflashparts = File.ReadAllText(fbdtxt).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            int c = 0;
            if (!skipModel)
            {
                (bool okModel, c) = await CheckModel(fbdflashparts, codename ?? "");
                if (!okModel)
                {
                    Log("机型校验失败，已停止刷机。");
                    return false;
                }
            }
            else if (fbdflashparts.Length > 0 && fbdflashparts[0].Contains("codename"))
            {
                c = 1; // 跳过机型识别行
            }

            // 2.1 刷入 super_empty
            for (int i = c; i < fbdflashparts.Length && succ; i++)
            {
                string line = fbdflashparts[i];
                if (line.Contains(' '))
                {
                    string[] partandpath = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if ((!partandpath[1].Contains("delete")) && (!partandpath[1].Contains("create")))
                    {
                        if (partandpath[0].Contains("super_empty"))
                        {
                            string file = partandpath[1].StartsWith("/")
                                ? partandpath[1]
                                : Path.Combine(fbdDir, partandpath[1]);
                            string output = await Fastboot($"wipe-super \"{file}\"");
                            Log(output);
                            if (IsFailed(output)) succ = false;
                        }
                    }
                }
                else
                {
                    if (line.Contains("super_empty"))
                    {
                        string output = await Fastboot($"wipe-super \"{Path.Combine(imgpath, line + ".img")}\"");
                        Log(output);
                        if (IsFailed(output)) succ = false;
                    }
                }
            }

            // 2.2 获取槽位信息
            string slot = "";
            string updateStatus = await Fastboot("getvar snapshot-update-status");
            Log(updateStatus);
            string active = await Fastboot("getvar current-slot");
            if (active.Contains("current-slot: a"))
                slot = "_a";
            else if (active.Contains("current-slot: b"))
                slot = "_b";
            else
                slot = "";
            Log($"当前活动槽位: {slot}");

            // 2.3 删除 -cow 分区
            string allvars = await Fastboot("getvar all");
            string[] cowparts = FeaturesHelper.GetVPartList(allvars);
            foreach (var cow in cowparts)
            {
                if (cow.Contains("-cow"))
                {
                    string output = await Fastboot($"delete-logical-partition {cow}");
                    Log(output);
                    if (IsFailed(output)) { succ = false; break; }
                }
            }

            // 2.4 删除对侧槽逻辑分区
            if (!string.IsNullOrEmpty(slot) && succ)
            {
                string deleteslot = slot == "_a" ? "_b" : "_a";
                string partVars = await Fastboot("getvar all");
                string[] deleteslotparts = FeaturesHelper.GetVPartList(partVars);
                foreach (var p in deleteslotparts)
                {
                    if (p.EndsWith(deleteslot))
                    {
                        string output = await Fastboot($"delete-logical-partition {p}");
                        Log(output);
                        if (IsFailed(output)) { succ = false; break; }
                    }
                }
            }

            // 2.5 按脚本重建动态分区（create/delete-logical-partition）
            if (succ)
            {
                string partVars = await Fastboot("getvar all");
                string[] vparts = FeaturesHelper.GetVPartList(partVars);
                for (int i = c; i < fbdflashparts.Length; i++)
                {
                    if (fbdflashparts[i].Contains(' '))
                    {
                        string[] partandpath = fbdflashparts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        string dmpart = $"{partandpath[0]}{slot}";
                        if (Array.Exists(vparts, element => element == dmpart) && (!partandpath[1].Contains("create")))
                        {
                            string output = await Fastboot($"delete-logical-partition {dmpart}");
                            Log(output);
                            if (IsFailed(output)) { succ = false; break; }
                        }
                        if ((Array.Exists(vparts, element => element == dmpart) && (!partandpath[1].Contains("delete")) && (!partandpath[1].Contains("create"))) ||
                            (!Array.Exists(vparts, element => element == dmpart) && partandpath[1].Contains("create") && (!partandpath[1].StartsWith('/'))))
                        {
                            string output = await Fastboot($"create-logical-partition {dmpart} 00");
                            Log(output);
                            if (IsFailed(output)) { succ = false; break; }
                        }
                    }
                    else
                    {
                        string dmpart = $"{fbdflashparts[i]}{slot}";
                        if (Array.Exists(vparts, element => element == dmpart))
                        {
                            string output = await Fastboot($"delete-logical-partition {dmpart}");
                            Log(output);
                            if (IsFailed(output)) { succ = false; break; }
                            output = await Fastboot($"create-logical-partition {dmpart} 00");
                            Log(output);
                            if (IsFailed(output)) { succ = false; break; }
                        }
                    }
                }
            }

            // 2.6 刷入 fastbootd 分区镜像
            if (succ)
            {
                for (int i = c; i < fbdflashparts.Length; i++)
                {
                    if (fbdflashparts[i].Contains(' '))
                    {
                        string[] partandpath = fbdflashparts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if ((!partandpath[1].Contains("delete")) && (!partandpath[1].Contains("create")))
                        {
                            if (!partandpath[0].Contains("super_empty"))
                            {
                                string file = partandpath[1].StartsWith("/")
                                    ? partandpath[1]
                                    : Path.Combine(fbdDir, partandpath[1]);
                                if (Path.GetExtension(file) == ".zst")
                                    file = await DecompressZst(file);
                                string output = _disableVbmeta && partandpath[0].Contains("vbmeta")
                                    ? await Fastboot($"{Global.VbmetaCommand} flash {partandpath[0]} \"{file}\"")
                                    : await Fastboot($"flash {partandpath[0]} \"{file}\"");
                                Log(output);
                                if (IsFailed(output)) { succ = false; break; }
                            }
                        }
                    }
                    else
                    {
                        if (!fbdflashparts[i].Contains("super_empty"))
                        {
                            string file = Path.Combine(imgpath, $"{fbdflashparts[i]}.img");
                            string output = _disableVbmeta && fbdflashparts[i].Contains("vbmeta")
                                ? await Fastboot($"{Global.VbmetaCommand} flash {fbdflashparts[i]} \"{file}\"")
                                : await Fastboot($"flash {fbdflashparts[i]} \"{file}\"");
                            Log(output);
                            if (IsFailed(output)) { succ = false; break; }
                        }
                    }
                }
            }
        }

        // ===== 阶段 3: 擦除数据 =====
        if (succ && _eraseData)
        {
            Log("正在擦除 metadata 和 userdata...");
            string out1 = await Fastboot("erase metadata");
            Log(out1);
            string out2 = await Fastboot("erase userdata");
            Log(out2);
            if (IsFailed(out1) || IsFailed(out2))
                succ = false;
        }

        Log(succ ? "刷机完成。" : "刷机过程中出现错误。");
        return succ;
    }
}
