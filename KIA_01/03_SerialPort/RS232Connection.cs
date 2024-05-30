using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

/* Events templates (Copy those into your code, then assign to the events of this object):
 
    void OnCOMOpen(RS232Connection connection)
    {}
    void OnCOMMessage(byte[] message, RS232Connection connection)
    {}
    void OnCOMError(int code, string message, RS232Connection connection)
    {}
    void OnCOMClose(RS232Connection connection)
    {}
 */

public class RS232Connection
{
    public SerialPort _client;                                                                  // Serial port connection.
    volatile Timer _receiveThread;                                                              // Incoming messages listening thread.
    volatile byte[] _lastRx;                                                                    // The last incomplete message.
    volatile List<byte[]> _rxBuffer = new List<byte[]>();                                       // The incoming data is stored if no OnMessageReceived event is defined.
    readonly object _rxBufferLock = new object();                                               // Lock the incoming buffer access.
    // Local connection settings:
    public string _port;                                                                        // Port name or address (depends on the OS).
    public int _baudRate;
    public byte _eof;                                                                           // Byte indicating the end of a message.
    public float _latency;                                                                      // Latency time in seconds.
    public int _dataBits;
    public Parity _parity;
    public StopBits _stopBits;
    public Handshake _handshake;
    // Events:
    public delegate void EventMessage(byte[] data, RS232Connection connection);                 // Event delegate with a byte array.
    public delegate void EventException(int code, string message, RS232Connection connection);  // Event delegate with int and string as arguments.
    public delegate void EventVoid(RS232Connection connection);                                 // Event delegate for generic events.
    public volatile EventVoid onOpen;                                                           // The connection was established properly (in COM it means "no error" since there is no connection).
    public volatile EventMessage onMessage;                                                     // New data has been received.
    public volatile EventException onError;                                                     // There was an error (the argument contains the error description).
    public volatile EventVoid onClose;                                                          // The connection was closed unexpectedly (in COM is useless).
    // Status:
    readonly object _clientLock = new object();                                                 // Lock the send method to avoid concurrency.

    ///<summary>Constructor</summary>
    public RS232Connection() { }
    ///<summary>Constructor</summary>
    public RS232Connection(string port, int baudrate, byte eof, EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null, float latency = 0.1f, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None)
    {
        Setup(port, baudrate, eof, evOpen, evMessage, evError, evClose, latency, parity, dataBits, stopBits, handshake);
    }
    ///<summary>Destructor</summary>
    ~RS232Connection()
    {
        Dispose();
    }

    ///<summary>SetsUp the RS232 port to send (not receive)</summary>
    public void Setup(string port, int baudrate, byte eof, EventVoid evOpen = null, EventMessage evMessage = null, EventException evError = null, EventVoid evClose = null, float latency = 0.1f, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One, Handshake handshake = Handshake.None)
    {
        if (!IsValidPort(port))
        {
            WriteLine("[RS232Connection] The requested port (" + port + ") is not available.", true);
            return;
        }
        // Local port parameters:
        _port = port;
        _baudRate = baudrate;
        _eof = eof;
        _latency = latency;
        _parity = parity;
        _dataBits = dataBits;
        _stopBits = stopBits;
        _handshake = handshake;
        // Begin setup:
        Dispose();
        // Events:
        onOpen = evOpen;
        onMessage = evMessage;
        onError = evError;
        onClose = evClose;
        if(_client == null)
            _client = new SerialPort();
        _client.PortName = _port;
        _client.BaudRate = _baudRate;
        _client.Parity = _parity;
        _client.DataBits = _dataBits;
        _client.StopBits = _stopBits;
        _client.ReadBufferSize = _client.WriteBufferSize = 1024*64;
        _client.Handshake = _handshake;
        _receiveThread = new Timer(ReceiveData);
    }

    ///<summary>Set the connection (to send and receive)</summary>
    public void Connect()
    {
        if (_client != null)
        {
            try
            {
                _client.Open();
                if (_receiveThread != null)
                    _receiveThread.Change(SecondsToMiliseconds(_latency), SecondsToMiliseconds(_latency));
                // Connection event (In RS232 it means "no errors" since there is no connection):
                SafeOnOpen();
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => { onError?.Invoke(e.HResult, e.Message, this); });
            }
        }
        else
        {
            WriteLine("[RS232Connection] You should call Setup() before attempt to connect.");
        }
    }

    ///<summary>Close the connection correctly</summary>
    public void Disconnect()
    {
        // Reset the connection to initial state:
        _receiveThread.Change(Timeout.Infinite, Timeout.Infinite);
        _client.DiscardInBuffer();
        _client.DiscardOutBuffer();
        _client.Close();
        _lastRx = new byte[] { };
    }
    ///<summary>Close the connection correctly without firing events</summary>
    public void Dispose()
    {
        // Clear events:
        onOpen = null;
        onMessage = null;
        onError = null;
        onClose = null;
        // Close listener:
        if (_receiveThread != null)
        {
            _receiveThread.Change(Timeout.Infinite, Timeout.Infinite);
            _receiveThread.Dispose();
            _receiveThread = null;
        }
        // Close client last (to avoid null reference exception):
        if (_client != null)
        {
            _client.Close();
            _client.Dispose();
            _client = null;
        }
        _lastRx = new byte[] { };
    }

    ///<summary>Listening thread, gets the incoming datagrams and stores into a buffer</summary>
    void ReceiveData(object state)
    {
        try
        {
            if(_client.BytesToRead > 0)
            {
                // Reads and appends the received data:
                byte[] data = new byte[_client.BytesToRead];
                _client.Read(data, 0, data.Length);
                byte[] rx = new byte[_lastRx.Length + data.Length];
                System.Buffer.BlockCopy(_lastRx, 0, rx, 0, _lastRx.Length);
                System.Buffer.BlockCopy(data, 0, rx, _lastRx.Length, data.Length);
                // Check for message completion condition:
                int lastIndex = -1;
                for(int eofIndex = 0; eofIndex < rx.Length; eofIndex++)
                {
                    if (rx[eofIndex] == _eof)
                    {
                        // Valid message received:
                        byte[] message = new byte[eofIndex - lastIndex];
                        System.Buffer.BlockCopy(rx, lastIndex + 1, message, 0, message.Length);
                        lastIndex = eofIndex;
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
                // Save last chunk of data:
                lastIndex++;
                _lastRx = new byte[rx.Length - lastIndex];
                System.Buffer.BlockCopy(rx, lastIndex, _lastRx, 0, _lastRx.Length);
            }
        }
        catch (System.Exception e)
        {
            ThreadPool.QueueUserWorkItem((object s) => {
                Disconnect();
                onError?.Invoke(e.HResult, e.Message, this);
                SafeOnClose();
            });
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
        lock (_clientLock)
        {
            try
            {
                _client.Write(data, 0, data.Length);
            }
            catch (System.Exception e)
            {
                ThreadPool.QueueUserWorkItem((object s) => { onError?.Invoke(e.HResult, e.Message, this); });
            }
        }
    }
    ///<summary>Sends a string</summary>
    public void SendData(string data)
    {
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
                onError?.Invoke(e.HResult, "[RS232Connection.SafeOnOpen] " + e.Message, this);
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
                onError?.Invoke(e.HResult, "[RS232Connection.SafeOnMessage] " + e.Message + " - Received: " + (ByteArrayToString(message)), this);
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
                onError?.Invoke(e.HResult, "[RS232Connection.SafeOnClose] " + e.Message, this);
            });
        }
    }

    ///<summary>TRUE if connected and listening</summary>
    public bool IsConnected()
    {
        return (_client != null && _client.IsOpen && _receiveThread != null);
    }

    ///<summary>Checks if the provided port is valid</summary>
    bool IsValidPort(string port)
    {
        string[] ports = GetAvailablePorts();
        for(int i = 0; i < ports.Length; i++)
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
}
