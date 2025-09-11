using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace UniversalUtilities
{
    public struct ColumnStruct
    {
        public string columnName;
        public Type columnType;
    }
    public abstract class CustomDataGridView<T> : DataGridView
    {
        public string ColumnWidthFile { get; set; }
        public string ColumnShownMenuFile { get; set; }
        public Color Background { get; private set; }
        public Color SelectColor { get; private set; }
        public DataTable myDataTable = new();
        private readonly List<ColumnStruct> columnCollection = new();
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // 禁掉清除背景消息
                return;
            base.WndProc(ref m);
        }
        private void EnableDoubleBuffering()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
        private void InitializeComponent()
        {
            DataGridViewCellStyle dataGridViewCellStyle35 = new();
            DataGridViewCellStyle dataGridViewCellStyle36 = new();
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToResizeColumns = true;
            AllowUserToResizeRows = false;
            BackgroundColor = Background;
            BorderStyle = BorderStyle.None;
            //ReadOnly = false;
            AllowDrop = true;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            EnableHeadersVisualStyles = false;

            EditMode = DataGridViewEditMode.EditProgrammatically;
            base.DoubleBuffered = true;
            GridColor = Color.Silver;

            RowHeadersVisible = false;
            RowTemplate.Height = 23;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            ShowCellToolTips = false;
            base.Font = new Font("微软雅黑", 10.5F, FontStyle.Regular);
            dataGridViewCellStyle35.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle35.BackColor = Background;
            dataGridViewCellStyle35.Font = new Font("微软雅黑", 10.5F, FontStyle.Regular);
            dataGridViewCellStyle35.ForeColor = Color.White;
            dataGridViewCellStyle35.SelectionBackColor = Background;
            dataGridViewCellStyle35.SelectionForeColor = Color.White;
            dataGridViewCellStyle35.WrapMode = DataGridViewTriState.False;
            ColumnHeadersDefaultCellStyle = dataGridViewCellStyle35;

            dataGridViewCellStyle36.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle36.BackColor = Background;
            dataGridViewCellStyle36.Font = new Font("微软雅黑", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134);
            dataGridViewCellStyle36.ForeColor = Color.White;
            dataGridViewCellStyle36.SelectionBackColor = SelectColor;
            dataGridViewCellStyle36.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle36.WrapMode = DataGridViewTriState.False;
            DefaultCellStyle = dataGridViewCellStyle36;

        }
        public CustomDataGridView(List<ColumnStruct> columns, Color back, Color selectBack)
        {
            Background = back;
            SelectColor = selectBack;
            EnableDoubleBuffering();
            InitializeComponent();

            columnCollection = columns;
            CreateDataTable();
            DataSource = myDataTable;
            Scroll += CustomDataGridView_Scroll;
            //columnCollection = new List<ColumnStruct>();
            //Columns.Clear();
            //DataSource = myDataTable;
            //ColumnHeaderMouseClick += new DataGridViewCellMouseEventHandler(base_ColumnHeaderMouseClick);
            Resize += ChannelList_Resize;
            Scroll += ChannelList_Scroll;
            cols = new List<ColumnWidth>();
            ColumnWidthChanged += ChannelList_ColumnWidthChanged;
            KeyDown += CustomDataGridView_KeyDown;
        }
        public abstract void ShowRows(List<T> objList);
        public abstract void ShowRow(T k);
        public abstract void ChangeValues(List<T> obj);
        public abstract void ChangeValue(T obj);
        private void CreateDataTable()
        {
            myDataTable = new DataTable("freshTable");
            myDataTable.Columns.Clear();
            myDataTable.Rows.Clear();
            DataColumn column;
            foreach (ColumnStruct c in columnCollection)
            {
                column = new DataColumn();
                column.DataType = c.columnType;
                column.ColumnName = c.columnName;
                myDataTable.Columns.Add(column);
            }
            base.DataSource = myDataTable;
        }
        public void ShowColumn(string column)
        {
            foreach (DataGridViewColumn C in Columns)
            {
                if (C.HeaderText == column)
                {
                    C.Visible = true;
                }
            }
        }
        public void HideColumn(string column)
        {
            foreach (DataGridViewColumn C in Columns)
            {
                if (C.HeaderText == column)
                {
                    C.Visible = false;
                }
            }
        }
        public bool ShownInColumns(string column)
        {
            foreach (DataGridViewColumn C in Columns)
            {
                if (C.HeaderText == column)
                {
                    return C.Visible;
                }
            }
            return false;
        }
        public void SaveColumn()
        {
            if (Columns.Count == 0)
                return;
            string reg = "";
            foreach (DataGridViewColumn column in base.Columns)
            {
                if (column.Visible)
                { reg += "1,"; }
                else
                { reg += "0,"; }
            }
            reg = reg.Substring(0, reg.Length - 1);
            FileStream fs = null;
            try
            {
                fs = new FileStream(ColumnShownMenuFile, FileMode.Create);
                using (StreamWriter rs = new StreamWriter(fs))
                {
                    fs = null;
                    rs.Write(reg);
                    rs.Flush();
                }
            }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
            }
        }
        public void ReadColumn()
        {
            try
            {
                FileInfo file = new FileInfo(ColumnShownMenuFile);
                if (file.Exists)
                {
                    string str;
                    FileStream fs = null;
                    try
                    {
                        fs = new FileStream(file.FullName, FileMode.Open);
                        using (StreamReader fr = new StreamReader(fs))
                        {
                            fs = null;
                            str = fr.ReadToEnd();
                        }
                    }
                    finally
                    {
                        if (fs != null)
                        {
                            fs.Dispose();
                        }
                    }
                    string[] s;
                    s = str.Split(',');
                    for (int i = 0; i < columnCollection.Count; i++)
                    {
                        if (i == s.Length)
                        {
                            ShowColumn(columnCollection[i].columnName);
                        }
                        else
                        {
                            if (s[i] == "0")
                            {
                                HideColumn(columnCollection[i].columnName);
                            }
                            else
                            {
                                ShowColumn(columnCollection[i].columnName);
                            }
                        }
                    }
                }
                else
                {
                    ColumnShowAll();
                }
            }
            catch (Exception e)
            {

            }
        }
        public void ColumnShowAll()
        {
            foreach (DataGridViewColumn C in Columns)
            {
                C.Visible = true;
            }
            SaveColumn();
        }
        #region 控制字体大小
        private void CustomDataGridView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Up)
            {
                IncreaseFont();
                e.Handled = true;
            }
            if (e.Control && e.KeyCode == Keys.Down)
            {
                DecreaseFont();
                e.Handled = true;
            }
        }
        private void ChannelList_Scroll(object sender, ScrollEventArgs e)
        {
            ChangeRowHeight();
        }
        private void ChannelList_Resize(object sender, EventArgs e)
        {
            ChangeRowHeight();
        }
        private void CustomDataGridView_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            ChangeRowHeight();
        }
        private void CustomDataGridView_Scroll(object sender, ScrollEventArgs e)
        {
            ChangeRowHeight();
        }
        private void Base_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            ChangeRowHeight();
        }
        public virtual void IncreaseFont()
        {
            if (RowCount == 0)
            { return; }
            base.Font = new Font(base.Font.Name, base.Font.Size + 0.5f, FontStyle.Regular, GraphicsUnit.Point);
            ChangeRowHeight();
        }
        public virtual void DecreaseFont()
        {
            if (RowCount == 0)
            { return; }
            if (base.Font.Size < 6)
            { return; }
            base.Font = new Font(base.Font.Name, base.Font.Size - 0.5f, FontStyle.Regular, GraphicsUnit.Point);
            ChangeRowHeight();
        }
        public virtual void ChangeRowHeight()
        {
            if (Rows.Count == 0)
            { return; }
            if (FirstDisplayedCell != null)
            {
                for (int i = FirstDisplayedCell.RowIndex; i < DisplayedRowCount(false) + FirstDisplayedCell.RowIndex; i++)
                {
                    Rows[i].Height = (int)base.Font.GetHeight() + 5;
                }
            }
        }
        #endregion
        #region 读取列宽
        [Serializable]
        private struct ColumnWidth
        {
            public int ID;
            public int width;
        }
        private readonly List<ColumnWidth> cols = new();
        private void ChannelList_ColumnWidthChanged(object sender, DataGridViewColumnEventArgs e)
        {
            SaveColumnWidth();
        }

        public virtual void OrignalColumnWidth()
        {
            if (File.Exists(ColumnWidthFile) == true)
            {
                ReadColumnWidth();
            }
        }
        /// <summary>
        /// 将各列列宽保存到文件
        /// </summary>
        private void SaveColumnWidth()
        {
            cols.Clear();
            foreach (DataGridViewColumn column in Columns)
            {
                ColumnWidth cw;
                cw.ID = column.Index;
                cw.width = column.Width;
                cols.Add(cw);
            }
            using FileStream fs = new(ColumnWidthFile, FileMode.Create);
            StreamWriter sw = new(fs);
            sw.Write(JsonConvert.SerializeObject(cols, Formatting.Indented));
            sw.Flush();
            sw.Close();
            sw.Dispose();
            fs.Close();
            fs.Dispose();
            //fs.Dispose();
        }
        /// <summary>
        /// 从保存的文件中读取各列列宽并赋值给各列
        /// </summary>
        private void ReadColumnWidth()
        {
            //try
            //{
            if (Columns.Count == 0)
                return;
            List<ColumnWidth> cols = new();
            using (FileStream fs = File.OpenRead(ColumnWidthFile))
            {
                StreamReader sr = new(fs);
                string str = sr.ReadToEnd();
                cols = JsonConvert.DeserializeObject<List<ColumnWidth>>(str);
            }
            foreach (ColumnWidth cw in cols)
            {
                Columns[cw.ID].Width = cw.width;
            }
            //}
            //catch
            //{ }
        }
        #endregion 
    }
}
