using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UniversalUtilities
{
    public static class Utilities
    {
        static readonly Random random = new();
        /// <summary>
        /// 求若干INT数值的最大公约数
        /// </summary>
        /// <param name="numbers"></param>
        /// <returns></returns>
        public static int MaxGYS(List<int> numbers)
        {
            int minNumber = numbers.Min();
            int gys = 1;
            for (int i = 1; i <= minNumber; i++)
            {
                for (int j = 0; j < numbers.Count; j++)
                {
                    if (numbers[j] % i != 0)
                    {
                        break;
                    }
                    else
                    {
                        if (numbers.Count == j + 1)
                        {
                            gys = i;
                        }
                    }
                }
            }
            return gys;
        }
        /// <summary>   
        /// CopyMemoryEx   
        /// </summary>   
        /// <param name="dest">目标缓存</param>   
        /// <param name="DestStart">目标缓存中开始复制的位置</param>   
        /// <param name="source">源数据</param>   
        /// <param name="SourceStart">源数据缓存中开始位置</param>   
        /// <param name="size">要从源数据中复制的长度</param>   
        /// <returns></returns>   
        public unsafe static long CopyMemoryEx(byte[] dest, int DestStart, byte[] source, int SourceStart, int size)
        {
            IntPtr dp;
            IntPtr sp;
            fixed (byte* ds = &dest[DestStart])
            {
                fixed (byte* sr = &source[SourceStart])
                {
                    dp = (IntPtr)ds;
                    sp = (IntPtr)sr;
                    return NativeMethods.CopyMemory(dp, sp, size);
                }
            }
        }
        public unsafe static long CopyMemoryEx(IntPtr dest, int DestStart, byte[] source, int SourceStart, int size)
        {
            IntPtr dp;
            IntPtr sp;
            dp = dest + DestStart;
            fixed (byte* sr = &source[SourceStart])
            {
                sp = (IntPtr)sr;
                return NativeMethods.CopyMemory(dp, sp, size);
            }
        }
        public static bool Ping(string ip)
        {
            try
            {
                using Ping p = new();
                PingOptions options = new()
                {
                    DontFragment = true
                };
                byte[] buffer = Encoding.ASCII.GetBytes(ip);
                int timeOut = 500;
                PingReply reply = p.Send(ip, timeOut, buffer, options);

                if (reply.Status == IPStatus.Success)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
        public static double DateTimeToVariantData(DateTime date)
        {
            DateTime oleBaseDate = new(year: 1899, month: 12, day: 30);
            double tick = (date - oleBaseDate).Ticks;
            tick /= (double)TimeSpan.TicksPerDay;
            return tick;
        }
        public static DateTime VariantDateToDateTime(double value)
        {
            DateTime oleBaseDate = new(year: 1899, month: 12, day: 30);
            try
            {
                long dayOffsetInTicks = (long)value * TimeSpan.TicksPerDay;
                long fractionalDayTicks = Math.Abs((long)((value - (long)value) * TimeSpan.TicksPerDay));
                return new DateTime(oleBaseDate.Ticks + dayOffsetInTicks + fractionalDayTicks);
            }
            catch
            {
                Log.WriteLog("", value.ToString(), "Utilities.VariantDateToDateTime");
                return DateTime.MaxValue;
            }
        }
        public static DateTime VariantDateToDateTime2(long value)
        {
            DateTime oleBaseDate = new(year: 2000, month: 1, day: 1);
            try
            {
                DateTime result = oleBaseDate.AddMilliseconds(value / 1000000);
                return result;
            }
            catch
            {
                Log.WriteLog("", value.ToString(), "Utilities.VariantDateToDateTime");
                return DateTime.MaxValue;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="dateBytes">原始BYTE数组</param>
        /// <param name="dateTime">日期形式的转换结果</param>
        /// <param name="dateDouble">数值形式的转换结果</param>
        /// <param name="big">是否BigEndian</param>
        /// <param name="stamp">DataType</param>
        /// <returns></returns>
        public static bool ConvertBytesToDateTime2(Span<byte> dateBytes, out DateTime dateTime, out double dateDouble, bool big, string stamp)
        {
            if (dateBytes == null)
            {
                dateTime = default;
                dateDouble = default;
                return false;
            }
            dateTime = default;
            dateDouble = default;
            if (big)
            { dateBytes.Reverse(); }
            if (stamp == "DOUBLE")
            {
                dateDouble = BitConverter.ToDouble(dateBytes.ToArray(), 0);
                if (dateDouble < 10000 || dateDouble > 100000)
                {
                    dateDouble = default;
                    return false;
                }
                else
                {
                    dateTime = VariantDateToDateTime(dateDouble);
                    if (dateTime.Year != DateTime.Now.Year)
                    {
                        return false;
                    }
                    return true;
                }
            }
            else
            {
                long dd = BitConverter.ToInt64(dateBytes.ToArray(), 0);
                dateTime = VariantDateToDateTime2(dd);
                return true;
            }
        }
        public static void AppendData(FileInfo fileName, Span<float> datas)
        {
            Span<byte> bytes = new byte[datas.Length * 4];
            int i = 0;
            foreach (float f in datas)
            {
                Span<byte> tmp = BitConverter.GetBytes(f);
                bytes[i] = tmp[0];
                bytes[i + 1] = tmp[1];
                bytes[i + 2] = tmp[2];
                bytes[i + 3] = tmp[3];
                i += 4;
            }
            AppendData(fileName, bytes);
        }
        public static void AppendData(FileInfo fileName, Span<byte> data)
        {
            using FileStream fs = new(fileName.FullName, FileMode.Append, FileAccess.Write);
            BinaryWriter bw = new(fs);
            bw.Write(data.ToArray());
            bw.Flush();
            bw.Close();
            bw.Dispose();
            fs.Close();
        }
        public static void AppendData(FileInfo fileName, byte[] data)
        {
            using FileStream fs = new(fileName.FullName, FileMode.Append, FileAccess.Write);
            BinaryWriter bw = new(fs);
            bw.Write(data);
            bw.Flush();
            bw.Close();
            bw.Dispose();
        }
        /// <summary>
        /// 返回文件的字节
        /// </summary>
        /// <param name="file">文件名称</param>
        /// <returns></returns>
        public static ReadOnlySpan<byte> GetFileData(FileInfo file)
        {
            ReadOnlySpan<byte> result = new byte[file.Length];
            using (FileStream fs = new(file.FullName, FileMode.Open, FileAccess.Read))
            {
                BinaryReader br = new(fs);
                result = br.ReadBytes(result.Length);
                br.Close();
                br.Dispose();
                fs.Close();
            }
            return result;
        }
        /// <summary>
        /// 将比特数组保存到目标文件
        /// </summary>
        /// <param name="fileName">要保存的文件名称</param>
        /// <param name="b">要保存的比特数组</param>
        public static void SaveDataToFile(string fileName, ReadOnlySpan<byte> b)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite))
            {
                BinaryWriter bw = new BinaryWriter(fs);
                bw.Write(b.ToArray());
                bw.Flush();
                bw.Close();
                bw.Dispose();
                fs.Close();
            }
        }
        /// <summary>
        /// 向指定文件末尾追加写入数据
        /// </summary>
        /// <param name="fileName">指定的文件地址</param>
        /// <param name="data">要追加写入的数据</param>
        public static void AppendData(string fileName, ReadOnlySpan<byte> data)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Append, FileAccess.Write))
            {
                BinaryWriter bw = new BinaryWriter(fs);
                bw.Write(data.ToArray());
                bw.Flush();
                bw.Close();
                bw.Dispose();
            }
        }
        /// <summary>
        /// 将指定的时间转换为整型数字
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static int ConvertDataTimeToInt(DateTime dt)
        {

            DateTime dtStart = TimeZoneInfo.ConvertTime(new DateTime(1970, 1, 1), TimeZoneInfo.Local); ;
            return (int)(dt - dtStart).TotalSeconds;
        }
        /// <summary>
        /// 将指定的整形数字转换为时间
        /// </summary>
        /// <param name="d"></param>
        /// <returns></returns>
        public static DateTime ConvertIntToDateTime(int d)
        {
            DateTime dateTimeStart = TimeZoneInfo.ConvertTime(new DateTime(1970, 1, 1), TimeZoneInfo.Local);
            long lTime = long.Parse(d.ToString() + "0000000");
            TimeSpan toNow = new TimeSpan(lTime);
            return dateTimeStart.Add(toNow);
        }
        /// <summary>
        /// 获取本机IP地址
        /// </summary>
        /// <returns></returns>
        public static async Task<string> GetIP()
        {
            string result = string.Empty;

            var urlList = new List<string>
            {
                "http://pv.sohu.com/cityjson?ie=utf-8",
                "http://ip.taobao.com/service/getIpInfo2.php?ip=myip"
            };
            foreach (var a in urlList)
            {
                try
                {
                    var req = WebRequest.Create(a);
                    req.Timeout = 20000;
                    var response = await req.GetResponseAsync();
                    var resStream = response.GetResponseStream();
                    if (resStream != null)
                    {
                        var sr = new StreamReader(resStream, Encoding.UTF8);
                        var htmlinfo = sr.ReadToEnd();
                        //匹配IP的正则表达式
                        var r = new Regex("((25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]\\d|\\d)\\.){3}(25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]\\d|[1-9])", RegexOptions.None);
                        var mc = r.Match(htmlinfo);
                        //获取匹配到的IP
                        result = mc.Groups[0].Value;
                        resStream.Close();
                        sr.Close();
                        response.Dispose();
                        return result;
                    }
                }
                catch
                {
                    return result;
                }
            }

            return result;
        }
        public static string GetTimeString(int ms, bool isMS = true)
        {
            DateTime s = new DateTime(1970, 1, 1);
            string sumTime = "";
            DateTime s1;
            if (isMS)
            {
                s1 = s.AddMilliseconds(ms);
            }
            else
            {
                s1 = s.AddSeconds(ms);
            }
            if ((s1 - s).Seconds < 60)
            {
                sumTime = (s1 - s).Seconds.ToString() + "秒";
            }
            if ((s1 - s).Minutes > 0 & (s1 - s).Minutes < 60)
            {
                sumTime = (s1 - s).Minutes.ToString() + "分" + (s1 - s).Seconds.ToString() + "秒";
            }
            if ((s1 - s).Hours > 0 & (s1 - s).Hours < 24)
            {
                sumTime = (s1 - s).Hours.ToString() + "小时" + (s1 - s).Minutes.ToString() + "分" + (s1 - s).Seconds.ToString() + "秒";
            }
            if ((s1 - s).Days > 0)
            {
                sumTime = (s1 - s).Days.ToString() + "天" + (s1 - s).Hours.ToString() + "小时" + (s1 - s).Minutes.ToString() + "分" + (s1 - s).Seconds.ToString() + "秒";
            }
            return sumTime;
        }
        public static void SetGDIHigh(this Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;  //使绘图质量最高，即消除锯齿
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }
        /// <summary>
        /// Creates the rounded rectangle path.
        /// </summary>
        /// <param name="rect">The rect.</param>
        /// <param name="cornerRadius">The corner radius.</param>
        /// <returns>GraphicsPath.</returns>
        public static GraphicsPath CreateRoundedRectanglePath(this RectangleF rect, int cornerRadius)
        {
            GraphicsPath roundedRect = new GraphicsPath();
            roundedRect.AddArc(rect.X, rect.Y, cornerRadius * 2, cornerRadius * 2, 180, 90);
            roundedRect.AddLine(rect.X + cornerRadius, rect.Y, rect.Right - cornerRadius * 2, rect.Y);
            roundedRect.AddArc(rect.X + rect.Width - cornerRadius * 2, rect.Y, cornerRadius * 2, cornerRadius * 2, 270, 90);
            roundedRect.AddLine(rect.Right, rect.Y + cornerRadius * 2, rect.Right, rect.Y + rect.Height - cornerRadius * 2);
            roundedRect.AddArc(rect.X + rect.Width - cornerRadius * 2, rect.Y + rect.Height - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 0, 90);
            roundedRect.AddLine(rect.Right - cornerRadius * 2, rect.Bottom, rect.X + cornerRadius * 2, rect.Bottom);
            roundedRect.AddArc(rect.X, rect.Bottom - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 90, 90);
            roundedRect.AddLine(rect.X, rect.Bottom - cornerRadius * 2, rect.X, rect.Y + cornerRadius * 2);
            roundedRect.CloseFigure();
            return roundedRect;
        }
        public static uint ParseRGB(Color color)
        {
            return ((uint)color.B << 16) | (ushort)((color.G << 8) | color.R);
        }
        public static long IPAddressToLong(string ip)
        {
            if (string.IsNullOrEmpty(ip))
            {
                return long.MinValue;
            }
            char[] separator = new char[] { '.' };
            string[] items = ip.Split(separator);
            return long.Parse(items[0]) << 24
                    | long.Parse(items[1]) << 16
                    | long.Parse(items[2]) << 8
                    | long.Parse(items[3]);
        }
        public static string LongToIPAddress(long ipInt)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append((ipInt >> 24) & 0xFF).Append(".");
            sb.Append((ipInt >> 16) & 0xFF).Append(".");
            sb.Append((ipInt >> 8) & 0xFF).Append(".");
            sb.Append(ipInt & 0xFF);
            return sb.ToString();
        }
        public static string ReadTextFromFile(string file, int line)
        {
            using FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read);
            StreamReader sr = new StreamReader(fs);
            int i = 0;
            if (!sr.EndOfStream)
            {
                i += 1;
                string str = sr.ReadLine();
                if (i == line)
                {
                    return str;
                }
            }
            else
            {
                return string.Empty;
            }
            return string.Empty;
        }
        /// <summary>
        /// 将时间转换成unix时间戳
        /// </summary>
        /// <param name="datetime"></param>
        /// <returns></returns>
        public static long ToUnixTimestamp(this DateTime datetime)
        {
            return (datetime.ToUniversalTime().Ticks - 621355968000000000) / 10000;
        }
        public static DateTime ToDateTime(this long timeStamp)
        {
            DateTime startTime = new DateTime(1970, 1, 1);
            return startTime.AddTicks(timeStamp * 10000);
        }
        public static bool ValidName(string str, out string invalidString)
        {
            invalidString = string.Empty;
            char[] invalidchars = new char[] { ':', '+', '-', '*', '/', '=', '<', '>', '|', '\\', '"', '\'', '[', ']', '@', '^', '.' };
            if (str.IndexOfAny(invalidchars) >= 0)
            {
                invalidString = "输入的项目中有非法字符，如下字符为非法字符： \r\n" + "                       : + - * / = < > | \\ \" ' [ ] @ ^ . ";
                return false;
            }
            return true;
        }
        public static ReadOnlySpan<byte> FloatsToBytes(ReadOnlySpan<float> floats)
        {

            if (floats == null)
                return null;
            Span<byte> result = new byte[floats.Length * 4];
            int pos = 0;
            foreach (float f in floats)
            {
                Span<byte> b = BitConverter.GetBytes(f);
                //Array.Copy(b, 0, result, pos, 4);
                for (int i = 0; i < 4; i++)
                {
                    result[i + pos] = b[i];
                }
                pos += 4;
            }
            return result;
        }
        public static ReadOnlySpan<float> BytesToFloats(byte[] bytes)
        {
            float[] result = new float[bytes.Length / 4];
            for (int i = 0; i < bytes.Length; i += 4)
            {
                result[i / 4] = BitConverter.ToSingle(bytes, i);
            }
            return result;
        }
        public static ReadOnlySpan<byte> DoublesToBytes(ReadOnlySpan<double> doubles)
        {

            if (doubles == null)
                return null;
            Span<byte> result = new byte[doubles.Length * 4];
            int pos = 0;
            foreach (double f in doubles)
            {
                Span<byte> b = BitConverter.GetBytes((float)f);
                for (int i = 0; i < 4; i++)
                {
                    result[i + pos] = b[i];
                }
                pos += 8;
            }
            return result;
        }
        public static decimal ChangeStringDataToDecimal(string strData)
        {
            decimal dData = 0.0M;
            try
            {
                if (strData.Contains("E"))
                {
                    dData = Convert.ToDecimal(decimal.Parse(strData.ToString(), System.Globalization.NumberStyles.Float));
                }
                else
                {
                    dData = decimal.Parse(strData);
                }
            }
            catch
            {
                dData = 0;
            }
            return dData;
        }
        public static byte[] GetBytesFromInt(int i)
        {
            string vd = string.Format("{0:X4}", i);
            char[] chs = vd.ToCharArray();
            byte[] data = Encoding.ASCII.GetBytes(chs);
            return data;
        }
        public static byte[] GetBytesFromInt2(int i)
        {
            string vd = string.Format("{0:X2}", i);
            char[] chs = vd.ToCharArray();
            byte[] data = Encoding.ASCII.GetBytes(chs);
            return data;
        }
        public static float GetFloatFromBytes(byte[] bs)
        {
            try
            {
                return GetFloatFromChar(Encoding.ASCII.GetString(bs).ToArray());
            }
            catch
            {
                return float.MinValue;
            }

        }
        public static float GetFloatFromChar(char[] chars)
        {
            float result = default;
            int length = chars.Length;
            for (int i = 0; i < length; i++)
            {
                int r = GetINTFromChar(chars[i]);
                result += (float)(Math.Pow(16, length - i - 1) * r);
            }
            return result;
        }
        public static int GetINTFromChar(char c)
        {
            int result = int.Parse(c.ToString(), System.Globalization.NumberStyles.HexNumber);
            return result;
        }
        ///<summary>
        ///用最小二乘法拟合多次曲线
        ///</summary>
        ///<param name="arrX">已知点的x坐标集合</param>
        ///<param name="arrY">已知点的y坐标集合</param>
        ///<param name="length">已知点的个数</param>
        ///<param name="dimension">方程的最高次数</param>
        public static double[] MultiLine(double[] arrX, double[] arrY, int length, int dimension)
        {
            int n = dimension + 1;
            double[,] Guass = new double[n, n + 1];      //高斯矩阵 例如：y=a0+a1*x+a2*x*x
            for (int i = 0; i < n; i++)
            {
                int j;
                for (j = 0; j < n; j++)
                {
                    Guass[i, j] = SumArr(arrX, j + i, length);
                }
                Guass[i, j] = SumArr(arrX, i, arrY, 1, length);
            }

            return ComputGauss(Guass, n);

        }
        private static double SumArr(double[] arr, int n, int length)
        {
            double s = 0;
            for (int i = 0; i < length; i++)
            {
                if (arr[i] != 0 || n != 0)
                    s += Math.Pow(arr[i], n);
                else
                    s++;
            }
            return s;
        }
        private static double SumArr(double[] arr1, int n1, double[] arr2, int n2, int length)
        {
            double s = 0;
            for (int i = 0; i < length; i++)
            {
                if ((arr1[i] != 0 || n1 != 0) && (arr2[i] != 0 || n2 != 0))
                    s += Math.Pow(arr1[i], n1) * Math.Pow(arr2[i], n2);
                else
                    s++;
            }
            return s;

        }
        private static double[] ComputGauss(double[,] Guass, int n)
        {
            int i, j;
            int k, m;
            double temp;
            double max;
            double s;
            double[] x = new double[n];

            for (i = 0; i < n; i++) x[i] = 0.0;//初始化


            for (j = 0; j < n; j++)
            {
                max = 0;

                k = j;
                for (i = j; i < n; i++)
                {
                    if (Math.Abs(Guass[i, j]) > max)
                    {
                        max = Guass[i, j];
                        k = i;
                    }
                }

                if (k != j)
                {
                    for (m = j; m < n + 1; m++)
                    {
                        temp = Guass[j, m];
                        Guass[j, m] = Guass[k, m];
                        Guass[k, m] = temp;

                    }
                }

                if (0 == max)
                {
                    // "此线性方程为奇异线性方程" 

                    return x;
                }


                for (i = j + 1; i < n; i++)
                {
                    s = Guass[i, j];
                    for (m = j; m < n + 1; m++)
                    {
                        Guass[i, m] = Guass[i, m] - Guass[j, m] * s / (Guass[j, j]);
                    }
                }
            }


            for (i = n - 1; i >= 0; i--)
            {
                s = 0;
                for (j = i + 1; j < n; j++)
                {
                    s += Guass[i, j] * x[j];
                }
                x[i] = (Guass[i, n] - s) / Guass[i, i];
            }

            return x;
        }
        public static string GetHEXString(ReadOnlySpan<byte> bs)
        {
            string result = string.Empty;
            foreach (byte b in bs)
            {
                result += string.Format("{0:x2}", b) + " ";
            }
            result = result.Substring(0, result.Length - 1);
            return result;
        }
        public static string GetHEXStringWithoutEmpty(ReadOnlySpan<byte> bs)
        {
            string result = string.Empty;
            foreach (byte b in bs)
            {
                result += string.Format("{0:x2}", b);
            }
            //result = result.Substring(0, result.Length - 1);
            return result;
        }
        public static byte[] HexStringToBytes(string hexString)
        {
            hexString = hexString.Replace(" ", "");
            if ((hexString.Length % 2) != 0)
            {
                hexString += " ";
            }
            byte[] returnBytes = new byte[hexString.Length / 2];
            for (int i = 0; i < returnBytes.Length; i++)
            {
                returnBytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2).Trim(), 16);
            }
            return returnBytes;
        }
        public static double[] ConvertDoubleArray(Array arr)
        {
            if (arr.Rank != 1) throw new ArgumentException();
            var retval = new double[arr.GetLength(0)];
            for (int ix = arr.GetLowerBound(0); ix <= arr.GetUpperBound(0); ++ix)
                retval[ix - arr.GetLowerBound(0)] = (double)arr.GetValue(ix);
            return retval;
        }
        public static double ConvertToDoubleFrom8421Bytes(byte[] src, int digits)
        {
            long sum = 0;

            int pow = 0;
            foreach (byte v in src)
            {
                sum += v * (long)Math.Pow(256, src.Length - pow - 1);
                pow += 1;
            }
            double result = 0;
            pow = 0;
            while (sum > 0)
            {
                long cur = sum % 16;
                sum -= cur;
                sum /= 16;
                result += cur * Math.Pow(10, pow);
                pow += 1;
            }
            return result / Math.Pow(10, digits);
        }
        public static List<byte> ConvertTo8421BytesFromDouble(double src, int byteCount, int digits)
        {
            int part1 = (int)src;
            int part2 = Convert.ToInt32(Math.Round((src - part1) * Math.Pow(10, digits)));

            int resultInt = 0;
            int i = 0;
            for (; i < digits; i++)
            {
                int cur = part2 % 10;
                part2 /= 10;
                resultInt += cur * (int)Math.Pow(16, i);
            }
            while (part1 > 0)
            {
                int cur = part1 % 10;
                part1 /= 10;
                resultInt += cur * (int)Math.Pow(16, i);
                i += 1;
            }
            resultInt &= ((int)Math.Pow(2, byteCount * 8) - 1);
            List<byte> result = BitConverter.GetBytes(resultInt).ToList();
            while (result.Count > byteCount)
            {
                result.RemoveAt(result.Count - 1);
            }
            result.Reverse();
            return result;
        }
        /// <summary>
        /// 将布尔值序列转换未一个BYTE
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        public static byte Convert2ByteFromBoolList(List<bool> list)
        {
            int result = 0x00;
            for (int i = 0; i < list.Count; i++)
            {
                result += list[i] ? (int)Math.Pow(2, i) : 0;
            }
            return Convert.ToByte(result);
        }
        /// <summary>
        /// 将一个BYTE转换为布尔值序列
        /// </summary>
        /// <param name="src"></param>
        /// <returns></returns>
        public static List<bool> Convert2BoolListFromByte(byte src)
        {
            List<bool> result = new();
            for (int i = 0; i < 8; i++)
            {
                result.Add((src & (int)Math.Pow(2, i)) != 0);
            }
            return result;
        }
        public static string GetEnumDisplayName(Enum value)
        {
            Type enumType = value.GetType();
            string name = Enum.GetName(enumType, value);
            MemberInfo member = enumType.GetMember(name)[0];
            DisplayAttribute attribute = member.GetCustomAttribute<DisplayAttribute>();
            return attribute?.GetName() ?? name;
        }
        public static List<string> GetEnumDisplayNames(Type enumType)
        {
            List<string> displayNames = new List<string>();
            foreach (Enum value in Enum.GetValues(enumType))
            {
                var displayName = GetEnumDisplayName(value);
                displayNames.Add(displayName);
            }
            return displayNames;
        }
        public static T GetEnumByDisplayName<T>(string displayName) where T : Enum
        {
            foreach (T t in Enum.GetValues(typeof(T)))
            {
                if (GetEnumDisplayName(t) == displayName)
                {
                    return t;
                }
            }
            return default;
        }
        /// <summary>
        /// 计算样本数据点R的平方
        /// </summary>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static double CalculateRSquare(double[] x, double[] y)
        {
            if (x.Length != y.Length)
                throw new Exception("Input arrays must have same length");
            if (x.Length < 2)
                throw new Exception("Input arrays must have at least two elements");

            double xMean = 0.0;
            double yMean = 0.0;
            for (int i = 0; i < x.Length; ++i)
            {
                xMean += x[i];
                yMean += y[i];
            }
            xMean /= x.Length;
            yMean /= y.Length;

            double numerator = 0.0;
            double denominatorX = 0.0;
            double denominatorY = 0.0;
            for (int i = 0; i < x.Length; ++i)
            {
                numerator += (x[i] - xMean) * (y[i] - yMean);
                denominatorX += (x[i] - xMean) * (x[i] - xMean);
                denominatorY += (y[i] - yMean) * (y[i] - yMean);
            }
            double r2 = numerator * numerator / (denominatorX * denominatorY);
            return r2;
        }
        public static byte[] GetByteFromDoubleList(ReadOnlySpan<double> data)
        {
            float[] floatData = new float[data.Length];
            for (int i = 0; i < data.Length; i += 1)
            {
                floatData[i] = (float)data[i];
            }
            int size = floatData.Length * sizeof(float);
            byte[] bytes = new byte[size];
            Buffer.BlockCopy(floatData, 0, bytes, 0, size);
            return bytes;
        }
        public static double[] GetDoublesFromBytes(ReadOnlySpan<byte> bytes)
        {
            double[] result = new double[bytes.Length / 4];
            float[] floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes.ToArray(), 0, floats, 0, bytes.Length);
            for (int i = 0; i < floats.Length; i += 1)
            {
                result[i] = floats[i];
            }
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ExtractFile(string fileName, string folder)
        {
            try
            {
                ZipFile.ExtractToDirectory(fileName, folder);
            }
            catch (IOException e)
            {
                throw new Exception(e.Message, e);
            }
        }
        public static void CompressFolders(ReadOnlySpan<string> folders, string zipFile)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempFolder);
            foreach (string folder in folders)
            {
                string relativePath = Path.GetFileName(folder);
                CopyFolder(folder, Path.Combine(tempFolder, relativePath));
            }
            ZipFile.CreateFromDirectory(tempFolder, zipFile);
            Directory.Delete(tempFolder, true);
        }
        private static void CopyFolder(string sourceFolder, string destFolder)
        {
            Directory.CreateDirectory(destFolder);
            string[] files = Directory.GetFiles(sourceFolder);
            foreach (string file in files)
            {
                string destFile = Path.Combine(destFolder, Path.GetFileName(file));
                File.Copy(file, destFile);
            }
            string[] subFolders = Directory.GetDirectories(sourceFolder);
            foreach (string subFolder in subFolders)
            {
                string destSubFolder = Path.Combine(destFolder, Path.GetFileName(subFolder));
                CopyFolder(subFolder, destSubFolder);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetRandomData(double max, double min)
        {
            double randomValue = random.NextDouble() * (max - min) + min;
            return randomValue;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetRandomSINData(double range, double period, double t)
        {
            return range * Math.Sin(2 * Math.PI * t / period) + GetRandomData(-0.5, 0.5);
        }
        public static int Cal_CRC_Code(byte[] ucpdata, int len)
        {
            int crc = 0xffff;
            int n;
            int len2 = 0;
            while (len-- > 0)
            {
                crc = ucpdata[len2] ^ crc;
                for (n = 0; n < 8; n++)
                {
                    int TT;
                    TT = crc & 1;
                    crc >>= 1;
                    crc &= 0x7fff;
                    if (TT == 1)
                    {
                        crc ^= 0xa001;
                        crc &= 0xffff;
                    }
                }
                len2++;
            }
            return crc;
        }
        /// <summary>
        /// crc-16/CCITT-FALSE
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public static byte[] CRC16(byte[] buffer) 
        {
            ushort crc = 0xFFFF;
            int size = buffer.Length;
            int i = 0;
            if (size > 0)
            {
                while (size-- > 0)
                {
                    crc = (ushort)((crc >> 8) | (crc << 8));
                    crc ^= buffer[i++];
                    crc ^= (ushort)(((byte)crc) >> 4);
                    crc ^= (ushort)(crc << 12);
                    crc ^= (ushort)((crc & 0xff) << 5);
                }
            }
            var ResCRC16 = new byte[2];
            ResCRC16[1] = (byte)(crc & 0xff);
            ResCRC16[0] = (byte)((crc >> 8) & 0xff);
            return ResCRC16;
        }
    }
}
