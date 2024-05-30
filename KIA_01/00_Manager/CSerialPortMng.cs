using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Office.Interop.Excel;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using KIA_01._02_FormList;
using System.Windows.Forms;

namespace KIA_01._00_Manager
{
    internal class CSerialPortMng
    {
        private static CSerialPortMng _instance;
        public static CSerialPortMng Instance { get { return _instance; } set { _instance = value; } }

        private RS232Connection[] m_RS232connection;


        private _01_LogDrawForm m_logForm;
        public CSerialPortMng()
        {
            m_RS232connection = new RS232Connection[8];
        }
        ~CSerialPortMng()
        {

        }
        public void Initialize(_01_LogDrawForm _logFoem)
        {
            m_logForm = _logFoem;

            m_RS232connection[0] = new RS232Connection("COM5", 115200, 0x0A, OnOpenSerialPort, OnMessageSerialPort, OnErrorSerialPort, OnCloseSerialPort);
            m_RS232connection[1] = new RS232Connection("COM6", 115200, 0x0A, OnOpenSerialPort, OnMessageSerialPort, OnErrorSerialPort, OnCloseSerialPort);
            m_RS232connection[2] = new RS232Connection("COM7", 115200, 0x0A, OnOpenSerialPort, OnMessageSerialPort, OnErrorSerialPort, OnCloseSerialPort);
            m_RS232connection[3] = new RS232Connection("COM8", 115200, 0x0A, OnOpenSerialPort, OnMessageSerialPort, OnErrorSerialPort, OnCloseSerialPort);
            m_RS232connection[4] = new RS232Connection("COM9", 115200, 0x0A, OnOpenSerialPort, OnMessageSerialPort, OnErrorSerialPort, OnCloseSerialPort);
            m_RS232connection[5] = new RS232Connection("COM10", 115200, 0x0A, OnOpenSerialPort, OnMessageSerialPort, OnErrorSerialPort, OnCloseSerialPort);
            m_RS232connection[6] = new RS232Connection("COM11", 115200, 0x0A, OnOpenSerialPort, OnMessageSerialPort, OnErrorSerialPort, OnCloseSerialPort);
            m_RS232connection[7] = new RS232Connection("COM12", 115200, 0x0A, OnOpenSerialPort, OnMessageSerialPort, OnErrorSerialPort, OnCloseSerialPort);
            for (int i = 0; i < 8; i++)
                m_RS232connection[i].Connect();
        }
        public void DisPlayBasicMode()
        {
            Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.Display_Scene_01());
            Send(DISPLAYPORT.ID_CPAD, CSerialPortMng.Instance.Display_Scene_01());
            Send(DISPLAYPORT.ID_DHS_L, CSerialPortMng.Instance.Display_Scene_02());
            Send(DISPLAYPORT.ID_DHS_R, CSerialPortMng.Instance.Display_Scene_02());
            Send(DISPLAYPORT.ID_DHF_L, CSerialPortMng.Instance.Display_Scene_02());
            Send(DISPLAYPORT.ID_DHF_R, CSerialPortMng.Instance.Display_Scene_02());
            Send(DISPLAYPORT.ID_TAIL_L, CSerialPortMng.Instance.Display_Scene_01());
            Send(DISPLAYPORT.ID_TAIL_R, CSerialPortMng.Instance.Display_Scene_01());
        }
        public void DisPlayAllOut()
        {
            Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.DisplayWaitMode());
            Send(DISPLAYPORT.ID_CPAD, CSerialPortMng.Instance.DisplayWaitMode());
            Send(DISPLAYPORT.ID_DHS_L, CSerialPortMng.Instance.DisplayWaitMode());
            Send(DISPLAYPORT.ID_DHS_R, CSerialPortMng.Instance.DisplayWaitMode());
            Send(DISPLAYPORT.ID_DHF_L, CSerialPortMng.Instance.DisplayWaitMode());
            Send(DISPLAYPORT.ID_DHF_R, CSerialPortMng.Instance.DisplayWaitMode());
            Send(DISPLAYPORT.ID_TAIL_L, CSerialPortMng.Instance.DisplayWaitMode());
            Send(DISPLAYPORT.ID_TAIL_R, CSerialPortMng.Instance.DisplayWaitMode());
        }

        public byte[] DisplayPortSend(byte Send)
        {
            byte[] tempArray = { 0x2E, 0x30, Send, 0x0D, 0x0A};
            return tempArray;
        }
        public byte[] DisplayWaitMode()
        {
            byte[] tempArray = { 0x2E, 0x30, 0x30, 0x0D, 0x0A};
            return tempArray;
        }
        public byte[] Display_Scene_01()
        {
            byte[] tempArray = { 0x2E, 0x30, 0x31, 0x0D, 0x0A};
            return tempArray;
        }
        public byte[] Display_Scene_02()
        {
            byte[] tempArray = { 0x2E, 0x30, 0x32, 0x0D, 0x0A};
            return tempArray;
        }
        public byte[] Display_Scene_03()
        {
            byte[] tempArray = { 0x2E, 0x30, 0x33, 0x0D, 0x0A };
            return tempArray;
        }

        private string ByteLog(byte[] tempArray)
        {
            return tempArray[0].ToString("X") + " " + tempArray[1].ToString("X") + " " + tempArray[2].ToString("X") + " 0" + tempArray[3].ToString("X") + " 0" + tempArray[4].ToString("X");
        }

        public void Send(DISPLAYPORT dISPLAYPORT, byte[] Rs232_Byte_Data)
        {
            try
            {
                switch (dISPLAYPORT)
                {
                    case DISPLAYPORT.ID_FRONT:
                        m_logForm.SetLogMessage("Serial Send -- FRONT -- " + ByteLog(Rs232_Byte_Data));
                        m_RS232connection[0].SendData(Rs232_Byte_Data);
                        break;

                    case DISPLAYPORT.ID_CPAD:
                        m_logForm.SetLogMessage("Serial Send -- CPAD -- " +ByteLog(Rs232_Byte_Data));
                        m_RS232connection[1].SendData(Rs232_Byte_Data);
                        break;

                    case DISPLAYPORT.ID_DHS_L:
                        m_logForm.SetLogMessage("Serial Send -- DHS_L -- " + ByteLog(Rs232_Byte_Data));
                        m_RS232connection[2].SendData(Rs232_Byte_Data);
                        break;

                    case DISPLAYPORT.ID_DHS_R:
                        m_logForm.SetLogMessage("Serial Send -- DHS_R -- " + ByteLog(Rs232_Byte_Data));
                        m_RS232connection[3].SendData(Rs232_Byte_Data);
                        break;

                    case DISPLAYPORT.ID_DHF_L:
                        m_logForm.SetLogMessage("Serial Send -- DHF_L -- " + ByteLog(Rs232_Byte_Data));
                        m_RS232connection[4].SendData(Rs232_Byte_Data);
                        break;

                    case DISPLAYPORT.ID_DHF_R:
                        m_logForm.SetLogMessage("Serial Send -- DHF_R -- " + ByteLog(Rs232_Byte_Data));
                        m_RS232connection[5].SendData(Rs232_Byte_Data);
                        break;

                    case DISPLAYPORT.ID_TAIL_L:
                        m_logForm.SetLogMessage("Serial Send -- TAIL_L -- " + ByteLog(Rs232_Byte_Data));
                        m_RS232connection[6].SendData(Rs232_Byte_Data);
                        break;

                    case DISPLAYPORT.ID_TAIL_R:
                        m_logForm.SetLogMessage("Serial Send -- TAIL_R -- " + ByteLog(Rs232_Byte_Data));
                        m_RS232connection[7].SendData(Rs232_Byte_Data);
                        break;
                }
            }
            catch (Exception e)
            {
                m_logForm.SetLogMessage(e.ToString());
            }
        }

        #region SerialEvent

        void OnOpenSerialPort(RS232Connection connection)
        {
            m_logForm.SetLogMessage("open Serial : " + connection._port);
        }
        void OnMessageSerialPort(byte[] byData, RS232Connection connection)
        {
            m_logForm.SetLogMessage(connection._port.ToString() + " : send");
        }
        void OnErrorSerialPort(int errorCode, string strMessage, RS232Connection connection)
        {
            m_logForm.SetLogMessage(errorCode.ToString() + " : " + strMessage);
        }
        void OnCloseSerialPort(RS232Connection connection)
        {
            connection.Disconnect();
        }

        #endregion

    }
}
