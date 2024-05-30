using KIA_01._01_NetWorkSource;
using KIA_01._02_FormList;
using LitJson;
using Newtonsoft.Json.Linq;
using  Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Threading;
using VNet;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

namespace KIA_01._00_Manager
{
    internal class CTCPMobileMng
    {
        private static CTCPMobileMng _instance;
        public static CTCPMobileMng Instance { get { return _instance; } set { _instance = value; } }
        private CVNetTCPServer m_tcpServer;
        private CVNetTCPSocket m_tcpSocket;
        private Queue<string> m_MsgQuee;

        private int m_nHansolCurrentNum = 0;

        private short m_nHansolResetCurrent = 0;

        private _01_LogDrawForm m_logForm;
        private bool m_bFinishSetting = false;
        public CTCPMobileMng()
        {
            m_MsgQuee = new Queue<string>();
        }
        ~CTCPMobileMng() { }
        public void Initialize(_01_LogDrawForm _logFoem)
        {
            m_logForm = _logFoem;
            m_tcpServer = new CVNetTCPServer(CConfigMng.Instance._nMobileServerPort, CConfigMng.Instance._strServerIp, OnOpen, OnConnection, OnError, OnClose, 100, CConfigMng.Instance._fkeepAliveTimeout);
            if (!m_tcpServer.IsConnected())
            {
                m_tcpServer.Connect();
            }
        }


        //-----------------------------------------------------------------------
        public void RaonTec_PC_SendPacket(RAONTEC_PC_Controll define)
        {
            try
            {
                string sendString = "";
                switch (define)
                {
                    case RAONTEC_PC_Controll.MSG_POWER_OFF:
                        sendString = "{\"id\":3001,\"pcid\":2}";

                        break;
                    case RAONTEC_PC_Controll.MSG_POWER_RESET:
                        sendString = "{\"id\":3002,\"pcid\":2}";
                        break;
                }
                m_tcpServer.HansolSendData(CConfigMng.Instance._strSendIp, sendString);
            }
            catch (Exception e)
            {
                m_logForm.SetLogMessage(e.Message);
            }
        }
        //시작되고 한솔피씨가 켜지면 보내는 패킷 + 한솔 피씨 전원제어
        public void Hansol_PC_SendPacket(Hansol_PC_Controll define)
        {
            try
            {
                string sendString = "";
                switch (define)
                {
                    case Hansol_PC_Controll.MSG_POWER_OFF:
                        sendString = "{\"id\":3001,\"pcid\":1}";
                        //sendString = GetSendString("id", 3001, "pcid", 1);
                        break;

                    case Hansol_PC_Controll.MSG_POWER_RESET:
                        sendString = "{\"id\":3002,\"pcid\":1}";
                        //sendString = GetSendString("id", 3002, "pcid", 1);
                        break;
                }
                m_tcpServer.HansolSendData(CConfigMng.Instance._strHansolPC_IP, sendString);
                m_nHansolResetCurrent++;
            }
            catch (Exception e)
            {
                m_logForm.SetLogMessage(e.Message);
            }

        }

        //--------------------------------------------------------------------
        public void SendWakeOnLanPacket(string macAddress)
        {
            // MAC 주소를 바이트 배열로 변환
            byte[] macBytes = new byte[6];
            string[] macAddressParts = macAddress.Split(':');

            for (int i = 0; i < 6; i++)
            {
                macBytes[i] = Convert.ToByte(macAddressParts[i], 16);
            }

            // Wake-on-LAN 패킷 구성
            byte[] magicPacket = new byte[102];

            // 처음 6바이트에 0xFF 반복
            for (int i = 0; i < 6; i++)
            {
                magicPacket[i] = 0xFF;
            }

            // 이후 MAC 주소를 16번 반복하여 패킷에 추가
            for (int i = 6; i < 102; i += 6)
            {
                Array.Copy(macBytes, 0, magicPacket, i, 6);
            }

            // UDP 소켓을 이용하여 패킷 전송
            using (UdpClient udpClient = new UdpClient())
            {
                udpClient.EnableBroadcast = true;
                udpClient.Send(magicPacket, magicPacket.Length, new IPEndPoint(IPAddress.Broadcast, 7));
            }
        }
        //---------------------------

        public void RaonTec_PC_On(string ipAddress, string Mac)
        {

        }
        public void Connection_Send_Packet(Mobile_PROTOCOL define)
        {
            try
            {
                string sendString = "";
                switch (define)
                {
                    case Mobile_PROTOCOL.MSG_RAONTECCONNECT_ON:
                        sendString = "{\"id\":8000}";
                        m_logForm.SetLogMessage("구동제어 피씨 On");
                        break;
                    case Mobile_PROTOCOL.MSG_RAONTECCONNECT_OFF:
                        sendString = "{\"id\":8001}";
                        m_logForm.SetLogMessage("구동제어 피씨 Off");
                        break;
                }
                m_tcpServer.SendData(sendString);
            }
            catch (Exception e)
            {
                m_logForm.SetLogMessage(e.Message);
            }

        }

        //모바일로 받아서 각 피씨로 send
        public void PacketParser(string byPacket, CVNetTCPSocket tcpUserConnection = null)
        {
            try
            {
                JObject packetData = (JObject)JToken.Parse(byPacket);
                m_logForm.SetLogMessage("[Mobile Packet] " + "id : " + packetData["id"].ToString());
                switch ((Mobile_PROTOCOL)int.Parse(packetData["id"].ToString()))
                {
                    //--------------------motion--------------------------------
                    case Mobile_PROTOCOL.ID_DRIVE_MODE:

                        break;

                    case Mobile_PROTOCOL.ID_REST_MODE:

                        break;

                    case Mobile_PROTOCOL.ID_SIDE_MIRROW_OPEN: // 사이드미러
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_LEFT_OPEN);
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_RIGHT_OPEN);
                        m_logForm.SetLogMessage("[사이드미러 오픈]");
                        break;

                    case Mobile_PROTOCOL.ID_SIDE_MIRROW_CLOSE:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_LEFT_CLOSE);
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_RIGHT_CLOSE);
                        m_logForm.SetLogMessage("[사이드미러 클로즈]");
                        break;

                    case Mobile_PROTOCOL.ID_FRONT_DOOR_LOCK_OPEN:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_FRONT_DOOR_KEY_LEFT_UNLOCK);
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_FRONT_DOOR_KEY_RIGHT_UNLOCK);
                        m_logForm.SetLogMessage("프론트 도어 UNLOCK");
                        break;

                    case Mobile_PROTOCOL.ID_FRONT_DOOR_LOCK_CLOSE:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_FRONT_DOOR_KEY_LEFT_LOCK);
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_FRONT_DOOR_KEY_RIGHT_LOCK);
                        m_logForm.SetLogMessage("프론트 도어 LOCK");
                        break;

                    case Mobile_PROTOCOL.ID_CONSOLE_TOP_UP:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_CONSOLE_TOP_OPEN);
                        m_logForm.SetLogMessage("[콘솔 탑 UP ]");
                        break;

                    case Mobile_PROTOCOL.ID_CONSOLE_TOP_DOWN:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_CONSOLE_TOP_CLOSE);
                        m_logForm.SetLogMessage("[콘솔 탑 DOWN ]");
                        break;

                    case Mobile_PROTOCOL.ID_CONSOLE_BOTTOM_UP:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_CONSOLE_BOTTOM_OPEN);
                        m_logForm.SetLogMessage("[콘솔 바텀 UP ]");
                        break;

                    case Mobile_PROTOCOL.ID_CONSOLE_BOTTOM_DOWN:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_CONSOLE_BOTTOM_CLOSE);
                        m_logForm.SetLogMessage("[콘솔 바텀 DOWN ]");
                        break;

                    case Mobile_PROTOCOL.ID_HANDLE_STEERING_WHEEL_OPEN:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_CIRCLE_OPEN);
                        m_logForm.SetLogMessage("[ID_HANDLE_STEERING_WHEEL_OPEN]");
                        break;

                    case Mobile_PROTOCOL.ID_HANDLE_STEERING_WHEEL_CLOSE:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_CIRCLE_CLOSE);
                        m_logForm.SetLogMessage("[ID_HANDLE_STEERING_WHEEL_CLOSE]");
                        break;

                    case Mobile_PROTOCOL.ID_TAIL_GATE_DOOR_LEFT_OPEN:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_TAIL_GATE_DOOR_LEFT_OPEN);
                        m_logForm.SetLogMessage("[테일 왼쪽 오픈]");
                        break;

                    case Mobile_PROTOCOL.ID_TAIL_GATE_DOOR_LEFT_CLOSE:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_TAIL_GATE_DOOR_LEFT_CLOSE);
                        m_logForm.SetLogMessage("[테일 왼쪽 클로즈]");
                        break;

                    case Mobile_PROTOCOL.ID_TAIL_GATE_DOOR_RIGHT_OPEN:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_TAIL_GATE_DOOR_RIGHT_OPEN);
                        m_logForm.SetLogMessage("[테일 오른쪽 오픈]");
                        break;

                    case Mobile_PROTOCOL.ID_TAIL_GATE_DOOR_RIGHT_CLOSE:
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_TAIL_GATE_DOOR_RIGHT_CLOSE);
                        m_logForm.SetLogMessage("[테일 오른쪽 클로즈]");
                        break;

                    case Mobile_PROTOCOL.ID_SOUND_VOLUME:
                        m_logForm.SetLogMessage("사운드볼륨");
                        break;

                    case Mobile_PROTOCOL.ID_POWER_OFF:
                        m_logForm.SetLogMessage("파워 off");
                        break;

                    case Mobile_PROTOCOL.ID_POWER_ON:
                        m_logForm.SetLogMessage("파워 on");
                        break;

                    case Mobile_PROTOCOL.ID_LIGHT_CHANGE_01:
                        m_logForm.SetLogMessage("ID_LIGHT_CHANGE_01");
                        break;

                    case Mobile_PROTOCOL.ID_LIGHT_CHANGE_02:
                        m_logForm.SetLogMessage("ID_LIGHT_CHANGE_02");
                        break;

                    case Mobile_PROTOCOL.ID_LIGHT_CHANGE_03:
                        m_logForm.SetLogMessage("ID_LIGHT_CHANGE_03");
                        break;

                    case Mobile_PROTOCOL.ID_LIGHT_CHANGE_04:
                        m_logForm.SetLogMessage("ID_LIGHT_CHANGE_04");
                        break;

                    //-------------------------------------------------------LED----------------------------------

                    case Mobile_PROTOCOL.ID_EMERGENCT_LIGHT_ON:         // 비상등 On
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x55, 0x40));
                        break;

                    case Mobile_PROTOCOL.ID_EMERGENCT_LIGHT_OFF:        // 비상등 Off
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x55, 0x41));
                        break;

                    case Mobile_PROTOCOL.ID_BREAK_LIGHT_ON:                   //브레이크 On
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x56, 0x40));
                        break;

                    case Mobile_PROTOCOL.ID_BREAK_LIGHT_OFF:                  //브레이크 Off
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x56, 0x41));
                        break;


                    case Mobile_PROTOCOL.ID_TURN_RIGHT_ON:              //Turn Right On
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x58, 0x40));
                        break;

                    case Mobile_PROTOCOL.ID_TURN_RIGHT_OFF:              //Turn Right Off
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x58, 0x41));
                        break;

                    case Mobile_PROTOCOL.ID_TURN_LEFT_ON:              //Turn Left On
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x57, 0x40));
                        break;

                    case Mobile_PROTOCOL.ID_TURN_LEFT_OFF:              //Turn Left Off
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x57, 0x41));
                        break;

                    case Mobile_PROTOCOL.ID_DRL_ON:              //DRL On
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x59, 0x40));
                        break;

                    case Mobile_PROTOCOL.ID_DRL_OFF:              //DRL Off
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x59, 0x41));
                        break;

                    case Mobile_PROTOCOL.ID_LIGHT_HIGH_ON:              //HI On
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x60, 0x40));
                        break;

                    case Mobile_PROTOCOL.ID_LIGHT_HIGH_OFF:              //HI Off
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x60, 0x41));
                        break;

                    case Mobile_PROTOCOL.ID_LIGHT_LOW_ON:              //LOW On
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x61, 0x40));
                        break;

                    case Mobile_PROTOCOL.ID_LIGHT_LOW_OFF:              //LOW Off
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x61, 0x41));
                        break;

                    case Mobile_PROTOCOL.ID_RESET_IDLE_STATE:              //IDLE On
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x62, 0x40));
                        CSerialPortMng.Instance.DisPlayBasicMode();
                        CTCPHansolSyncMng.Instance.Hansol_PC_SyncSendPacket(HANSOL_SYNC_DISPLAY.ID_DISPLAY_ON);
                        break;

                    case Mobile_PROTOCOL.ID_RESET_IDLE_OFF:                //IDLE OFF
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x62, 0x41));
                        break;

                    case Mobile_PROTOCOL.ID_ALL_LIGHT_ON:              //실내등 ON
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x63, 0x40));
                        break;

                    case Mobile_PROTOCOL.ID_ALL_LIGHT_OFF:              //실내등 OFF
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x63, 0x41));
                        break;

                    case Mobile_PROTOCOL.ID_EMERGENCE:                  //긴급제동
                        CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_EMERGENCE_STOP);
                        break;



                    //-------------------------------------------option-----------------------------

                    case Mobile_PROTOCOL.ID_CLUTCH_ON:
                        m_logForm.SetLogMessage("클러치 온 (수정)");
                        break;

                    case Mobile_PROTOCOL.ID_CLUTCH_OFF:
                        m_logForm.SetLogMessage("클러치 오프 (수정)");
                        break;

                    case Mobile_PROTOCOL.ID_ELECTROMAGNET_ON:
                        m_logForm.SetLogMessage("ELECTROMAGNET_ON (수정)");
                        break;

                    case Mobile_PROTOCOL.ID_ELECTROMAGNET_OFF:
                        m_logForm.SetLogMessage("ELECTROMAGNET_OFF (수정)");
                        break;

                    case Mobile_PROTOCOL.ID_DRIVE_MOTOR_ON:
                        m_logForm.SetLogMessage("DRIVE_MOTOR_ON (수정)");
                        break;

                    case Mobile_PROTOCOL.ID_DRIVE_MOTOR_OF:
                        m_logForm.SetLogMessage("DRIVE_MOTOR_OF (수정)");
                        break;

                    case Mobile_PROTOCOL.ID_REAR_DOOR_TOUCH_LOCK_ON:
                        m_logForm.SetLogMessage("REAR_DOOR_TOUCH_LOCK_ON (수정)");
                        break;

                    case Mobile_PROTOCOL.ID_REAR_DOOR_TOUCH_LOCK_OFF:
                        m_logForm.SetLogMessage("REAR_DOOR_TOUCH_LOCK_OFF (수정)");
                        break;

                        // ------------------------------------ 1.10 
                    case Mobile_PROTOCOL.ID_DISPLAY_MOVIE_01:   //디스플레이 프론트 메인영상
                        CSerialPortMng.Instance.Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.Display_Scene_01());
                        CSerialPortMng.Instance.Send(DISPLAYPORT.ID_CPAD, CSerialPortMng.Instance.Display_Scene_01());
                        break;

                    case Mobile_PROTOCOL.ID_DISPLAY_MOVIE_02:   //디스플레이 엠블럼 영상
                        CSerialPortMng.Instance.Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.Display_Scene_02());
                        CSerialPortMng.Instance.Send(DISPLAYPORT.ID_CPAD, CSerialPortMng.Instance.Display_Scene_02());
                        //CSerialPortMng.Instance.Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.Display_Scene_02());
                        break;

                    case Mobile_PROTOCOL.ID_DISPLAY_MOVIE_03:   //충전영상  
                        CSerialPortMng.Instance.Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.Display_Scene_03());
                        CSerialPortMng.Instance.Send(DISPLAYPORT.ID_CPAD, CSerialPortMng.Instance.Display_Scene_03());
                        //CSerialPortMng.Instance.Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.Display_Scene_03());
                        break;



                    case Mobile_PROTOCOL.ID_DISPLAY_MOVIE_STOP:   //디스플레이 스탑
                        CSerialPortMng.Instance.DisPlayAllOut();
                        CTCPHansolSyncMng.Instance.Hansol_PC_SyncSendPacket(HANSOL_SYNC_DISPLAY.ID_DISPLAY_OFF);
                        break;
                    case Mobile_PROTOCOL.ID_DISPLAY_ALL_ON:
                        m_logForm.SetLogMessage("모든 한솔 디스플레이 켜기");
                        CSerialPortMng.Instance.DisPlayBasicMode();
                        CTCPHansolSyncMng.Instance.Hansol_PC_SyncSendPacket(HANSOL_SYNC_DISPLAY.ID_DISPLAY_ON);
                        break;
                    case Mobile_PROTOCOL.ID_PC_OFF:         //전원제어

                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x63, 0x41));
                        Thread.Sleep(10);
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x62, 0x41));
                       /* CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x61, 0x41));
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x60, 0x41));
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x59, 0x41));
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x57, 0x41));
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x58, 0x41));
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x56, 0x41));
                        CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedMotionProtocol(0x55, 0x41));*/
                        Thread.Sleep(2000);
                        for (int i = 0; i < 5; i++)
                        {
                            Hansol_PC_SendPacket(Hansol_PC_Controll.MSG_POWER_OFF);
                            Thread.Sleep(2000);
                        }
                        for (int i = 0; i < 5; i++)
                        {
                            RaonTec_PC_SendPacket(RAONTEC_PC_Controll.MSG_POWER_OFF);
                            Thread.Sleep(2000);
                        }

                        /*HansolPC_ShutDownThread.Start();
                        HansolPC_ShutDownThread.Join();*/

                        //-----------------------------------------------------

                        Thread.Sleep(1000);

                        Process.Start("shutdown.exe", "-s -t 5");
                        break;
                }
            }
            catch(Exception e)
            {
                m_logForm.SetLogMessage(e.Message);
            }
        }

        //-----------------------------------PC 종료 스레드---------------------------------
        Thread HansolPC_ShutDownThread = new Thread(() =>
        {
            for(int i = 0; i < 5; i++)
            {
                CTCPMobileMng.Instance.Hansol_PC_SendPacket(Hansol_PC_Controll.MSG_POWER_OFF);
                Thread.Sleep(2000);
                CTCPMobileMng.Instance.RaonTec_PC_SendPacket(RAONTEC_PC_Controll.MSG_POWER_OFF);
                Thread.Sleep(2000);
            }
            CTCPMobileMng.Instance.m_logForm.SetLogMessage("피씨 OFF 패킷 SEND");
        });
        //---------------------------------------------------------------------------------
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
            if (connection.GetRemoteIP() == CConfigMng.Instance._strHansolPC_IP)
            {
                m_logForm.SetLogMessage("------------------------------------------------------------------------");
                m_logForm.SetLogMessage("        한솔 PC Colse  =  " + connection.GetRemoteIP().ToString() + "   ----   " + connection.GetRemotePort().ToString());
                m_logForm.SetLogMessage("------------------------------------------------------------------------");
            }
            else
            {
                m_logForm.SetLogMessage("------------------------------------------------------------------------");
                m_logForm.SetLogMessage("        TCP Mobile Close = " + connection.GetRemoteIP().ToString() + "   ----   " + connection.GetRemotePort().ToString());
                m_logForm.SetLogMessage("------------------------------------------------------------------------");
            }
        }

        void OnClientError(int code, string message, CVNetTCPSocket connection)
        {
            m_logForm.SetLogMessage("    TCP Mobile Error = " + code.ToString() + " " + connection.GetRemoteIP() + "   ----   " + connection.GetRemotePort() + " - " + message);
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
            m_logForm.SetLogMessage("----------------------[TCP Mobile SERVER OPEN]-------------------");
            m_logForm.SetLogMessage("------------------------------------------------------------------------");
        }
        private void OnClientConneted(CVNetTCPSocket connection)
        {
            if (m_bFinishSetting == true)
                return;
            //한솔피씨가 접속이 되면 수행할 작업
            if(connection.GetRemoteIP() == CConfigMng.Instance._strHansolPC_IP)
            {
                
                m_logForm.SetLogMessage("------------------------------------------------------------------------");
                m_logForm.SetLogMessage("        한솔 PC Connected = " + connection.GetRemoteIP().ToString() + "   ----   " + connection.GetRemotePort().ToString());
                m_logForm.SetLogMessage("------------------------------------------------------------------------");
                m_nHansolCurrentNum++;
                if (m_nHansolResetCurrent != 0)
                {
                    m_bFinishSetting = true;
                    m_logForm.SetLogMessage("                               [한솔피씨 세팅완료]                 ");
                }
                else
                {
                    m_logForm.SetLogMessage("------------------------------------------------------------------------");
                    m_logForm.SetLogMessage("                               [한솔 PC 재부팅중]                 ");
                    m_logForm.SetLogMessage("------------------------------------------------------------------------");
                    Hansol_PC_SendPacket(Hansol_PC_Controll.MSG_POWER_RESET);
                }
                
            }
            else
            {
               
                m_logForm.SetLogMessage("------------------------------------------------------------------------");
                m_logForm.SetLogMessage("        New Mobile Connected = " + connection.GetRemoteIP().ToString() + "   ----   " + connection.GetRemotePort().ToString());
                m_logForm.SetLogMessage("------------------------------------------------------------------------");
            }
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
            m_logForm.SetLogMessage("TCP Mobile Server is closed.");
        }
        public static string ByteArrayToString(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }
        #endregion
    }


}
