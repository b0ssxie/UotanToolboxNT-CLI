using System;
using System.IO;


namespace UotanToolbox.Common
{
    //修补FRP文件的代码，工具箱暂未启用此功能！-zicai
    public class FrpPatcher(string filePath, string function)
    {
        private readonly string _filePath = filePath;
        private readonly string _function = function.ToLower();

        public bool Run()
        {
            byte target = 0x02;
            if (_function == "oemunlockon")
            {
                target = 0x01;
            }
            if (_function == "oemunlockoff")
            {
                target = 0x00;
            }
            if (_function is not "oemunlockon" and not "oemunlockoff")
            {
                throw new ArgumentException("参数错误");
            }
            if (!File.Exists(_filePath))
            {
                throw new FileNotFoundException($"找不到 {_filePath}");
            }
            byte[] fileBytes = File.ReadAllBytes(_filePath);
            byte lastByte = fileBytes[^1];
            if (lastByte is not 0x00 and not 0x01)
            {
                throw new InvalidOperationException("frp文件末尾1字节16进制数值不是00或01");
            }
            if (lastByte == target)
            {
                return true;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(_filePath);
                bytes[^1] = target;
                File.WriteAllBytes(_filePath, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
