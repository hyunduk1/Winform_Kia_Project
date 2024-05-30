using KIA_01._01_NetWorkSource;
using KIA_01._02_FormList;
using LitJson;
using Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using VNet;
using System.Timers;
using System.Windows.Forms;

namespace KIA_01._00_Manager
{
    internal class CNetWorkMng
    {
        private static CNetWorkMng _instance;
        public static CNetWorkMng Instance { get { return _instance; } set { _instance = value; } }

        private CVNetTCPServer m_tcpServer;
        private CVNetTCPSocket m_tcpSocket;
        private Queue<string> m_MsgQuee;

        private _01_LogDrawForm m_logForm;

        private byte[] FirstSetting;


        public CNetWorkMng()
        {
            m_MsgQuee = new Queue<string>();
        }
        ~CNetWorkMng() { }


        public void Initialize(_01_LogDrawForm _logFoem)
        {
            m_logForm = _logFoem;
            m_tcpServer = new CVNetTCPServer(CConfigMng.Instance._nServerPort, CConfigMng.Instance._strServerIp, OnOpen, OnConnection, OnError, OnClose, 100, CConfigMng.Instance._fkeepAliveTimeout);

            if (!m_tcpServer.IsConnected())
            {
                m_tcpServer.Connect();
            }
        }

        public void Start()
        {
        }

        public void PacketParser(byte[] byPacket, CVNetTCPSocket tcpUserConnection = null)
        {
            switch (byPacket[1])
            {
                case (byte)MOTION_PROTOCOL.ID_Reset_RecivePacket:      //초기화후 받는 프로토콜
                    CSerialPortMng.Instance.DisPlayBasicMode();
                    SendPacket(PROTOCOL.ID_SIDE_MIRROW_LEFT_OPEN);
                    SendPacket(PROTOCOL.ID_SIDE_MIRROW_RIGHT_OPEN);
                    m_logForm.SetLogMessage("초기화 패킷 받음");
                    break;
            }
            string PacketMessage = System.BitConverter.ToString(byPacket);
            m_logForm.SetLogMessage(PacketMessage);
        }

        /// <summary>
        /// true = Idle 상태 LED ON , FALSE IDLE 상태 OFF
        /// </summary>
        /// <param name="power"></param>
        /// <returns></returns>
        public byte[] LedPower(bool power)
        {
            byte[] tempArray;
            if (power == true)
                tempArray = new byte[] { 0x02, 0x63, 0x40, 0x00, 0x00, 0xFF, 0x64, 0x14, 0x0D, 0x0A };
            else
                tempArray = new byte[] { 0x02, 0x63, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x0A };

            string message = System.BitConverter.ToString(tempArray);
            m_logForm.SetLogMessage("LED_PROTOCOL : " + message);
            return tempArray;
        }

        public byte[] LedMotionProtocol(byte CommendType, byte Mode)
        {
            byte[] tempArray = { 0x02, CommendType, Mode, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x0A };
            string message = System.BitConverter.ToString(tempArray);
            m_logForm.SetLogMessage("LED_PROTOCOL : " + message);
            return tempArray;
        }
        public byte[] LedProtocol(byte Chenel, byte Mode, byte Bright , byte R, byte G, byte B)
        {
            byte[] tempArray = {0x02, 0x88, Chenel, Mode, Bright, R, G, B, 0x0D, 0x0A};
            string message = System.BitConverter.ToString(tempArray);
            m_logForm.SetLogMessage("LED_PROTOCOL : " + message);
            return tempArray;
        }
        public byte[] MotionProtocol(byte Type, byte Send)
        {
            byte[] tempArray = { 0x02, Type, Send, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x0A};
            string message = System.BitConverter.ToString(tempArray);
            m_logForm.SetLogMessage("MOTION_PROTOCOL : " + message);
            return tempArray;
        }
        private byte[] ResetClientProtocol()
        {
            byte[] tempArray = { 0x02, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0D, 0X0A};
            return tempArray;
        }
        Byte GetHex(string srcValue)
        {
            return Convert.ToByte(srcValue, 16);
        }
        
        public void SendPacket(PROTOCOL protocol)
        {
            switch (protocol)
            {
                //--------------------------------------------- 핸들전진
                case PROTOCOL.ID_HANDLE_FORWORD_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(2)[0]), GetHex(CDataMng.Instance.ExcelData(2)[1])));
                    break;

                case PROTOCOL.ID_HANDLE_FORWORD_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(3)[0]), GetHex(CDataMng.Instance.ExcelData(3)[1])));
                    break;

                case PROTOCOL.ID_HANDLE_FORWORD_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(4)[0]), GetHex(CDataMng.Instance.ExcelData(4)[1])));
                    break;

                case PROTOCOL.ID_HANDLE_FORWORD_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(5)[0]), GetHex(CDataMng.Instance.ExcelData(5)[1])));
                    break;
                //----------------------------------------------- 핸들틸팅
                case PROTOCOL.ID_HANDLE_TILTING_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(6)[0]), GetHex(CDataMng.Instance.ExcelData(6)[1])));
                    break;
                case PROTOCOL.ID_HANDLE_TILTING_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(7)[0]), GetHex(CDataMng.Instance.ExcelData(7)[1])));
                    break;
                case PROTOCOL.ID_HANDLE_TILTING_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(8)[0]), GetHex(CDataMng.Instance.ExcelData(8)[1])));
                    break;
                case PROTOCOL.ID_HANDLE_TILTING_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(9)[0]), GetHex(CDataMng.Instance.ExcelData(9)[1])));
                    break;
                //------------------------------------------------ 왼쪽 사이드미러
                case PROTOCOL.ID_SIDE_MIRROW_LEFT_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(10)[0]), GetHex(CDataMng.Instance.ExcelData(10)[1])));
                    break;
                case PROTOCOL.ID_SIDE_MIRROW_LEFT_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(11)[0]), GetHex(CDataMng.Instance.ExcelData(11)[1])));
                    break;
                case PROTOCOL.ID_SIDE_MIRROW_LEFT_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(12)[0]), GetHex(CDataMng.Instance.ExcelData(12)[1])));
                    break;
                case PROTOCOL.ID_SIDE_MIRROW_LEFT_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(13)[0]), GetHex(CDataMng.Instance.ExcelData(13)[1])));
                    break;

                //------------------------------------------------오른쪽 사이드미러

                case PROTOCOL.ID_SIDE_MIRROW_RIGHT_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(14)[0]), GetHex(CDataMng.Instance.ExcelData(14)[1])));
                    break;
                case PROTOCOL.ID_SIDE_MIRROW_RIGHT_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(15)[0]), GetHex(CDataMng.Instance.ExcelData(15)[1])));
                    break;
                case PROTOCOL.ID_SIDE_MIRROW_RIGHT_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(16)[0]), GetHex(CDataMng.Instance.ExcelData(16)[1])));
                    break;
                case PROTOCOL.ID_SIDE_MIRROW_RIGHT_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(17)[0]), GetHex(CDataMng.Instance.ExcelData(17)[1])));
                    break;
                    
                //------------------------------------------------ 운전석 손잡이

                case PROTOCOL.ID_FRONT_DOOR_KEY_LEFT_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(18)[0]), GetHex(CDataMng.Instance.ExcelData(18)[1])));
                    break;
                case PROTOCOL.ID_FRONT_DOOR_KEY_LEFT_LOCK:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(19)[0]), GetHex(CDataMng.Instance.ExcelData(19)[1])));
                    break;
                case PROTOCOL.ID_FRONT_DOOR_KEY_LEFT_UNLOCK:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(20)[0]), GetHex(CDataMng.Instance.ExcelData(20)[1])));
                    break;
                case PROTOCOL.ID_FRONT_DOOR_KEY_LEFT_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(21)[0]), GetHex(CDataMng.Instance.ExcelData(21)[1])));
                    break;

                //------------------------------------------------ 조수석 손잡이

                case PROTOCOL.ID_FRONT_DOOR_KEY_RIGHT_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(22)[0]), GetHex(CDataMng.Instance.ExcelData(22)[1])));
                    break;
                case PROTOCOL.ID_FRONT_DOOR_KEY_RIGHT_LOCK:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(23)[0]), GetHex(CDataMng.Instance.ExcelData(23)[1])));
                    break;
                case PROTOCOL.ID_FRONT_DOOR_KEY_RIGHT_UNLOCK:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(24)[0]), GetHex(CDataMng.Instance.ExcelData(24)[1])));
                    break;
                case PROTOCOL.ID_FRONT_DOOR_KEY_RIGHT_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(25)[0]), GetHex(CDataMng.Instance.ExcelData(25)[1])));
                    break;

                //------------------------------------------------ 기어봉 같은 손잡이

                case PROTOCOL.ID_CONSOLE_TOP_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(26)[0]), GetHex(CDataMng.Instance.ExcelData(26)[1])));
                    break;
                case PROTOCOL.ID_CONSOLE_TOP_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(27)[0]), GetHex(CDataMng.Instance.ExcelData(27)[1])));
                    break;
                case PROTOCOL.ID_CONSOLE_TOP_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(28)[0]), GetHex(CDataMng.Instance.ExcelData(28)[1])));
                    break;
                case PROTOCOL.ID_CONSOLE_TOP_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(29)[0]), GetHex(CDataMng.Instance.ExcelData(29)[1])));
                    break;

                case PROTOCOL.ID_CONSOLE_BOTTOM_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(43)[0]), GetHex(CDataMng.Instance.ExcelData(43)[1])));
                    break;
                case PROTOCOL.ID_CONSOLE_BOTTOM_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(44)[0]), GetHex(CDataMng.Instance.ExcelData(44)[1])));
                    break;
                case PROTOCOL.ID_CONSOLE_BOTTOM_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(45)[0]), GetHex(CDataMng.Instance.ExcelData(45)[1])));
                    break;
                case PROTOCOL.ID_CONSOLE_BOTTOM_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(46)[0]), GetHex(CDataMng.Instance.ExcelData(46)[1])));
                    break;

                //------------------------------------------------ 핸들쪽 파란색 열쇠 같은거(?)

                case PROTOCOL.ID_HANDLE_CIRCLE_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(30)[0]), GetHex(CDataMng.Instance.ExcelData(30)[1])));
                    break;
                case PROTOCOL.ID_HANDLE_CIRCLE_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(31)[0]), GetHex(CDataMng.Instance.ExcelData(31)[1])));
                    break;
                case PROTOCOL.ID_HANDLE_CIRCLE_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(32)[0]), GetHex(CDataMng.Instance.ExcelData(32)[1])));
                    break;
                case PROTOCOL.ID_HANDLE_CIRCLE_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(33)[0]), GetHex(CDataMng.Instance.ExcelData(33)[1])));
                    break;

                //------------------------------------------------ 왼쪽 뒷문

                case PROTOCOL.ID_TAIL_GATE_DOOR_LEFT_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(34)[0]), GetHex(CDataMng.Instance.ExcelData(34)[1])));
                    break;

                case PROTOCOL.ID_TAIL_GATE_DOOR_LEFT_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(35)[0]), GetHex(CDataMng.Instance.ExcelData(35)[1])));
                    break;

                case PROTOCOL.ID_TAIL_GATE_DOOR_LEFT_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(36)[0]), GetHex(CDataMng.Instance.ExcelData(36)[1])));
                    break;

                case PROTOCOL.ID_TAIL_GATE_DOOR_LEFT_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(37)[0]), GetHex(CDataMng.Instance.ExcelData(37)[1])));
                    break;

                //------------------------------------------------오른쪽 뒷문

                case PROTOCOL.ID_TAIL_GATE_DOOR_RIGHT_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(38)[0]), GetHex(CDataMng.Instance.ExcelData(38)[1])));
                    break;
                case PROTOCOL.ID_TAIL_GATE_DOOR_RIGHT_OPEN:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(39)[0]), GetHex(CDataMng.Instance.ExcelData(39)[1])));
                    break;
                case PROTOCOL.ID_TAIL_GATE_DOOR_RIGHT_CLOSE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(40)[0]), GetHex(CDataMng.Instance.ExcelData(40)[1])));
                    break;
                case PROTOCOL.ID_TAIL_GATE_DOOR_RIGHT_STATE:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(41)[0]), GetHex(CDataMng.Instance.ExcelData(41)[1])));
                    break;

                //------------------------------------------------ 긴급정지

                case PROTOCOL.ID_EMERGENCE_STOP:
                    SendPacket(MotionProtocol(GetHex(CDataMng.Instance.ExcelData(42)[0]), GetHex(CDataMng.Instance.ExcelData(42)[1])));
                    break;
            }
        }

        public void SendPacket(byte[] byPacket)
        {
            m_tcpServer.SendData(byPacket);
            
        }
        //----------------------------------------------------------------------------------
        void OnTCPMessage(byte[] message, CVNetTCPSocket connection)
        {
            int msgLen = 0;
            for (int i = 0; i < message.Length; i++)
            {
                if (message[i] == 0x0a)
                {
                    msgLen = i + 1;
                    break;
                }
            }
            byte[] msg = new byte[msgLen];
            System.Buffer.BlockCopy(message, 0, msg, 0, msgLen);
            if (msgLen > 0)
            {
                PacketParser(msg, connection);
            }
            else
            {
                string Errormessage = System.BitConverter.ToString(message);
                m_logForm.SetLogMessage("[Error] : " + Errormessage);
            }
            /*int msgLen = 0;

            for (int i = 0; i < message.Length; i++)
            {
                if (message[i] == '}')
                {
                    msgLen = i + 1;
                    break;
                }
            }
            try
            {
                byte[] msg = new byte[msgLen];
                System.Buffer.BlockCopy(message, 0, msg, 0, msgLen);
                m_MsgQuee.Enqueue(connection.ByteArrayToString(msg));
                if (msgLen > 0)
                {
                    PacketParser(msg, connection);
                }
            }

            catch (Exception e)
            {
                m_logForm.SetLogMessage(e.ToString());
            }*/
        }

        #region NetWorkEvent
        void OnClientClose(CVNetTCPSocket connection)
        {
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
            m_logForm.SetLogMessage("        TCP Client Close = " + connection.GetRemoteIP().ToString() + "   ----   " + connection.GetRemotePort().ToString());
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
            CTCPMobileMng.Instance.Connection_Send_Packet(Mobile_PROTOCOL.MSG_RAONTECCONNECT_OFF);
        }

        void OnClientError(int code, string message, CVNetTCPSocket connection)
        {
            m_logForm.SetLogMessage("    TCP Client Error = " + code.ToString() + " " + connection.GetRemoteIP() + "   ----   " + connection.GetRemotePort() + " - " + message);
        }

        //----------------------------------------------------------------------------------

        public void Stop()
        {
            m_tcpServer.Dispose();
            m_logForm.SetLogMessage("TCP Server stopped.");
        }
        private void OnOpen(CVNetTCPServer server)
        {
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
            m_logForm.SetLogMessage("---------------[PORT]--" + server.GetPort() + "-------[IP]--" + server.GetIP() + "------------------");
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
            m_logForm.SetLogMessage("--------------------------[TCP SERVER OPEN]-----------------------");
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
        }
        private void OnClientConneted(CVNetTCPSocket connection)
        {
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
            m_logForm.SetLogMessage("        New Client Connected = " + connection.GetRemoteIP().ToString() + "   ----   "+ connection.GetRemotePort().ToString());
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
            CTCPMobileMng.Instance.Connection_Send_Packet(Mobile_PROTOCOL.MSG_RAONTECCONNECT_ON);
        }
        private void OnConnection(CVNetTCPSocket connection, CVNetTCPServer server)
        {
            OnClientConneted(connection);
            connection.onMessage = OnTCPMessage;
            connection.onClose = OnClientClose;
            connection.onError = OnClientError;
        }
        private void OnError(int code, string message, CVNetTCPServer server)
        {
            m_logForm.SetLogMessage("Error Code( " + code.ToString() +"): {"+ message +"}");
        }
        private void OnClose(CVNetTCPServer server)
        {
            m_logForm.SetLogMessage("TCP Server is closed.");
        }
        public static string ByteArrayToString(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }
        #endregion
    }
}
