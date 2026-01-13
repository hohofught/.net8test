#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// 커스텀 번역 프롬프트 설정 창
/// 프롬프트 활성화/비활성화, 편집, 프리셋 관리를 통합한 폼
/// </summary>
public class TranslationPromptSettingsForm : Form
{
    #region Fields
    
    private CheckBox chkEnabled = null!;
    private TextBox txtPrompt = null!;
    private ComboBox cboPresets = null!;
    private Button btnSavePreset = null!;
    private Button btnDeletePreset = null!;
    private Button btnApply = null!;
    private Button btnCancel = null!;
    private Button btnClear = null!;
    private Label lblStatus = null!;
    private Label lblPlaceholderHelp = null!;
    private GroupBox grpPresets = null!;
    private GroupBox grpPrompt = null!;
    private Panel pnlEnabled = null!;
    
    private PromptPresetCollection _presets;
    private readonly string _presetsPath;
    private bool _isEnabled;
    
    #endregion
    
    #region Properties
    
    /// <summary>
    /// 커스텀 프롬프트 활성화 여부
    /// </summary>
    public bool IsEnabled => chkEnabled.Checked && !string.IsNullOrWhiteSpace(txtPrompt.Text);
    
    /// <summary>
    /// 편집된 프롬프트 텍스트
    /// </summary>
    public string PromptText => txtPrompt.Text.Trim();
    
    #endregion
    
    #region Constructor
    
    public TranslationPromptSettingsForm(string? initialPrompt = null, bool isEnabled = false)
    {
        _presetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "translation_prompt_presets.json");
        _presets = LoadPresets();
        _isEnabled = isEnabled;
        
        InitializeComponent();
        ApplyTheme();
        LoadPresetsToCombo();
        
        chkEnabled.Checked = isEnabled;
        if (!string.IsNullOrEmpty(initialPrompt))
        {
            txtPrompt.Text = initialPrompt;
        }
        
        UpdateUIState();
        
        // MainForm의 항상 위 설정 상속
        this.TopMost = MainForm.IsAlwaysOnTop;
    }
    
    #endregion
    
    #region UI Initialization
    
    private void InitializeComponent()
    {
        this.Text = "🔧 커스텀 번역 프롬프트 설정";
        this.Size = new Size(720, 650);
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
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));  // 활성화 토글
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));  // 프리셋
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 프롬프트 편집
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 상태 표시
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // 버튼
        
        // 1. 활성화 토글 영역
        pnlEnabled = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        
        chkEnabled = new CheckBox
        {
            Text = "커스텀 프롬프트 사용",
            Font = new Font("Segoe UI Semibold", 12),
            AutoSize = true,
            Cursor = Cursors.Hand,
            Location = new Point(10, 15)
        };
        chkEnabled.CheckedChanged += ChkEnabled_CheckedChanged;
        
        var lblInfo = new Label
        {
            Text = "활성화하면 번역 시 아래 프롬프트가 AI에게 전달됩니다.",
            AutoSize = true,
            Location = new Point(10, 45),
            Font = new Font("Segoe UI", 9)
        };
        
        pnlEnabled.Controls.AddRange(new Control[] { chkEnabled, lblInfo });
        mainPanel.Controls.Add(pnlEnabled, 0, 0);
        
        // 2. 프리셋 영역
        grpPresets = new GroupBox
        {
            Text = "프리셋 관리",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        
        var presetPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        
        cboPresets = new ComboBox
        {
            Width = 280,
            DropDownStyle = ComboBoxStyle.DropDown,
            Font = new Font("Segoe UI", 10),
            Margin = new Padding(0, 5, 10, 0)
        };
        cboPresets.SelectedIndexChanged += CboPresets_SelectedIndexChanged;
        
        btnSavePreset = CreateButton("💾 저장", 80);
        btnSavePreset.Click += BtnSavePreset_Click;
        
        btnDeletePreset = CreateButton("🗑️ 삭제", 80);
        btnDeletePreset.Click += BtnDeletePreset_Click;
        
        presetPanel.Controls.AddRange(new Control[] { cboPresets, btnSavePreset, btnDeletePreset });
        grpPresets.Controls.Add(presetPanel);
        mainPanel.Controls.Add(grpPresets, 0, 1);
        
        // 3. 프롬프트 편집 영역
        grpPrompt = new GroupBox
        {
            Text = "프롬프트 내용",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        
        var promptPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        promptPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        promptPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        
        txtPrompt = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 11),
            AcceptsReturn = true,
            AcceptsTab = true
        };
        txtPrompt.TextChanged += (s, e) => UpdateUIState();
        promptPanel.Controls.Add(txtPrompt, 0, 0);
        
        var placeholderPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        
        lblPlaceholderHelp = new Label
        {
            Text = "플레이스홀더: {text} = 번역할 텍스트, {lang} = 대상 언어",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 8, 10, 0)
        };
        
        var btnInsertText = CreateButton("{text}", 70);
        btnInsertText.Click += (s, e) => InsertPlaceholder("{text}");
        
        var btnInsertLang = CreateButton("{lang}", 70);
        btnInsertLang.Click += (s, e) => InsertPlaceholder("{lang}");
        
        btnClear = CreateButton("초기화", 70);
        btnClear.Click += (s, e) => { txtPrompt.Text = ""; };
        
        placeholderPanel.Controls.AddRange(new Control[] { lblPlaceholderHelp, btnInsertText, btnInsertLang, btnClear });
        promptPanel.Controls.Add(placeholderPanel, 0, 1);
        
        grpPrompt.Controls.Add(promptPanel);
        mainPanel.Controls.Add(grpPrompt, 0, 2);
        
        // 4. 상태 표시 영역
        lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10),
            Padding = new Padding(5, 0, 0, 0)
        };
        mainPanel.Controls.Add(lblStatus, 0, 3);
        
        // 5. 하단 버튼 영역
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0)
        };
        
        btnCancel = CreateButton("취소", 100);
        btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
        
        btnApply = CreateButton("✅ 적용", 100);
        btnApply.BackColor = Color.FromArgb(80, 200, 120);
        btnApply.Click += BtnApply_Click;
        
        buttonPanel.Controls.AddRange(new Control[] { btnCancel, btnApply });
        mainPanel.Controls.Add(buttonPanel, 0, 4);
        
        this.Controls.Add(mainPanel);
    }
    
    private Button CreateButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(5, 5, 0, 0)
        };
    }
    
    private void ApplyTheme()
    {
        UiTheme.ApplyTheme(this);
        
        // 추가 스타일링
        lblPlaceholderHelp.ForeColor = UiTheme.ColorTextMuted;
        
        // GroupBoxes use UiTheme.ApplyTheme handling, usually ColorTextMuted for ForeColor
        // If we want Primary color for headers:
        grpPresets.ForeColor = UiTheme.ColorPrimary;
        grpPrompt.ForeColor = UiTheme.ColorPrimary;
    }
    
    private void UpdateUIState()
    {
        bool enabled = chkEnabled.Checked;
        
        // 프리셋, 프롬프트 영역 활성화/비활성화
        grpPresets.Enabled = enabled;
        grpPrompt.Enabled = enabled;
        
        // 상태 메시지 업데이트
        if (!enabled)
        {
            lblStatus.Text = "⚪ 커스텀 프롬프트가 비활성화되어 있습니다.";
            lblStatus.ForeColor = UiTheme.ColorTextMuted;
        }
        else if (string.IsNullOrWhiteSpace(txtPrompt.Text))
        {
            lblStatus.Text = "⚠️ 프롬프트 내용을 입력해주세요.";
            lblStatus.ForeColor = UiTheme.ColorWarning;
        }
        else
        {
            int charCount = txtPrompt.Text.Length;
            bool hasTextPlaceholder = txtPrompt.Text.Contains("{text}");
            
            if (!hasTextPlaceholder)
            {
                lblStatus.Text = $"⚠️ {{text}} 플레이스홀더가 없습니다. 번역할 텍스트가 끝에 추가됩니다. ({charCount}자)";
                lblStatus.ForeColor = UiTheme.ColorWarning;
            }
            else
            {
                lblStatus.Text = $"✅ 커스텀 프롬프트가 활성화됩니다. ({charCount}자)";
                lblStatus.ForeColor = UiTheme.ColorSuccess;
            }
        }
    }
    
    #endregion
    
    #region Event Handlers
    
    private void ChkEnabled_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateUIState();
        
        // 활성화 시 기본 프롬프트 자동 삽입
        if (chkEnabled.Checked && string.IsNullOrWhiteSpace(txtPrompt.Text))
        {
            cboPresets.SelectedIndex = 1; // [기본] 표준 번역 선택
        }
    }
    
    #endregion
    
    #region Preset Management
    
    private PromptPresetCollection LoadPresets()
    {
        try
        {
            if (File.Exists(_presetsPath))
            {
                var json = File.ReadAllText(_presetsPath);
                return JsonConvert.DeserializeObject<PromptPresetCollection>(json) ?? new PromptPresetCollection();
            }
        }
        catch { }
        
        return new PromptPresetCollection();
    }
    
    private void SavePresets()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_presets, Formatting.Indented);
            File.WriteAllText(_presetsPath, json);
        }
        catch { }
    }
    
    private void LoadPresetsToCombo()
    {
        cboPresets.Items.Clear();
        
        // 기본 프리셋 추가
        cboPresets.Items.Add("-- 프리셋 선택 --");
        cboPresets.Items.Add("[기본] 표준 번역");
        cboPresets.Items.Add("[기본] 게임 번역");
        cboPresets.Items.Add("[기본] 소설/문학 번역");
        cboPresets.Items.Add("[기본] 기술 문서 번역");
        
        // 사용자 프리셋 추가
        foreach (var preset in _presets.Presets)
        {
            cboPresets.Items.Add(preset.Name);
        }
        
        cboPresets.SelectedIndex = 0;
    }
    
    private void CboPresets_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selected = cboPresets.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(selected) || selected.StartsWith("--")) return;
        
        // 기본 프리셋
        if (selected.StartsWith("[기본]"))
        {
            txtPrompt.Text = GetDefaultPresetPrompt(selected);
            return;
        }
        
        // 사용자 프리셋
        var preset = _presets.Presets.Find(p => p.Name == selected);
        if (preset != null)
        {
            txtPrompt.Text = preset.Prompt;
        }
    }
    
    private string GetDefaultPresetPrompt(string presetName)
    {
        return presetName switch
        {
            "[기본] 표준 번역" => "다음 텍스트를 {lang}(으)로 자연스럽게 번역해주세요.\n원문의 의미와 뉘앙스를 최대한 유지해주세요.\n\n{text}",
            
            "[기본] 게임 번역" => "다음 게임 텍스트를 {lang}(으)로 번역해주세요.\n- 게임 용어와 고유명사는 그대로 유지하세요\n- 대화체는 자연스러운 구어체로 번역하세요\n- 시스템 메시지는 간결하게 번역하세요\n\n{text}",
            
            "[기본] 소설/문학 번역" => "다음 문학 텍스트를 {lang}(으)로 번역해주세요.\n- 원작의 문체와 분위기를 최대한 살려주세요\n- 비유와 은유 표현도 자연스럽게 번역해주세요\n- 등장인물의 말투 특성을 유지해주세요\n\n{text}",
            
            "[기본] 기술 문서 번역" => "다음 기술 문서를 {lang}(으)로 번역해주세요.\n- 전문 용어는 가능한 한글 표준 용어를 사용하세요\n- 코드, 명령어, 변수명은 번역하지 마세요\n- 간결하고 명확한 문장으로 번역해주세요\n\n{text}",
            
            _ => ""
        };
    }
    
    private void BtnSavePreset_Click(object? sender, EventArgs e)
    {
        var name = cboPresets.Text.Trim();
        if (string.IsNullOrEmpty(name) || name.StartsWith("--") || name.StartsWith("[기본]"))
        {
            MessageBox.Show("새 프리셋 이름을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(txtPrompt.Text))
        {
            MessageBox.Show("프롬프트 내용을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        var existing = _presets.Presets.Find(p => p.Name == name);
        if (existing != null)
        {
            existing.Prompt = txtPrompt.Text;
        }
        else
        {
            _presets.Presets.Add(new PromptPreset { Name = name, Prompt = txtPrompt.Text });
            cboPresets.Items.Add(name);
        }
        
        SavePresets();
        MessageBox.Show($"프리셋 '{name}'이(가) 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    
    private void BtnDeletePreset_Click(object? sender, EventArgs e)
    {
        var name = cboPresets.Text.Trim();
        if (string.IsNullOrEmpty(name) || name.StartsWith("--") || name.StartsWith("[기본]"))
        {
            MessageBox.Show("기본 프리셋은 삭제할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        var preset = _presets.Presets.Find(p => p.Name == name);
        if (preset != null)
        {
            if (MessageBox.Show($"'{name}' 프리셋을 삭제하시겠습니까?", "삭제 확인", 
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _presets.Presets.Remove(preset);
                cboPresets.Items.Remove(name);
                SavePresets();
                
                cboPresets.SelectedIndex = 0;
                txtPrompt.Text = "";
            }
        }
    }
    
    #endregion
    
    #region Actions
    
    private void InsertPlaceholder(string placeholder)
    {
        var selStart = txtPrompt.SelectionStart;
        txtPrompt.Text = txtPrompt.Text.Insert(selStart, placeholder);
        txtPrompt.SelectionStart = selStart + placeholder.Length;
        txtPrompt.Focus();
    }
    
    private void BtnApply_Click(object? sender, EventArgs e)
    {
        if (chkEnabled.Checked && string.IsNullOrWhiteSpace(txtPrompt.Text))
        {
            MessageBox.Show("프롬프트 내용을 입력하거나 활성화를 해제하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        this.DialogResult = DialogResult.OK;
        this.Close();
    }
    
    #endregion
}

#region Data Classes

public class PromptPreset
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
}

public class PromptPresetCollection
{
    public List<PromptPreset> Presets { get; set; } = new();
}

#endregion
