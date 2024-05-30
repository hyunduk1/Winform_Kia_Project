using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;

namespace VNet
{
    class CVNetUDPSocket
    { 
        public volatile UdpClient _client;                                                          // Connection object.
        public volatile CVNetUDPSocket _auxClient;                                                   // The auxiliary port to allow both protocols (IPv4 and IPv6).
        volatile Thread _receiveThread;                                                             // Incoming messages listening thread.
        volatile List<byte[]> _rxBuffer = new List<byte[]>();                                       // The incoming datagrams are stored if no OnMessageReceived event is defined.
        readonly object _rxBufferLock = new object();                                               // Lock the incoming buffer access.
                                                                                                    // Local connection settings:
        volatile string         _localIP = "";                                                              // The requested local IP (optional, empty means default).
        volatile int            _port;                                                                         // The shared application port.
        volatile int            _bufferSize = 65536;                                                           // The allowed size of input and output buffers (automatically detected).
        volatile IPEndPoint _remoteIPEndpoint;                                                      // The Endpoint to listen to.
                                                                                                    // Events:
        public delegate void EventMessage(byte[] data, string ip, CVNetUDPSocket connection);        // Event delegate with a byte array and a string as arguments.
        public delegate void EventException(int code, string message, CVNetUDPSocket connection);    // Event delegate with int and string as arguments.
        public delegate void EventVoid(CVNetUDPSocket connection);                                   // Event delegate for generic events.
        public volatile EventVoid onOpen;                                                           // The connection was established properly (in UDP it means "no error" since there is no connection).
        public volatile EventMessage onMessage;                                                     // A datagram has been received.
        public volatile EventException onError;                                                     // There was an error (the argument contains the error description).
        public volatile EventVoid onClose;                                                          // The connection was closed unexpectedly (in UDP is useless).
                                                                                                    // Status:
        volatile Timer  _keepAliveTimer;                                                             // Timer to keep the connection alive (Mono).
        volatile float  _keepAliveTimeout;                                                           // Time interval in seconds to send some "keep alive" message (Mono).
        volatile bool   _disconnecting = false;                                                       // Flag: Disconnection requested by the user.
        readonly object _clientLock = new object();                                                 // Lock the send method to avoid concurrency.

        ///<summary>Constructor</summary>
        public CVNetUDPSocket() { }
        ///<summary>Constructor</summary>
        public CVNetUDPSocket(int port, string localIP = "", EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null)
        {
            Setup(port, localIP, evOpen, evMessage, evError, evClose);
        }
        ///<summary>Destructor</summary>
        ~CVNetUDPSocket()
        {
            Dispose();
        }

        ///<summary>SetsUp the UDP client to send (not receive)</summary>
        public void Setup(int port, string localIP = "", EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null)
        {
            Dispose();
            if (IsValidPort(port))
            {
                // Local IP parameters:
                _port = port;
                _localIP = localIP.Replace(" ", string.Empty).ToLower();
                // Begin setup:
                _keepAliveTimer = new Timer(KeepAliveTimedOut, null, Timeout.Infinite, Timeout.Infinite);
                // Events:
                onOpen = evOpen;
                onMessage = evMessage;
                onError = evError;
                onClose = evClose;
                // Set connection accordingly:
                try
                {
                    switch (_localIP)
                    {
                        case "ipv4":
                            _client = new UdpClient(_port, AddressFamily.InterNetwork);                 // IPV4 only (fails if not available).
                            break;
                        case "ipv6":
                            _client = new UdpClient(_port, AddressFamily.InterNetworkV6);               // IPV6 only (fails if not available).
                            break;
                        case "":
                            if (string.IsNullOrEmpty(GetDefaultIPAddress("ipv4")))
                            {
                                _client = new UdpClient(_port, AddressFamily.InterNetworkV6);           // IPV6 only if not IPV4 avalable.
                            }
                            else if (string.IsNullOrEmpty(GetDefaultIPAddress("ipv6")))
                            {
                                _client = new UdpClient(_port, AddressFamily.InterNetwork);             // IPV4 only if not IPV6 avalable.
                            }
                            else
                            {
                                _client = new UdpClient(_port, AddressFamily.InterNetwork);
                                _auxClient = new CVNetUDPSocket(_port, "ipv6", null, OnMessageAux, OnErrorAux, OnCloseAux);
                            }
                            break;
                        default:
                            _client = new UdpClient(AddressParser(_localIP, _port));
                            break;
                    }
                    // Socket settings:
                    _client.MulticastLoopback = false;
                    _client.EnableBroadcast = true;

                    // Get the maximum send buffer available (This procedure is important on Android, but fails on Apple):
                    IPAddress _type = IPAddress.IPv6Loopback;
                    if (_client.Client.AddressFamily == AddressFamily.InterNetwork)
                        _type = IPAddress.Loopback;
                    for (int c = 64; c > 1; c--)
                    {
                        _bufferSize = 1024 * c;
                        if (_bufferSize > 65504) _bufferSize = 65504;       // Maximum UDP buffer available (65536 - 32)
                        SetSocketBuffer(_bufferSize);
                        IPEndPoint loop = AddressParser(_type.ToString(), _port);
                        try
                        {
                            byte[] data = new byte[_bufferSize];
                            _client.Send(data, data.Length, loop);
                            _client.Receive(ref loop);
                            break;                                          // Breaks the for loop if transference succeeded.
                        }
                        catch { }
                    }

                    _disconnecting = false;
                }
                catch (System.Exception e)
                {
                    ThreadPool.QueueUserWorkItem((object s) => {
                        onError?.Invoke(e.HResult, "[UDPConnection.Setup] " + e.Message, this);
                    });
                }
            }
            else
            {
                WriteLine("[UDPConnection.Setup] You should provide a valid port (1 to 65535).", true);
            }
        }
        ///<summary>Set the connection (to send and receive), provide the keepAliveTimeout in seconds</summary>
        public void Connect(int port, string remoteIP = "", float keepAliveTimeout = 0f)
        {
            if (_client != null)
            {
                if (!IsValidPort(port))
                {
                    WriteLine("[UDPConnection] You should provide a valid port (1 to 65535).", true);
                    return;
                }
                // Force a message to be sent periodically in order to keep the connection alive (Mono):
                _keepAliveTimeout = keepAliveTimeout;
                if (_keepAliveTimeout <= 0f) _keepAliveTimeout = Timeout.Infinite;
                _keepAliveTimer.Change(SecondsToMiliseconds(_keepAliveTimeout), SecondsToMiliseconds(_keepAliveTimeout));
                try
                {
                    // If no IP is provided, any IP will be listened:
                    if (string.IsNullOrEmpty(remoteIP))
                    {
                        if (_client.Client.AddressFamily == AddressFamily.InterNetwork)
                            _remoteIPEndpoint = AddressParser("ipv4", port);
                        else
                            _remoteIPEndpoint = AddressParser("ipv6", port);
                    }
                    else
                        _remoteIPEndpoint = AddressParser(remoteIP, port);
                    // Connect also the auxiliar connection:
                    if (_auxClient != null)
                        _auxClient.Connect(port, remoteIP);
                    // Start the listener:
                    if (_remoteIPEndpoint.AddressFamily == _client.Client.AddressFamily)
                    {
                        // Start the listening thread:
                        if (_receiveThread != null)
                        {
                            _receiveThread.Abort();
                            _receiveThread = null;
                        }
                        _disconnecting = false;
                        _receiveThread = new Thread(new ThreadStart(ReceiveData)) { IsBackground = true, Priority = ThreadPriority.Highest };
                        if (_receiveThread != null)
                            _receiveThread.Start();
                        // Connection event (In UDP it means "no errors" since there is no connection):
                        ThreadPool.QueueUserWorkItem((object s) => { SafeOnOpen(); });
                        // Send the first package to ensure connection in some systems:
                        if (_client != null && _client.Client.AddressFamily == AddressFamily.InterNetwork)
                            SendData(IPV4BroadcastAddress(), new byte[] { });   // Just send some data.
                    }
                }
                catch (System.Exception e)
                {
                    ThreadPool.QueueUserWorkItem((object s) => {
                        onError?.Invoke(e.HResult, "[UDPConnection.Connect] " + e.Message, this);
                    });
                }
            }
            else
            {
                WriteLine("[UDPConnection.Connect] You should call Setup() before attempt to connect.");
            }
        }
        ///<summary>Close the connection correctly</summary>
        public void Disconnect()
        {
            // Reset the connection to initial state:
            Setup(_port, _localIP, onOpen, onMessage, onError, onClose);
        }
        ///<summary>Close the connection correctly without firing events</summary>
        public void Dispose()
        {
            _disconnecting = true;
            // Clear events:
            onOpen = null;
            onMessage = null;
            onError = null;
            onClose = null;
            try
            {
                // Close auxiliar connection:
                if (_auxClient != null)
                {
                    _auxClient.Dispose();
                    _auxClient = null;
                }
                // Close "keep alive" timer:
                if (_keepAliveTimer != null)
                {
                    _keepAliveTimer.Dispose();
                    _keepAliveTimer = null;
                }
                // Close listener:
                if (_receiveThread != null)
                {
                    _receiveThread.Abort();
                    _receiveThread = null;
                }
                // Close client last (to avoid null reference exception):
                if (_client != null)
                {
                    _client.Close();
                    _client.Dispose();
                    _client = null;
                }
            }
            catch { }   // The "Thread aborted" exception is inevitable and harmless (so it's hidden).
        }

        ///<summary>Set the buffer size</summary>
        void SetSocketBuffer(int buffer)
        {
            _client.Client.ReceiveBufferSize = buffer;
            _client.Client.SendBufferSize = buffer;
        }
        ///<summary>Keep alive connection timeout event</summary>
        void KeepAliveTimedOut(object state)
        {
            if (_client.Client.AddressFamily == AddressFamily.InterNetwork)
                SendData(IPV4BroadcastAddress(), new byte[] { });   // Just send some data.
        }

        ///<summary>Listening thread, gets the incoming datagrams and stores into a buffer</summary>
        void ReceiveData()
        {
            while (_client != null)
            {
                try
                {
                    // Reads received data:
                    IPEndPoint endpoint = _remoteIPEndpoint;
                    byte[] data = _client.Receive(ref endpoint);
                    string remoteIP = endpoint.Address.ToString();
                    // Save the received data or call the onMessage event:
                    if (data.Length > 0 && !endpoint.Address.IsIPv4MappedToIPv6 && (remoteIP != GetIP() || endpoint.Port != _port))
                    {
                        if (onMessage != null)
                        {
                            ThreadPool.QueueUserWorkItem((object s) => { SafeOnMessage(data, remoteIP); });
                        }
                        else
                        {
                            lock (_rxBufferLock)
                            {
                                _rxBuffer.Add(data);                // Add received message to buffer (if no event defined).
                            }
                        }
                    }
                }
                catch (SocketException e)
                {
                    // Code 10054: Connection closed by the remote host (Inconsistent error in UDP, connection will be restored automatically).
                    if (e.ErrorCode != 10054)
                    {
                        ThreadPool.QueueUserWorkItem((object s) => {
                            onError?.Invoke(e.ErrorCode, "[UDPConnection.ReceiveData] " + e.Message, this);
                        });
                    }
                    if (!_disconnecting)
                    {
                        Thread.Sleep(1000);                         // Wait a decent time for the OS to recover.
                                                                    // Restore connection:
                        ThreadPool.QueueUserWorkItem((object s) => {
                            Disconnect();
                            // Some Ethernet adapters lose connection as if it were TCP, so it needs to be restarted automatically:
                            if (_remoteIPEndpoint.Address.ToString() == "0.0.0.0" || _remoteIPEndpoint.Address.ToString() == "::")
                                Connect(_port, "", _keepAliveTimeout);                                      // No IP was provided.
                            else
                                Connect(_port, _remoteIPEndpoint.Address.ToString(), _keepAliveTimeout);    // Maintains the provided IP.
                        });
                    }
                }
                catch (System.OutOfMemoryException e)
                {
                    Thread.Sleep(1000);
                    ThreadPool.QueueUserWorkItem((object s) => {
                        onError?.Invoke(e.HResult, "[UDPConnection.ReceiveData] " + e.Message, this);
                    });
                }
            }
        }
        ///<summary>Returns true if there is any data into the buffer</summary>
        public bool DataAvailable()
        {
            return (_rxBuffer.Count > 0);
        }
        ///<summary>Get the next received message</summary>
        public byte[] GetMessage()
        {
            byte[] message = null;
            if (DataAvailable())
            {
                lock (_rxBufferLock)
                {
                    message = _rxBuffer[0];
                    _rxBuffer.RemoveAt(0);
                }
            }
            return message;
        }
        ///<summary>Flush the input message buffer</summary>
        public void ClearInputBuffer()
        {
            lock (_rxBufferLock)
            {
                _rxBuffer.Clear();
            }
        }
        ///<summary>Sends a byte array chosing destination (UDP only)</summary>
        public void SendData(IPEndPoint remoteIPEndpoint, byte[] data)
        {
            lock (_clientLock)
            {
                try
                {
                    if (remoteIPEndpoint != null)
                    {
                        if (_client != null && remoteIPEndpoint.AddressFamily == _client.Client.AddressFamily)
                            _client.Send(data, data.Length, remoteIPEndpoint);
                        // Repeat the data in the auxiliary connection:
                        if (_auxClient != null)
                            _auxClient.SendData(remoteIPEndpoint, data);
                    }
                    else
                    {
                        ThreadPool.QueueUserWorkItem((object s) => {
                            onError?.Invoke(1, "[UDPConnection.SendData] The provided remote endpoint was null or no network available.", this);
                        });
                    }
                }
                catch (System.Exception e)
                {
                    ThreadPool.QueueUserWorkItem((object s) => {
                        onError?.Invoke(e.HResult, "[UDPConnection.SendData] " + e.Message, this);
                    });
                }
            }
        }
        ///<summary>Sends a byte array chosing destination (UDP only)</summary>
        public void SendData(IPEndPoint remoteIPEndpoint, string data)
        {
            SendData(remoteIPEndpoint, StringToByteArray(data));
        }
        ///<summary>Sends a byte array chosing destination IP and port (UDP only)</summary>
        public void SendData(string ip, byte[] data, int port = 0)
        {
            if (port == 0)
                SendData(AddressParser(ip, _port), data);
            else
                SendData(AddressParser(ip, port), data);
        }
        ///<summary>Sends a byte array chosing destination (UDP only)</summary>
        public void SendData(string ip, string data, int port = 0)
        {
            if (port == 0)
                SendData(AddressParser(ip, _port), data);
            else
                SendData(AddressParser(ip, port), data);
        }
        ///<summary>Sends a byte array</summary>
        public void SendData(byte[] data)
        {
            SendData(_remoteIPEndpoint, data);
        }
        ///<summary>Sends a string</summary>
        public void SendData(string data)
        {
            SendData(_remoteIPEndpoint, StringToByteArray(data));
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
                    onError?.Invoke(e.HResult, "[UDPConnection.SafeOnOpen] " + e.Message, this);
                });
            }
        }
        ///<summary>Calls the onMessage event preventing crashes if external code fails</summary>
        void SafeOnMessage(byte[] message, string ip)
        {
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onMessage?.Invoke(message, ip, this);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) =>
                {
                    onError?.Invoke(e.HResult, "[UDPConnection.SafeOnMessage] " + e.Message + " - Received: " + (ByteArrayToString(message)), this);
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
                    onError?.Invoke(e.HResult, "[UDPConnection.SafeOnClose] " + e.Message, this);
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
                    if (_client != null && _client.Client.AddressFamily == AddressFamily.InterNetwork)
                        return GetDefaultIPAddress("ipv4");
                    else if (_client != null && _client.Client.AddressFamily == AddressFamily.InterNetworkV6)
                        return GetDefaultIPAddress("ipv6");
                }
                return _localIP;
            }
            else
            {
                // Returns the auxiliary connection IP:
                if (_auxClient != null)
                    return _auxClient.GetIP();
                return "";
            }
        }
        ///<summary>Gets the IPV4 broadcast address for the current network</summary>
        public string IPV4BroadcastAddress()
        {
            // Only IPV4 address is allowed:
            string[] sAddress = GetDefaultIPAddress("ipv4").Split('.');
            if (sAddress.Length == 4)
            {
                // Convert address into bytes for calculations:
                byte[] address = new byte[sAddress.Length];
                for (int i = 0; i < sAddress.Length; i++)
                {
                    address[i] = byte.Parse(sAddress[i]);
                }
                // Calculate subnet mask:
                byte[] mask;
                if (address[0] >= 0 && address[0] <= 127)
                    mask = new byte[] { 255, 0, 0, 0 };
                else if (address[0] >= 128 && address[0] <= 191)
                    mask = new byte[] { 255, 255, 0, 0 };
                else if (address[0] >= 192 && address[0] <= 223)
                    mask = new byte[] { 255, 255, 255, 0 };
                else
                    mask = new byte[] { 0, 0, 0, 0 };
                // Calculate IPV4 broadcast address:
                string broadcast = "";
                for (int i = 0; i < address.Length; i++)
                {
                    broadcast += (address[i] | (mask[i] ^ 255)).ToString();
                    if (i < address.Length - 1)
                        broadcast += ".";
                }
                return broadcast;
            }
            return null;
        }
        ///<summary>Get the captured application port</summary>
        public int GetPort()
        {
            return _port;
        }
        ///<summary>TRUE if connected and listening</summary>
        public bool IsConnected()
        {
            bool secondaryActive = (_auxClient != null) ? _auxClient.IsConnected() : false;
            return (_client != null && _receiveThread != null && _receiveThread.IsAlive && !_disconnecting) || secondaryActive;
        }
        ///<summary>Gets the input/output buffer</summary>
        public int GetIOBufferSize()
        {
            int buffer = _bufferSize;
            // Gets the smallest buffer:
            if (_auxClient != null && _auxClient.GetIOBufferSize() < buffer)
                buffer = _auxClient.GetIOBufferSize();
            return buffer;
        }

        ///<summary>Event onMessage for auxiliary connection</summary>
        void OnMessageAux(byte[] data, string remoteIP, CVNetUDPSocket connection)
        {
            // The auxiliary connection never saves the data in its buffer:
            if (onMessage != null)
            {
                // This is to detect erros in the code assigned by the user to this event:
                try
                {
                    onMessage?.Invoke(data, remoteIP, connection);
                }
                catch (System.Exception e)
                {
                    ThreadPool.QueueUserWorkItem((object s) => {
                        onError?.Invoke(e.HResult, "[UDPConnection.OnMessageAux] " + e.Message + " - Received: " + (ByteArrayToString(data)), connection);
                    });
                }
            }
            else
            {
                lock (_rxBufferLock)
                {
                    _rxBuffer.Add(data);
                }
            }
        }
        ///<summary>Event onError for auxiliary connection</summary>
        void OnErrorAux(int error, string message, CVNetUDPSocket connection)
        {
            // Adds a label to identify that the exception happened in the auxiliary connection:
            onError?.Invoke(error, "[UDPConnection.OnErrorAux] " + message, connection);
        }
        ///<summary>Event onClose for auxiliary connection</summary>
        void OnCloseAux(CVNetUDPSocket connection)
        {
            Disconnect();
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onClose?.Invoke(connection);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[UDPConnection.OnCloseAux] " + e.Message, this);
                });
            }
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
                            onError?.Invoke(e.HResult, "[UDPConnection.AddressParser] " + e.Message, this);
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

        /// <summary>UTF8 byte[] conversion to string</summary>
        public string ByteArrayToString(byte[] content)
        {
            return System.Text.Encoding.UTF8.GetString(content);
        }
        /// <summary>UTF8 string conversion to byte[]</summary>
        public byte[] StringToByteArray(string content)
        {
            return System.Text.Encoding.UTF8.GetBytes(content);
        }
        ///<summary>Converts a float in seconds into an int in miliseconds</summary>
        public int SecondsToMiliseconds(float seconds)
        {
            if (seconds < 0f)
                return Timeout.Infinite;
            else
                return (int)System.Math.Floor(1000f * seconds);
        }
        /// <summary>Writes to the console depending on the platform.</summary>
        void WriteLine(string line, bool error = false)
        {
            System.Console.WriteLine(line);
        }

    }
}
