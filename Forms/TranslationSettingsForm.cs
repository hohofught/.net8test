#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// 번역 설정 통합 폼
/// 언어, 스타일, 게임 프리셋, 단어장, 커스텀 프롬프트를 한 곳에서 관리
/// </summary>
public class TranslationSettingsForm : Form
{
    #region Fields
    
    private ComboBox cmbTargetLang = null!;
    private ComboBox cmbStyle = null!;
    private ComboBox cmbGamePreset = null!;
    private Button btnLoadGlossary = null!;
    private Label lblGlossaryStatus = null!;
    private CheckBox chkCustomPrompt = null!;
    private TextBox txtCustomPrompt = null!;
    private ComboBox cboPromptPresets = null!;
    private Button btnSavePreset = null!;
    private Label lblStatus = null!;
    private Button btnApply = null!;
    private Button btnCancel = null!;
    
    private TranslationSettings _settings;
    private string? _glossaryPath;
    private PromptPresetCollection _promptPresets;
    private readonly string _presetsPath;
    
    #endregion
    
    #region Properties
    
    /// <summary>현재 번역 설정</summary>
    public TranslationSettings Settings => _settings;
    
    /// <summary>선택된 대상 언어</summary>
    public string TargetLanguage => cmbTargetLang.SelectedItem?.ToString()?.Split('(')[0].Trim() ?? "한국어";
    
    /// <summary>선택된 번역 스타일</summary>
    public string TranslationStyle => cmbStyle.SelectedItem?.ToString() ?? "자연스럽게";
    
    /// <summary>커스텀 프롬프트 활성화 여부</summary>
    public bool UseCustomPrompt => chkCustomPrompt.Checked && !string.IsNullOrWhiteSpace(txtCustomPrompt.Text);
    
    /// <summary>커스텀 프롬프트 텍스트</summary>
    public string CustomPromptText => txtCustomPrompt.Text.Trim();
    
    /// <summary>단어장 파일 경로</summary>
    public string? GlossaryPath => _glossaryPath;
    
    #endregion
    
    #region Constructor
    
    public TranslationSettingsForm(
        TranslationSettings? currentSettings = null,
        string? targetLang = null,
        string? style = null,
        string? customPrompt = null,
        string? glossaryPath = null)
    {
        _settings = currentSettings ?? new TranslationSettings();
        _glossaryPath = glossaryPath;
        _presetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "translation_prompt_presets.json");
        _promptPresets = LoadPromptPresets();
        
        InitializeComponent();
        ApplyTheme();
        LoadPromptPresetsToCombo();
        
        // 초기값 설정
        if (!string.IsNullOrEmpty(targetLang))
            SelectComboItem(cmbTargetLang, targetLang);
        if (!string.IsNullOrEmpty(style))
            SelectComboItem(cmbStyle, style);
        if (!string.IsNullOrEmpty(customPrompt))
        {
            txtCustomPrompt.Text = customPrompt;
            chkCustomPrompt.Checked = true;
        }
        if (!string.IsNullOrEmpty(glossaryPath) && _settings.Glossary.Count > 0)
        {
            lblGlossaryStatus.Text = $"✅ {_settings.Glossary.Count}개 로드됨";
            lblGlossaryStatus.ForeColor = UiTheme.ColorSuccess;
        }
        
        UpdateUIState();
        this.TopMost = MainForm.IsAlwaysOnTop;
    }
    
    #endregion
    
    #region UI Initialization
    
    private void InitializeComponent()
    {
        this.Text = "⚙️ 번역 설정";
        this.Size = new Size(650, 700);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15),
            RowCount = 5,
            ColumnCount = 1
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); // 기본 설정
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));  // 단어장
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 커스텀 프롬프트
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // 상태
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));  // 버튼
        
        // 1. 기본 설정 영역
        var grpBasic = new GroupBox
        {
            Text = "기본 설정",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        
        var basicPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 2
        };
        basicPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        basicPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        
        // 대상 언어
        basicPanel.Controls.Add(CreateLabel("대상 언어:"), 0, 0);
        cmbTargetLang = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbTargetLang.Items.AddRange(new object[] { "한국어 (ko)", "English (en)", "日本語 (ja)", "中文 (zh)", "Español (es)", "Français (fr)", "Deutsch (de)" });
        cmbTargetLang.SelectedIndex = 0;
        basicPanel.Controls.Add(cmbTargetLang, 1, 0);
        
        // 번역 스타일
        basicPanel.Controls.Add(CreateLabel("번역 스타일:"), 0, 1);
        cmbStyle = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbStyle.Items.AddRange(new object[] { "자연스럽게", "게임 번역", "소설/문학 번역", "대화체", "공식 문서", "기술 문서" });
        cmbStyle.SelectedIndex = 0;
        basicPanel.Controls.Add(cmbStyle, 1, 1);
        
        // 게임 프리셋
        basicPanel.Controls.Add(CreateLabel("게임 프리셋:"), 0, 2);
        cmbGamePreset = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbGamePreset.Items.AddRange(new object[] { "(없음)", "붕괴학원2", "원신", "붕괴: 스타레일", "명일방주", "소녀전선", "블루 아카이브" });
        cmbGamePreset.SelectedIndex = 0;
        cmbGamePreset.SelectedIndexChanged += CmbGamePreset_SelectedIndexChanged;
        basicPanel.Controls.Add(cmbGamePreset, 1, 2);
        
        grpBasic.Controls.Add(basicPanel);
        mainPanel.Controls.Add(grpBasic, 0, 0);
        
        // 2. 단어장 영역
        var grpGlossary = new GroupBox
        {
            Text = "단어장 (용어집)",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        
        var glossaryPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        
        btnLoadGlossary = CreateButton("📂 단어장 열기", 130);
        btnLoadGlossary.Click += BtnLoadGlossary_Click;
        
        lblGlossaryStatus = new Label
        {
            Text = "로드되지 않음",
            AutoSize = true,
            Margin = new Padding(10, 10, 0, 0)
        };
        
        var btnClearGlossary = CreateButton("❌ 초기화", 90);
        btnClearGlossary.Click += (s, e) => 
        {
            _settings.Glossary.Clear();
            _glossaryPath = null;
            lblGlossaryStatus.Text = "로드되지 않음";
            lblGlossaryStatus.ForeColor = UiTheme.ColorTextMuted;
        };
        
        glossaryPanel.Controls.AddRange(new Control[] { btnLoadGlossary, lblGlossaryStatus, btnClearGlossary });
        grpGlossary.Controls.Add(glossaryPanel);
        mainPanel.Controls.Add(grpGlossary, 0, 1);
        
        // 3. 커스텀 프롬프트 영역
        var grpPrompt = new GroupBox
        {
            Text = "커스텀 프롬프트",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        
        var promptLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        promptLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        promptLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        promptLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        
        // 활성화 체크박스
        chkCustomPrompt = new CheckBox
        {
            Text = "커스텀 프롬프트 사용",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10)
        };
        chkCustomPrompt.CheckedChanged += (s, e) => UpdateUIState();
        promptLayout.Controls.Add(chkCustomPrompt, 0, 0);
        
        // 프리셋 선택
        var presetPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        
        cboPromptPresets = new ComboBox
        {
            Width = 280,
            DropDownStyle = ComboBoxStyle.DropDown,
            Font = new Font("Segoe UI", 9)
        };
        cboPromptPresets.SelectedIndexChanged += CboPromptPresets_SelectedIndexChanged;
        
        btnSavePreset = CreateButton("💾 저장", 70);
        btnSavePreset.Click += BtnSavePreset_Click;
        
        presetPanel.Controls.AddRange(new Control[] { cboPromptPresets, btnSavePreset });
        promptLayout.Controls.Add(presetPanel, 0, 1);
        
        // 프롬프트 입력
        txtCustomPrompt = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            AcceptsReturn = true
        };
        promptLayout.Controls.Add(txtCustomPrompt, 0, 2);
        
        grpPrompt.Controls.Add(promptLayout);
        mainPanel.Controls.Add(grpPrompt, 0, 2);
        
        // 4. 상태 영역
        lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9),
            Text = "💡 Tip: {text}에 번역할 텍스트가, {lang}에 대상 언어가 삽입됩니다."
        };
        mainPanel.Controls.Add(lblStatus, 0, 3);
        
        // 5. 버튼 영역
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0)
        };
        
        btnCancel = CreateButton("취소", 90);
        btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
        
        btnApply = CreateButton("✅ 적용", 90);
        btnApply.BackColor = Color.FromArgb(80, 200, 120);
        btnApply.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
        
        buttonPanel.Controls.AddRange(new Control[] { btnCancel, btnApply });
        mainPanel.Controls.Add(buttonPanel, 0, 4);
        
        this.Controls.Add(mainPanel);
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
        Margin = new Padding(5, 3, 0, 0)
    };
    
    private void ApplyTheme()
    {
        UiTheme.ApplyTheme(this);
        lblGlossaryStatus.ForeColor = UiTheme.ColorTextMuted;
        lblStatus.ForeColor = UiTheme.ColorTextMuted;
    }
    
    private void UpdateUIState()
    {
        bool enabled = chkCustomPrompt.Checked;
        cboPromptPresets.Enabled = enabled;
        btnSavePreset.Enabled = enabled;
        txtCustomPrompt.Enabled = enabled;
        txtCustomPrompt.BackColor = enabled ? UiTheme.ColorInputBackground : UiTheme.ColorSurface;
    }
    
    private void SelectComboItem(ComboBox combo, string text)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i]?.ToString()?.Contains(text) == true)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }
    
    #endregion
    
    #region Event Handlers
    
    private void CmbGamePreset_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var game = cmbGamePreset.SelectedItem?.ToString() ?? "";
        if (game != "(없음)")
        {
            _settings = TranslationSettings.GetGamePreset(game);
            if (_settings.Glossary.Count > 0)
            {
                lblGlossaryStatus.Text = $"🎮 {_settings.Glossary.Count}개 (프리셋)";
                lblGlossaryStatus.ForeColor = UiTheme.ColorPrimary;
            }
        }
        else
        {
            _settings = new TranslationSettings();
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
                lblGlossaryStatus.Text = $"✅ {_settings.Glossary.Count}개 로드됨";
                lblGlossaryStatus.ForeColor = UiTheme.ColorSuccess;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"단어장 로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    
    #endregion
    
    #region Prompt Presets
    
    private PromptPresetCollection LoadPromptPresets()
    {
        try
        {
            if (File.Exists(_presetsPath))
            {
                var json = File.ReadAllText(_presetsPath);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<PromptPresetCollection>(json) ?? new PromptPresetCollection();
            }
        }
        catch { }
        return new PromptPresetCollection();
    }
    
    private void SavePromptPresets()
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(_promptPresets, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(_presetsPath, json);
        }
        catch { }
    }
    
    private void LoadPromptPresetsToCombo()
    {
        cboPromptPresets.Items.Clear();
        cboPromptPresets.Items.Add("-- 프리셋 선택 --");
        cboPromptPresets.Items.Add("[기본] 표준 번역");
        cboPromptPresets.Items.Add("[기본] 게임 번역");
        cboPromptPresets.Items.Add("[기본] 소설/문학 번역");
        
        foreach (var preset in _promptPresets.Presets)
            cboPromptPresets.Items.Add(preset.Name);
        
        cboPromptPresets.SelectedIndex = 0;
    }
    
    private void CboPromptPresets_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selected = cboPromptPresets.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(selected) || selected.StartsWith("--")) return;
        
        if (selected.StartsWith("[기본]"))
        {
            txtCustomPrompt.Text = GetDefaultPrompt(selected);
            return;
        }
        
        var preset = _promptPresets.Presets.Find(p => p.Name == selected);
        if (preset != null)
            txtCustomPrompt.Text = preset.Prompt;
    }
    
    private string GetDefaultPrompt(string name) => name switch
    {
        "[기본] 표준 번역" => "다음 텍스트를 {lang}(으)로 자연스럽게 번역해주세요.\n\n{text}",
        "[기본] 게임 번역" => "다음 게임 텍스트를 {lang}(으)로 번역해주세요.\n- 고유명사와 용어는 유지\n- 대화체는 자연스럽게\n\n{text}",
        "[기본] 소설/문학 번역" => "다음 문학 텍스트를 {lang}(으)로 번역해주세요.\n- 문체와 분위기 유지\n- 등장인물 말투 유지\n\n{text}",
        _ => ""
    };
    
    private void BtnSavePreset_Click(object? sender, EventArgs e)
    {
        var name = cboPromptPresets.Text.Trim();
        if (string.IsNullOrEmpty(name) || name.StartsWith("--") || name.StartsWith("[기본]"))
        {
            MessageBox.Show("새 프리셋 이름을 입력하세요.", "알림");
            return;
        }
        if (string.IsNullOrWhiteSpace(txtCustomPrompt.Text))
        {
            MessageBox.Show("프롬프트 내용을 입력하세요.", "알림");
            return;
        }
        
        var existing = _promptPresets.Presets.Find(p => p.Name == name);
        if (existing != null)
            existing.Prompt = txtCustomPrompt.Text;
        else
        {
            _promptPresets.Presets.Add(new PromptPreset { Name = name, Prompt = txtCustomPrompt.Text });
            cboPromptPresets.Items.Add(name);
        }
        
        SavePromptPresets();
        MessageBox.Show($"'{name}' 저장됨", "완료");
    }
    
    #endregion
}
