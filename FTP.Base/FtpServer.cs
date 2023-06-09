﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Security.Cryptography.X509Certificates;

namespace FTP.FUC
{
    public class FtpServer : IDisposable
    {
        private bool _disposed = false;
        private bool _listening = false;
        private TcpListener _listener = null;
        private List<ClientConnection> _activeConnections = null;
        private Dictionary<string, string> _accounts = new Dictionary<string, string>();

        private X509Certificate2 ServerCertificate;
        public byte[] IP { get; set; }
        public int Port { get; set; }
        public string RootDirectory { get; set; }


        public FtpServer(string ip, int port, string root)
        {
            string[] ips = ip.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            this.IP = new byte[4];
            for (int i = 0; i < 4; ++i)
            {
                this.IP[i] = Convert.ToByte(ips[i]);
            }
            this.Port = port;
            this.RootDirectory = root;

            //this._accounts["nothing2say"] = "123";
        }

        public void Start()
        {
            _listener = new TcpListener(new IPAddress(this.IP), this.Port);
            _listener.Start();
            _activeConnections = new List<ClientConnection>();
            _listening = true;
            _listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
        }

        public void Stop()
        {
            _listening = false;
            _listener.Stop();
            _listener = null;
        }

        public void AddUser(string userName, string pwd)
        {
            this._accounts[userName] = pwd;
        }

        private void HandleAcceptTcpClient(IAsyncResult result)
        {
            if (_listening)
            {
                _listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
                TcpClient client = _listener.EndAcceptTcpClient(result);
                ClientConnection connection = new ClientConnection(client, this.RootDirectory, this.ServerCertificate);
                connection.CheckUser = CheckUser;
                _activeConnections.Add(connection);
                ThreadPool.QueueUserWorkItem(connection.HandleClient, client);
            }
        }

        private bool CheckUser(string userName, string pwd)
        {
            return this._accounts.ContainsKey(userName) && this._accounts[userName] == pwd;
        }

        public bool ImportCertificate(string path, string password)
        {
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                ServerCertificate = string.IsNullOrEmpty(password) ? new X509Certificate2(path) : new X509Certificate2(path, password);

                return true;
            }
            catch (Exception)
            {
                return false;
                //throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_listener != null)
                    {
                        _listening = false;
                        _listener.Stop();
                    }

                    foreach (ClientConnection conn in _activeConnections)
                    {
                    }
                }
            }

            _disposed = true;
        }
    }
}
