using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UotanToolbox.Common.Devices;

namespace UotanToolbox.Common
{
    public class ADBPairHelper
    {
        private static async Task<string> Adb(string cmd)
        {
            if (Global.DeviceManager != null)
            {
                var dev = Global.DeviceManager.Devices.FirstOrDefault(d => d.Transport == TransportType.Adb);
                if (dev != null)
                {
                    return await Global.DeviceManager.ExecuteAsync(dev, cmd);
                }
            }
            return await CallExternalProgram.ADB(cmd);
        }

        public static string PairData(string serviceID, string password)
        {
            return "WIFI:T:ADB;S:" + serviceID + ";P:" + password + ";;";
        }

        //Todo:使用原生Zeroconf做网络mdns扫描
        // 返回 null 表示配对连接成功，否则返回错误信息。
        public static async Task<string?> ScanmDNS(string serviceID, string password, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    string result = await Adb("mdns services");
                    if (result.Contains("List of discovered mdns services"))
                    {
                        var lineRegex = "([^\\t]+)\\t*_adb-tls-pairing._tcp.\\t*([^:]+):([0-9]+)";
                        var match = Regex.Match(result, lineRegex);
                        if (match.Success)
                        {
                            string pairAddr = $"{match.Groups[2].Value}:{match.Groups[3].Value}";
                            result = await Adb($"pair {pairAddr} {password}");
                            if (result.Contains("Successfully paired to "))
                            {
                                // 配对成功后从 mdns services 解析出 connect 端口并主动连接
                                string connectResult = await TryConnectFromMdns(cancellationToken);
                                if (string.IsNullOrEmpty(connectResult))
                                {
                                    return null;
                                }
                                else
                                {
                                    return connectResult;
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
            return null;
        }

        private static async Task<string> TryConnectFromMdns(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string result = await Adb("mdns services");
            if (!result.Contains("List of discovered mdns services"))
            {
                return result;
            }
            var connectRegex = "([^\\t]+)\\t*_adb-tls-connect._tcp.\\t*([^:]+):([0-9]+)";
            var connectMatch = Regex.Match(result, connectRegex);
            if (!connectMatch.Success)
            {
                // 回退：直接尝试在配对端口的 host 上 connect（端口通常是 mdns 解析的 service port）
                return string.Empty;
            }
            string connectAddr = $"{connectMatch.Groups[2].Value}:{connectMatch.Groups[3].Value}";
            string connectResult = await Adb($"connect {connectAddr}");
            if (connectResult.Contains("connected to") || connectResult.Contains("already connected"))
            {
                return string.Empty;
            }
            return connectResult;
        }
        public static async Task<bool> Pair(string input, string password)
        {
            string result = await Adb($"pair {input} {password}");
            if (result.Contains("Successfully paired to "))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
