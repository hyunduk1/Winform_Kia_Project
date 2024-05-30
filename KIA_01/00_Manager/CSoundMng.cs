using KIA_01._02_FormList;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Media;
using KIA_01._01_NetWorkSource;

namespace KIA_01._00_Manager
{
    internal class CSoundMng
    {
        private static CSoundMng _instance;
        public static CSoundMng Instance { get { return _instance; } set { _instance = value; } }

        private _01_LogDrawForm m_logForm;

        private SoundPlayer m_soundPlayer;
        public CSoundMng()
        {
            m_soundPlayer = new SoundPlayer();
        }
        ~CSoundMng()
        {

        }
        public void Initialize(_01_LogDrawForm _logFoem)
        {
            m_logForm = _logFoem;
            try
            {
                m_soundPlayer.SoundLocation = CConfigMng.Instance._strSoundPath;
                m_soundPlayer.Play();
            }
            catch (Exception e)
            {
                m_logForm.SetLogMessage(e.Message);
            }
            
        }
    }
}
