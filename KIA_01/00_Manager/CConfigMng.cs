using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO;
using VNet;

namespace KIA_01._01_NetWorkSource
{
    class CConfigMng
    {
        private static CConfigMng _instance;
        public static CConfigMng Instance { get { return _instance; } set { _instance = value; } }

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);
        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);
        private static string strPath;
        private string m_strServerIp; public string _strServerIp { get { return m_strServerIp; } set { m_strServerIp = value; } }
        private int m_nServerPort; public int _nServerPort { get { return m_nServerPort; } set { m_nServerPort = value; } }
        private int m_nMobileServerPort; public int _nMobileServerPort { get { return m_nMobileServerPort; } set { m_nMobileServerPort = value; } }
        public int m_nHansolSyncPort; public int _nHansolSyncPort { get { return m_nHansolSyncPort; } set { m_nHansolSyncPort = value; } }
        private string m_strSendIp; public string _strSendIp { get { return m_strSendIp; } set { m_strSendIp = value; } }
        private int m_nSendPort; public int _nSendPort { get { return m_nSendPort; } set { m_nSendPort = value; } }
        private string m_strExcelLoad; public string _strExcelLoad { get { return m_strExcelLoad; } set { m_strExcelLoad = value; } }
        private float m_fKeepAliveTimeout; public float _fkeepAliveTimeout { get { return m_fKeepAliveTimeout; } set { m_fKeepAliveTimeout = value; } }
        private string m_strSoundPath; public string _strSoundPath { get { return m_strSoundPath; } set { m_strSoundPath = value; } }
        private string m_strHansolPC_IP; public string _strHansolPC_IP { get { return m_strHansolPC_IP; } set { m_strHansolPC_IP = value; } }
        public CConfigMng()
        {
            Console.WriteLine(strPath = Directory.GetCurrentDirectory() + "\\Config.ini");
            strPath = Directory.GetCurrentDirectory() + "\\Config.ini";
            Console.WriteLine(strPath);
            m_strServerIp = IniReadValue("NET_WORK", "SERVER_IP");
            m_nServerPort = IniReadValueInt("NET_WORK", "SERVER_PORT");
            m_nMobileServerPort = IniReadValueInt("NET_WORK", "MOBILE_SERVER_PORT");
            m_nHansolSyncPort = IniReadValueInt("NET_WORK", "HANSOL_SYNC_PORT");

            m_strSendIp = IniReadValue("NET_WORK", "SEND_IP");
            m_nSendPort = IniReadValueInt("NET_WORK", "SEND_PORT");
            m_strExcelLoad = IniReadValue("NET_WORK", "EXCEL_PATH");
            m_strSoundPath = IniReadValue("NET_WORK", "SOUND_PATH");

            m_fKeepAliveTimeout = IniReadValueFloat("NET_WORK", "KEEP_ALIVE_TIME");
            m_strHansolPC_IP = IniReadValue("NET_WORK", "HANSOL_PC_IP");
        }
        ~CConfigMng()
        {

        }

        public static string IniReadValue(string Section, string Key)
        {
            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString(Section, Key, "", temp, 255, strPath);
            return temp.ToString();
        }
        public static float IniReadValueFloat(string Section, string Key)
        {
            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString(Section, Key, "", temp, 255, strPath);
            float result = 0.0f;
            float.TryParse(temp.ToString(), out result);
            return result;
        }
        public static bool IniReadValuebool(string Section, string Key)
        {
            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString(Section, Key, "", temp, 255, strPath);
            int result = 0;
            int.TryParse(temp.ToString(), out result);
            if (result == 1)
            {
                return true;
            }
            return false;
        }
        public static int IniReadValueInt(string Section, string Key)
        {
            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString(Section, Key, "", temp, 255, strPath);
            int result = 0;
            int.TryParse(temp.ToString(), out result);
            return result;
        }
        public static int IniReadValueIntTimeData(string Section, string Key, string strDataPath)
        {
            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString(Section, Key, "", temp, 255, strDataPath);
            int result = 0;
            int.TryParse(temp.ToString(), out result);
            return result;
        }
    }
}
