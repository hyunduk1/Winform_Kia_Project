using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

/*
 * Dialog format SSP:
 * ------------------
 * [0]0x7F [1]SEQ+ID [2]LENGTH [3]CMD+DATA[LENGTH] [x][y]CRC16
 * LENGTH: Byte count of CMD+DATA.
 * CRC16: Calculation includes [1], [2] & CMD+DATA[LENGTH] only.
 */

/* Events templates (Copy those into your code, then assign to the events of this object):
 
    void OnSSPOpen(RS232_ITL connection)
    {}
    void OnSSPMessage(byte[] message, RS232_ITL connection)
    {}
    void OnSSPError(int code, string message, RS232_ITL connection)
    {}
    void OnSSPClose(RS232_ITL connection)
    {}
 */

public class RS232_SSP
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
    public delegate void EventMessage(byte[] data, RS232_SSP connection);                       // Event delegate with a byte array.
    public delegate void EventException(int code, string message, RS232_SSP connection);        // Event delegate with int and string as arguments.
    public delegate void EventVoid(RS232_SSP connection);                                       // Event delegate for generic events.
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
    public volatile byte _address = 0x00;                                                       // Address corresponding a banknote validator.
    volatile bool _sequenceState = false;                                                       // Sequence flag to enable/disable bit 7 in address.
    volatile int _setupProcess = 0;                                                             // Sequence control of the setup process.

    ///<summary>Control commands for billacceptor</summary>
    public struct Codes
    {
        public static readonly byte[] _sync = { 0x11 };
        public static readonly byte[] _enable = { 0x0A };
        public static readonly byte[] _disable = { 0x09 };
        public static readonly byte[] _reset = { 0x01 };
        public static readonly byte[] _poll = { 0x07 };

        public static readonly byte[] _reject = { 0x08 };
        public static readonly byte[] _hold = { 0x18 };

        public static readonly byte[] _protocolVersion = { 0x06, 0x06 };
        public static readonly byte[] _fwVersion = { 0x20 };
        public static readonly byte[] _serialNum = { 0x0C };
        public static readonly byte[] _dsVersion = { 0x21 };

        public static readonly byte[] _setupReq = { 0x05, 0x05 };               // Ask device settings
        public static readonly byte[] _getCode = { 0x30, 0x05, 0x39 };          // For non documented code sections (?).
        public static readonly byte[] _enableHighEvs = { 0x19 };
        public static readonly byte[] _setInhibits = { 0x02, 0xFF, 0xFF };      // Enable all channels.

        public static readonly byte[] _displayOn = { 0x03 };
        public static readonly byte[] _displayOff = { 0x04 };
    }

    ///<summary>Reply commands from device</summary>
    public enum State
    {
        _enabled = 0x00,            // This state doesnt exists, it's just to know that the device accepts banknotes.
        _ticketInBezel = 0xAD,
        _printedToStacker = 0xAF,
        _channelDisable = 0xB5,
        _initialising = 0xB6,
        _stacking = 0xCC,
        _noteClearedFromFront = 0xE1,
        _noteClearedIntoStacker = 0xE2,
        _stackerRemoved = 0xE3,
        _stackerReplaced = 0xE4,
        _stackerFull = 0xE7,
        _disabled = 0xE8,
        _unsafeJam = 0xE9,
        _safeJam = 0xEA,
        _stacked = 0xEB,
        _rejected = 0xEC,
        _rejecting = 0xED,
        _noteCredit = 0xEE,
        _read = 0xEF,
        _ok = 0xF0,
        _reset = 0xF1,
        _unknown = 0xF2,
        _wrongParams = 0xF3,
        _params = 0xF4,
        _notProcessed = 0xF5,
        _error = 0xF6,
        _fail = 0xF8,
        _keyNotSet = 0xF9,
        _keyNotSetBis = 0xFA
    }

    ///<summary>Constructor</summary>
    public RS232_SSP() { }
    ///<summary>Constructor</summary>
    public RS232_SSP(string port, int baudrate, EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null, float latency = 0.1f, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None)
    {
        Setup(port, baudrate, evOpen, evMessage, evError, evClose, latency, parity, dataBits, stopBits, handshake);
    }
    ///<summary>Destructor</summary>
    ~RS232_SSP()
    {
        Dispose();
    }

    ///<summary>SetsUp the RS232 connection properties</summary>
    public void Setup(string port, int baudrate, EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null, float latency = 0.1f, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None)
    {
        Dispose();
        lock (_clientLock)
        {
            if (!IsValidPort(port))
                WriteLine("[RS232_SSP] The requested port (" + port + ") is not available. Connection will be retried until success.", true);
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
                        _setupProcess = 1;
                        SendData(Codes._sync);
                        _pingTimer.Change(300, 300);
                    }
                    catch (System.Exception e)
                    {
                        ThreadPool.QueueUserWorkItem((object s) => { onError?.Invoke(e.HResult, "[RS232_SSP.Connect] " + e.Message, this); });
                    }
                }
            }
            else
            {
                WriteLine("[RS232_SSP.Connect] You should call Setup() before attempt to Connect().");
            }
        }
    }
    ///<summary>Close the connection correctly</summary>
    public void Disconnect()
    {
        lock(_clientLock)
        {
            _deviceReady = false;
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
                // Reads & accumulates received data:
                byte[] data = new byte[_client.BytesToRead];
                _client.Read(data, 0, data.Length);
                byte[] rx = new byte[_lastRx.Length + data.Length];
                System.Buffer.BlockCopy(_lastRx, 0, rx, 0, _lastRx.Length);
                System.Buffer.BlockCopy(data, 0, rx, _lastRx.Length, data.Length);
                // ITL Data structure: [0]0x7F [1]SEQ+ID [2]LENGTH [3]CMD+DATA[LENGTH] [x][y]CRC16
                int lastEof = -1;
                for (int index = 0; index < rx.Length; index++)
                {
                    // Check potential new message:
                    if (rx[index] == 0x7F && rx.Length > index + 2)
                    {
                        int cmdLen = rx[index + 2] + 5;             // Obtain & check package length.
                        if (index + cmdLen <= rx.Length)
                        {
                            // Complete package received:
                            byte[] package = new byte[cmdLen];
                            System.Buffer.BlockCopy(rx, index, package, 0, package.Length);
                            lastEof = index + cmdLen - 1;
                            if (CheckCRC16(package))
                            {
                                // Filter the package extracting the data only:
                                byte[] message = new byte[package[2]];
                                System.Buffer.BlockCopy(package, 3, message, 0, message.Length);
                                // State analysis:
                                if((State)message[0] == State._ok)
                                {
                                    if (message.Length > 1)
                                        _state = (State)message[1];
                                    else if (_deviceReady)
                                        _state = State._enabled;
                                }
                                else if((State)message[0] != State._ok)
                                {
                                    _state = (State)message[0];
                                }
                                // Reset/Setup procedure control:
                                if (_setupProcess > 0 && message.Length == 1 && message[0] == (byte)State._ok)
                                {
                                    // Continue with setup process:
                                    switch (_setupProcess)
                                    {
                                        case 1:     // SYNC ok, send PROTOCOL.
                                            _setupProcess = 2;
                                            SendData(Codes._protocolVersion);
                                            break;
                                        case 2:     // PROTOCOL ok, send CHANNELS.
                                            _setupProcess = 3;
                                            SendData(Codes._setInhibits);
                                            break;
                                        case 3:     // CHANNELS ok, device started.
                                            _setupProcess = 4;
                                            _deviceReady = true;
                                            _pingTimer.Change(300, 300);
                                            SafeOnOpen();
                                            break;
                                    }
                                }
                                else
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
                                SendData(Codes._sync);
                                ThreadPool.QueueUserWorkItem((object s) => { onError?.Invoke(1, "[RS232_SSP.ReceiveData] Error CRC16: " + System.BitConverter.ToString(rx), this); });
                            }
                        }
                    }
                }
                // Save last chunk of data only:
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
                    _sequenceState = !_sequenceState;
                    byte[] aux = new byte[data.Length + 2];
                    aux[0] = (byte)(_sequenceState ? _address | 0x80 : _address);   // Address+SEQ
                    aux[1] = (byte)data.Length;             // DataLen
                    System.Buffer.BlockCopy(data, 0, aux, 2, data.Length);
                    aux = AddCRC16(aux);
                    byte[] msg = new byte[aux.Length + 1];
                    msg[0] = 0x7F;
                    System.Buffer.BlockCopy(aux, 0, msg, 1, aux.Length);
                    _client.Write(msg, 0, msg.Length);
                    Thread.Sleep(20);                       // Delay to allow the device to react.
                }
                catch (System.Exception e)
                {
                    ThreadPool.QueueUserWorkItem((object o) => { onError?.Invoke(e.HResult, "[RS232_SSP.SendData] " + e.Message, this); });
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
                onError?.Invoke(e.HResult, "[RS232_SSP.SafeOnOpen] " + e.Message, this);
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
                onError?.Invoke(e.HResult, "[RS232_SSP.SafeOnMessage] " + e.Message + " - Received: " + (ByteArrayToString(message)), this);
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
                onError?.Invoke(e.HResult, "[RS232_SSP.SafeOnClose] " + e.Message, this);
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

    /// <summary>CRC16 ITL-SSP (All bytes are included in calculation).</summary>
    byte[] GetCRC16(byte[] bufData)
    {
        // Polynomials  x16+x15+x2+1    //16[15--- ---- ---- -2-0]
        ushort polynomial = 0x8005;     //    1000 0000 0000 0101
        ushort crc = 0xFFFF;            // Seed
        for (int i = 0; i < bufData.Length; i++)
        {
            crc ^= (ushort)(bufData[i] << 8);
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) > 0)
                {
                    crc <<= 1;
                    crc ^= polynomial;
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        return System.BitConverter.GetBytes(crc);
    }
    /// <summary>Validate CRC16 for a received command.</summary>
    bool CheckCRC16(byte[] cmd)
    {
        // The 2 last bytes should be the CRC16:
        byte[] data = new byte[cmd.Length - 3];     // Discard header and CRC16 bytes.
        System.Buffer.BlockCopy(cmd, 1, data, 0, data.Length);
        byte[] crc = GetCRC16(data);
        return crc[0] == cmd[cmd.Length - 2] && crc[1] == cmd[cmd.Length - 1];
    }
    /// <summary>Add CRC16 to a data structure (Should not include header).</summary>
    byte[] AddCRC16(byte[] cmd)
    {
        byte[] crc = GetCRC16(cmd);
        byte[] data = new byte[cmd.Length + crc.Length];
        System.Buffer.BlockCopy(cmd, 0, data, 0, cmd.Length);
        System.Buffer.BlockCopy(crc, 0, data, cmd.Length, crc.Length);
        return data;
    }

    /// <summary>Reset device (It resets the port too).</summary>
    public void Reset()
    {
        if(_deviceReady)
        {
            _pingTimer.Change(Timeout.Infinite, Timeout.Infinite);
            ThreadPool.QueueUserWorkItem((object s) => {
                // Reset/connect device:
                _deviceReady = false;
                _setupProcess = 0;
                SendData(Codes._sync);
                _state = State._reset;
                SendData(Codes._reset);     // On reset the RS232 port disconnects completelly.
                Disconnect();
                Thread.Sleep(5000);         // Give time to the device to respond.
                Connect(_timeout);
            });
        }
    }
    /// <summary>Enable the device to accept bills.</summary>
    public void Enable()
    {
        if (_deviceReady)
        {
            SendData(Codes._enable);
            SendData(Codes._displayOn);
        }
    }
    /// <summary>Disable the device without disposing.</summary>
    public void Disable()
    {
        if (_deviceReady)
        {
            SendData(Codes._disable);
            SendData(Codes._displayOff);
        }
    }
    /// <summary>Sends the PING/POLL message</summary>
    void SendPing(object state)
    {
        if(!_deviceReady)
        {
            SendData(Codes._sync);
        }
        else
        {
            SendData(Codes._poll);
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