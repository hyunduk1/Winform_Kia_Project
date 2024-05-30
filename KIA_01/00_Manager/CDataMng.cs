using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Office.Interop.Excel;
using System.Runtime.InteropServices;
using KIA_01._01_NetWorkSource;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;
using System.IO;
using System.Data.Common;
using KIA_01._02_FormList;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using VNet;

namespace KIA_01._00_Manager
{
    internal class CDataMng
    {
        private static CDataMng _instance;
        public static CDataMng Instance { get { return _instance; } set { _instance = value; } }

        private _01_LogDrawForm m_logForm;


        static Excel.Application excelApp = null;
        static Excel.Workbook workBook = null;
        static Excel.Worksheet workSheet = null;


        public CDataMng()
        {
        }
        ~CDataMng() { }

        public void Initialize(_01_LogDrawForm _logForm)
        {
            m_logForm = _logForm;
            try
            {
                string path = Path.Combine(CConfigMng.Instance._strExcelLoad);
                excelApp = new Excel.Application();
                workBook = excelApp.Workbooks.Open(CConfigMng.Instance._strExcelLoad);
                workSheet = workBook.Worksheets.get_Item(3) as Excel.Worksheet;
            }
            catch (Exception e)
            {
                m_logForm.SetLogMessage(e.ToString());
            }
        }

        #region ExcelLoad
        public string[] ExcelData(short Index)
        {
            try
            {
                if (Index == 1)
                    return null;

                //string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                Excel.Range range = workSheet.UsedRange;
                int rowCount = range.Columns.Count;

                string[] tempArray = new string[3];
                tempArray[0] = range.Cells[Index, 2].Value2.ToString();
                tempArray[1] = range.Cells[Index, 3].Value2.ToString();
                tempArray[2] = range.Cells[Index, 4].Value2.ToString();
                return tempArray;


            }
            catch (Exception ex)
            {
                m_logForm.SetLogMessage(ex.Message);
                return null; 
            }
            finally
            {
                /*ReleaseObject(workSheet);
                ReleaseObject(workBook);
                ReleaseObject(excelApp);*/
            }
        }
        public void ExitExcel() // 엑셀 닫기
        {
            try
            {
                workBook.Close(true);
                excelApp.Quit();

                ReleaseObject(workSheet);
                ReleaseObject(workBook);
                ReleaseObject(excelApp);
            }
            catch
            {

            }
        }

        static void ReleaseObject(object obj)//가비지
        {
            try
            {
                if (obj != null)
                {
                    Marshal.ReleaseComObject(obj);
                    obj = null;
                }
            }
            catch (Exception ex)
            {
                obj = null;
                throw ex;
            }
            finally
            {
                GC.Collect();
            }
        }
        //------------------------------------------------------------------------------------
        #endregion
    }
}