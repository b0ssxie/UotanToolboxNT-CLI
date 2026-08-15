using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Common
{
    public class FeaturesHelper
    {
        /// <summary>
        /// CLI 模式的轻量翻译表（原 GUI 使用资源文件，这里内置常用键的中文翻译）。
        /// </summary>
        private static readonly Dictionary<string, string> Translations = new()
        {
            ["Home_Android"] = "Android",
            ["Home_Fastboot"] = "Fastboot",
            ["Home_Fastbootd"] = "Fastbootd",
            ["Home_Recovery"] = "Recovery",
            ["Home_Sideload"] = "Sideload",
            ["Home_OpenHOS"] = "OpenHarmony",
            ["Info_UnauthorizedDevice"] = "未授权设备",
            ["Info_BLstatusUnlocked"] = "已解锁",
            ["Info_BLstatusLocked"] = "已上锁",
            ["Info_ASlot"] = "A 槽",
            ["Info_BSlot"] = "B 槽",
            ["Info_AOnly"] = "A/B 分区 (单槽)",
            ["Info_Day"] = "天",
            ["Info_Hour"] = "时",
            ["Info_Minute"] = "分",
            ["Info_Second"] = "秒",
        };

        public static string GetTranslation(string key)
        {
            if (Translations.TryGetValue(key, out var value))
                return value;
            return key;
        }

        private static string BuildAdbArgs(string deviceId, string cmd)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || cmd.TrimStart().StartsWith("-s ", StringComparison.Ordinal))
            {
                return cmd;
            }

            return $"-s {deviceId} {cmd}";
        }

        private static string BuildFastbootArgs(string deviceId, string cmd)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || cmd.TrimStart().StartsWith("-s ", StringComparison.Ordinal))
            {
                return cmd;
            }

            return $"-s {deviceId} {cmd}";
        }

        private static string BuildHdcArgs(string deviceId, string cmd)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || cmd.TrimStart().StartsWith("-t ", StringComparison.Ordinal))
            {
                return cmd;
            }

            return $"-t {deviceId} {cmd}";
        }

        public static async Task<string> AdbCmd(string deviceId, string cmd)
        {
            // if a specific device id was supplied, try the device manager first
            if (!string.IsNullOrEmpty(deviceId) && Global.DeviceManager != null)
            {
                var dev = Global.DeviceManager.Devices.FirstOrDefault(d => d.Id == deviceId && d.Transport == TransportType.Adb);
                if (dev != null)
                {
                    return await Global.DeviceManager.ExecuteAsync(dev, cmd);
                }
            }
            // fallback to raw command; omit -s when no device is provided
            return await CallExternalProgram.ADB(BuildAdbArgs(deviceId, cmd));
        }

        public static async Task<string> FastbootCmd(string deviceId, string cmd)
        {
            if (!string.IsNullOrEmpty(deviceId) && Global.DeviceManager != null)
            {
                var dev = Global.DeviceManager.Devices.FirstOrDefault(d => d.Id == deviceId && d.Transport == TransportType.Fastboot);
                if (dev != null)
                {
                    return await Global.DeviceManager.ExecuteAsync(dev, cmd);
                }
            }
            return await CallExternalProgram.Fastboot(BuildFastbootArgs(deviceId, cmd));
        }

        public static async Task<string> HdcCmd(string deviceId, string cmd)
        {
            if (!string.IsNullOrEmpty(deviceId) && Global.DeviceManager != null)
            {
                var dev = Global.DeviceManager.Devices.FirstOrDefault(d => d.Id == deviceId && d.Transport == TransportType.Hdc);
                if (dev != null)
                {
                    return await Global.DeviceManager.ExecuteAsync(dev, cmd);
                }
            }
            return await CallExternalProgram.HDC(BuildHdcArgs(deviceId, cmd));
        }

        public static async void PushMakefs(string device)
        {
            _ = await AdbCmd(device, $"push \"{Path.Combine(Global.runpath, "Push", "mkfs.f2fs")}\" /tmp/");
            _ = await AdbCmd(device, "shell chmod +x /tmp/mkfs.f2fs");
            _ = await AdbCmd(device, $"push \"{Path.Combine(Global.runpath, "Push", "mkntfs")}\" /tmp/");
            _ = await AdbCmd(device, "shell chmod +x /tmp/mkntfs");
        }

        public static async Task GetPartTable(string device)
        {
            _ = await AdbCmd(device, $"push \"{Path.Combine(Global.runpath, "Push", "parted")}\" /tmp/");
            _ = await AdbCmd(device, "shell chmod +x /tmp/parted");
            Global.sdatable = await AdbCmd(device, "shell /tmp/parted /dev/block/sda print");
            Global.sdbtable = await AdbCmd(device, "shell /tmp/parted /dev/block/sdb print");
            Global.sdctable = await AdbCmd(device, "shell /tmp/parted /dev/block/sdc print");
            Global.sddtable = await AdbCmd(device, "shell /tmp/parted /dev/block/sdd print");
            Global.sdetable = await AdbCmd(device, "shell /tmp/parted /dev/block/sde print");
            Global.sdftable = await AdbCmd(device, "shell /tmp/parted /dev/block/sdf print");
            Global.sdgtable = await AdbCmd(device, "shell /tmp/parted /dev/block/sdg print");
            Global.sdhtable = await AdbCmd(device, "shell /tmp/parted /dev/block/sdh print");
            Global.emmcrom = await AdbCmd(device, "shell /tmp/parted /dev/block/mmcblk0 print");
        }

        public static async Task GetPartTableSystem(string device)
        {
            _ = await AdbCmd(device, $"push \"{Path.Combine(Global.runpath, "Push", "parted")}\" /data/local/tmp/");
            _ = await AdbCmd(device, "shell su -c \"chmod +x /data/local/tmp/parted\"");
            Global.sdatable = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/sda print\"");
            Global.sdbtable = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/sdb print\"");
            Global.sdctable = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/sdc print\"");
            Global.sddtable = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/sdd print\"");
            Global.sdetable = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/sde print\"");
            Global.sdftable = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/sdf print\"");
            Global.sdgtable = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/sdg print\"");
            Global.sdhtable = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/sdh print\"");
            Global.emmcrom = await AdbCmd(device, "shell su -c \"/data/local/tmp/parted /dev/block/mmcblk0 print\"");
        }

        public static async Task GetPartTableSystemDebug(string device)
        {
            _ = await AdbCmd(device, "root");
            _ = await AdbCmd(device, $"push \"{Path.Combine(Global.runpath, "Push", "parted")}\" /data/local/tmp/");
            _ = await AdbCmd(device, "shell chmod +x /data/local/tmp/parted");
            Global.sdatable = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/sda print");
            Global.sdbtable = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/sdb print");
            Global.sdctable = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/sdc print");
            Global.sddtable = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/sdd print");
            Global.sdetable = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/sde print");
            Global.sdftable = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/sdf print");
            Global.sdgtable = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/sdg print");
            Global.sdhtable = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/sdh print");
            Global.emmcrom = await AdbCmd(device, "shell /data/local/tmp/parted /dev/block/mmcblk0 print");
        }

        public static string[] GetVPartList(string allinfo)
        {
            string[] vparts = new string[1000];
            string[] allinfos = allinfo.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < allinfos.Length; i++)
            {
                if (allinfos[i].Contains("is-logical") && allinfos[i].Contains("yes"))
                {
                    string[] vpartinfos = allinfos[i].Split([':', ' '], StringSplitOptions.RemoveEmptyEntries);
                    vparts[i] = vpartinfos[2];
                }
            }
            vparts = [.. vparts.Where(s => !string.IsNullOrEmpty(s))];
            return vparts;
        }

        public static string FindDisk(string Partname)
        {
            string sdxdisk = "";
            string[] diskTables = [Global.sdatable, Global.sdetable, Global.sdbtable, Global.sdctable, Global.sddtable, Global.sdftable, Global.sdgtable, Global.sdhtable, Global.emmcrom];
            string[] diskNames = ["sda", "sde", "sdb", "sdc", "sdd", "sdf", "sdg", "sdh", "mmcblk0p"];
            for (int i = 0; i < diskTables.Length; i++)
            {
                if (diskTables[i].Contains(Partname))
                {
                    if (StringHelper.Partno(diskTables[i], Partname) != null)
                    {
                        sdxdisk = diskNames[i];
                        break;
                    }
                }
            }
            return sdxdisk;
        }

        public static string FindPart(string Partname)
        {
            string sdxdisk = "";
            string[] diskTables = [Global.sdatable, Global.sdetable, Global.sdbtable, Global.sdctable, Global.sddtable, Global.sdftable, Global.sdgtable, Global.sdhtable, Global.emmcrom];
            foreach (string diskTable in diskTables)
            {
                if (diskTable.IndexOf(Partname) != -1)
                {
                    sdxdisk = diskTable;
                    break;
                }
            }
            return sdxdisk;
        }

        public static async Task<string> ActiveApp(string output)
        {
            string adb_output;
            if (output.Contains("moe.shizuku.privileged.api"))
            {
                adb_output = await AdbCmd(Global.thisdevice, "shell sh /storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh");
                return adb_output.Contains("info: shizuku_starter exit with 0")
                    ? "Shizuku" + GetTranslation("Appmgr_ActiveSucc")
                    : "Shizuku" + GetTranslation("Appmgr_ActiveFail");
            }
            else if (output.Contains("com.oasisfeng.greenify"))
            {
                int a = 0;
                adb_output = await AdbCmd(Global.thisdevice, "shell pm grant com.oasisfeng.greenify android.permission.WRITE_SECURE_SETTINGS");
                if (!string.IsNullOrEmpty(adb_output))
                {
                    a++;
                }
                adb_output = await AdbCmd(Global.thisdevice, "shell pm grant com.oasisfeng.greenify android.permission.DUMP");
                if (!string.IsNullOrEmpty(adb_output))
                {
                    a++;
                }
                adb_output = await AdbCmd(Global.thisdevice, "shell pm grant com.oasisfeng.greenify android.permission.READ_LOGS");
                if (!string.IsNullOrEmpty(adb_output))
                {
                    a++;
                }
                adb_output = await AdbCmd(Global.thisdevice, "shell am force-stop com.oasisfeng.greenify");
                if (!string.IsNullOrEmpty(adb_output))
                {
                    a++;
                }
                return a == 0
                    ? GetTranslation("Appmgr_Greenify") + GetTranslation("Appmgr_ActiveSucc")
                    : GetTranslation("Appmgr_Greenify") + GetTranslation("Appmgr_ActiveFail");
            }
            else if (output.Contains("com.rosan.dhizuku"))
            {
                adb_output = await AdbCmd(Global.thisdevice, "shell dpm set-device-owner com.rosan.dhizuku/.server.DhizukuDAReceiver");
                return adb_output.Contains("Success: Device owner set to package")
                    ? "Dhizuku" + GetTranslation("Appmgr_ActiveSucc")
                    : "Dhizuku" + GetTranslation("Appmgr_ActiveFail");
            }
            else if (output.Contains("com.oasisfeng.island"))
            {
                adb_output = await AdbCmd(Global.thisdevice, "shell pm grant com.oasisfeng.island android.permission.INTERACT_ACROSS_USERS");
                return string.IsNullOrEmpty(adb_output)
                    ? "Island" + GetTranslation("Appmgr_ActiveSucc")
                    : "Island" + GetTranslation("Appmgr_ActiveFail");
            }
            else if (output.Contains("me.piebridge.brevent"))
            {
                adb_output = await AdbCmd(Global.thisdevice, "shell sh /data/data/me.piebridge.brevent/brevent.sh");
                return adb_output.Contains("..success")
                    ? "Brevent" + GetTranslation("Appmgr_ActiveSucc")
                    : "Brevent" + GetTranslation("Appmgr_ActiveFail");
            }
            else if (output.Contains("com.catchingnow.icebox"))
            {
                adb_output = await AdbCmd(Global.thisdevice, "shell sh /sdcard/Android/data/com.catchingnow.icebox/files/start.sh");
                return adb_output.Contains("success")
                    ? "IceBox" + GetTranslation("Appmgr_ActiveSucc")
                    : "IceBox" + GetTranslation("Appmgr_ActiveFail");
            }
            else if (output.Contains("web1n.stopapp"))
            {
                adb_output = await AdbCmd(Global.thisdevice, "shell sh /storage/emulated/0/Android/data/web1n.stopapp/files/starter.sh");
                return adb_output.Contains("success to register app changed listener.")
                    ? GetTranslation("Appmgr_Stopapp") + GetTranslation("Appmgr_ActiveSucc")
                    : GetTranslation("Appmgr_Stopapp") + GetTranslation("Appmgr_ActiveFail");
            }
            else if (output.Contains("com.web1n.permissiondog"))
            {
                adb_output = await AdbCmd(Global.thisdevice, "shell sh /storage/emulated/0/Android/data/com.web1n.permissiondog/files/starter.sh");
                return adb_output.Contains("success to register app changed listener.")
                    ? GetTranslation("Appmgr_PermissionDog") + GetTranslation("Appmgr_ActiveSucc")
                    : GetTranslation("Appmgr_PermissionDog") + GetTranslation("Appmgr_ActiveFail");
            }
            return "当前主界面应用不被支持！";

        }
    }
}
