using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class PortableManager : Form
{
    private static readonly Color Ink = Color.FromArgb(30, 35, 48);
    private static readonly Color Muted = Color.FromArgb(89, 96, 112);
    private static readonly Color Canvas = Color.FromArgb(246, 247, 250);
    private static readonly Color Line = Color.FromArgb(223, 226, 234);
    private static readonly Color Accent = Color.FromArgb(94, 71, 153);
    private static readonly Color AccentSoft = Color.FromArgb(240, 235, 249);
    private static readonly Color Danger = Color.FromArgb(161, 45, 58);
    private readonly string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    private readonly Panel tabs = new Panel();
    private readonly List<Panel> pages = new List<Panel>();
    private readonly List<ActionButton> navigation = new List<ActionButton>();
    private readonly TableLayoutPanel workspace = new TableLayoutPanel();
    private readonly TableLayoutPanel navStack = new TableLayoutPanel();
    private readonly Label pageTitle = new Label();
    private readonly Label pageSubtitle = new Label();
    private readonly TextBox log = new TextBox();
    private readonly Label status = new Label();
    private readonly Label saved = new Label();
    private readonly ProgressBar progress = new ProgressBar();
    private readonly DataGridView backups = new DataGridView();
    private readonly TextBox backupSearch = new TextBox();
    private readonly ComboBox backupType = new ComboBox();
    private readonly Label backupSelection = new Label();
    private readonly Label backupDetails = new Label();
    private readonly ActionButton restoreButton = new ActionButton();
    private readonly ActionButton previewBackupButton = new ActionButton();
    private readonly ActionButton deleteButton = new ActionButton();
    private readonly ComboBox exportSource = new ComboBox();
    private readonly ComboBox kind = new ComboBox();
    private readonly ComboBox format = new ComboBox();
    private readonly Label exportSummary = new Label();
    private readonly Label exportResult = new Label();
    private readonly ActionButton openExportButton = new ActionButton();
    private readonly CheckBox restoreIcaros = new CheckBox();
    private readonly CheckBox restorePlaylist = new CheckBox();
    private readonly CheckBox importIcaros = new CheckBox();
    private readonly TextBox importPath = new TextBox();
    private readonly TextBox importPreview = new TextBox();
    private readonly Label importState = new Label();
    private readonly ActionButton importButton = new ActionButton();
    private readonly ActionButton logToggle = new ActionButton();
    private readonly ToolTip hints = new ToolTip();
    private List<Snapshot> snapshots = new List<Snapshot>();
    private string validatedImport = "";
    private string lastExport = "";
    private bool busy, refreshing, recordsVisible;

    private sealed class Snapshot
    {
        public string Path, Name, Type, Components;
        public DateTime Time;
        public bool Association;
        public override string ToString() { return Name; }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 3 && args[0] == "--elevated-job")
        {
            int code = 1;
            string result;
            try { result = Execute(Encoding.Unicode.GetString(Convert.FromBase64String(args[1])), null, out code); }
            catch (Exception error) { result = "操作未完成：" + error.Message; }
            File.WriteAllText(args[2], result, new UTF8Encoding(false));
            Environment.Exit(code);
            return;
        }
        try { Application.Run(new PortableManager()); }
        catch (Exception error) { MessageBox.Show(error.Message, "PotPlayer 便携管理", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private PortableManager()
    {
        Text = "PotPlayer 便携管理";
        Font = new Font("Microsoft YaHei UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 660);
        Rectangle available = Screen.PrimaryScreen.WorkingArea;
        ClientSize = new Size(Math.Min(1140,available.Width-40),Math.Min(820,available.Height-80));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Canvas; ForeColor = Ink; DoubleBuffered = true;
        string iconPath = System.IO.Path.Combine(root, @"x64\PotPlayerMini64.exe");
        if (File.Exists(iconPath)) Icon = Icon.ExtractAssociatedIcon(iconPath);

        TableLayoutPanel shell = Grid(2);
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Dock = DockStyle.Fill;
        Panel sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 24, 18, 18) };
        sidebar.Paint += delegate(object sender, PaintEventArgs e) { using (Pen pen = new Pen(Line)) e.Graphics.DrawLine(pen,sidebar.Width-1,0,sidebar.Width-1,sidebar.Height); };
        TableLayoutPanel side = Grid(1);
        side.RowCount = 4;
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 244));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        side.Dock = DockStyle.Fill;
        Panel brand = new Panel { Dock = DockStyle.Fill };
        if (Icon != null) brand.Controls.Add(new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(0,0,32,32), AccessibleName = "PotPlayer" });
        brand.Controls.Add(new Label { Text = "PotPlayer", Font = new Font(Font.FontFamily,15,FontStyle.Bold), Bounds = new Rectangle(40,0,120,34) });
        brand.Controls.Add(new Label { Text = "便携管理", ForeColor = Muted, Bounds = new Rectangle(0,48,152,24) });
        side.Controls.Add(brand,0,0);
        navStack.Dock = DockStyle.Top; navStack.Height = 224; navStack.ColumnCount = 1; navStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); navStack.Margin = new Padding(0);
        side.Controls.Add(navStack,0,1);
        TableLayoutPanel location = Grid(1); location.Dock = DockStyle.Fill;
        location.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        location.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        location.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        location.Controls.Add(TextLabel("当前便携目录",9,Muted),0,0);
        Label pathLabel = TextLabel(new DirectoryInfo(root).Name,9,Ink); pathLabel.AutoEllipsis = true;
        hints.SetToolTip(pathLabel,root); location.Controls.Add(pathLabel,0,1);
        location.Controls.Add(Button("打开目录",delegate { OpenDirectory(root); },false),0,2);
        side.Controls.Add(location,0,3); sidebar.Controls.Add(side); shell.Controls.Add(sidebar,0,0);

        workspace.Dock = DockStyle.Fill; workspace.ColumnCount = 1; workspace.RowCount = 4;
        workspace.Padding = new Padding(28, 0, 28, 0); workspace.Margin = new Padding(0);
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
        Panel header = new Panel { Dock = DockStyle.Fill };
        pageTitle.Font = new Font(Font.FontFamily,22,FontStyle.Bold); pageTitle.Bounds = new Rectangle(0,20,700,38);
        pageSubtitle.ForeColor = Muted; pageSubtitle.Bounds = new Rectangle(0,65,700,26);
        header.Resize += delegate { pageTitle.Width = pageSubtitle.Width = header.ClientSize.Width; };
        header.Controls.Add(pageTitle); header.Controls.Add(pageSubtitle); workspace.Controls.Add(header,0,0);
        tabs.Dock = DockStyle.Fill; tabs.Margin = new Padding(0); workspace.Controls.Add(tabs,0,1);
        Panel records = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0,12,0,0) };
        log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.Dock = DockStyle.Fill; log.BackColor = Color.White; log.BorderStyle = BorderStyle.FixedSingle; log.Font = new Font("Microsoft YaHei UI",9);
        log.AccessibleName = "操作记录"; records.Controls.Add(log); workspace.Controls.Add(records,0,2);
        TableLayoutPanel footer = Grid(3); footer.Dock = DockStyle.Fill;
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,116));
        status.Text = "就绪"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true;
        progress.Dock = DockStyle.Fill; progress.Margin = new Padding(8,20,16,20); progress.Visible = false;
        SetupButton(logToggle,"显示记录(&L)",false); logToggle.Click += delegate { ShowRecords(!recordsVisible); };
        logToggle.Dock = DockStyle.Fill; logToggle.Margin = new Padding(0,7,0,7);
        footer.Controls.Add(status,0,0); footer.Controls.Add(progress,1,0); footer.Controls.Add(logToggle,2,0); workspace.Controls.Add(footer,0,3);
        shell.Controls.Add(workspace,1,0); Controls.Add(shell);
        BuildHome(); BuildBackups(); BuildExport(); BuildImport(); RefreshSnapshots(); ShowPage(0);
        KeyPreview = true;
        KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D4) { ShowPage((int)e.KeyCode-(int)Keys.D1); e.Handled = true; }
        };
        FormClosing += delegate(object sender, FormClosingEventArgs e) {
            if (busy) { e.Cancel = true; status.Text = "正在操作，请等待完成后再关闭窗口。"; }
        };
    }

    private static TableLayoutPanel Grid(int columns)
    {
        TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = columns, Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent };
        if (columns==1) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        return grid;
    }
    private Label TextLabel(string text, float size, Color color)
    {
        return new Label { Text = text, Font = new Font(Font.FontFamily,size), ForeColor = color, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), AutoEllipsis = true };
    }
    private Label Note(string text, int height)
    {
        Label label = TextLabel(text,9.5f,Muted); label.Height = height; label.Dock = DockStyle.Top; label.Margin = new Padding(0,4,0,12); return label;
    }
    private Panel Page(string name, string description)
    {
        int index = pages.Count;
        Panel page = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Visible = false, AccessibleName = name };
        pages.Add(page); tabs.Controls.Add(page);
        ActionButton nav = Button(name,delegate { ShowPage(index); },false); nav.Dock = DockStyle.Fill; nav.Margin = new Padding(0,0,0,8); nav.TextAlign = ContentAlignment.MiddleLeft;
        nav.Tag = description; navigation.Add(nav); navStack.RowStyles.Add(new RowStyle(SizeType.Absolute,56)); navStack.Controls.Add(nav,0,index);
        hints.SetToolTip(nav,"Ctrl+"+(index+1)); return page;
    }
    private void ShowPage(int index)
    {
        if (busy) return;
        for (int i=0;i<pages.Count;i++) {
            pages[i].Visible = i==index; navigation[i].Selected = i==index;
            navigation[i].AccessibleDescription = i==index ? "当前页面" : "切换到"+navigation[i].Text;
            navigation[i].Invalidate();
        }
        pages[index].BringToFront(); pageTitle.Text = navigation[index].Text; pageSubtitle.Text = (string)navigation[index].Tag;
    }
    private void ShowRecords(bool show)
    {
        recordsVisible = show; workspace.RowStyles[2].Height = show ? 166 : 0; logToggle.Text = show ? "收起记录(&L)" : "显示记录(&L)";
    }
    private void SetupButton(ActionButton button, string text, bool primary)
    {
        button.Text = text; button.Primary = primary; button.Size = new Size(156,42); button.Font = Font;
        button.Margin = new Padding(0,0,10,0); button.Cursor = Cursors.Hand;
    }
    private ActionButton Button(string text, Action click, bool primary)
    {
        ActionButton button = new ActionButton(); SetupButton(button,text,primary);
        button.Click += delegate { if (!busy) click(); }; return button;
    }
    private FlowLayoutPanel Row(params Control[] controls)
    {
        FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
        row.Controls.AddRange(controls); return row;
    }
    private TableLayoutPanel Card(string title, string description)
    {
        TableLayoutPanel panel = Grid(1); panel.Dock = DockStyle.Fill; panel.BackColor = Color.White; panel.Padding = new Padding(20); panel.Margin = new Padding(0,0,0,16);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,32)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        Label heading = TextLabel(title,12,Ink); heading.Font = new Font(Font.FontFamily,12,FontStyle.Bold);
        panel.Controls.Add(heading,0,0); panel.Controls.Add(TextLabel(description,9.5f,Muted),0,1);
        panel.Paint += delegate(object sender, PaintEventArgs e) { using (Pen pen = new Pen(Line)) e.Graphics.DrawRectangle(pen,0,0,panel.Width-1,panel.Height-1); };
        return panel;
    }
    private void BuildHome()
    {
        Panel page = Page("常用操作","启动播放器，保存你的设置，管理便携包。 ");
        TableLayoutPanel layout = Grid(2); ScrollLayout(page,layout);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,53)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,216)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        TableLayoutPanel playback = Card("PotPlayer + madVR","保留现有皮肤、解码器和长按倍速操作。"); playback.Margin = new Padding(0,0,16,16);
        ActionButton launch = Button("启动 PotPlayer",delegate { Launch("PotPlayerPortable.exe"); },true); launch.Dock = DockStyle.Top; launch.Margin = new Padding(0,12,0,12);
        playback.RowStyles.Add(new RowStyle(SizeType.Absolute,76)); playback.Controls.Add(launch,0,2);
        playback.Controls.Add(TextLabel("右方向键：短按快进 · 长按 3 倍速",9.5f,Muted),0,3); layout.Controls.Add(playback,0,0);
        TableLayoutPanel configuration = Card("当前已保存配置","保存时可确认关闭正在运行的相关程序。");
        saved.Dock = DockStyle.Fill; saved.ForeColor = Accent; saved.Font = new Font(Font.FontFamily,11,FontStyle.Bold);
        configuration.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); configuration.Controls.Add(saved,0,2);
        configuration.Controls.Add(Button("保存当前配置",delegate { Run("Save","保存当前配置",null,null,null,false); },true),0,3); layout.Controls.Add(configuration,1,0);
        TableLayoutPanel tools = Card("播放器与系统组件","按需要操作；注册组件会请求管理员权限。"); tools.Margin = new Padding(0,0,16,0);
        tools.RowStyles.Add(new RowStyle(SizeType.Absolute,56)); tools.RowStyles.Add(new RowStyle(SizeType.Absolute,56)); tools.RowStyles.Add(new RowStyle(SizeType.Absolute,56)); tools.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
        tools.Controls.Add(Row(Button("madVR 设置",delegate { Launch(@"madVR\madHcCtrl.exe"); Feedback("请双击通知区域的 madVR 图标打开设置。",false); },false),Button("Icaros 配置",delegate { Launch(@"Icaros\IcarosConfig.exe"); },false)),0,2);
        tools.Controls.Add(Row(Button("初始化本目录",delegate { Run("Init","初始化本目录",null,null,null,false); },false),Button("检查组件(&K)",delegate { Run("Check","检查组件文件",null,null,null,false); },false)),0,3);
        tools.Controls.Add(Row(Button("注册 madVR / 字幕",delegate { Run("Register","注册组件",null,null,null,true); },false),Button("注册视频关联",delegate { Run("Associations","注册视频关联",null,null,null,false); },false)),0,4);
        tools.Controls.Add(Button("创建桌面快捷方式",delegate { Run("Shortcut","创建桌面快捷方式",null,null,null,false); },false),0,5); layout.Controls.Add(tools,0,1);
        TableLayoutPanel migration = Card("备份与迁移","你的自定义配置是导出与恢复的来源。"); migration.Margin = new Padding(0);
        migration.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); migration.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); migration.RowStyles.Add(new RowStyle(SizeType.Absolute,64)); migration.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        migration.Controls.Add(Button("管理配置备份",delegate { ShowPage(1); },false),0,2);
        migration.Controls.Add(Button("导出便携包",delegate { ShowPage(2); },false),0,3);
        migration.Controls.Add(Note("首次使用、移动目录或换电脑后，先初始化本目录，再注册组件。",58),0,4);
        migration.Controls.Add(Button("应用固定画质预设",delegate {
            if (MessageBox.Show(this,"这会替换当前 madVR 自定义设置并切换到 madVR 渲染器，操作前自动备份。\n使用自己保存的最佳配置时，不需要点击此按钮。","应用固定预设",MessageBoxButtons.OKCancel,MessageBoxIcon.Information) == DialogResult.OK)
                Run("Quality","应用固定画质预设",null,null,null,false);
        },false),0,5);
        migration.Controls.Add(Note("仅在希望重置画质方案时使用固定预设。",30),0,6); layout.Controls.Add(migration,1,1); page.Controls.Add(layout);
        tools.Resize += delegate { foreach (Control row in tools.Controls) if (row is FlowLayoutPanel) foreach (Control button in row.Controls) button.Width = Math.Max(124,(row.Width-10)/2); };
    }
    private void BuildBackups()
    {
        Panel page = Page("配置与备份","按时间查看历史设置，恢复一份或批量删除。 ");
        TableLayoutPanel layout = Grid(1); layout.Dock = DockStyle.Fill;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        TableLayoutPanel filters = Grid(3); filters.Dock = DockStyle.Fill; filters.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
        Panel searchField = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12,10,12,10), BackColor = Color.White, Margin = new Padding(0,0,12,10) };
        backupSearch.Dock = DockStyle.Fill; backupSearch.BorderStyle = BorderStyle.None; backupSearch.AccessibleName = "搜索备份名称或组件"; hints.SetToolTip(backupSearch,"搜索备份名称、日期或组件");
        searchField.Controls.Add(backupSearch);
        searchField.Paint += delegate(object sender, PaintEventArgs e) { using (Pen pen = new Pen(Line)) e.Graphics.DrawRectangle(pen,0,0,searchField.Width-1,searchField.Height-1); };
        Label searchLabel = TextLabel("搜索",9,Muted); searchLabel.Dock = DockStyle.Left; searchLabel.Width = 44; searchField.Controls.Add(searchLabel);
        filters.Controls.Add(searchField,0,0);
        backupType.DropDownStyle = ComboBoxStyle.DropDownList; backupType.Anchor = AnchorStyles.Left | AnchorStyles.Right; backupType.Margin = new Padding(0,0,12,10); backupType.AccessibleName = "备份类型";
        backupType.Items.AddRange(new object[] { "全部类型","保存快照","操作前备份","文件关联" }); backupType.SelectedIndex = 0; filters.Controls.Add(backupType,1,0);
        filters.Controls.Add(Button("刷新",RefreshSnapshots,false),2,0); layout.Controls.Add(filters,0,0);
        backups.Dock = DockStyle.Fill; backups.Margin = new Padding(0); backups.BackgroundColor = Color.White; backups.BorderStyle = BorderStyle.None;
        backups.AllowUserToAddRows = false; backups.AllowUserToDeleteRows = false; backups.AllowUserToResizeRows = false;
        backups.ReadOnly = true; backups.MultiSelect = true; backups.SelectionMode = DataGridViewSelectionMode.FullRowSelect; backups.RowHeadersVisible = false; backups.AutoGenerateColumns = false;
        backups.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; backups.EnableHeadersVisualStyles = false; backups.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single; backups.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; backups.ColumnHeadersHeight = 44;
        backups.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(237,239,244), ForeColor = Ink, Font = new Font(Font.FontFamily,9.5f,FontStyle.Bold), Padding = new Padding(10,0,0,0), SelectionBackColor = Color.FromArgb(237,239,244), SelectionForeColor = Ink };
        backups.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Ink, SelectionBackColor = AccentSoft, SelectionForeColor = Ink, Padding = new Padding(10,0,0,0), Font = new Font(Font.FontFamily,9.5f) };
        backups.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251,252,254); backups.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; backups.GridColor = Color.FromArgb(239,241,245); backups.RowTemplate.Height = 46;
        AddColumn("时间",23,145); AddColumn("类型",16,105); AddColumn("备份名称",35,180); AddColumn("包含组件",26,150);
        backups.AccessibleName = "配置备份列表，支持多选"; layout.Controls.Add(backups,0,1);
        TableLayoutPanel selectionRow = Grid(3); selectionRow.Dock = DockStyle.Fill;
        selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,78)); selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));
        backupSelection.Dock = DockStyle.Fill; backupSelection.TextAlign = ContentAlignment.MiddleLeft; backupSelection.ForeColor = Muted;
        selectionRow.Controls.Add(backupSelection,0,0); selectionRow.Controls.Add(Button("全选",delegate { backups.SelectAll(); },false),1,0); selectionRow.Controls.Add(Button("取消选择",delegate { backups.ClearSelection(); },false),2,0); layout.Controls.Add(selectionRow,0,2);
        restoreIcaros.Text = "恢复 Icaros（需管理员）"; restoreIcaros.AutoSize = true;
        restorePlaylist.Text = "恢复播放列表（备份包含时）"; restorePlaylist.AutoSize = true;
        layout.Controls.Add(Row(restoreIcaros,restorePlaylist),0,3);
        SetupButton(restoreButton,"恢复所选备份",true); restoreButton.Click += delegate { RestoreBackup(); };
        SetupButton(previewBackupButton,"查看恢复范围",false); previewBackupButton.Click += delegate { PreviewBackup(); };
        SetupButton(deleteButton,"删除所选备份(&D)",false); deleteButton.Destructive = true; deleteButton.Click += delegate { if (!busy) DeleteBackups(); };
        layout.Controls.Add(Row(restoreButton,previewBackupButton,deleteButton),0,4);
        backupDetails.Dock = DockStyle.Fill; backupDetails.ForeColor = Muted; backupDetails.TextAlign = ContentAlignment.MiddleLeft; backupDetails.AutoEllipsis = true; layout.Controls.Add(backupDetails,0,5);
        page.Controls.Add(layout);
        backupSearch.TextChanged += delegate { FilterBackups(); }; backupType.SelectedIndexChanged += delegate { FilterBackups(); };
        backups.SelectionChanged += delegate { UpdateBackupSelection(); };
        backups.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) PreviewBackup(); };
        backups.KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Delete) { DeleteBackups(); e.Handled = true; }
            if (e.KeyCode == Keys.Enter) { PreviewBackup(); e.Handled = true; }
            if (e.KeyCode == Keys.Escape) { backups.ClearSelection(); e.Handled = true; }
        };
        ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("查看恢复范围",null,delegate { PreviewBackup(); }); menu.Items.Add("恢复所选备份",null,delegate { RestoreBackup(); });
        menu.Items.Add("打开备份目录",null,delegate { Snapshot selected = SelectedBackup(); if (selected!=null) OpenDirectory(selected.Path); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("删除所选备份",null,delegate { DeleteBackups(); }); backups.ContextMenuStrip = menu;
        menu.Opening += delegate(object sender, CancelEventArgs e) { e.Cancel = busy || backups.SelectedRows.Count == 0; menu.Items[0].Enabled = menu.Items[1].Enabled = menu.Items[2].Enabled = backups.SelectedRows.Count==1; };
        backups.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button==MouseButtons.Right && e.RowIndex>=0 && !backups.Rows[e.RowIndex].Selected) { backups.ClearSelection(); backups.Rows[e.RowIndex].Selected=true; } };
    }
    private void AddColumn(string title, float weight, int minimum)
    {
        backups.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, Name = title, FillWeight = weight, MinimumWidth = minimum, SortMode = DataGridViewColumnSortMode.Automatic });
    }
    private void FilterBackups()
    {
        if (refreshing) return;
        DataGridViewColumn sortColumn = backups.SortedColumn ?? backups.Columns[0];
        ListSortDirection sortDirection = backups.SortOrder==SortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
        refreshing = true; backups.Rows.Clear();
        string query = backupSearch.Text.Trim(); string type = backupType.SelectedIndex > 0 ? backupType.Text : "";
        foreach (Snapshot snapshot in snapshots)
        {
            if (type.Length>0 && snapshot.Type!=type) continue;
            string searchable = snapshot.Name+" "+snapshot.Type+" "+snapshot.Components+" "+snapshot.Time.ToString("yyyy-MM-dd HH:mm:ss");
            if (query.Length>0 && searchable.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0) continue;
            int index = backups.Rows.Add(snapshot.Time.ToString("yyyy-MM-dd HH:mm:ss"),snapshot.Type,snapshot.Name,snapshot.Components);
            backups.Rows[index].Tag = snapshot;
            foreach (DataGridViewCell cell in backups.Rows[index].Cells) cell.ToolTipText = snapshot.Path+"\r\n"+snapshot.Components;
        }
        backups.Sort(sortColumn,sortDirection); backups.ClearSelection(); refreshing = false; UpdateBackupSelection();
    }
    private void UpdateBackupSelection()
    {
        if (refreshing) return;
        int count = backups.SelectedRows.Count;
        backupSelection.Text = "共 "+backups.Rows.Count+" 项备份  ·  已选择 "+count+" 项";
        restoreButton.Enabled = previewBackupButton.Enabled = !busy && count==1; deleteButton.Enabled = !busy && count>0;
        backupDetails.Text = backups.Rows.Count==0 ? "没有找到备份：清空搜索或更换类型；首次使用请先保存当前配置。" : count==1 ? ((Snapshot)backups.SelectedRows[0].Tag).Components : "Ctrl 多选 · Shift 连选 · 双击查看范围 · 删除移入回收站";
    }
    private Snapshot SelectedBackup()
    {
        if (busy) return null;
        if (backups.SelectedRows.Count!=1) { Feedback("恢复或查看范围时，请只选择一项备份。",false); return null; }
        return (Snapshot)backups.SelectedRows[0].Tag;
    }
    private void RestoreBackup()
    {
        Snapshot selected = SelectedBackup(); if (selected==null) return;
        Run("Restore","恢复备份",selected.Path,null,new string[] { restoreIcaros.Checked ? "IncludeIcaros" : "", restorePlaylist.Checked ? "IncludeHistory" : "" },restoreIcaros.Checked && !selected.Association);
    }
    private void PreviewBackup()
    {
        Snapshot selected = SelectedBackup(); if (selected==null) return;
        ShowRecords(true);
        if (selected.Association) Feedback("恢复文件关联登记中的打开命令与图标。操作前会备份当前登记。\r\n来源："+selected.Path,false);
        else Run("Preview","查看恢复范围",selected.Path,null,null,false);
    }
    private void DeleteBackups()
    {
        if (busy) return;
        if (backups.SelectedRows.Count==0) { Feedback("请先选择要删除的备份。",false); return; }
        List<string> paths = new List<string>(); List<string> names = new List<string>();
        foreach (DataGridViewRow row in backups.SelectedRows) { Snapshot snapshot=(Snapshot)row.Tag; paths.Add(snapshot.Path); if (names.Count<15) names.Add(snapshot.Name); }
        string message = "将以下 "+paths.Count+" 项备份移入回收站：\r\n\r\n"+String.Join("\r\n",names.ToArray());
        if (paths.Count>names.Count) message += "\r\n以及其余 "+(paths.Count-names.Count)+" 项所选备份";
        message += "\r\n\r\n当前已保存配置和正在使用的设置不会删除。是否继续？";
        if (MessageBox.Show(this,message,"删除所选备份",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK) return;
        Run("DeleteBackups","删除所选备份",null,null,null,false,null,paths.ToArray());
    }
    private void BuildExport()
    {
        Panel page = Page("迁移导出","从当前保存的设置或历史快照生成迁移包。 ");
        TableLayoutPanel layout = Grid(1); ScrollLayout(page,layout);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,160)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,216)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        TableLayoutPanel sourceCard = Card("配置来源","默认使用最后保存到 Data 的配置。调整后请先保存。");
        exportSource.DropDownStyle = ComboBoxStyle.DropDownList; exportSource.Dock = DockStyle.Fill; exportSource.AccessibleName = "导出配置来源"; sourceCard.Controls.Add(exportSource,0,2); layout.Controls.Add(sourceCard,0,0);
        TableLayoutPanel packageCard = Card("导出内容与格式","完整便携包用于迁移播放器；仅配置包用于同步另一台电脑。");
        TableLayoutPanel selectors = Grid(2); selectors.Dock = DockStyle.Fill; selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64)); selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36)); selectors.RowStyles.Add(new RowStyle(SizeType.Absolute,26)); selectors.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        selectors.Controls.Add(TextLabel("包含内容",9,Muted),0,0); selectors.Controls.Add(TextLabel("文件格式",9,Muted),1,0);
        kind.DropDownStyle = format.DropDownStyle = ComboBoxStyle.DropDownList; kind.Dock = format.Dock = DockStyle.Fill; kind.Margin = new Padding(0,0,16,0); format.Margin = new Padding(0);
        kind.AccessibleName = "导出内容"; format.AccessibleName = "导出格式";
        kind.Items.AddRange(new object[] { "完整便携包：播放器、组件与配置","仅配置包：同步播放器与组件设置" }); kind.SelectedIndex = 0;
        format.Items.AddRange(new object[] { "ZIP 压缩包","文件夹" }); format.SelectedIndex = 0;
        selectors.Controls.Add(kind,0,1); selectors.Controls.Add(format,1,1); packageCard.RowStyles.Add(new RowStyle(SizeType.Absolute,68)); packageCard.Controls.Add(selectors,0,2);
        exportSummary.Dock = DockStyle.Fill; exportSummary.ForeColor = Muted; exportSummary.AutoEllipsis = true; packageCard.Controls.Add(exportSummary,0,3); layout.Controls.Add(packageCard,0,1);
        TableLayoutPanel resultCard = Card("迁移说明","导出不包含旧备份、快照、播放列表和观看记录。");
        resultCard.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); resultCard.Controls.Add(Note("完整包在目标电脑解压后，先初始化，再注册 madVR 和字幕组件。仅配置包在目标电脑选择“配置导入”，检查后覆盖应用。",52),0,2);
        exportResult.Dock = DockStyle.Fill; exportResult.AutoEllipsis = true; exportResult.ForeColor = Accent; resultCard.Controls.Add(exportResult,0,3); layout.Controls.Add(resultCard,0,2);
        SetupButton(openExportButton,"打开导出位置",false); openExportButton.Enabled = false; openExportButton.Click += delegate { if (lastExport.Length>0) OpenDirectory(Directory.Exists(lastExport) ? lastExport : System.IO.Path.GetDirectoryName(lastExport)); };
        layout.Controls.Add(Row(Button("选择位置并导出(&E)",Export,true),Button("查看所选配置",delegate { Snapshot selected=exportSource.SelectedItem as Snapshot; if (selected!=null) { ShowRecords(true); Run("Preview","查看导出配置",selected.Path,null,null,false); } },false),openExportButton),0,3);
        page.Controls.Add(layout);
        kind.SelectedIndexChanged += delegate { UpdateExportSummary(); }; format.SelectedIndexChanged += delegate { UpdateExportSummary(); }; UpdateExportSummary();
    }
    private void UpdateExportSummary()
    {
        exportSummary.Text = kind.SelectedIndex==0 ? "包含现有皮肤、长按倍速工具、管理窗口和所选配置。" : "包含 PotPlayer、madVR、LAV 与字幕等所选组件的设置。";
        if (format.SelectedIndex==1) exportSummary.Text += " 同名目录存在时会创建带时间的新目录。";
        hints.SetToolTip(exportSummary,exportSummary.Text);
    }
    private void Export()
    {
        if (busy) return;
        Snapshot source = exportSource.SelectedItem as Snapshot;
        if (source==null) { Feedback("没有可用的配置来源，请先保存配置。",false); return; }
        string destination; string name=kind.SelectedIndex==0 ? "PotPlayer-Portable" : "PotPlayer-Config";
        List<string> options=new List<string> { "PackageKind="+(kind.SelectedIndex==0 ? "Portable" : "Config"), "Format="+(format.SelectedIndex==0 ? "Zip" : "Directory") };
        if (format.SelectedIndex==0)
        {
            using (SaveFileDialog dialog=new SaveFileDialog { Filter="ZIP 压缩包 (*.zip)|*.zip", FileName=name+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip", InitialDirectory=Directory.GetParent(root).FullName, OverwritePrompt=true })
            { if (dialog.ShowDialog(this)!=DialogResult.OK) return; destination=dialog.FileName; if (File.Exists(destination)) options.Add("Overwrite"); }
        }
        else
        {
            using (FolderBrowserDialog dialog=new FolderBrowserDialog { Description="选择导出包的父目录；将创建一个新的 "+name+" 文件夹。", SelectedPath=Directory.GetParent(root).FullName })
            { if (dialog.ShowDialog(this)!=DialogResult.OK) return; destination=System.IO.Path.Combine(dialog.SelectedPath,name); if (Directory.Exists(destination)) destination+="-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"); }
        }
        exportResult.Text = "正在导出…"; openExportButton.Enabled = false;
        Run("Export","导出迁移包",source.Path,destination,options.ToArray(),false,delegate(bool success,string output) {
            exportResult.Text=success ? "已导出："+destination : "导出未完成，请查看操作记录。";
            if (success) lastExport=destination; openExportButton.Enabled=success; hints.SetToolTip(exportResult,destination);
        });
    }
    private void BuildImport()
    {
        Panel page = Page("配置导入","选择来源，检查范围，再覆盖应用配置。 ");
        TableLayoutPanel layout = Grid(1); ScrollLayout(page,layout);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,214)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        TableLayoutPanel source = Card("1  选择配置包","支持配置 ZIP、完整便携 ZIP 和配置文件夹，也可直接拖入此页面。");
        page.AllowDrop = source.AllowDrop = true;
        DragEventHandler enter = delegate(object sender, DragEventArgs e) { e.Effect=!busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
        DragEventHandler drop = delegate(object sender, DragEventArgs e) { if (busy) return; string[] files=(string[])e.Data.GetData(DataFormats.FileDrop); if (files.Length!=1) { Feedback("请一次拖入一个 ZIP 或配置文件夹。",false); return; } importPath.Text=files[0]; PreviewImport(); };
        page.DragEnter += enter; source.DragEnter += enter; page.DragDrop += drop; source.DragDrop += drop;
        source.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); source.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        importPath.Dock=DockStyle.Fill; importPath.AccessibleName="导入配置路径"; importPath.Margin=new Padding(0,0,0,8); source.Controls.Add(importPath,0,2);
        importPath.TextChanged += delegate { validatedImport=""; importButton.Enabled=false; importPreview.Clear(); importState.Text="选择配置后，点击检查范围。"; importState.ForeColor=Muted; };
        importPath.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode==Keys.Enter) { PreviewImport(); e.SuppressKeyPress=true; } };
        source.Controls.Add(Row(Button("选择 ZIP",delegate {
            using (OpenFileDialog dialog=new OpenFileDialog { Filter="配置或便携 ZIP (*.zip)|*.zip", CheckFileExists=true })
                if (dialog.ShowDialog(this)==DialogResult.OK) { importPath.Text=dialog.FileName; PreviewImport(); }
        },false),Button("选择文件夹",delegate {
            using (FolderBrowserDialog dialog=new FolderBrowserDialog { Description="选择配置目录、快照目录或完整便携包目录。" })
                if (dialog.ShowDialog(this)==DialogResult.OK) { importPath.Text=dialog.SelectedPath; PreviewImport(); }
        },false),Button("检查范围",PreviewImport,false)),0,3); layout.Controls.Add(source,0,0);
        TableLayoutPanel preview=Card("2  检查导入范围","确认所含组件和来源，验证通过后才能应用。");
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute,30)); preview.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        importState.Dock=DockStyle.Fill; importState.ForeColor=Muted; importState.Text="尚未选择配置包"; preview.Controls.Add(importState,0,2);
        importPreview.Dock=DockStyle.Fill; importPreview.Multiline=true; importPreview.ReadOnly=true; importPreview.ScrollBars=ScrollBars.Vertical; importPreview.BorderStyle=BorderStyle.None; importPreview.BackColor=Color.White; importPreview.Font=new Font(Font.FontFamily,9.5f); importPreview.AccessibleName="配置检查结果"; preview.Controls.Add(importPreview,0,3); layout.Controls.Add(preview,0,1);
        importIcaros.Text="同时导入 Icaros 设置（需管理员权限）"; importIcaros.AutoSize=true; layout.Controls.Add(importIcaros,0,2);
        SetupButton(importButton,"导入并覆盖应用",true); importButton.Enabled=false;
        importButton.Click += delegate { if (!busy && validatedImport.Length>0 && validatedImport==importPath.Text.Trim()) Run("Import","导入并应用配置",validatedImport,null,new string[] { importIcaros.Checked ? "IncludeIcaros" : "" },importIcaros.Checked,delegate(bool success,string output) { importState.Text=success ? "配置已应用，操作前设置已自动备份。" : "应用未完成，请查看操作记录。"; importState.ForeColor=success ? Accent : Danger; }); };
        Label importHint = Note("应用前自动备份；组件路径适配当前目录。",34); importHint.Width = 450;
        layout.Controls.Add(Row(importButton,importHint),0,3); page.Controls.Add(layout);
    }
    private void PreviewImport()
    {
        if (busy) return;
        string path=importPath.Text.Trim();
        if (path.Length==0) { importState.Text="请选择要导入的 ZIP 或配置目录。"; importState.ForeColor=Danger; importPath.Focus(); return; }
        importState.Text="正在检查配置内容…"; importState.ForeColor=Muted;
        Run("Preview","检查导入范围",path,null,null,false,delegate(bool success,string output) {
            importPreview.Text=output; validatedImport=success ? path : "";
            importState.Text=success ? "验证通过，可以覆盖应用以下配置。" : "配置检查未通过，请检查来源。"; importState.ForeColor=success ? Accent : Danger;
        });
    }
    private void OpenDirectory(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute=true }); }
        catch (Exception error) { Feedback("无法打开目录："+error.Message,true); }
    }
    private void ScrollLayout(Panel page, TableLayoutPanel layout)
    {
        page.AutoScroll = true; layout.Dock = DockStyle.Top; layout.Height = 620;
        page.Resize += delegate { layout.Height = Math.Max(620,page.ClientSize.Height); };
    }
    private void Feedback(string text, bool error)
    {
        Append(text); status.Text=text.Replace("\r\n"," "); status.ForeColor=error ? Danger : Ink; hints.SetToolTip(status,text); if (error) ShowRecords(true);
    }

    private List<Snapshot> FindSnapshots()
    {
        List<Snapshot> results = new List<Snapshot>();
        string data = System.IO.Path.Combine(root, "Data");
        if (Directory.Exists(data))
            foreach (string path in Directory.GetDirectories(data, "Snapshot-*")) AddSnapshot(results, path, "保存快照");
        string backup = System.IO.Path.Combine(root, "Backups");
        if (Directory.Exists(backup))
            foreach (string path in Directory.GetDirectories(backup)) AddSnapshot(results, path, "操作前备份");
        results.Sort(delegate(Snapshot a, Snapshot b) { return b.Time.CompareTo(a.Time); });
        return results;
    }
    private void AddSnapshot(List<Snapshot> results, string path, string type)
    {
        bool association = File.Exists(System.IO.Path.Combine(path, "原关联登记.json"));
        string config = Directory.Exists(System.IO.Path.Combine(path, "Data")) && !File.Exists(System.IO.Path.Combine(path, "PotPlayer.reg")) ? System.IO.Path.Combine(path, "Data") : path;
        List<string> names = new List<string>();
        foreach (string name in new string[] { "PotPlayer", "LAV", "madVR", "VSFilter", "XySubFilter", "Icaros-machine" })
            if (File.Exists(System.IO.Path.Combine(config, name+".reg"))) names.Add(name == "Icaros-machine" ? "Icaros" : name);
        if (!association && names.Count == 0) return;
        results.Add(new Snapshot { Path = path, Name = new DirectoryInfo(path).Name, Type = association ? "文件关联" : type, Components = association ? "打开命令与图标" : String.Join("、", names.ToArray()), Time = Directory.GetLastWriteTime(path), Association = association });
    }
    private void RefreshSnapshots()
    {
        string selectedPath = exportSource.SelectedItem is Snapshot ? ((Snapshot)exportSource.SelectedItem).Path : "";
        string dataPath = System.IO.Path.Combine(root, "Data");
        string file = System.IO.Path.Combine(dataPath, "PotPlayer.reg");
        saved.Text = File.Exists(file) ? File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm:ss") : "尚未保存配置";
        snapshots = FindSnapshots();
        exportSource.Items.Clear();
        exportSource.Items.Add(new Snapshot { Path = dataPath, Name = "当前已保存配置（Data，"+saved.Text+"）" });
        foreach (Snapshot snapshot in snapshots) if (!snapshot.Association) exportSource.Items.Add(snapshot);
        exportSource.SelectedIndex = 0;
        for (int i=0; i<exportSource.Items.Count; i++)
            if (((Snapshot)exportSource.Items[i]).Path == selectedPath) { exportSource.SelectedIndex = i; break; }
        FilterBackups();
    }
    private void Launch(string relative)
    {
        try
        {
            string file = System.IO.Path.Combine(root, relative);
            Process.Start(new ProcessStartInfo(file) { WorkingDirectory = System.IO.Path.GetDirectoryName(file), UseShellExecute = true });
            Feedback("已启动："+System.IO.Path.GetFileName(file),false);
        }
        catch (Exception error) { Feedback("启动失败："+error.Message,true); }
    }
    private void Append(string text) { log.AppendText(text+Environment.NewLine); }
    private static string Literal(string value) { return "'"+value.Replace("'", "''")+"'"; }
    private string Script(string action, string source, string destination, string[] options, string[] backupPaths)
    {
        string file = System.IO.Path.Combine(root, @"Tools\Portable.Core.ps1");
        string command = "$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=New-Object Text.UTF8Encoding($false); & ([scriptblock]::Create([IO.File]::ReadAllText("+Literal(file)+",[Text.Encoding]::UTF8))) -RootPath "+Literal(root)+" -Action "+Literal(action);
        command += " -ExpectedUserSid "+Literal(WindowsIdentity.GetCurrent().User.Value);
        if (source != null) command += " -SourcePath "+Literal(source);
        if (destination != null) command += " -DestinationPath "+Literal(destination);
        if (backupPaths != null)
        {
            List<string> quoted = new List<string>();
            foreach (string path in backupPaths) quoted.Add(Literal(path));
            command += " -BackupPaths @("+String.Join(",",quoted.ToArray())+")";
        }
        if (options != null)
            foreach (string option in options)
            {
                if (String.IsNullOrEmpty(option)) continue;
                int separator = option.IndexOf('=');
                command += separator < 0 ? " -"+option : " -"+option.Substring(0,separator)+" "+Literal(option.Substring(separator+1));
            }
        return command;
    }
    private static string Execute(string script, Action<string> line, out int code)
    {
        string shell = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        ProcessStartInfo info = new ProcessStartInfo(shell, "-NoProfile -NonInteractive -OutputFormat Text -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        string nativeModules = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(shell), "Modules");
        string inheritedModules = info.EnvironmentVariables["PSModulePath"];
        // 子进程优先加载系统 PowerShell 的模块，避免误加载父进程的 PowerShell 7 模块。
        info.EnvironmentVariables["PSModulePath"] = nativeModules+(String.IsNullOrEmpty(inheritedModules) ? "" : ";"+inheritedModules);
        StringBuilder output = new StringBuilder();
        using (Process process = new Process { StartInfo = info })
        {
            object gate = new object();
            DataReceivedEventHandler receive = delegate(object sender, DataReceivedEventArgs e) {
                if (e.Data == null) return;
                lock (gate) output.AppendLine(e.Data);
                if (line != null) line(e.Data);
            };
            process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
            code = process.ExitCode;
        }
        return output.ToString();
    }
    private static void CloseApplications(List<Process> processes)
    {
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (process.HasExited) continue;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill();
                        if (!process.WaitForExit(5000)) throw new TimeoutException("程序未能关闭："+process.ProcessName);
                    }
                }
                catch (InvalidOperationException) { if (!process.HasExited) throw; }
            }
        }
        finally { foreach (Process process in processes) process.Dispose(); }
    }
    private void Run(string action, string description, string source, string destination, string[] options, bool administrator, Action<bool,string> completed = null, string[] backupPaths = null)
    {
        if (busy) return;
        List<Process> running = new List<Process>();
        if (action == "Save" || action == "Export")
        {
            string[] names = { "PotPlayerMini64", "PotPlayerPortable", "IcarosConfig", "madHcCtrl" };
            string[] labels = { "PotPlayer", "按键工具", "Icaros 配置", "madVR 托盘工具" };
            List<string> details = new List<string>();
            try
            {
                for (int i=0;i<names.Length;i++)
                    foreach (Process process in Process.GetProcessesByName(names[i]))
                    {
                        running.Add(process);
                        // 在确认前保留进程句柄，防止退出后的进程 ID 被其他程序复用。
                        IntPtr handle = process.Handle;
                        details.Add(labels[i]+"（进程 "+process.Id+"）");
                    }
            }
            catch (Exception error)
            {
                foreach (Process process in running) process.Dispose();
                Feedback("无法检查相关程序："+error.Message,true); return;
            }
            if (running.Count>0)
            {
                string message = description+"需要关闭以下程序：\r\n\r\n"+String.Join("\r\n",details.ToArray());
                message += "\r\n\r\n确认后先正常退出；3 秒内未退出的程序将被强制结束，然后继续"+description+"。\r\n尚未应用的修改可能丢失。是否继续？";
                if (MessageBox.Show(this,message,"关闭程序并继续",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)
                {
                    foreach (Process process in running) process.Dispose();
                    status.Text = "已取消"+description; return;
                }
            }
        }
        busy = true; tabs.Enabled = false; foreach (ActionButton nav in navigation) nav.Enabled=false; status.ForeColor=Ink; progress.Visible = true; progress.Style = ProgressBarStyle.Marquee; status.Text = description+"……";
        Append("\r\n["+DateTime.Now.ToString("HH:mm:ss")+"] "+description);
        string script = Script(action,source,destination,options,backupPaths);
        BackgroundWorker worker = new BackgroundWorker();
        int code = 1; string output = "";
        worker.DoWork += delegate {
            CloseApplications(running);
            if (running.Count>0) BeginInvoke((Action)delegate { Append("已关闭确认列表中的程序，继续"+description+"。"); });
            bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (administrator && !elevated)
            {
                string resultFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PotPlayerManager-Result-"+Guid.NewGuid().ToString("N")+".txt");
                try
                {
                    ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, "--elevated-job "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))+" \""+resultFile+"\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                    using (Process process = Process.Start(info)) { process.WaitForExit(); code = process.ExitCode; }
                    output = File.ReadAllText(resultFile,Encoding.UTF8);
                    BeginInvoke((Action)delegate { Append(output.TrimEnd()); });
                }
                finally { if (File.Exists(resultFile)) File.Delete(resultFile); }
            }
            else output = Execute(script, delegate(string text) { BeginInvoke((Action)delegate { Append(text); }); }, out code);
        };
        worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e) {
            if (e.Error != null) { code=1; output="操作未完成："+e.Error.Message; Append(output); }
            busy = false; tabs.Enabled = true; foreach (ActionButton nav in navigation) nav.Enabled=true; progress.Visible = false; status.Text = code == 0 ? description+"完成" : description+"未完成，请查看操作记录";
            if ((code == 0 && action != "Preview" && action != "Check") || action == "DeleteBackups") RefreshSnapshots();
            if (completed != null) completed(code == 0,output);
            importButton.Enabled = validatedImport.Length > 0 && validatedImport == importPath.Text.Trim();
            status.ForeColor=code==0 ? Ink : Danger;
            hints.SetToolTip(status,status.Text);
            if (code!=0) ShowRecords(true);
            UpdateBackupSelection();
            worker.Dispose();
        };
        worker.RunWorkerAsync();
    }

    private sealed class ActionButton : Button
    {
        public bool Primary, Destructive, Selected;
        private bool hovered, pressed;
        public ActionButton()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; UseVisualStyleBackColor=false; BackColor=Color.Transparent;
            SetStyle(ControlStyles.Opaque,false);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false; pressed=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale=DeviceDpiScale(e.Graphics);
            Rectangle box=new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3));
            Color fill=Primary ? Accent : Selected ? AccentSoft : Color.White;
            Color text=Primary ? Color.White : Destructive ? Danger : Selected ? Accent : Ink;
            if (!Enabled) { fill=Color.FromArgb(235,237,242); text=Color.FromArgb(112,119,133); }
            else if (pressed) fill=Primary ? Color.FromArgb(73,53,126) : Color.FromArgb(225,220,237);
            else if (hovered) fill=Primary ? Color.FromArgb(78,58,133) : Color.FromArgb(243,240,249);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using (GraphicsPath path=Rounded(box,6*scale))
            using (Brush brush=new SolidBrush(fill))
            using (Pen border=new Pen(Primary ? fill : Line)) { e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(border,path); }
            TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis;
            flags |= TextAlign==ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            Rectangle textBox=new Rectangle((int)(12*scale),2,Math.Max(1,Width-(int)(24*scale)),Height-4);
            TextRenderer.DrawText(e.Graphics,Text,Font,textBox,text,flags);
            if (Focused && ShowFocusCues)
                using (GraphicsPath focus=Rounded(Rectangle.Inflate(box,-4,-4),3*scale))
                using (Pen pen=new Pen(Primary ? Color.White : Accent,scale)) e.Graphics.DrawPath(pen,focus);
        }
        private static float DeviceDpiScale(Graphics graphics) { return graphics.DpiX/96f; }
        private static GraphicsPath Rounded(Rectangle rectangle, float radius)
        {
            float d=radius*2; GraphicsPath path=new GraphicsPath();
            path.AddArc(rectangle.Left,rectangle.Top,d,d,180,90); path.AddArc(rectangle.Right-d,rectangle.Top,d,d,270,90);
            path.AddArc(rectangle.Right-d,rectangle.Bottom-d,d,d,0,90); path.AddArc(rectangle.Left,rectangle.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
        }
    }
}
