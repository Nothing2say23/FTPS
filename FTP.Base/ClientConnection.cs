using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Globalization;

namespace FTP.FUC
{
    internal class ClientConnection
    {
        private enum DataConnectionType
        {
            Active,
            Passive
        }

        private TcpClient _controlClient = null;
        private NetworkStream _controlStream = null;
        private StreamReader _controlReader = null;
        private StreamWriter _controlWriter = null;


        private TcpClient _dataClient;
        private NetworkStream _dataStream = null;
        private StreamReader _dataReader = null;
        private StreamWriter _dataWriter = null;

        private string _username = null;
        private string _transferType = null;

        private DataConnectionType _dataConnectionType;
        private IPEndPoint _dataEndpoint = null;
        private TcpListener _passiveListener = null;

        //private X509Certificate _cert = null;
        private SslStream _sslStream;
        //private SslStream _datasslStream = null;
        private X509Certificate2 ServerCert;
        public bool isSslEnable = false;


        private bool _isLogin = false;
        private string _rootDirectory = string.Empty;
        private string _currentDirectory = string.Empty;

        public Func<string, string, bool> CheckUser = null;

        public ClientConnection(TcpClient client, string root, X509Certificate2 ServerCertificate)
        {
            this._controlClient = client;
            _controlStream = this._controlClient.GetStream();
            _controlReader = new StreamReader(_controlStream);
            _controlWriter = new StreamWriter(_controlStream);
            this._rootDirectory = root;
            this._currentDirectory = root;
            this.ServerCert = ServerCertificate;
        }
            public void HandleClient(object obj)
        {
            _controlWriter.WriteLine("220 Service Ready.");
            _controlWriter.Flush();

            string line;

            try
            {
                if (isSslEnable)
                {
                    _controlReader = new StreamReader(_sslStream);
                    _controlWriter = new StreamWriter(_sslStream);
                }
                while (!string.IsNullOrEmpty(line = _controlReader.ReadLine()))
                {
                    Console.WriteLine(line);
                    string response = null;

                    string[] command = line.Split(' ');

                    string cmd = command[0].ToUpperInvariant();
                    string arguments = command.Length > 1 ? line.Substring(command[0].Length + 1) : null;

                    if (string.IsNullOrWhiteSpace(arguments))
                        arguments = null;

                    if (cmd != "USER" && cmd != "PASS" && _isLogin == false)
                        return;

                    if (response == null)
                    {
                        switch (cmd)
                        {
                            case "USER":
                                response = User(arguments);
                                break;
                            case "PASS":
                                response = Password(arguments);

                                break;
                            case "CWD":
                                response = ChangeWorkingDirectory(arguments);
                                break;
                            case "CDUP":
                                response = ChangeWorkingDirectory("..");
                                break;
                            case "PORT":
                                response = Port(arguments);
                                break;
                            case "PASV":
                                response = Passive();
                                break;
                            case "LIST":
                                response = List(arguments);
                                break;
                            case "PWD":
                                response = "257 "+this._currentDirectory+" is current directory.";
                                break;
                            case "RETR":
                                response = Retrieve(arguments);
                                break;
                            case "AUTH":
                                response = Auth(arguments);
                                break;
                            case "QUIT":
                                response = "221 Service closing control connection";
                                break;
                            case "TYPE":
                                string[] splitArgs = arguments.Split(' ');
                                response = Type(splitArgs[0], splitArgs.Length > 1 ? splitArgs[1] : null);
                                break;
                            case "OPTS":
                                response = "200 OPTS UTF8 command successful - UTF8 encoding now ON";
                                break;
                            case "STOR":
                                response = Stor(arguments);
                                break;
                            default:
                                response = "502 Command not implemented";
                                break;
                        }
                    }

                    if (_controlClient == null || !_controlClient.Connected)
                    {
                        break;
                    }
                    else
                    {
                        _controlWriter.WriteLine(response);
                        _controlWriter.Flush();

                        // Close the connection
                        if (response.StartsWith("221"))
                        {
                            break;
                        }

                        //if (cmd == "AUTH")
                        //{
                        //    _cert = new X509Certificate("server.cer");

                        //    _sslStream = new SslStream(_controlStream);

                        //    _sslStream.AuthenticateAsServer(_cert);

                        //    _controlReader = new StreamReader(_sslStream);
                        //    _controlWriter = new StreamWriter(_sslStream);
                        //}
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        #region File System Methods

        //public bool clientcertification()
        private static long CopyStream(Stream input, Stream output, int bufferSize)
        {
            byte[] buffer = new byte[bufferSize];
            int count = 0;
            long total = 0;

            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, count);
                total += count;
            }

            return total;
        }

        private static long CopyStreamAscii(Stream input, Stream output, int bufferSize)
        {
            char[] buffer = new char[bufferSize];
            int count = 0;
            long total = 0;

            using (StreamReader rdr = new StreamReader(input))
            {
                using (StreamWriter wtr = new StreamWriter(output, Encoding.UTF8))
                {
                    while ((count = rdr.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        wtr.Write(buffer, 0, count);
                        total += count;
                    }
                }
            }

            return total;
        }

        private long CopyStream(Stream input, Stream output)
        {
            if (_transferType == "I")
            {
                return CopyStream(input, output, 4096);
            }
            else
            {
                return CopyStreamAscii(input, output, 4096);
            }
        }

        private bool IsPathValid(string pathname)
        {
            if (pathname == string.Empty)
                return false;
            else
                return true;
        }

        private string NormalizeFilename(string pathname)
        {
            return Path.Combine(_currentDirectory, pathname);
        }

        #endregion

        #region FTP Commands

        private string User(string username)
        {
            _username = username;

            return "331 Username ok, need password";
        }

        private string Password(string password)
        {
            if (this.CheckUser != null && this.CheckUser(this._username, password))
            {
                this._isLogin = true;
                return "230 User logged in";
            }
            else
            {
                this._isLogin = false;
                return "530 Not logged in";
            }
        }

        private string Retrieve(string pathname)
        {
            pathname = new DirectoryInfo(NormalizeFilename(pathname)).FullName;


            if (File.Exists(pathname))
            {
                if (_dataConnectionType == DataConnectionType.Active)
                {
                    _dataClient = new TcpClient();
                    _dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoRetrieve, pathname);
                }
                else
                {
                    _passiveListener.BeginAcceptTcpClient(DoRetrieve, pathname);
                }
            
                return string.Format("150 Opening {0} mode data transfer for RETR", _dataConnectionType);
            }
            else
                return "550 File Not Found";
        }

        private string Stor(string pathname)
        {
            pathname = NormalizeFilename(pathname);
            if (IsPathValid(pathname))
            {
                if (_dataConnectionType == DataConnectionType.Active)
                {
                    _dataClient = new TcpClient();
                    _dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoStor, pathname);
                }
                else
                {
                    _passiveListener.BeginAcceptTcpClient(DoStor, pathname);
                }
                return string.Format("150 Opening {0} mode data transfer for STOR", _dataConnectionType);
            }
            else
                return "550 STOR failed";
        }
        private string List(string pathname)
        {
            if (pathname == null)
            {
                pathname = string.Empty;
            }

            pathname = new DirectoryInfo(NormalizeFilename(pathname)).FullName;
 
            if (IsPathValid(pathname))
            {
                if (_dataConnectionType == DataConnectionType.Active)
                {
                    _dataClient = new TcpClient();
                    _dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoList, pathname);
                }
                else
                {
                    _passiveListener.BeginAcceptTcpClient(DoList, pathname);
                }

                return string.Format("150 Opening {0} mode data transfer for LIST", _dataConnectionType);
            }
            else
                return "450 Requested file action not taken";
        }

        private string Auth(string authMode)
        {
            if (authMode == "TLS")
            {
                //_cert = this.ServerCert;
                //_controlStream = new 
                _sslStream = new SslStream(_controlStream)
                {
                    ReadTimeout = 5000,
                    WriteTimeout = 5000
                };
                _sslStream.AuthenticateAsServer(ServerCert, false, SslProtocols.Tls, true);

                //_controlReader = new StreamReader(_sslStream);
                //_controlWriter = new StreamWriter(_sslStream);
                isSslEnable = true;

                return "234 Enabling TLS Connection";
            }
            else
            {
                isSslEnable = false;
                return "504 Unrecognized AUTH mode";
            }
        }

        private string ChangeWorkingDirectory(string pathname)
        {
            pathname = pathname.TrimStart('/');
            pathname = pathname.Replace('/', '\\');
            this._currentDirectory = this._rootDirectory + "\\" + pathname;
            return "250 Changed to new directory";
        }

        private string Type(string typeCode, string formatControl)
        {
            string response = "500 ERROR";

            switch (typeCode)
            {
                case "A":
                case "I":
                    _transferType = typeCode;
                    response = "200 OK";
                    break;
                case "E":
                case "L":
                default:
                    response = "504 Command not implemented for that parameter.";
                    break;
            }

            if (formatControl != null)
            {
                switch (formatControl)
                {
                    case "N":
                        response = "200 OK";
                        break;
                    case "T":
                    case "C":
                    default:
                        response = "504 Command not implemented for that parameter.";
                        break;
                }
            }

            return response;
        }

        private string Port(string hostPort)
        {
            _dataConnectionType = DataConnectionType.Active;

            string[] ipAndPort = hostPort.Split(',');

            byte[] ipAddress = new byte[4];
            //byte[] port = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                ipAddress[i] = Convert.ToByte(ipAndPort[i]);
            }
            int portNum = (int.Parse(ipAndPort[4]) << 8) | int.Parse(ipAndPort[5]);
            //for (int i = 4; i < 6; i++)
            //{
            //    port[i - 4 + 2] = Convert.ToByte(ipAndPort[i]);
            //}

            //if (BitConverter.IsLittleEndian)
            //    Array.Reverse(port);

            //var v1 = new IPAddress(ipAddress);
            //var v2 = BitConverter.ToInt16(port, 0);
            //_dataEndpoint = new IPEndPoint(new IPAddress(ipAddress), BitConverter.ToInt32(port, 0));
            _dataEndpoint = new System.Net.IPEndPoint(new IPAddress(ipAddress), portNum);

            return "200 Data Connection Established";
        }

        private string Passive()
        {
            _dataConnectionType = DataConnectionType.Passive;

            IPAddress localAddress = ((IPEndPoint)_controlClient.Client.LocalEndPoint).Address;

            _passiveListener = new TcpListener(localAddress, 0);
            _passiveListener.Start();

            IPEndPoint localEndpoint = ((IPEndPoint)_passiveListener.LocalEndpoint);

            byte[] address = localEndpoint.Address.GetAddressBytes();
            short port = (short)localEndpoint.Port;

            byte[] portArray = BitConverter.GetBytes(port);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(portArray);

            return string.Format("227 Entering Passive Mode ({0},{1},{2},{3},{4},{5})", address[0], address[1], address[2], address[3], portArray[0], portArray[1]);
        }



        #endregion

        #region Async Methods
        private void DoRetrieve(IAsyncResult result)
        {
            if (_dataConnectionType == DataConnectionType.Active)
            {
                this._dataClient.EndConnect(result);
            }
            else
            {
                this._dataClient = _passiveListener.EndAcceptTcpClient(result);
            }

            string pathname = (string)result.AsyncState;
            //_sslStream.BeginRead()
            if (isSslEnable)
            {
                using (FileStream fs = new FileStream(pathname, FileMode.Open, FileAccess.Read))
                {
                    CopyStream(fs, _sslStream);
                }
            }
            else
            {
                using (NetworkStream dataStream = _dataClient.GetStream())
                {
                    using (FileStream fs = new FileStream(pathname, FileMode.Open, FileAccess.Read))
                    {
                        CopyStream(fs, dataStream);
                        //using (SslStream datasslStream = new SslStream(dataStream))
                        //{
                        //    CopyStream(fs, datasslStream);
                        //}
                    }
                    dataStream.Close();

                }
            }

            _dataClient.Close();
            _dataClient = null;
            _controlWriter.WriteLine("226 Closing data connection, file transfer successful");
            _controlWriter.Flush();
            //FileStream fs = new FileStream(pathname, FileMode.Open, FileAccess.Read);
            //try
            //{
            //    dataSession();

            //    if (_transferType == "I")
            //    {
            //        byte[] bytes = new byte[4096];
            //        BinaryReader binaryReader = new BinaryReader(fs);
            //        int count = binaryReader.Read(bytes, 0, bytes.Length);

            //        while (count > 0)
            //        {
            //            this._dataBiWriter.Write(bytes, 0, count);
            //            this._dataBiWriter.Flush();
            //            count = _dataBiReader.Read(bytes, 0, bytes.Length);
            //        }
            //        this._dataBiWriter.Write(0);
            //        dataSessionClose();
            //    }
            //    else
            //    {
            //        StreamReader streamReader = new StreamReader(fs);
            //        while (streamReader.Peek() > -1)
            //        {
            //            _dataWriter.WriteLine(streamReader.ReadLine());
            //        }
            //    }
            //}
            //finally
            //{
            //    _dataClient = null;
            //    fs.Close();
            //    _controlWriter.WriteLine("226 Closing data connection, file transfer successful");
            //    _controlWriter.Flush();
            //}
        }

        private void DoStor(IAsyncResult result)
        {
            if (_dataConnectionType == DataConnectionType.Active)
            {
                _dataClient.EndConnect(result);
            }
            else
            {
                _dataClient = _passiveListener.EndAcceptTcpClient(result);
            }

            string pathname = (string)result.AsyncState;
            FileInfo fileinfo = new FileInfo(pathname);
            if (!fileinfo.Directory.Exists)
            {
                fileinfo.Directory.Create();
            }
            long bufferload;
            using (FileStream fs = new FileStream(pathname, FileMode.Create, FileAccess.Write))
            {
                using (NetworkStream dataStream = _dataClient.GetStream())
                {

                    bufferload = CopyStream(dataStream, fs);
                }
                fs.Close();
            }
            if (bufferload == 0)
                Console.WriteLine("文件上传失败");

            _dataClient.Close();
            _dataClient = null;
            _controlWriter.WriteLine("226 Closing data connection, file transfer successful");
            _controlWriter.Flush();
        }
        private void DoList(IAsyncResult result)
        {
            if (_dataConnectionType == DataConnectionType.Active)
            {
                _dataClient.EndConnect(result);
            }
            else
            {
                _dataClient = _passiveListener.EndAcceptTcpClient(result);
            }

            string pathname = (string)result.AsyncState;
            //string line = string.Empty;
            //DateTimeFormatInfo dateTimeFormat = new CultureInfo("en-US", true).DateTimeFormat;
            //string[] dir = Directory.GetDirectories(pathname);
            //string[] files = Directory.GetFiles(pathname);  
            using (NetworkStream dataStream = _dataClient.GetStream())
            {
                _dataReader = new StreamReader(dataStream, Encoding.UTF8);
                _dataWriter = new StreamWriter(dataStream, Encoding.UTF8);

                IEnumerable<string> directories = Directory.EnumerateDirectories(pathname);
                foreach (string dir in directories)
                {
                    DirectoryInfo d = new DirectoryInfo(dir);

                    string date = d.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180) ?
                        d.LastWriteTime.ToString("MMM dd  yyyy") :
                        d.LastWriteTime.ToString("MMM dd HH:mm");

                    string line = string.Format("drwxr-xr-x    2 2003     2003     {0,8} {1} {2}", "4096", date, d.Name);

                    _dataWriter.WriteLine(line);
                    _dataWriter.Flush();
                }
                IEnumerable<string> files = Directory.EnumerateFiles(pathname);


                foreach (string file in files)
                {
                    FileInfo f = new FileInfo(file);

                    string date = f.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180) ?
                        f.LastWriteTime.ToString("MMM dd  yyyy") :
                        f.LastWriteTime.ToString("MMM dd HH:mm");

                    string line = string.Format("-rw-r--r--    2 2003     2003     {0,8} {1} {2}", f.Length, date, f.Name);

                    _dataWriter.WriteLine(line);
                    _dataWriter.Flush();
                }
                //for (int i = 0; i < dir.Length; i++)
                //{
                //    string folderName = System.IO.Path.GetFileName(dir[i]);
                //    DirectoryInfo d = new DirectoryInfo(dir[i]);
                //    line = string.Empty;
                //    line = string.Format("{0}dwr-\t{1}\t{2} {3:dd yyyy}\t{4}{5}",
                //        line, Dns.GetHostName(), dateTimeFormat.GetAbbreviatedMonthName(d.CreationTime.Month),
                //        d.CreationTime, folderName, Environment.NewLine);

                //    _dataWriter.WriteLine(line);
                //    _dataWriter.Flush();
                //}
                //for (int i = 0; i < files.Length; i++)
                //{
                //    FileInfo f = new FileInfo(files[i]);
                //    string fileName = f.Name;
                //    line = string.Empty;
                //    line = string.Format("{0}-wr-\t{1}\t{2} {3} {4:dd yyyy}\t{5}{6}",
                //        line, Dns.GetHostName(), f.Length, dateTimeFormat.GetAbbreviatedMonthName(f.CreationTime.Month),
                //        f.CreationTime, fileName, Environment.NewLine);

                //    _dataWriter.WriteLine(line);
                //    _dataWriter.Flush();
                //}
            }

            _dataClient.Close();
            _dataClient = null;

            _controlWriter.WriteLine("226 Transfer complete");
            _controlWriter.Flush();
        }


        #endregion
    }
}