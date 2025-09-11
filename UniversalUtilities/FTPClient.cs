using FluentFTP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UniversalUtilities
{
    public class FTPClient : IDisposable
    {
        private FtpClient ftpClient = new();
        private readonly string ipaddress;
        bool connect4 = false;
        bool connect6 = false;
        public FTPClient(string ip)
        {
            ipaddress = ip;
        }
        public async Task<bool> ConnectToServer4()
        {
            ftpClient = new(ipaddress, "4", "4");
            ftpClient.SocketKeepAlive = true;
            await ftpClient.ConnectAsync();
            connect4 = ftpClient.IsConnected;
            return connect4;
        }
        public async Task<bool> ConnectToServer6()
        {
            ftpClient = new(ipaddress, "6", "6");
            ftpClient.SocketKeepAlive = true;
            await ftpClient.ConnectAsync();
            connect6 = ftpClient.IsConnected;
            return connect6;
        }
        public async Task<List<string>> GetFileList()
        {
            
            var files = await ftpClient.GetListingAsync("/", FtpListOption.NoPath);
            List<string> result = new();
            foreach (var f in files)
            {
                result.Add(f.Name);
            }
            return result;
        }
        public async Task SaveFTPFile(string file)
        {
            string str = await ReadFTPFile(file);
            using (FileStream fs = new(file, FileMode.Create))
            {
                StreamWriter sw = new(fs);
                sw.Write(str);
                sw.Flush();
                sw.Close();
                sw.Dispose();
                fs.Dispose();
            }
        }
        public async Task<string> ReadFTPFile(string file)
        {
            MemoryStream stream = new();
            bool b = await ftpClient.DownloadAsync(stream, file);
            if (b)
            {
                string str = Encoding.UTF8.GetString(stream.ToArray());
                return str;
            }
            else
            {
                return string.Empty;
            }

        }
        public async Task<FtpStatus> UploadFileToFTP(string file)
        {
            FileInfo f = new(file);
            var status = await ftpClient.UploadFileAsync(file, f.Name);
            return status;
        }
        public void Dispose()
        {
            ftpClient.Dispose();
        }
    }
}
