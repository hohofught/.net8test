#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// 번역 설정 통합 폼 (개선된 버전)
/// - 파일 미리보기
/// - 단어장 편집 (DataGridView)
/// - 프롬프트 설정
/// </summary>
public class TranslationSettingsFormEx : Form
{
    #region Fields
    
    private ComboBox cmbTargetLang = null!;
    private ComboBox cmbStyle = null!;
    private ComboBox cmbGamePreset = null!;
    
    // 파일 미리보기
    private TextBox txtFilePreview = null!;
    private Button btnLoadFile = null!;
    private Label lblFileName = null!;
    
    // 단어장 편집
    private DataGridView dgvGlossary = null!;
    private Button btnAddTerm = null!;
    private Button btnRemoveTerm = null!;
    private Button btnLoadGlossary = null!;
    private Button btnSaveGlossary = null!;
    
    // 프롬프트
    private CheckBox chkCustomPrompt = null!;
    private TextBox txtCustomPrompt = null!;
    
    private Button btnApply = null!;
    private Button btnCancel = null!;
    
    private TranslationSettings _settings;
    private string? _loadedFilePath;
    private string? _glossaryPath;
    private string? _savePath;
    private bool _autoSave = true;
    
    #endregion
    
    #region Properties
    
    public TranslationSettings Settings => _settings;
    public string TargetLanguage => cmbTargetLang.SelectedItem?.ToString()?.Split('(')[0].Trim() ?? "한국어";
    public string TranslationStyle => cmbStyle.SelectedItem?.ToString() ?? "자연스럽게";
    public bool UseCustomPrompt => chkCustomPrompt.Checked && !string.IsNullOrWhiteSpace(txtCustomPrompt.Text);
    public string CustomPromptText => txtCustomPrompt.Text.Trim();
    public string? GlossaryPath => _glossaryPath;
    public string? LoadedFilePath => _loadedFilePath;
    public string? LoadedFileContent => txtFilePreview?.Text;
    public string? SavePath => _savePath;
    public bool AutoSaveEnabled => _autoSave;
    
    #endregion
    
    public TranslationSettingsFormEx(TranslationSettings? currentSettings = null)
    {
        _settings = currentSettings ?? new TranslationSettings();
        InitializeComponent();
        ApplyTheme();
        LoadGlossaryToGrid();
        this.TopMost = MainForm.IsAlwaysOnTop;
    }
    
    private void InitializeComponent()
    {
        this.Text = "⚙️ 번역 설정 (확장)";
        this.Size = new Size(900, 750);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MinimumSize = new Size(700, 550);
        
        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        
        // Panel MinSize와 SplitterDistance를 폼 로드 후 안전하게 설정
        // (컨트롤이 폼에 추가된 후에야 Width가 유효함)
        this.Load += (s, e) => {
            try {
                mainSplit.Panel1MinSize = 300;
                mainSplit.Panel2MinSize = 250;
                mainSplit.SplitterDistance = Math.Max(300, Math.Min(450, mainSplit.Width - 250));
            } catch { }
        };
        
        // === 좌측: 파일 미리보기 + 기본 설정 ===
        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        
        // 파일 로드 영역
        var filePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        btnLoadFile = CreateButton("📂 파일 열기", 110);
        btnLoadFile.Click += BtnLoadFile_Click;
        var btnCloseFile = CreateButton("❌ 닫기", 70);
        btnCloseFile.Click += (s, e) => {
            _loadedFilePath = null;
            _savePath = null;
            txtFilePreview.Clear();
            lblFileName.Text = "파일이 로드되지 않음";
            lblFileName.ForeColor = UiTheme.ColorTextMuted;
        };
        lblFileName = new Label { Text = "파일이 로드되지 않음", AutoSize = true, Margin = new Padding(10, 10, 0, 0) };
        filePanel.Controls.AddRange(new Control[] { btnLoadFile, btnCloseFile, lblFileName });
        leftPanel.Controls.Add(filePanel, 0, 0);
        
        // 파일 미리보기
        var grpPreview = new GroupBox { Text = "파일 미리보기", Dock = DockStyle.Fill };
        txtFilePreview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9.5F),
            ReadOnly = true,
            WordWrap = false
        };
        grpPreview.Controls.Add(txtFilePreview);
        leftPanel.Controls.Add(grpPreview, 0, 1);
        
        // 기본 설정
        var grpBasic = new GroupBox { Text = "기본 설정", Dock = DockStyle.Fill };
        var basicPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 2,
            Padding = new Padding(5)
        };
        basicPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        basicPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        
        basicPanel.Controls.Add(CreateLabel("대상 언어:"), 0, 0);
        cmbTargetLang = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbTargetLang.Items.AddRange(new object[] { "한국어 (ko)", "English (en)", "日本語 (ja)", "中文 (zh)" });
        cmbTargetLang.SelectedIndex = 0;
        basicPanel.Controls.Add(cmbTargetLang, 1, 0);
        
        basicPanel.Controls.Add(CreateLabel("번역 스타일:"), 0, 1);
        cmbStyle = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbStyle.Items.AddRange(new object[] { "자연스럽게", "게임 번역", "소설/문학 번역", "대화체" });
        cmbStyle.SelectedIndex = 0;
        basicPanel.Controls.Add(cmbStyle, 1, 1);
        
        basicPanel.Controls.Add(CreateLabel("게임 프리셋:"), 0, 2);
        cmbGamePreset = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbGamePreset.Items.AddRange(new object[] { "(없음)", "붕괴학원2", "원신", "붕괴: 스타레일", "블루 아카이브" });
        cmbGamePreset.SelectedIndex = 0;
        cmbGamePreset.SelectedIndexChanged += CmbGamePreset_SelectedIndexChanged;
        basicPanel.Controls.Add(cmbGamePreset, 1, 2);
        
        grpBasic.Controls.Add(basicPanel);
        leftPanel.Controls.Add(grpBasic, 0, 2);
        
        mainSplit.Panel1.Controls.Add(leftPanel);
        
        // === 우측: 단어장 + 프롬프트 ===
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        
        // 단어장 편집
        var grpGlossary = new GroupBox { Text = "단어장 편집", Dock = DockStyle.Fill };
        var glossaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        glossaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        glossaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        
        var glossaryButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        btnAddTerm = CreateButton("➕ 추가", 80);
        btnAddTerm.Click += BtnAddTerm_Click;
        btnRemoveTerm = CreateButton("➖ 삭제", 80);
        btnRemoveTerm.Click += BtnRemoveTerm_Click;
        btnLoadGlossary = CreateButton("📂 불러오기", 100);
        btnLoadGlossary.Click += BtnLoadGlossary_Click;
        btnSaveGlossary = CreateButton("💾 저장", 80);
        btnSaveGlossary.Click += BtnSaveGlossary_Click;
        glossaryButtons.Controls.AddRange(new Control[] { btnAddTerm, btnRemoveTerm, btnLoadGlossary, btnSaveGlossary });
        glossaryLayout.Controls.Add(glossaryButtons, 0, 0);
        
        dgvGlossary = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        dgvGlossary.Columns.Add("SourceTerm", "원어");
        dgvGlossary.Columns.Add("TargetTerm", "번역어");
        dgvGlossary.Columns["SourceTerm"].FillWeight = 50;
        dgvGlossary.Columns["TargetTerm"].FillWeight = 50;
        glossaryLayout.Controls.Add(dgvGlossary, 0, 1);
        
        grpGlossary.Controls.Add(glossaryLayout);
        rightPanel.Controls.Add(grpGlossary, 0, 0);
        
        // 커스텀 프롬프트
        var grpPrompt = new GroupBox { Text = "커스텀 프롬프트", Dock = DockStyle.Fill };
        var promptLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        promptLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        promptLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        
        chkCustomPrompt = new CheckBox { Text = "커스텀 프롬프트 사용", Dock = DockStyle.Fill };
        chkCustomPrompt.CheckedChanged += (s, e) => txtCustomPrompt.Enabled = chkCustomPrompt.Checked;
        promptLayout.Controls.Add(chkCustomPrompt, 0, 0);
        
        txtCustomPrompt = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5F),
            Enabled = false
        };
        promptLayout.Controls.Add(txtCustomPrompt, 0, 1);
        
        grpPrompt.Controls.Add(promptLayout);
        rightPanel.Controls.Add(grpPrompt, 0, 1);
        
        // 하단 버튼
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 5, 0, 0)
        };
        btnCancel = CreateButton("취소", 90);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnApply = CreateButton("✅ 적용", 90);
        btnApply.BackColor = Color.FromArgb(80, 200, 120);
        btnApply.Click += BtnApply_Click;
        buttonPanel.Controls.AddRange(new Control[] { btnCancel, btnApply });
        rightPanel.Controls.Add(buttonPanel, 0, 2);
        
        mainSplit.Panel2.Controls.Add(rightPanel);
        
        this.Controls.Add(mainSplit);
    }
    
    private Label CreateLabel(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 8, 5, 0)
    };
    
    private Button CreateButton(string text, int width) => new Button
    {
        Text = text,
        Width = width,
        Height = 30,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        Margin = new Padding(3)
    };
    
    private void ApplyTheme()
    {
        UiTheme.ApplyTheme(this);
        dgvGlossary.BackgroundColor = UiTheme.ColorBackground;
        dgvGlossary.DefaultCellStyle.BackColor = UiTheme.ColorBackground;
        dgvGlossary.DefaultCellStyle.ForeColor = Color.White;
        dgvGlossary.DefaultCellStyle.SelectionBackColor = UiTheme.ColorPrimary;
        dgvGlossary.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.ColorSurface;
        dgvGlossary.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        dgvGlossary.EnableHeadersVisualStyles = false;
        txtFilePreview.BackColor = UiTheme.ColorBackground;
        txtFilePreview.ForeColor = Color.White;
        txtCustomPrompt.BackColor = UiTheme.ColorBackground;
        txtCustomPrompt.ForeColor = Color.White;
    }
    
    #region Event Handlers
    
    private void BtnLoadFile_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "텍스트 파일|*.txt;*.json;*.tsv;*.csv|모든 파일|*.*",
            Title = "번역할 파일 선택"
        };
        
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var content = File.ReadAllText(ofd.FileName);
                txtFilePreview.Text = content;
                _loadedFilePath = ofd.FileName;
                
                // 자동 저장 경로 생성
                var dir = Path.GetDirectoryName(ofd.FileName) ?? "";
                var name = "translated_" + Path.GetFileName(ofd.FileName);
                _savePath = Path.Combine(dir, name);
                
                lblFileName.Text = $"✅ {Path.GetFileName(ofd.FileName)} ({new FileInfo(ofd.FileName).Length / 1024}KB)";
                lblFileName.ForeColor = UiTheme.ColorSuccess;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일 로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void BtnAddTerm_Click(object? sender, EventArgs e)
    {
        dgvGlossary.Rows.Add("", "");
        dgvGlossary.CurrentCell = dgvGlossary.Rows[dgvGlossary.Rows.Count - 2].Cells[0];
    }
    
    private void BtnRemoveTerm_Click(object? sender, EventArgs e)
    {
        if (dgvGlossary.SelectedRows.Count > 0)
        {
            foreach (DataGridViewRow row in dgvGlossary.SelectedRows)
            {
                if (!row.IsNewRow)
                    dgvGlossary.Rows.Remove(row);
            }
        }
    }
    
    private void BtnLoadGlossary_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "JSON 파일|*.json|TSV 파일|*.tsv|모든 파일|*.*",
            Title = "단어장 파일 선택"
        };
        
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                _settings.Glossary = TranslationSettings.LoadGlossary(ofd.FileName);
                _glossaryPath = ofd.FileName;
                LoadGlossaryToGrid();
                MessageBox.Show($"{_settings.Glossary.Count}개 용어 로드됨", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"단어장 로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void BtnSaveGlossary_Click(object? sender, EventArgs e)
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "JSON 파일|*.json",
            Title = "단어장 저장",
            FileName = "glossary.json"
        };
        
        if (sfd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                SaveGridToGlossary();
                var json = System.Text.Json.JsonSerializer.Serialize(
                    new { JP_TO_KR = _settings.Glossary },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                );
                File.WriteAllText(sfd.FileName, json);
                _glossaryPath = sfd.FileName;
                MessageBox.Show($"단어장 저장 완료: {_settings.Glossary.Count}개", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    private void CmbGamePreset_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var game = cmbGamePreset.SelectedItem?.ToString() ?? "";
        if (game != "(없음)")
        {
            _settings = TranslationSettings.GetGamePreset(game);
            LoadGlossaryToGrid();
        }
    }
    
    private void BtnApply_Click(object? sender, EventArgs e)
    {
        SaveGridToGlossary();
        DialogResult = DialogResult.OK;
        Close();
    }
    
    #endregion
    
    #region Helpers
    
    private void LoadGlossaryToGrid()
    {
        dgvGlossary.Rows.Clear();
        foreach (var kvp in _settings.Glossary)
        {
            dgvGlossary.Rows.Add(kvp.Key, kvp.Value);
        }
    }
    
    private void SaveGridToGlossary()
    {
        _settings.Glossary.Clear();
        foreach (DataGridViewRow row in dgvGlossary.Rows)
        {
            if (row.IsNewRow) continue;
            var source = row.Cells["SourceTerm"].Value?.ToString()?.Trim();
            var target = row.Cells["TargetTerm"].Value?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(target))
            {
                _settings.Glossary[source] = target;
            }
        }
    }
    
    #endregion
}
