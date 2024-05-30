using System.Collections.Generic;
// Communications:
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;

namespace VNet
{
    class CVNetTCPSocket
    {
        // Main objects:
        public volatile Socket _client;                                                             // Connection object.
        volatile Thread _receiveThread;                                                             // Incoming messages listening thread.
        volatile List<byte[]> _rxBuffer = new List<byte[]>();                                       // The incoming datagrams are stored if no OnMessageReceived event is defined.
        readonly object _rxBufferLock = new object();                                               // Lock the incoming buffer access.
                                                                                                    // Parameters:
        volatile string _localIP = "";                                                              // The requested local IP (optional, empty means default).
        volatile IPEndPoint _localIPEndpoint;                                                       // The Endpoint to bind to.
        volatile IPEndPoint _remoteIPEndpoint;                                                      // The Endpoint to listen to.
        volatile byte[] _eof = { (byte)'\n' };                                                      // Sequence determining the end of a received message ('\n' by default).
        byte[] _buffer = { };                                                                       // Buffer to recompose segmented messages.
                                                                                                    // Events:
        public delegate void EventMessage(byte[] data, CVNetTCPSocket connection);                  // Event delegate with a byte array as arguments.
        public delegate void EventException(int code, string message, CVNetTCPSocket connection);   // Event delegate with int and string as arguments.
        public delegate void EventVoid(CVNetTCPSocket connection);                                  // Event delegate for generic events.
        public volatile EventVoid onOpen;                                                           // The connection was established properly (in UDP it means "no error" since there is no connection).
        public volatile EventMessage onMessage;                                                     // A datagram has been received.
        public volatile EventException onError;                                                     // There was an error (the argument contains the error description).
        public volatile EventVoid onClose;                                                          // The connection was closed.
        volatile EventVoid onDeleteMe;                                                              // The connection was closed (used by TCPServer).
                                                                                                    // Status:
        volatile bool _setup = false;
        volatile Timer _timeoutTimer;                                                               // Connection timeout timer.
        volatile float _timeout;                                                                    // Timeout time in seconds until automatically firing onClose event.
        volatile bool _disconnecting;                                                               // Flag: Disconnection requested by the user/server.
        readonly object _clientLock = new object();                                                 // Lock the send method to avoid concurrency.
        volatile Timer _keepAliveTimer;                                                             // Timer to keep the connection alive (Mono).
        volatile float _keepAliveTimeout;                                                           // Time interval in seconds to send some "keep alive" message.
        volatile int _activityCnt;                                                                  // Activity counter to track if several consecutive PING/PONG fails.
        volatile int _activity = 3;                                                                 // How many times the PING/PONG should fail to validate inactivity?
        volatile bool _disableWatchdog = false;                                                     // Flag: Disables the _activityCnt flag to be false (and prevents Disconnect).
        public volatile bool _fragmentation = false;                                                // Flag: Fragmentation was detected at least once.
        public volatile int _mtu = -1;                                                              // Biggest message size during fragmentation.

        ///<summary>Constructor</summary>
        public CVNetTCPSocket() { }
        ///<summary>Constructor</summary>
        public CVNetTCPSocket(string localIP = "", EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null, byte[] eof = null)
        {
            Setup(localIP, evOpen, evMessage, evError, evClose, eof);
        }
        ///<summary>(Reserver for WSServer use) Constructor</summary>
        public CVNetTCPSocket(Socket client, EventVoid deleteMeCallback, float keepAliveTimeout)
        {
            _client = client;
            _client.NoDelay = true;
            _client.DontFragment = true;
            _localIPEndpoint = (IPEndPoint)client.LocalEndPoint;
            _remoteIPEndpoint = (IPEndPoint)client.RemoteEndPoint;
            onDeleteMe = deleteMeCallback;
            // Set the "Keep alive" timer to probe connection activity:
            _keepAliveTimeout = keepAliveTimeout;
            _keepAliveTimer = new Timer(KeepAliveTimedOut, null, _keepAliveTimeout > 0f ? SecondsToMiliseconds(_keepAliveTimeout) : Timeout.Infinite, Timeout.Infinite);
            _activityCnt = _activity;
        }
        ///<summary>Destructor</summary>
        ~CVNetTCPSocket()
        {
            Dispose();
        }



        ///<summary>(Reserver for WSServer use) Starts listening and calling OnMessage event</summary>
        public void StartListening()
        {
            if (_receiveThread != null)
            {
                _receiveThread.Abort();
                _receiveThread = null;
            }
            _receiveThread = new Thread(new ThreadStart(ReceiveData)) { IsBackground = true };
            _receiveThread.Start();
        }

        ///<summary>Sets the TCP client to a specific local IP and port</summary>
        public void Setup(string localIP = "", EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null, byte[] eof = null)
        {
            Dispose();
            lock (_clientLock)
            {
                _disconnecting = false;
                _timeoutTimer = new Timer(ConnectionTimedOut, null, Timeout.Infinite, Timeout.Infinite);
                _keepAliveTimer = new Timer(KeepAliveTimedOut, null, Timeout.Infinite, Timeout.Infinite);
                _activityCnt = 0;
                if (eof != null)
                    SetEOF(eof);
                // Events:
                onOpen = evOpen;
                onMessage = evMessage;
                onError = evError;
                onClose = evClose;
                try
                {
                    // Local address binding:
                    _localIP = localIP.Replace(" ", string.Empty).ToLower();
                    if (!string.IsNullOrEmpty(_localIP))
                    {
                        if (_localIP == "ipv4" || _localIP == "ipv6")
                            _localIPEndpoint = AddressParser(GetDefaultIPAddress(_localIP));            // Gets the defaut IP of the requested type.
                        else
                            _localIPEndpoint = AddressParser(_localIP);                                 // Parses the provided IP.
                    }
                    // Setup is done:
                    _setup = true;
                }
                catch (System.Exception e)
                {
                    ThreadPool.QueueUserWorkItem((object s) => {
                        onError?.Invoke(e.HResult, "[CVnetTCPSocket.Setup] " + e.Message, this);
                    });
                }
            }
        }
        ///<summary>Sets a new EOF sequence, enabling stream emulation</summary>
        public void SetEOF(byte[] eof)
        {
            if (eof != null)
                _eof = eof;
            else
                _eof = new byte[] { };
        }
        ///<summary>Sets a new EOF sequence, enabling stream emulation</summary>
        public void SetEOF(string eof)
        {
            SetEOF(StringToByteArray(eof));
        }
        ///<summary>Sets an empty EOF sequence, disabling stream emulation</summary>
        public void ClearEOF()
        {
            _eof = new byte[] { };
        }
        ///<summary>Set the connection (to send and receive)</summary>
        void Connect(IPEndPoint remoteIPEndpoint, float timeout = 5f, float keepAliveTimeout = 15f, bool disableWatchdog = false)
        {
            lock (_clientLock)
            {
                if (_setup && _client == null)
                {
                    _disconnecting = false;
                    _remoteIPEndpoint = remoteIPEndpoint;
                    if (_localIPEndpoint != null)
                    {
                        // Create and bind the TCP socket:
                        _client = new Socket(_localIPEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        LingerOption lo = new LingerOption(true, 0);
                        _client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, lo);
                        _client.NoDelay = true;
                        _client.DontFragment = true;
                        _client.Bind(_localIPEndpoint);
                    }
                    else
                    {
                        // Create the TCP socket based on remote IP endpoint:
                        _client = new Socket(_remoteIPEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        LingerOption lo = new LingerOption(true, 0);
                        _client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, lo);
                        _client.NoDelay = true;
                        _client.DontFragment = true;
                    }
                    // Start/stop the connection timeout timer:
                    _timeout = timeout;
                    _timeoutTimer.Change(_timeout > 0f ? SecondsToMiliseconds(_timeout) : Timeout.Infinite, Timeout.Infinite);
                    _keepAliveTimeout = keepAliveTimeout;
                    _disableWatchdog = disableWatchdog;
                    _activityCnt = 0;
                    // Try the connection:
                    ThreadPool.QueueUserWorkItem(Connecting);
                }
                else
                {
                    WriteLine("[CVnetTCPSocket] You should call Setup() before attempt to connect.", true);
                }
            }
        }
        ///<summary>Set the connection (to send and receive)</summary>
        public void Connect(int port, string remoteIP, float timeout = 5f, float keepAliveTimeout = 15f, bool disableWatchdog = false)
        {
            try
            {
                _disconnecting = false;
                // Calculate the remote endpoint:
                if (IsValidPort(port))
                    Connect(AddressParser(remoteIP, port), timeout, keepAliveTimeout, disableWatchdog);
                else
                    WriteLine("[CVnetTCPSocket.Connect] You should provide a valid port (1 to 65535).", true);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[CVnetTCPSocket.Connect] " + e.Message, this);
                });
            }
        }
        ///<summary>Close the connection correctly</summary>
        public void Disconnect()
        {
            lock (_clientLock)
            {
                _disconnecting = true;
                System.Array.Resize(ref _buffer, 0);
                // Stop timeout timer:
                if (_timeoutTimer != null)
                    _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                // Stop "keep alive" timer:
                if (_keepAliveTimer != null)
                    _keepAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _activityCnt = 0;
                // Close client:
                if (_client != null)
                {
                    try
                    {
                        if (_client.Connected)
                            _client.Shutdown(SocketShutdown.Both);
                    }
                    catch { }
                    _client.Close();
                    _client = null;
                }
                // Close listener:
                try
                {
                    Thread aux = _receiveThread;
                    _receiveThread = null;

                    //여기 수정------
                    //    if (aux.IsAlive)
                    //        aux.Abort();
                    //기존 이거에서 아래처럼 예외처리 if 문 추가
                    if (aux != null)
                    {
                        if (aux.IsAlive)
                            aux.Abort();
                    }
                    //--------------------
                }
                catch { }
            }
        }
        ///<summary>Close the connection correctly without firing events</summary>
        public void Dispose()
        {
            Disconnect();
            lock (_clientLock)
            {
                // Clear events:
                onOpen = null;
                onMessage = null;
                onError = null;
                onClose = null;
                onDeleteMe?.Invoke(this);
                onDeleteMe = null;
                // Delete timers:
                if (_timeoutTimer != null)
                {
                    _timeoutTimer.Dispose();
                    _timeoutTimer = null;
                }
                if (_keepAliveTimer != null)
                {
                    _keepAliveTimer.Dispose();
                    _keepAliveTimer = null;
                }
                // Clear setup:
                _localIPEndpoint = null;
                _remoteIPEndpoint = null;
                _setup = false;
            }
        }

        ///<summary>Tries to connect and starts the listener</summary>
        void Connecting(object state)
        {
            if (_client != null && !_disconnecting)                     // Bugfix for weird "null object" when retrying connection.
            {
                try
                {
                    _client.Connect(_remoteIPEndpoint);                 // The execution blocks until the connection is established or fails.
                    if (_client.Connected)
                    {
                        lock (_clientLock)
                        {
                            // Connection was established:
                            _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                            // Fire onOpen event:
                            ThreadPool.QueueUserWorkItem((object s) => { SafeOnOpen(); });
                            // Force a message to be sent periodically in order to keep the connection alive:
                            _keepAliveTimer.Change(_keepAliveTimeout > 0f ? SecondsToMiliseconds(_keepAliveTimeout) : Timeout.Infinite, Timeout.Infinite);
                            _activityCnt = _activity;
                        }
                    }
                }
                catch (SocketException e)
                {
                    // These errors are also close events (Let the timeout retry the connection):
                    if (e.ErrorCode != 10065 && e.ErrorCode != 10061 && e.ErrorCode != 10055 && e.ErrorCode != 10038 && e.ErrorCode != 10004)
                        ThreadPool.QueueUserWorkItem((object s) => {
                            onError?.Invoke(e.ErrorCode, "[CVnetTCPSocket.Connecting] " + e.Message, this);
                        });
                }
                catch { }   // Eventual exception, do nothing, let the _timeoutTimer Connect() again. 
            }
        }
        ///<summary>Connection timeout event</summary>
        void ConnectionTimedOut(object state)
        {
            // Reset the connection
            Disconnect();
            Connect(_remoteIPEndpoint, _timeout, _keepAliveTimeout, _disableWatchdog);
        }
        ///<summary>Keep alive connection timeout event</summary>
        void KeepAliveTimedOut(object state)
        {
            if (onDeleteMe == null)
            {
                if (_activityCnt > 0)
                {
                    // Client side keeps sending ping messages:
                    _keepAliveTimer.Change(_keepAliveTimeout > 0f ? SecondsToMiliseconds(_keepAliveTimeout) : Timeout.Infinite, Timeout.Infinite);
                    if (!_disableWatchdog)
                        _activityCnt--;
                    SendData(new byte[] { });               // Custom client PING message.
                }
                else
                {
                    // Connection closed unexpectedly, allow to connect again:
                    ThreadPool.QueueUserWorkItem((object s) =>
                    {
                        Disconnect();
                        SafeOnClose();
                    });
                }
            }
            else
            {
                // Server side checks the activity:
                if (_activityCnt > 0)
                {
                    // The client is active, reset the timer:
                    _keepAliveTimer.Change(_keepAliveTimeout > 0f ? SecondsToMiliseconds(_keepAliveTimeout) : Timeout.Infinite, Timeout.Infinite);
                    _activityCnt = 0;
                }
                else
                {
                    // This client belongs to a server and must be disposed:
                    ThreadPool.QueueUserWorkItem((object s) =>
                    {
                        SafeOnClose();
                        Dispose();
                    });
                }
            }
        }
        ///<summary>Listening thread, gets the incoming datagrams and stores into a buffer</summary>
        void ReceiveData()
        {
            while (IsConnected())
            {
                try
                {
                    // Reads received data from server:
                    byte[] buffer = new byte[_client.ReceiveBufferSize];
                    int rxCount = IsConnected() ? _client.Receive(buffer) : 0;
                    // Save the received data or call the onMessage event:
                    if (rxCount > 0)
                    {
                        lock (_rxBufferLock)
                        {
                            _activityCnt = _activity;
                            if (onDeleteMe != null)                  // Reset activity timer on server side.
                                _keepAliveTimer.Change(_keepAliveTimeout > 0f ? SecondsToMiliseconds(_keepAliveTimeout) : Timeout.Infinite, Timeout.Infinite);
                            if (_eof.Length > 0)
                            {
                                // Streamed incoming message:
                                int offset = _buffer.Length;
                                System.Array.Resize(ref _buffer, _buffer.Length + rxCount);
                                System.Buffer.BlockCopy(buffer, 0, _buffer, offset, rxCount);
                                // Cut buffer on each _eof occurrence:
                                int index = -1;         // Index of the first byte of eof.
                                int next = -1;          // Index of the next possible command.
                                do
                                {
                                    index = IndexOfEOF(_buffer, _eof);
                                    if (index >= 0)
                                    {
                                        // Complete message detected:
                                        byte[] message = new byte[index + 1];
                                        System.Buffer.BlockCopy(_buffer, 0, message, 0, message.Length);
                                        next = index + _eof.Length;
                                        if (message.Length > 0)
                                        {
                                            // Fire onMessage event or save to filtered buffer:
                                            if (onMessage != null)
                                            {
                                                byte[] aux = new byte[message.Length];
                                                message.CopyTo(aux, 0);
                                                ThreadPool.QueueUserWorkItem((object s) => { SafeOnMessage(aux); });
                                            }
                                            else
                                            {
                                                lock (_rxBufferLock)
                                                {
                                                    _rxBuffer.Add(message);             // Add received message to buffer (if no event defined).
                                                }
                                            }
                                        }
                                        else if (onDeleteMe != null)
                                        {
                                            SendData(new byte[] { });                   // Custom server PONG message.
                                        }
                                        // Save remaining data:
                                        int size = _buffer.Length - next;
                                        message = new byte[size];
                                        System.Buffer.BlockCopy(_buffer, next, message, 0, size);
                                        _buffer = message;
                                    }
                                } while (index > 0);
                                // Fragmentation detection:
                                if (_buffer.Length > 0)
                                {
                                    _fragmentation = true;
                                    if (rxCount > _mtu)
                                        _mtu = rxCount;
                                }
                            }
                            else
                            {
                                // Full message received (The fragmentation detection is not possible here):
                                byte[] message = new byte[rxCount];
                                System.Buffer.BlockCopy(buffer, 0, message, 0, message.Length);
                                // Fire onMessage event or save to filtered buffer:
                                if (onMessage != null)
                                {
                                    ThreadPool.QueueUserWorkItem((object s) => { SafeOnMessage(message); });
                                }
                                else
                                {
                                    lock (_rxBufferLock)
                                    {
                                        _rxBuffer.Add(message);             // Add received message to buffer (if no event defined).
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // The connection was lost:
                        if (!_disconnecting)
                        {
                            _disconnecting = true;                          // So important to stop all parallel calls.
                            if (onDeleteMe == null)
                            {
                                // Connection closed unexpectedly, allow to connect again:
                                ThreadPool.QueueUserWorkItem((object s) => {
                                    SafeOnClose();
                                });
                            }
                            else
                            {
                                // This client belongs to a server and must be disposed:
                                ThreadPool.QueueUserWorkItem((object s) => {
                                    SafeOnClose();
                                    Dispose();
                                });
                            }
                            Disconnect();
                        }
                    }
                }
                catch (SocketException e)
                {
                    // Error codes 10054, 10060, 10065 & 26542 are also disconnection events:
                    if (e.ErrorCode == 10053 || e.ErrorCode == 10054 || e.ErrorCode == 10060 || e.ErrorCode == 10065 || e.ErrorCode == 26542)
                    {
                        _disconnecting = true;                          // So important to stop all parallel calls.
                        if (onDeleteMe == null)
                        {
                            ThreadPool.QueueUserWorkItem((object s) => {
                                Disconnect();
                                SafeOnClose();
                            });
                        }
                        else
                        {
                            // This client belongs to a server and must be disposed:
                            ThreadPool.QueueUserWorkItem((object s) => {
                                SafeOnClose();
                                Dispose();
                            });
                        }
                    }
                    else if (!_disconnecting)
                    {
                        Disconnect();
                        // Unknown error, must be seen:
                        ThreadPool.QueueUserWorkItem((object s) => {
                            onError?.Invoke(e.ErrorCode, "[CVnetTCPSocket.ReceiveData] " + e.Message, this);
                            SafeOnClose();
                        });
                    }
                }
                catch
                {
                    if (!_disconnecting && onDeleteMe == null)
                    {
                        _disconnecting = true;                          // So important to stop all parallel calls.
                        ThreadPool.QueueUserWorkItem((object s) => {
                            Disconnect();
                            SafeOnClose();
                        });
                    }
                }
            }
        }
        ///<summary>Find an array into another array and returns the first index</summary>
        int IndexOfEOF(byte[] buffer, byte[] eof)
        {
            int index = -1;
            for (int i = 0; i <= buffer.Length - eof.Length; i++)
            {
                if (buffer[i] == eof[0])
                {
                    bool match = true;
                    for (int c = 0; c < eof.Length; c++)
                    {
                        if (buffer[i + c] != eof[c])
                            match = false;
                    }
                    if (match)
                    {
                        index = i;
                        break;
                    }
                }
            }
            return index;
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

        ///<summary>Sends a byte array</summary>
        public void SendData(byte[] data)
        {
            if (IsConnected())
            {
                try
                {
                    if (_eof.Length > 0)
                    {
                        if (data.Length == 0)
                        {
                            System.Array.Resize(ref data, _eof.Length);
                            _eof.CopyTo(data, 0);                   // The only way to send an empty message in TCP (ping/pong).
                        }
                        else if (IndexOfEOF(data, _eof) < 0)
                        {
                            byte[] auxArray = new byte[data.Length + _eof.Length];
                            data.CopyTo(auxArray, 0);
                            _eof.CopyTo(auxArray, data.Length);
                            data = auxArray;
                        }
                    }
                    _client.Send(data);
                }
                catch (System.Exception e)
                {
                    ThreadPool.QueueUserWorkItem((object s) => {
                        onError?.Invoke(e.HResult, "[CVnetTCPSocket.SendData] " + e.Message, this);
                    });
                }
            }
        }
        ///<summary>Sends a string</summary>
        public void SendData(string data)
        {
            if (IsConnected())
                SendData(StringToByteArray(data));
        }


        ///<summary>Calls the onOpen event preventing crashes if external code fails</summary>
        void SafeOnOpen()
        {
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onOpen?.Invoke(this);
                StartListening();
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[CVnetTCPSocket.SafeOnOpen] " + e.Message, this);
                });
            }
        }
        ///<summary>Calls the onMessage event preventing crashes if external code fails</summary>
        void SafeOnMessage(byte[] message)
        {
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onMessage?.Invoke(message, this);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[CVnetTCPSocket.SafeOnMessage] " + e.Message + " - Received: " + (ByteArrayToString(message)), this);
                });
            }
        }
        ///<summary>Calls the onClose event preventing crashes if external code fails</summary>
        public void SafeOnClose()
        {
            // This is to detect erros in the code assigned by the user to this event:
            try
            {
                onClose?.Invoke(this);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => {
                    onError?.Invoke(e.HResult, "[CVnetTCPSocket.SafeOnClose] " + e.Message, this);
                });
            }
        }

        ///<summary>Get the local active IP (Empty if no connected)</summary>
        public string GetIP()
        {
            // Returns the primary connection IP:
            if (_localIP == "" || _localIP == "ipv4" || _localIP == "ipv6")
            {
                if (_client != null && _client.AddressFamily == AddressFamily.InterNetwork)
                    return GetDefaultIPAddress("ipv4");
                else if (_client != null && _client.AddressFamily == AddressFamily.InterNetworkV6)
                    return GetDefaultIPAddress("ipv6");
            }
            return _localIP;
        }
        ///<summary>Get the captured application port</summary>
        public string GetPort()
        {
            if (_client != null)
                return (_client.LocalEndPoint as IPEndPoint).Port.ToString();
            return "0";
        }
        ///<summary>TRUE if connected and listening</summary>
        public bool IsConnected()
        {
            return (_client != null && _client.Connected && !_disconnecting);
        }
        ///<summary>Gets the last valid remote IP</summary>
        public string GetRemoteIP()
        {
            if (_remoteIPEndpoint != null)
                return _remoteIPEndpoint.Address.ToString();
            else
                return "";
        }
        ///<summary>Gets the last valid remote Port</summary>
        public string GetRemotePort()
        {
            if (_remoteIPEndpoint != null)
                return _remoteIPEndpoint.Port.ToString();
            else
                return "";
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
                            onError?.Invoke(e.HResult, "[CVnetTCPSocket.AddressParser] " + e.Message, this);
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
            return (int)System.Math.Floor(1000f * seconds);
        }

        void WriteLine(string line, bool error = false)
        {
            System.Console.WriteLine(line);
        }
    }
}
