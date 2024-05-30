using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Runtime.InteropServices;
using System.IO;
using Microsoft.SqlServer.Server;
using KIA_01._00_Manager;

namespace KIA_01._02_FormList
{
    public partial class _01_LogDrawForm : Form
    {
        private bool m_bLogColorChange = true;
        public _01_LogDrawForm()
        {
            InitializeComponent();
        }
        //----------------------------------------------------------------------------------------------------------------
        #region Event
        private void LogDrawForm_Load(object sender, EventArgs e)
        {
        }

        #endregion
        //-------------------------------------------------------------------------------------------------------------------

        #region Log
        public void SetLogMessage(string InMessage)
        {
            try
            {
                if (_LOGrichTextBox1.InvokeRequired)
                {
                    _LOGrichTextBox1.Invoke(new Action(() => SetLogMessage(InMessage.ToString())));
                    return;
                }
                string SetMessage = "";
                string strTime = "";

                GetSystemTime(out strTime);

                SetMessage = string.Format(strTime.ToString() + "  " + InMessage.ToString());

                _LOGrichTextBox1.SelectionFont = new Font("맑은 고딕", 16, FontStyle.Bold);

                if (m_bLogColorChange == true)
                    _LOGrichTextBox1.SelectionColor = Color.FromArgb(255, 255, 255);
                else
                    _LOGrichTextBox1.SelectionColor = Color.FromArgb(200, 200, 200);

                m_bLogColorChange = !m_bLogColorChange;

                _LOGrichTextBox1.SelectionCharOffset = -10;
                _LOGrichTextBox1.AppendText(SetMessage + Environment.NewLine);

                _LOGrichTextBox1.SelectionCharOffset = 0;
                _LOGrichTextBox1.SelectionStart = _LOGrichTextBox1.Text.Length;
                _LOGrichTextBox1.ScrollToCaret();
            }
            catch (Exception e)
            {
                SetLogMessage(e.ToString());
            }
        }
        public void GetSystemTime(out string OutTime)
        {
            OutTime = string.Format("[" + DateTime.Now.ToString("yyyy.MM.dd") + "_" + DateTime.Now.ToString("HH:mm:ss") + "] ");
        }
        #endregion
    }
}
