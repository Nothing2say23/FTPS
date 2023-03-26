using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using FTP.MODL;
using System.Threading.Tasks;

namespace FTP.FUC
{
    public class FtpClient
    {
        private const int BUFFSIZE = 1024 * 2;
        public bool isSsl = false;

        public string ServerIP { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public string URI { get; set; }

        public Action CompleteDownload = null;

        public Action CompleteUpload = null;

        public Action<string> FailDownload = null;

        public Action<string> FailUpload = null;



        /// <summary>
        /// 设置FTP属性
        /// </summary>
        /// <param name="FtpServerIP">FTP连接地址</param>
        /// <param name="this.UserName">用户名</param>
        /// <param name="Password">密码</param>
        public FtpClient(string serverIP, string userName, string passwordName)
        {
            this.ServerIP = serverIP;
            this.UserName = userName;
            this.Password = passwordName;
            //this.URI = "ftp://" + this.ServerIP + "/";
            this.URI = "ftp://" + this.ServerIP ;
            this.isSsl = false;
        }
        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
        // 创建FTP连接
        /// <summary>
        /// 上传
        /// </summary>
        /// <param name="relativePath">服务器的相对路径</param>
        /// <param name="localPath">本地文件的绝对路径</param>
        public void Upload(string relativePath, string localPath)
        {
            try
            {
                Task task = Task.Factory.StartNew(() =>
                {
                    UploadFile(relativePath, localPath);
                });
                task.ContinueWith(t =>
                {
                    if (task.Exception == null)
                    {
                        if (this.CompleteUpload != null)
                            this.CompleteUpload();
                    }
                    else if (this.FailUpload != null)
                    {
                        this.FailUpload(task.Exception.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                if (this.FailUpload != null)
                    this.FailUpload(ex.Message);
            }
            finally
            {

            }
        }

        private void UploadFile(string relativePath, string localPath)
        {
            FtpWebRequest reqFTP = null;
            FileStream fs = null;
            //Stream strm = null;
            FileInfo fileInf = new FileInfo(localPath);
            if (fileInf.Exists == false)
                throw new Exception("上传之前未选中本地文件");
            try
            {
                string uri;
                if (relativePath.Length == 0){
                    uri = this.URI + "/" + Path.GetFileName(localPath);
                }
                else
                    uri = this.URI + relativePath + "/" + Path.GetFileName(localPath);
                reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(uri));
                reqFTP.EnableSsl = this.isSsl;
                reqFTP.Credentials = new NetworkCredential(this.UserName, Password);
                reqFTP.KeepAlive = true;
                reqFTP.Method = WebRequestMethods.Ftp.UploadFile;
                reqFTP.UseBinary = true;
                reqFTP.UsePassive = false;
                reqFTP.ContentLength = fileInf.Length;
                reqFTP.Timeout = 3000;
                //ServicePoint sp = reqFTP.ServicePoint;
                ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(ValidateServerCertificate);
                //ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors)
                byte[] buff = new byte[BUFFSIZE];
                int contentLen = 0;
                fs = fileInf.OpenRead();
                using (Stream strm=reqFTP.GetRequestStream()) {
                    if (strm == null)
                    {
                        Console.WriteLine("服务器未响应...");
                    }
                    Console.WriteLine("打开上传流，文件上传中...");
                    contentLen = fs.Read(buff, 0, BUFFSIZE);
                    if (this.isSsl)
                    {
                        using (SslStream sslStream = new SslStream(strm))
                        {
                            while (contentLen != 0)
                            {
                                sslStream.Write(buff, 0, contentLen);
                                contentLen = fs.Read(buff, 0, BUFFSIZE);
                            }
                            sslStream.Close();
                        }
                    }
                    else
                    {
                        while (contentLen != 0)
                        {
                            strm.Write(buff, 0, contentLen);
                            contentLen = fs.Read(buff, 0, BUFFSIZE);
                        }
                    }

                    Console.WriteLine("文件上传完成");
                    strm.Close();
                    fs.Close();
                }


                //if (this.CompleteUpload != null)
                //    this.CompleteUpload();

            }
            finally
            {
                if (reqFTP != null)
                    reqFTP.Abort();
                if (fs != null)
                    fs.Close();
                //if (strm != null)
                //    strm.Close();
            }
        }

        /// <summary>
        /// 下载
        /// </summary>
        /// <param name="filePath">本地保存路径</param>
        /// <param name="fileName">需要下载的FTP服务器路径上的文件名，同时也是本地保存的文件名</param>
        public void Download(string filePath, string serverPath, string fileName)
        {
            try
            {
                Task task = Task.Factory.StartNew(() =>
                    {
                        DownloadFile(filePath, serverPath, fileName);
                    });
                task.ContinueWith(t =>
                {
                    if (task.Exception == null)
                    {
                        if (this.CompleteDownload != null)
                            this.CompleteDownload();
                    }
                    else if (this.FailDownload != null)
                    {
                        List<string> ex = new List<string>();
                        foreach (var item in t.Exception.InnerExceptions)
                        {
                            ex.Add(item.Message);
                        }
                        this.FailDownload(string.Join("\n", ex));
                    }
                });
            }
            catch (Exception ex)
            {
                if (this.FailDownload != null)
                    this.FailDownload(ex.Message);
            }
            //finally
            //{

            //}
        }

        private void DownloadFile(string filePath, string serverPath, string fileName)
        {
            FtpWebRequest ftpRequest = null;
            FileStream outputStream = null;
            FtpWebResponse response = null;
            Stream ftpStream = null;

            try
            {
                outputStream = new FileStream(filePath + "\\" + fileName, FileMode.Create);
                string uri = this.URI + serverPath + "/" + fileName;
                ftpRequest = (FtpWebRequest)FtpWebRequest.Create(new Uri(uri));
                ftpRequest.EnableSsl = this.isSsl;
                ftpRequest.Method = WebRequestMethods.Ftp.DownloadFile;
                ftpRequest.UseBinary = true;
                ftpRequest.UsePassive = false;
                ftpRequest.Proxy = null;
                ftpRequest.KeepAlive = true;
                ftpRequest.Credentials = new NetworkCredential(this.UserName, this.Password);
                ftpRequest.Timeout = 30000;
                ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(ValidateServerCertificate);
                //X509Certificate _cert = X509Certificate.CreateFromCertFile(@"F:\C\C#_src\FTPDemo\SSLFTP\server.cer");
                //X509CertificateCollection certCollection = new X509CertificateCollection();
                //certCollection.Add(_cert);               
                response = (FtpWebResponse)ftpRequest.GetResponse();
                //Console.WriteLine("开始进行流传输");
                ftpStream = response.GetResponseStream();
                //if (ftpStream.CanSeek)
                //{
                //    Console.WriteLine("找不到ftpStream");
                //}

                int readCount = 0;
                byte[] buffer = new byte[BUFFSIZE];
                //readCount = sslStream.Read(buffer, 0, BUFFSIZE);
                readCount = ftpStream.Read(buffer, 0, BUFFSIZE);
                try
                {
                    while (readCount > 0)
                    {
                        outputStream.Write(buffer, 0, readCount);
                        //sslStream.Flush();
                        //readCount = sslStream.Read(buffer, 0, BUFFSIZE);
                        ftpStream.Flush();
                        readCount = ftpStream.Read(buffer, 0, BUFFSIZE);
                    }
                    Console.WriteLine("transfer finished");
                    // ftpRequest.s
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                if (ftpRequest != null)
                {
                    ftpRequest.Abort();
                }

                //ftpRequest = null;
                //if (sslStream != null)
                //    sslStream.Close();
                if (ftpStream != null)
                    ftpStream.Close();
                if (response != null)
                    response.Close();
                if (outputStream != null)
                    outputStream.Close();
            }
        }




        /// <summary>
        /// 获取当前目录下明细(包含文件和文件夹)，并按文件种类排序（文件在前，文件夹在后）
        /// </summary>
        /// <param name="path">相对FTP根目录的路径</param>
        /// <returns></returns>
        public List<FtpFileModel> GetFilesDetailList(string path = "")
        {
            Console.WriteLine(path);
            FtpWebRequest ftp = null;
            WebResponse response = null;
            StreamReader reader = null;
            List<FtpFileModel> fileList = new List<FtpFileModel>();
            try
            {
                StringBuilder result = new StringBuilder();
                ftp = (FtpWebRequest)FtpWebRequest.Create(new Uri(this.URI + path));
                //ftp.EnableSsl = true;   // 使能ssl
                ftp.Credentials = new NetworkCredential(this.UserName, Password);
                ftp.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
                ftp.UsePassive = false;
                response = ftp.GetResponse();
                reader = new StreamReader(response.GetResponseStream(), Encoding.Default);

                string line = string.Empty;// 读取的首行内容为"total 0"，剔除
                while ((line = reader.ReadLine()) != null)
                {
                    try
                    {
                        fileList.Add(new FtpFileModel(line, path));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Trim() != "远程服务器返回错误: (550) 文件不可用(例如，未找到文件，无法访问文件)。")
                {
                    throw new Exception("FtpHelper GetFileList Error --> " + ex.Message.ToString());
                }
            }
            finally
            {
                if (reader != null)
                    reader.Close();
                if (response != null)
                    response.Close();
                if (ftp != null)
                    ftp.Abort();
            }

            return fileList.OrderBy(f => f.FileType).ToList();
        }

        /// <summary>
        /// 获取当前目录下文件列表(仅文件)
        /// </summary>
        /// <returns></returns>
        public string[] GetFileList(string mask)
        {
            string[] downloadFiles;
            StringBuilder result = new StringBuilder();
            FtpWebRequest reqFTP;
            try
            {
                reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(this.URI));
                //reqFTP.EnableSsl = true;
                reqFTP.UseBinary = true;
                reqFTP.Credentials = new NetworkCredential(this.UserName, Password);
                reqFTP.Method = WebRequestMethods.Ftp.ListDirectory;
                reqFTP.UsePassive = false;
                WebResponse response = reqFTP.GetResponse();
                StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.Default);

                string line = reader.ReadLine();
                while (line != null)
                {
                    if (mask.Trim() != string.Empty && mask.Trim() != "*.*")
                    {
                        string mask_ = mask.Substring(0, mask.IndexOf("*"));
                        if (line.Substring(0, mask_.Length) == mask_)
                        {
                            result.Append(line);
                            result.Append("\n");
                        }
                    }
                    else
                    {
                        result.Append(line);
                        result.Append("\n");
                    }
                    line = reader.ReadLine();
                }
                result.Remove(result.ToString().LastIndexOf('\n'), 1);
                reader.Close();
                response.Close();
                return result.ToString().Split('\n');
            }
            catch (Exception ex)
            {
                downloadFiles = null;
                if (ex.Message.Trim() != "远程服务器返回错误: (550) 文件不可用(例如，未找到文件，无法访问文件)。")
                {
                    throw new Exception("FtpHelper GetFileList Error --> " + ex.Message.ToString());
                }
                return downloadFiles;
            }
        }
 
    }
}
