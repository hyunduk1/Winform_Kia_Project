using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

/*
 * Dialog format ID003 (JCM Global - Taiko):
 * -----------------------------------------
 * [0]0xFC [1]LENGTH [2]CMD+DATA[N] [x][y]CRC16
 * LENGTH: Full byte count (from [0] to [y]).
 * CRC16: Calculation includes all excepting [x] & [y].
 */

/* Events templates (Copy those into your code, then assign to the events of this object):
 
    void OnID003Open(RS232_ID003 connection)
    {}
    void OnID003Message(byte[] message, RS232_ID003 connection)
    {}
    void OnID003Error(int code, string message, RS232_ID003 connection)
    {}
    void OnID003Close(RS232_ID003 connection)
    {}
 */

public class RS232_ID003
{
    public SerialPort _client;                                                                  // Serial port connection.
    volatile Timer _receiveThread;                                                              // Incoming messages listening thread.
    volatile Timer _timeoutTimer;                                                               // Connection timeout timer.
    volatile byte[] _lastRx;                                                                    // The last incomplete message.
    volatile List<byte[]> _rxBuffer = new List<byte[]>();                                       // The incoming data is stored if no OnMessageReceived event is defined.
    readonly object _rxBufferLock = new object();                                               // Lock the incoming buffer access.
    // Local connection settings:
    public string _port;                                                                        // Port name or address (depends on the OS).
    public int _baudRate;
    public float _latency;                                                                      // Latency time in seconds.
    public int _dataBits;
    public Parity _parity;
    public StopBits _stopBits;
    public Handshake _handshake;
    // Events:
    public delegate void EventMessage(byte[] data, RS232_ID003 connection);                     // Event delegate with a byte array.
    public delegate void EventException(int code, string message, RS232_ID003 connection);      // Event delegate with int and string as arguments.
    public delegate void EventVoid(RS232_ID003 connection);                                     // Event delegate for generic events.
    public volatile EventVoid onOpen;                                                           // The connection was established properly (in COM it means "no error" since there is no connection).
    public volatile EventMessage onMessage;                                                     // New data has been received.
    public volatile EventException onError;                                                     // There was an error (the argument contains the error description).
    public volatile EventVoid onClose;                                                          // The connection was closed unexpectedly (in COM is useless).
    // Status:
    volatile bool _setup = false;                                                               // Flag: Remembers if Setup() was already called.
    internal bool _disconnecting = false;                                                       // Flag: Disconnection requested by the user/server.
    volatile float _timeout;                                                                    // Timeout time in seconds until retrying the connection (Check again if requested port exists).
    readonly object _clientLock = new object();                                                 // Lock the send method to avoid concurrency.
    // Device specific auxiliary status:
    Timer _pingTimer;                                                                           // Timer that sends the ping/poll message to the device.
    public volatile bool _deviceReady = false;                                                  // Flag to know when the device is active (enabled or disabled).
    public volatile int _deviceActivity = 0;                                                    // Device activity counter.
    public volatile State _state;                                                               // Current state of the device.
    volatile int _setupProcess = 0;                                                             // Sequence control of the setup process.

    ///<summary>Device control commands</summary>
    public struct Codes
    {
        public static readonly byte[] _poll =           { 0x11 };
        public static readonly byte[] _ack =            { 0x50 };
        // Device control:
        public static readonly byte[] _reset =          { 0x40 };
        public static readonly byte[] _stack1 =         { 0x41 };
        public static readonly byte[] _stack2 =         { 0x42 };
        public static readonly byte[] _return =         { 0x43 };
        public static readonly byte[] _hold =           { 0x44 };
        public static readonly byte[] _wait =           { 0x45 };
        // Setting command:
        public static readonly byte[] _enable =         { 0xC0, 0x00, 0x00 };   // Enable all banknotes.
        public static readonly byte[] _disable =        { 0xC0, 0xFF, 0xFF };   // Disable all banknotes.
        public static readonly byte[] _securityON =     { 0xC1, 0xFF, 0xFF };   // Enable high security read.
        public static readonly byte[] _securityOFF =    { 0xC1, 0x00, 0x00 };   // Disable high security read.
        public static readonly byte[] _commMode =       { 0xC2, 0x00 };         // Polling mode.
        public static readonly byte[] _inhibitEna =     { 0xC3, 0x00 };         // Allow all.
        public static readonly byte[] _inhibitDis =     { 0xC3, 0xFF };         // Prevent all.
        public static readonly byte[] _directionOn =    { 0xC4, 0x00 };         // Not inhibit.
        public static readonly byte[] _directionOff =   { 0xC4, 0x0F };         // Inhibit.
        public static readonly byte[] _optionFunc =     { 0xC5, 0x00 };         // All disabled (?)
        // Setting status request:
        public static readonly byte[] _ena_dis_req =    { 0x80 };
        public static readonly byte[] _security_req =   { 0x81 };
        public static readonly byte[] _commMode_req =   { 0x82 };
        public static readonly byte[] _inhibit_req =    { 0x83 };
        public static readonly byte[] _direction_req =  { 0x84 };
        public static readonly byte[] _optionFunc_req = { 0x85 };

        public static readonly byte[] _version =        { 0x88 };
        public static readonly byte[] _bootVersion =    { 0x89 };
        public static readonly byte[] _currency =       { 0x8A };
    }

    ///<summary>Reply commands from device</summary>
    public enum State
    {
        _enabled = 0x11,
        _accepting = 0x12,
        _escrow = 0x13,             // +data
        _stacking = 0x14,
        _ventValid = 0x15,
        _stacked = 0x16,
        _rejecting = 0x17,          // +data
        _returning = 0x18,
        _holding = 0x19,
        _disabled = 0x1A,
        _initialize = 0x1B,
        // Power up:
        _powerUp = 0x40,
        _powerUpWithBillInAcceptor = 0x41,
        _powerUpWithBillInStacker = 0x42,
        // Error:
        _stackerFull = 0x43,
        _stacketOpen = 0x44,
        _jamInAcceptor = 0x45,
        _jamInStacker = 0x46,
        _pause = 0x47,
        _cheated = 0x48,
        _failure = 0x49,            // +data
        _commError = 0x4A,
        
        // Poll request:
        _enq = 0x05,
        // Operation:
        _ack = 0x50,
        _invalidCmd = 0x48,
        // Setting:
        _ena_dis = 0xC0,            // +data
        _security = 0xC1,           // +data
        _commMode = 0xC2,           // +data
        _inhibit = 0xC3,            // +data
        _direction = 0xC4,          // +data
        _optionalFunc = 0xC5,       // +data
        // Setting status:
        _ena_dis_req = 0x80,        // +data
        _security_req = 0x81,       // +data
        _commMode_req = 0x82,       // +data
        _inhibit_req = 0x83,        // +data
        _direction_req = 0x84,      // +data
        _optionalFunc_req = 0x85,   // +data
        _versionInfo = 0x88,        // +data
        _bootVersion = 0x89,        // +data
        _denominationData = 0x8A    // +data
    }

    ///<summary>Constructor</summary>
    public RS232_ID003() { }
    ///<summary>Constructor</summary>
    public RS232_ID003(string port, int baudrate, EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null, float latency = 0.1f, Parity parity = Parity.Even, int dataBits = 8, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None)
    {
        Setup(port, baudrate, evOpen, evMessage, evError, evClose, latency, parity, dataBits, stopBits, handshake);
    }
    ///<summary>Destructor</summary>
    ~RS232_ID003()
    {
        Dispose();
    }

    ///<summary>SetsUp the RS232 connection properties</summary>
    public void Setup(string port, int baudrate, EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null, float latency = 0.1f, Parity parity = Parity.Even, int dataBits = 8, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None)
    {
        Dispose();
        lock (_clientLock)
        {
            if (!IsValidPort(port))
                WriteLine("[RS232_ID003] The requested port (" + port + ") is not available. Connection will be retried until success.", true);
            // Local port parameters:
            _port = port;
            _baudRate = baudrate;
            _latency = latency;
            _parity = parity;
            _dataBits = dataBits;
            _stopBits = stopBits;
            _handshake = handshake;
            // Events:
            onOpen = evOpen;
            onMessage = evMessage;
            onError = evError;
            onClose = evClose;
            // Threads:
            _receiveThread = new Timer(ReceiveData);
            _timeoutTimer = new Timer(ConnectionTimedOut, null, Timeout.Infinite, Timeout.Infinite);
            _pingTimer = new Timer(SendPing);
            _setup = true;
        }
    }
    ///<summary>Try to open the requested connection</summary>
    public void Connect(float timeout = 5f)
    {
        lock (_clientLock)
        {
            if (_setup && _client == null)
            {
                _timeout = timeout;
                _timeoutTimer.Change(_timeout > 0f ? SecondsToMiliseconds(_timeout) : Timeout.Infinite, Timeout.Infinite);
                // If the port is not available, just keep trying:
                if (IsValidPort(_port))
                {
                    _disconnecting = false;
                    try
                    {
                        // Open the serial port:
                        _client = new SerialPort();
                        _client.PortName = _port;
                        _client.BaudRate = _baudRate;
                        _client.Parity = _parity;
                        _client.DataBits = _dataBits;
                        _client.StopBits = _stopBits;
                        _client.ReadBufferSize = _client.WriteBufferSize = 1024 * 64;
                        _client.Handshake = _handshake;
                        _client.Open();
                        // Set related timers:
                        _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        _receiveThread.Change(SecondsToMiliseconds(_latency), SecondsToMiliseconds(_latency));
                        // Start device setup process:
                        _deviceReady = false;
                        SendData(Codes._poll);
                        _pingTimer.Change(300, 300);
                    }
                    catch (System.Exception e)
                    {
                        ThreadPool.QueueUserWorkItem((object s) => { onError?.Invoke(e.HResult, "[RS232_ID003.Connect] " + e.Message, this); });
                    }
                }
            }
            else
            {
                WriteLine("[RS232_ID003.Connect] You should call Setup() before attempt to Connect().");
            }
        }
    }
    ///<summary>Close the connection correctly</summary>
    public void Disconnect()
    {
        lock (_clientLock)
        {
            Disable();
            _deviceReady = false;
            _disconnecting = true;
            // Reset the connection to initial state:
            if (_receiveThread != null)
                _receiveThread.Change(Timeout.Infinite, Timeout.Infinite);
            if (_timeoutTimer != null)
                _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (_pingTimer != null)
                _pingTimer.Change(Timeout.Infinite, Timeout.Infinite);
            // Close client:
            if (_client != null)
            {
                _client.Close();
                _client.Dispose();
                _client = null;
            }
            _lastRx = new byte[] { };
        }
    }
    ///<summary>Close the connection correctly without firing events</summary>
    public void Dispose()
    {
        Disconnect();
        lock (_clientLock)
        {
            // Dispose Timers:
            if (_receiveThread != null)
            {
                _receiveThread.Dispose();
                _receiveThread = null;
            }
            if (_timeoutTimer != null)
            {
                _timeoutTimer.Dispose();
                _timeoutTimer = null;
            }
            if (_pingTimer != null)
            {
                _pingTimer.Dispose();
                _pingTimer = null;
            }
            // Clear events:
            onOpen = null;
            onMessage = null;
            onError = null;
            onClose = null;
            // Clear setup:
            _lastRx = new byte[] { };
            _setup = false;
        }
    }

    ///<summary>Connection timeout event</summary>
    void ConnectionTimedOut(object state)
    {
        // Retry the connection
        Connect(_timeout);
    }

    ///<summary>Listening thread, gets the incoming datagrams and stores into a buffer</summary>
    void ReceiveData(object state)
    {
        try
        {
            if (_client.BytesToRead > 0)
            {
                _deviceActivity = 5;
                // Reads received data:
                byte[] data = new byte[_client.BytesToRead];
                _client.Read(data, 0, data.Length);
                byte[] rx = new byte[_lastRx.Length + data.Length];
                System.Buffer.BlockCopy(_lastRx, 0, rx, 0, _lastRx.Length);
                System.Buffer.BlockCopy(data, 0, rx, _lastRx.Length, data.Length);
                // ID003 data structure: [0]0xFC [1]LENGTH [2]CMD+DATA[N] [x][y]CRC16
                int lastEof = -1;
                for (int index = 0; index < rx.Length; index++)
                {
                    // Check potential new message:
                    if (rx[index] == 0xFC && rx.Length > index + 2)
                    {
                        int cmdLen = rx[index + 1];        // Obtain command length.
                        if (index + cmdLen <= rx.Length)
                        {
                            // Complete package received:
                            byte[] package = new byte[cmdLen];
                            System.Buffer.BlockCopy(rx, index, package, 0, package.Length);
                            lastEof = index + cmdLen - 1;
                            if(CheckCRC16(package))
                            {
                                // Filter the package extracting the data only:
                                byte[] message = new byte[cmdLen - 4];
                                System.Buffer.BlockCopy(package, 2, message, 0, message.Length);
                                // Save the device state:
                                _state = (State)message[0];
                                switch (_state)
                                {
                                    case State._powerUp:
                                    case State._powerUpWithBillInAcceptor:
                                    case State._powerUpWithBillInStacker:
                                        Reset();
                                        return;
                                    case State._initialize:
                                    case State._ena_dis:
                                    case State._security:
                                    case State._inhibit:
                                        switch (_setupProcess)
                                        {
                                            case 0:
                                                _setupProcess = 1;
                                                SendData(Codes._disable);
                                                return;
                                            case 1:
                                                _setupProcess = 2;
                                                SendData(Codes._securityON);
                                                return;
                                            case 2:
                                                _setupProcess = 3;
                                                SendData(Codes._inhibitEna);
                                                return;
                                            case 3:
                                                _setupProcess = 4;
                                                _deviceReady = true;
                                                SafeOnOpen();
                                                return;
                                        }
                                        break;
                                    case State._enabled:
                                    case State._disabled:
                                        if (!_deviceReady)
                                        {
                                            Reset();
                                            return;
                                        }
                                        break;
                                }
                                // Dispatch received message:
                                if (_deviceReady)
                                {
                                    // Save the received message or call the onMessage event:
                                    if (onMessage != null)
                                    {
                                        SafeOnMessage(message);
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
                            else
                            {
                                ThreadPool.QueueUserWorkItem((object s) => { onError?.Invoke(1, "[RS232_SSP.ReceiveData] Error CRC16: " + System.BitConverter.ToString(rx), this); });
                            }
                        }
                    }
                }
                // Save last chunk of data:
                lastEof++;
                _lastRx = new byte[rx.Length - lastEof];
                System.Buffer.BlockCopy(rx, lastEof, _lastRx, 0, _lastRx.Length);
            }
        }
        catch (System.Exception e)
        {
            if (!_disconnecting && e.HResult == -2146232800)
            {
                // The connection was lost (port became unavailable):
                _pingTimer.Change(Timeout.Infinite, Timeout.Infinite);
                ThreadPool.QueueUserWorkItem((object s) =>
                {
                    SafeOnClose();
                });
                Disconnect();
            }
            else
            {
                // All other possible errors:
                ThreadPool.QueueUserWorkItem((object s) => { onError?.Invoke(e.HResult, "[RS232_SSP.ReceiveData] " + e.Message, this); });
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

    ///<summary>Sends a byte array</summary>
    public void SendData(byte[] data)
    {
        if (IsConnected())
        {
            lock (_clientLock)
            {
                try
                {
                    byte[] msg = new byte[data.Length + 2];
                    msg[0] = 0xFC;                          // [0]Header
                    msg[1] = (byte)(msg.Length + 2);        // [1]LENGTH
                    System.Buffer.BlockCopy(data, 0, msg, 2, data.Length);
                    msg = AddCRC16(msg);
                    _client.Write(msg, 0, msg.Length);
                    Thread.Sleep(20);                       // Delay to allow the device to react.
                }
                catch (System.Exception e)
                {
                    ThreadPool.QueueUserWorkItem((object o) => { onError?.Invoke(e.HResult, "[RS232_ID003.SendData] " + e.Message, this); });
                }
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
        }
        catch (System.Exception e)
        {
            ThreadPool.QueueUserWorkItem((object s) => {
                onError?.Invoke(e.HResult, "[RS232_ID003.SafeOnOpen] " + e.Message, this);
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
                onError?.Invoke(e.HResult, "[RS232_ID003.SafeOnMessage] " + e.Message + " - Received: " + (ByteArrayToString(message)), this);
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
                onError?.Invoke(e.HResult, "[RS232_ID003.SafeOnClose] " + e.Message, this);
            });
        }
    }

    ///<summary>TRUE if connected and listening</summary>
    public bool IsConnected()
    {
        return (_client != null && _client.IsOpen && _receiveThread != null);
    }

    ///<summary>Checks if the provided port is valid</summary>
    public bool IsValidPort(string port)
    {
        string[] ports = GetAvailablePorts();
        for (int i = 0; i < ports.Length; i++)
        {
            if (port == ports[i])
                return true;
        }
        return false;
    }

    ///<summary>Get the list of system available ports</summary>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
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
#if UNITY_5_3_OR_NEWER
        if (error)
            UnityEngine.Debug.LogError(line);
        else
            UnityEngine.Debug.Log(line);
#else
        System.Console.WriteLine(line);
#endif
    }

    /*******************************
     * BillAcceptor device methods *
     *******************************/

    /// <summary>CRC16 Cashcode (All bytes are included in calculation).</summary>
    byte[] GetCRC16(byte[] bufData)
    {
        // Polynomials  x16+x12+x5+1    //[0--- -5-- ---- 12---]16
        ushort polynomial = 0x8408;     // 1000 0100 0000  1000
        ushort crc = 0;                 // Seed
        for (int i = 0; i < bufData.Length; i++)
        {
            crc ^= bufData[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) > 0)
                {
                    crc >>= 1;
                    crc ^= polynomial;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        return System.BitConverter.GetBytes(crc);
    }
    /// <summary>Validate CRC16 for a received command.</summary>
    bool CheckCRC16(byte[] cmd)
    {
        // The 2 last bytes should be the CRC16:
        byte[] data = new byte[cmd.Length - 2];
        System.Buffer.BlockCopy(cmd, 0, data, 0, data.Length);
        byte[] crc = GetCRC16(data);
        return crc[0] == cmd[cmd.Length - 2] && crc[1] == cmd[cmd.Length - 1];
    }
    /// <summary>Add CRC16 to a data structure.</summary>
    byte[] AddCRC16(byte[] cmd)
    {
        byte[] crc = GetCRC16(cmd);
        byte[] data = new byte[cmd.Length + crc.Length];
        System.Buffer.BlockCopy(cmd, 0, data, 0, cmd.Length);
        System.Buffer.BlockCopy(crc, 0, data, cmd.Length, crc.Length);
        return data;
    }

    /// <summary>Reset device.</summary>
    public void Reset()
    {
        // Reset/connect device:
        _deviceReady = false;
        _setupProcess = 0;
        SendData(Codes._reset);
    }
    /// <summary>Enable the device to accept bills.</summary>
    public void Enable()
    {
        if(_deviceReady)
            SendData(Codes._enable);
    }
    /// <summary>Disable the device without disposing.</summary>
    public void Disable()
    {
        if(_deviceReady)
            SendData(Codes._disable);
    }
    /// <summary>Sends the PING/POLL message</summary>
    void SendPing(object state)
    {
        SendData(Codes._poll);
        if(_deviceReady)
        {
            // Device inactivity detection:
            _deviceActivity--;
            if (_deviceActivity <= 0)
            {
                _deviceActivity = 0;
                _deviceReady = false;
                _pingTimer.Change(Timeout.Infinite, Timeout.Infinite);
                Disconnect();
                SafeOnClose();
            }
        }
    }
}