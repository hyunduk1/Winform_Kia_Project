using KIA_01._00_Manager;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KIA_01._02_FormList
{
    public partial class _02_SettingForm : Form
    {
        public _02_SettingForm()
        {
            InitializeComponent();
        }

        private void ALL_STOP_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_EMERGENCE_STOP);
        }


        //Handle
        private void Handle_Forword_Stop_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_FORWORD_STOP);
        }
        private void Handle_Forword_1_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_FORWORD_OPEN);
        }
        private void Handle_Forword_2_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_FORWORD_CLOSE);
        }
        private void Handle_Forword_State_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_FORWORD_STATE);
        }


        //핸들틸팅
        private void Handle_Tilting_Stop_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_TILTING_STOP);
        }
        private void Handle_Tilting_1_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_TILTING_OPEN);
        }
        private void Handle_Tilting_2_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_TILTING_CLOSE);
        }
        private void Handle_Tilting_State_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_HANDLE_TILTING_STATE);
        }

        //사이드미러 왼
        private void Side_Left_Stop_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_LEFT_STOP);
        }
        private void Side_Left_1_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_LEFT_OPEN);
        }
        private void Side_Left_2_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_LEFT_CLOSE);
        }
        private void Side_Left_State_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_LEFT_STATE);
        }

        //사이드미러 오
        private void Side_Right_Stop_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_RIGHT_STOP);
        }
        private void Side_Right_1_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_RIGHT_OPEN);
        }
        private void Side_Right_2_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_RIGHT_CLOSE);
        }
        private void Side_Right_State_BTN_Click(object sender, EventArgs e)
        {
            CNetWorkMng.Instance.SendPacket(PROTOCOL.ID_SIDE_MIRROW_RIGHT_STATE);
        }

        private void Front_Left_Stop_Click(object sender, EventArgs e)
        {

        }
    }
}
