using KIA_01._01_NetWorkSource;
using KIA_01._02_FormList;
using LitJson;
using Newtonsoft.Json.Linq;
using Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using VNet;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

namespace KIA_01._00_Manager
{
    internal class CTCPHansolSyncMng
    {
        private static CTCPHansolSyncMng _instance;
        public static CTCPHansolSyncMng Instance { get { return _instance; } set { _instance = value; } }
        private CVNetTCPServer m_tcpServer;
        private Queue<string> m_MsgQuee;

        private _01_LogDrawForm m_logForm;

        private int m_nCurrentNum = 0;

        public CTCPHansolSyncMng()
        {
            m_MsgQuee = new Queue<string>();
        }
        ~CTCPHansolSyncMng() { }
        public void Initialize(_01_LogDrawForm _logFoem)
        {
            m_logForm = _logFoem;
            m_tcpServer = new CVNetTCPServer(CConfigMng.Instance.m_nHansolSyncPort, CConfigMng.Instance._strServerIp, OnOpen, OnConnection, OnError, OnClose, 100, CConfigMng.Instance._fkeepAliveTimeout);
            if (!m_tcpServer.IsConnected())
            {
                m_tcpServer.Connect();
            }
        }
        public void Hansol_PC_SyncSendPacket(HANSOL_SYNC_DISPLAY define)
        {
            string sendString = "";
            switch (define)
            {
                case HANSOL_SYNC_DISPLAY.ID_LEFT_DISPLAY_IDLE:
                    sendString = "{\"id\":6000}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_LEFT_DISPLAY_OPEN:
                    sendString = "{\"id\":6001}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_LEFT_DISPLAY_CLOSE:
                    sendString = "{\"id\":6002}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_RIGHT_DISPLAY_IDLE:
                    sendString = "{\"id\":6003}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_RIGHT_DISPLAY_OPEN:
                    sendString = "{\"id\":6004}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_RIGHT_DISPLAY_CLOSE:
                    sendString = "{\"id\":6005}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_BACK_DISPLAY_IDLE:
                    sendString = "{\"id\":6006}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_BACK_DISPLAY_OPEN:
                    sendString = "{\"id\":6007}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_BACK_DISPLAY_CLOSE:
                    sendString = "{\"id\":6008}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_DISPLAY_ON:
                    sendString = "{\"id\":8000}";
                    break;

                case HANSOL_SYNC_DISPLAY.ID_DISPLAY_OFF:
                    sendString = "{\"id\":8001}";
                    break;
            }
            m_tcpServer.HansolSendData(CConfigMng.Instance._strHansolPC_IP, sendString);
        }


        //모바일로 받아서 각 피씨로 send
        public void PacketParser(string byPacket, CVNetTCPSocket tcpUserConnection = null)
        {
            return;
        }
        public string GetSendString(string a, int b, string c, int d)
        {
            string strTemp;
            strTemp = "{" +
                "\"" + a + "\"" + ":" + b + "," +
                "\"" + c + "\"" + ":" + d +
                "}";
            return strTemp;
        }
        public string GetSendString(string a, int b)
        {
            string strTemp;
            strTemp = "{" +
                            "\"" + a + "\"" + ":" + b +
                            "}";
            return strTemp;

        }
        //---------------------------------------------------------------------------

        #region NetWorkEvent
        void OnClientClose(CVNetTCPSocket connection)
        {
            if (m_nCurrentNum >= 2)
                return;
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
            m_logForm.SetLogMessage("        TCP HANSOL SYNC Close = " + connection.GetRemoteIP().ToString() + "   ----   " + connection.GetRemotePort().ToString());
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
        }

        void OnClientError(int code, string message, CVNetTCPSocket connection)
        {
            m_logForm.SetLogMessage("    TCP HANSOLSYNC Error = " + code.ToString() + " " + connection.GetRemoteIP() + "   ----   " + connection.GetRemotePort() + " - " + message);
        }

        //----------------------------------------------------------------------------------
        void OnTCPMessage(byte[] message, CVNetTCPSocket connection)
        {
            int msgLen = 0;
            for (int i = 0; i < message.Length; i++)
            {
                if (message[i] == '}')
                {
                    msgLen = i + 1;         // '#' is excluded.
                    break;
                }
            }
            byte[] msg = new byte[msgLen];
            System.Buffer.BlockCopy(message, 0, msg, 0, msgLen);
            if (msgLen > 0)
            {
                PacketParser(connection.ByteArrayToString(msg), connection);
            }
        }
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
            m_logForm.SetLogMessage("---------------[TCP HANSOL SYNC SERVER OPEN]-----------------");
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
        }
        private void OnClientConneted(CVNetTCPSocket connection)
        {
            m_nCurrentNum++;
            if (m_nCurrentNum >= 2)
                return;
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
            m_logForm.SetLogMessage("        New HANSOL SYNC Connected = " + connection.GetRemoteIP().ToString() + "   ----   " + connection.GetRemotePort().ToString());
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
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
            m_logForm.SetLogMessage("Error Code( " + code.ToString() + "): {" + message + "}");
        }
        private void OnClose(CVNetTCPServer server)
        {
            m_logForm.SetLogMessage("TCP HANSOL SYNC SERVER is closed.");
        }
        public static string ByteArrayToString(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }
        #endregion
    }


}
