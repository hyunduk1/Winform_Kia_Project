using KIA_01._00_Manager;
using KIA_01._01_NetWorkSource;
using KIA_01._02_FormList;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KIA_01
{
    public partial class MainForm : Form
    {
        private static MainForm _instance;
        public static MainForm Instance { get { return _instance; } set { _instance = value; } }
        private Form m_MainForm;
        private Form m_SubForm;
        private Form m_ColorForm;
        public _01_LogDrawForm _logForm;

        public MainForm()
        {

            if (CConfigMng.Instance == null)
                CConfigMng.Instance = new CConfigMng();
            if (CNetWorkMng.Instance == null)
                CNetWorkMng.Instance = new CNetWorkMng();
            if (CDataMng.Instance == null)
                CDataMng.Instance = new CDataMng();
            if (CSerialPortMng.Instance == null)
                CSerialPortMng.Instance = new CSerialPortMng();
            if(CTCPMobileMng.Instance == null)
                CTCPMobileMng.Instance = new CTCPMobileMng();
            if (CTCPHansolSyncMng.Instance == null)
                CTCPHansolSyncMng.Instance = new CTCPHansolSyncMng();
            /*if(CSoundMng.Instance == null)
                CSoundMng.Instance = new CSoundMng();*/

            InitializeComponent();
            
            OpenChildForm(new _01_LogDrawForm(), 0);
            OpenChildForm(new _02_SettingForm(), 1);
            OpenChildForm(new ColorPicker(), 2);
            _logForm =(_01_LogDrawForm) m_MainForm;
            CNetWorkMng.Instance.Initialize((_01_LogDrawForm) m_MainForm);
            CSerialPortMng.Instance.Initialize((_01_LogDrawForm)m_MainForm);
            CDataMng.Instance.Initialize((_01_LogDrawForm)m_MainForm);
            CTCPMobileMng.Instance.Initialize((_01_LogDrawForm)m_MainForm);
            CTCPHansolSyncMng.Instance.Initialize((_01_LogDrawForm)m_MainForm);
           // CSoundMng.Instance.Initialize((_01_LogDrawForm)m_MainForm);
            this.FormClosed += MainForm_FormClosed;

            //CNetWorkMng.Instance.OnTcpOpenEvent += NetworkMng_OnTcpOpenEvent;
        }

        void OpenChildForm(Form childForm, int nParentIndex)
        {
            childForm.TopLevel = false;
            childForm.FormBorderStyle = FormBorderStyle.None;
            childForm.Dock = DockStyle.Fill;
            _MainDefaultForm.Controls.Add(childForm);
            _MainDefaultForm.Tag = childForm;
            childForm.BringToFront();
            childForm.Show();

            switch (nParentIndex)
            {
                case 0:
                    m_MainForm = childForm;
                    break;

                case 1:
                    m_SubForm = childForm;
                    break;
                case 2:
                    m_ColorForm = childForm;
                    break;

            }
            childForm.Visible = false;

        }
        private void _LogDrawFormBtn_Click(object sender, EventArgs e)
        {
            m_MainForm.Visible = true;
            m_SubForm.Visible = false;
            m_ColorForm.Visible = false;
        }

        private void _SettingFormBtn_Click(object sender, EventArgs e)
        {
            m_MainForm.Visible = false;
            m_SubForm.Visible = true;
            m_ColorForm.Visible = false;
        }

        private void guna2Panel1_Paint(object sender, PaintEventArgs e)
        {

        }
        public void LogMessage(string str)
        {
            _logForm.SetLogMessage(str);
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            CDataMng.Instance.ExitExcel();
        }

        private void _MainFormBtn_Click_1(object sender, EventArgs e)
        {
            m_MainForm.Visible = false;
            m_SubForm.Visible = false;
            m_ColorForm.Visible = false;
        }

        #region SerialEvent
        private void Front_00_Btn_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.DisplayWaitMode());
        }
        private void Front_01_Btn_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_FRONT, CSerialPortMng.Instance.Display_Scene_01());
        }
        private void CPAD_00_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_CPAD, CSerialPortMng.Instance.DisplayWaitMode());
        }
        private void CPAD_01_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_CPAD, CSerialPortMng.Instance.Display_Scene_01());
        }

        private void DHS_L_00_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_DHS_L, CSerialPortMng.Instance.DisplayWaitMode());
        }

        private void DHS_L_01_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_DHS_L, CSerialPortMng.Instance.Display_Scene_01());
        }

        private void DHS_R_00_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_DHS_R, CSerialPortMng.Instance.DisplayWaitMode());
        }

        private void DHS_R_01_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_DHS_R, CSerialPortMng.Instance.Display_Scene_01());
        }

        private void DHF_L_00_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_DHF_L, CSerialPortMng.Instance.DisplayWaitMode());
        }

        private void DHF_L_01_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_DHF_L, CSerialPortMng.Instance.Display_Scene_01());
        }

        private void DHF_R_00_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_DHF_R, CSerialPortMng.Instance.DisplayWaitMode());
        }

        private void DHF_R_01_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_DHF_R, CSerialPortMng.Instance.Display_Scene_01());
        }

        private void TAIL_L_00_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_TAIL_L, CSerialPortMng.Instance.DisplayWaitMode());
        }
        private void TAIL_L_01_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_TAIL_L, CSerialPortMng.Instance.Display_Scene_01());
        }
        private void TAIL_R_00_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_TAIL_R, CSerialPortMng.Instance.DisplayWaitMode());
        }
        private void TAIL_R_01_BTN_Click(object sender, EventArgs e)
        {
            CSerialPortMng.Instance.Send(DISPLAYPORT.ID_TAIL_R, CSerialPortMng.Instance.Display_Scene_01());
        }


        #endregion

        Byte GetHex(string srcValue)
        {
            return Convert.ToByte(srcValue, 16);
        }
        private void LED_PROTOCOL_Click(object sender, EventArgs e)
        {
            try
            {
                CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.LedProtocol(GetHex(ChanelTextBox1.Text), GetHex(ModeTextBox1.Text), GetHex(BrightTextBox2.Text), GetHex(R_TextBox3.Text), GetHex(G_TextBox4.Text), GetHex(B_TextBox5.Text)));
            }
            catch (Exception ex)
            {
                _logForm.SetLogMessage("LED 입력값 입력하세요. error : " + ex.Message);
            }
        }

        private void Motion_Send_BTN_Click(object sender, EventArgs e)
        {
            try
            {
                CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.MotionProtocol(GetHex(Motion_Type_TextBox.Text), GetHex(Motion_Send_TextBox.Text)));
            }
            catch(Exception ex)
            {
                _logForm.SetLogMessage("LED 입력값 입력하세요. error : " + ex.Message);
            }
        }
        private void Excel_Test_Btn_Click(object sender, EventArgs e)
        {

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*";
                DialogResult result = openFileDialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    try
                    {
                        string selectedFilePath = openFileDialog.FileName;
                        MessageBox.Show("선택한 파일: " + selectedFilePath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("파일을 불러오는 도중 오류가 발생했습니다: " + ex.Message);
                    }
                }
            }
            _logForm.SetLogMessage(CDataMng.Instance.ExcelData(3)[2]);
        }
        private void ALL_STOP_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.MotionProtocol(0x99, 0x00));
        }

        private void Break_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(CNetWorkMng.Instance.MotionProtocol(0x55, 0x40));
        }

        private void guna2GradientButton1_Click(object sender, EventArgs e)
        {
            CTCPHansolSyncMng.Instance.Hansol_PC_SyncSendPacket(HANSOL_SYNC_DISPLAY.ID_RIGHT_DISPLAY_CLOSE);
        }

        private void guna2Button4_Click(object sender, EventArgs e)
        {
            m_MainForm.Visible = false;
            m_SubForm.Visible = false;
            m_ColorForm.Visible = true;
        }

        private void guna2Button2_Click(object sender, EventArgs e)
        {
            CTCPMobileMng.Instance.SendWakeOnLanPacket("B4-4B-D6-5F-79-EA");
           // CTCPMobileMng.Instance.RaonTec_PC_On("10.74.20.160", "B4-4B-D6-5F-79-EA");
        }
    }
}
