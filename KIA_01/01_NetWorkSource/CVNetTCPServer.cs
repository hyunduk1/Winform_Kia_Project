using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using KIA_01._00_Manager;
using System;

namespace VNet
{
    class CVNetTCPServer
    {
        // Main objects:
        public volatile Socket _server;                                         // The TCP server socket.
        public volatile CVNetTCPServer _auxServer;                                   // The auxiliary port to allow both protocols (IPv4 and IPv6).
        volatile Thread _listener;                                                  // The listener thread that gets the TCP connections.
        volatile List<CVNetTCPSocket> _connections = new List<CVNetTCPSocket>();    // Sockets for every accepted incoming connection.
        readonly object _connectionLock = new object();                         // Lock the connection list access to avoid concurrency.
                                                                                // Parameters:
        volatile string _localIP = "";                                          // The requested local IP (optional, empty means default).
        volatile int _port;                                                     // The server listening port.
        volatile IPEndPoint _localIPEndpoint;                                   // The Endpoint to listen to.
        public volatile int _maxConnections = 100;                              // Maximum number of persistent connections.
        public volatile float _keepAliveTimeout;                                // Time interval in seconds to probe connection activity.
                                                                                // Events:
        public delegate void EventConnection(CVNetTCPSocket connection, CVNetTCPServer server);
        public delegate void EventException(int code, string message, CVNetTCPServer server);
        public delegate void EventVoid(CVNetTCPServer server);
        public volatile EventVoid onOpen;                                       // The server has started to listen properly.
        public volatile EventConnection onNewConnection;                        // A new connection was accepted and established.
        public volatile EventException onError;                                 // There was an error (the argument contains the error description).
        public volatile EventVoid onClose;                                      // The server was closed (and maybe all its connections).
                                                                                // Status:
        volatile bool _disconnecting = false;                                   // Flag: Disconnection requested by the user/server.
        readonly object _serverLock = new object();                             // Lock the control methods to avoid concurrency.

        ///<summary>Constructor</summary>
        public CVNetTCPServer() { }
        ///<summary>Constructor</summary>
        public CVNetTCPServer(int port, string localIP = "", EventVoid evOpen = null, EventConnection evConnection = null, EventException evError = null, EventVoid evClose = null, int maxConnections = 100, float keepAliveTimeout = 40f)
        {
            Setup(port, localIP, evOpen, evConnection, evError, evClose, maxConnections, keepAliveTimeout);
        }
        ///<summary>Destructor</summary>
        ~CVNetTCPServer()
        {
            Dispose();
        }

        ///<summary>Creates the TCP server</summary>
        public void Setup(int port, string localIP = "", EventVoid evOpen = null, EventConnection evConnection = null, EventException evError = null, EventVoid evClose = null, int maxConnections = 100, float keepAliveTimeout = 40f)
        {
            Dispose();
            lock (_serverLock)
            {
                if (IsValidPort(port))
                {
                    // Local address:
                    _port = port;
                    _localIP = localIP.Replace(" ", string.Empty).ToLower();
                    _maxConnections = maxConnections;
                    _keepAliveTimeout = keepAliveTimeout;
                    // Events:
                    onOpen = evOpen;
                    onNewConnection = evConnection;
                    onError = evError;
                    onClose = evClose;
                    // Create connection(s):
                    try
                    {
                        switch (_localIP)
                        {
                            case "ipv4":
                            case "ipv6":
                                _localIPEndpoint = AddressParser(GetDefaultIPAddress(_localIP), _port);
                                break;
                            case "":
                                if (string.IsNullOrEmpty(GetDefaultIPAddress("ipv4")))
                                {
                                    _localIPEndpoint = AddressParser(GetDefaultIPAddress("ipv6"), _port);   // IPV6 only if not IPV4 avalable.
                                }
                                else if (string.IsNullOrEmpty(GetDefaultIPAddress("ipv6")))
                                {
                                    _localIPEndpoint = AddressParser(GetDefaultIPAddress("ipv4"), _port);   // IPV4 only if not IPV6 avalable.
                                }
                                else
                                {
                                    _localIPEndpoint = AddressParser(GetDefaultIPAddress("ipv4"), _port);
                                    _auxServer = new CVNetTCPServer(_port, "ipv6", null, OnNewConnectionAux, OnErrorAux);
                                }
                                break;
                            default:
                                _localIPEndpoint = AddressParser(_localIP, _port);
                                break;
                        }
                        // Set the server:
                        _server = new Socket(_localIPEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        _server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 0));
                        _server.Bind(_localIPEndpoint);
                    }
                    catch (System.Exception e)
                    {
                        ThreadPool.QueueUserWorkItem((object s) => {
                            onError?.Invoke(e.HResult, "[TCPServer.Setup] " + e.Message, this);
                        });
                    }
                }
                else
                {
                    WriteLine("[TCPServer.Setup] You should provide a valid port (1 to 65535).", true);
                }
            }
        }
        ///<summary>Starts listening the incoming connections</summary>
        public void Connect()
        {
            lock (_serverLock)
            {
                // Connect the main server:
                if (_server != null && !IsConnected()){
                    try{
                        // Start server socket:
                        _disconnecting = false;
                        _server.Listen(10);                                     // Maximum amount of pending connections queue.
                        _listener = new Thread(new ThreadStart(Listener));      // Start the listening thread.
                        _listener.Start();
                        // Connection event:
                        ThreadPool.QueueUserWorkItem((object s) => { SafeOnOpen(); });
                    }
                    catch (System.Exception e)
                    {
                        ThreadPool.QueueUserWorkItem((object s) => {
                            onError?.Invoke(e.HResult, "[TCPServer.Connect] " + e.Message, this);
                        });
                    }
                    // Connect the auxiliary server:
                    if (_auxServer != null)
                        _auxServer.Connect();
                }
                else
                {
                    WriteLine("[TCPServer.Connect] You should call Setup() before attempt to connect.");
                }
            }
        }
        ///<summary>Close the server and all of its connections</summary>
        public void Disconnect()
        {
            Setup(_port, _localIP, onOpen, onNewConnection, onError, onClose, _maxConnections);
        }
        ///<summary>Close the server and all of its connections without firing events</summary>
        public void Dispose()
        {
            lock (_serverLock)
            {
                _disconnecting = true;
                // Clear events:
                onOpen = null;
                onNewConnection = null;
                onError = null;
                onClose = null;
                try
                {
                    if (_auxServer != null)
                    {
                        _auxServer.Dispose();
                        _auxServer = null;
                    }
                    if (_server != null)
                    {
                        _server.Dispose();
                        _server = null;
                    }
                    if (_listener != null)
                    {
                        _listener.Abort();
                        _listener = null;
                    }
                }
                catch { }   // The "Thread aborted" exception is inevitable and harmless (so it's hidden).
                CloseAllConnections();
            }
        }

        ///<summary>Listening thread, accepts the new incoming connections</summary>
        void Listener()
        {
            try
            {
                while (IsConnected())
                {
                    Socket tcpSocket = _server.Accept();                    // Code blocks here until a connection is received.
                                                                            // There is a new connection:
                    if (_connections.Count < _maxConnections)
                    {
                        // Accept the connection normally:
                        CVNetTCPSocket connection = new CVNetTCPSocket(tcpSocket, RemoveConnection, _keepAliveTimeout);
                        _connections.Add(connection);
                        // Fire the connection event:
                        ThreadPool.QueueUserWorkItem((object s) => { SafeOnNewConnection(connection); });
                    }
                    else
                    {
                        // Reject silently incoming connections if maximum was reached:
                        tcpSocket.Shutdown(SocketShutdown.Both);
                        tcpSocket.Dispose();
                    }
                }
            }
            catch { }
            // Fire the OnClose event (if listener dies without reason):
            if (!_disconnecting)
                ThreadPool.QueueUserWorkItem((object s) => { SafeOnClose(); });
        }
        ///<summary>Force the close of all current connections</summary>
        void CloseAllConnections()
        {
            lock (_connectionLock)
            {
                while (_connections.Count > 0)
                {
                    _connections[0].Dispose();
                }
            }
        }
        ///<summary>Forces the close of the connection and removes it from the list</summary>
        public void CloseConnection(CVNetTCPSocket connection)
        {
            lock (_connectionLock)
            {
                if (_connections.Contains(connection))
                    connection.Dispose();
                else if (_auxServer != null)
                    _auxServer.CloseConnection(connection);
            }
        }
        ///<summary>Removes the connection from the list</summary>
        void RemoveConnection(CVNetTCPSocket connection)
        {
            lock (_connectionLock)
            {
                if (_connections.Contains(connection))
                    _connections.Remove(connection);
                else if (_auxServer != null)
                    _auxServer.RemoveConnection(connection);
            }
        }
        public void HansolSendData(string Ip, string strPacket)
        {
            for(int i = 0; i < _connections.Count; i++)
            {
                _connections[i].SendData(strPacket);
                if (_connections[i].GetIP() == Ip)
                {
                    
                    //break;
                }
            }
        }
        public void SendData(string strPacket)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                _connections[i].SendData(strPacket);
            }
        }
        public void SendData(byte[] strPacket)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                _connections[i].SendData(strPacket);
            }
        }
        public void SendDataALL(string strPacket)
        {
            for (int i = 0; i < _connections.Count; i++){
                _connections[i].SendData(strPacket);
            }
        }

        public bool SendDataALL(string strIP,string strPacket)
        {
            CVNetTCPSocket tempUser = GetUserInfo(strIP);
            if (tempUser != null){
                tempUser.SendData(strPacket);
                return true;
            }
            return false;
        }
        public CVNetTCPSocket GetUserInfo(string strIP)
        {
            for(int i=0; i< _connections.Count; i++){
                if(_connections[i].GetIP()== strIP){
                    return _connections[i];
                }
            }
            return null;
         }


        ///<summary>Send a message to all available connections</summary>
        public void Distribute(byte[] data)
        {
            PurgeConnections();
            for (int i = 0; i < _connections.Count; i++)
            {
                _connections[i].SendData(data);
            }
            if (_auxServer != null)
                _auxServer.Distribute(data);
        }
        ///<summary>Send a message to all available connections</summary>
        public void Distribute(string data)
        {
            Distribute(System.Text.Encoding.UTF8.GetBytes(data));
        }
        ///<summary>Gets how many connections were established</summary>
        public int GetConnectionsCount()
        {
            int count = 0;
            lock (_connectionLock)
            {
                count = _connections.Count + (_auxServer != null ? _auxServer.GetConnectionsCount() : 0);
            }
            return count;
        }
        ///<summary>Gets the connection from the list in the provided index</summary>
        public CVNetTCPSocket GetConnection(int index)
        {
            PurgeConnections();
            lock (_connectionLock)
            {
                if (index >= 0 && index < _connections.Count)
                    return _connections[index];
                else
                    return null;
            }
        }
        ///<summary>Sometimes the connections don't close themselves</summary>
        void PurgeConnections()
        {
            lock (_connectionLock)
            {
                int index = 0;
                while (index < _connections.Count)
                {
                    if (_connections[index].IsConnected())
                    {
                        index++;
                    }
                    else
                    {
                        // Dispose the useless connection:
                        _connections[index].SafeOnClose();
                        _connections[index].Dispose();
                    }
                }
            }
        }

        ///<summary>Calls the onOpen event preventing crashes if external code fails</summary>
        void SafeOnOpen()
        {
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onOpen?.Invoke(this);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[TCPServer.SafeOnOpen] " + e.Message, this);
                });
            }
        }
        ///<summary>Calls the onNewConnection event preventing crashes if external code fails</summary>
        void SafeOnNewConnection(CVNetTCPSocket connection)
        {
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onNewConnection?.Invoke(connection, this);
                connection.StartListening();
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[TCPServer.SafeOnNewConnection] " + e.Message, this);
                });
            }
        }
        ///<summary>Calls the onClose event preventing crashes if external code fails</summary>
        void SafeOnClose()
        {
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onClose?.Invoke(this);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[TCPServer.SafeOnClose] " + e.Message, this);
                });
            }
        }

        ///<summary>Get the local active IP (allows to get the secondary address)</summary>
        public string GetIP(bool secondary = false)
        {
            if (!secondary)
            {
                // Returns the primary connection IP:
                if (_localIP == "" || _localIP == "ipv4" || _localIP == "ipv6")
                {
                    if (_server != null && _server.AddressFamily == AddressFamily.InterNetwork)
                        return GetDefaultIPAddress("ipv4");
                    else if (_server != null && _server.AddressFamily == AddressFamily.InterNetworkV6)
                        return GetDefaultIPAddress("ipv6");
                }
                return _localIP;
            }
            else
            {
                // Returns the auxiliary connection IP:
                if (_auxServer != null)
                    return _auxServer.GetIP();
                return "";
            }
        }
        ///<summary>Get the captured application port</summary>
        public int GetPort()
        {
            return _port;
        }
        ///<summary>TRUE if connected and listening</summary>
        public bool IsConnected()
        {
            bool secondaryActive = (_auxServer != null) ? _auxServer.IsConnected() : false;
            return (!_disconnecting && _server != null && _listener != null && _server.IsBound && _listener.IsAlive) || secondaryActive;
        }

        ///<summary>Event onMessage for auxiliary server</summary>
        void OnNewConnectionAux(CVNetTCPSocket connection, CVNetTCPServer server)
        {
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onNewConnection?.Invoke(connection, server);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[TCPServer.OnNewConnectionAux] " + e.Message, server);
                });
            }
        }
        ///<summary>Event onError for auxiliary server</summary>
        void OnErrorAux(int error, string message, CVNetTCPServer server)
        {
            // Adds a label to identify that the exception happened in the auxiliary server:
            onError?.Invoke(error, "[TCPServer.OnErrorAux] " + message, server);
        }

        ///<summary>Returns a valid IpEndPoint from the provided IP or mode or URL</summary>
        public IPEndPoint AddressParser(string ipOrUrl = "ipv4", int port = 0)
        {
            IPEndPoint endPoint = null;
            if (string.IsNullOrEmpty(ipOrUrl))
                ipOrUrl = "";
            else
                ipOrUrl = ipOrUrl.Replace(" ", string.Empty).ToLower();
            // Creates a "defaul" remote endpoint:
            if (string.IsNullOrEmpty(ipOrUrl) || ipOrUrl == "ipv4")
                endPoint = new IPEndPoint(IPAddress.Any, port);
            else if (ipOrUrl == "ipv6")
                endPoint = new IPEndPoint(IPAddress.IPv6Any, port);
            else
            {
                // Create an instance of IPAddress for IP V4 or V6:
                IPAddress address = null;
                try
                {
                    address = IPAddress.Parse(ipOrUrl);
                }
                catch { }

                if (address == null)
                {
                    // IP parsing has failed, so parse as URL:
                    try
                    {
                        IPHostEntry ipHostInfo = Dns.GetHostEntry(ipOrUrl);     // Gets the IP from a URL
                        address = ipHostInfo.AddressList[0];
                        endPoint = new IPEndPoint(address, port);
                    }
                    catch (System.Exception e)
                    {
                        ThreadPool.QueueUserWorkItem((object s) => {
                            onError?.Invoke(e.HResult, "[TCPServer.AddressParser] " + e.Message, this);
                        });
                    }
                }
                else
                {
                    // IP succedded to be parsed:
                    endPoint = new IPEndPoint(address, port);
                }
            }
            return endPoint;
        }
        ///<summary>Gets the default IP address (IPv4 or IPv6)</summary>
        public string GetDefaultIPAddress(string ipMode = "")
        {
            string ipv4 = "";
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    ipv4 = (socket.LocalEndPoint as IPEndPoint).Address.ToString();
                }
            }
            catch { }
            string ipv6 = "";
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, 0))
                {
                    socket.Connect("2001::1", 65530);
                    ipv6 = (socket.LocalEndPoint as IPEndPoint).Address.ToString();
                }
            }
            catch { }
            switch (ipMode.ToLower())
            {
                case "ipv4":
                    return ipv4;
                case "ipv6":
                    return ipv6;
                default:
                    return string.IsNullOrEmpty(ipv4) ? ipv6 : ipv4;
            }
        }
        ///<summary>Checks if the provided port is a valid number</summary>
        bool IsValidPort(int port)
        {
            if (port >= 1 && port <= 65535)
                return true;
            return false;
        }
        ///<summary>Get the list of physical MAC addresses</summary>
        public string[] GetMacAddress()
        {
            List<string> macAdress = new List<string>();
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                PhysicalAddress address = adapter.GetPhysicalAddress();
                if (address.ToString() != "")
                {
                    macAdress.Add(address.ToString());
                }
            }
            return macAdress.ToArray();
        }

        /// <summary>Writes to the console depending on the platform.</summary>
        void WriteLine(string line, bool error = false)
        {
            System.Console.WriteLine(line);
        }
    }
}
