using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms
{
    public class PromptCustomizationForm : Form
    {
        private List<string> _allLines;
        private Func<string, Task<string>> _aiGenerator;
        private string _targetLang;
        private Dictionary<string, string>? _glossary;

        public string GeneratedPrompt { get; private set; } = "";
        
        // Controls
        private ListBox lstPreview = null!;
        private TextBox txtSearch = null!;
        private TextBox txtPrompt = null!;
        private Button btnAnalyze = null!;
        private Button btnDetailedAnalysis = null!;
        private Button btnConfirm = null!;
        private Button btnCancel = null!;
        private Button btnPreview = null!;
        private ProgressBar progressBar = null!;
        private SplitContainer splitContainer = null!;
        private Label lblGlossaryStatus = null!;
        private Label lblLineCount = null!;
        private ComboBox cboPresets = null!;
        private TextBox txtPreviewResult = null!;
        private List<int> _selectedIndices = new();

        // Preset Storage
        private static readonly string PresetsPath = 
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt_presets.json");
        private Dictionary<string, string> _presets = new();

        // Genre Templates
        private static readonly Dictionary<string, string> GenreTemplates = new()
        {
            ["🎮 게임 대사"] = "다음 게임 대사를 자연스러운 한국어로 번역하세요.\n- 캐릭터 말투와 개성을 유지하세요\n- 게임 용어는 일반적인 한국 게임 용어 사용\n- 태그(@, #, %% 등)는 절대 번역하지 마세요\n- 간결하고 임팩트 있는 대사를 유지하세요",
            ["📖 소설/웹소설"] = "다음 소설 텍스트를 자연스러운 한국어로 번역하세요.\n- 문학적 표현과 분위기를 유지하세요\n- 등장인물 대화는 한국어 어법에 맞게 조정\n- 서술 부분은 현재형 유지, 대화는 해체/합쇼체 혼용\n- 고유명사는 음역 처리",
            ["🖥️ UI/시스템"] = "다음 UI 텍스트를 번역하세요.\n- 간결하고 명확한 표현 사용\n- 버튼/메뉴는 동사형 명령문으로\n- 영문 약어(OK, Cancel 등)는 한글로 변환\n- 변수(%s, {0} 등)는 그대로 유지",
            ["📱 모바일 앱"] = "다음 앱 텍스트를 번역하세요.\n- 친근하고 간결한 어조 사용\n- 이모지와 특수문자 유지\n- 글자 수 제한을 고려한 짧은 표현\n- 버튼 텍스트는 2-4글자로 간결하게",
            ["📄 문서/매뉴얼"] = "다음 문서를 번역하세요.\n- 공식적이고 정확한 어조 사용\n- 기술 용어는 일관되게 번역\n- 목록과 단계는 명확하게 구분\n- 전문 용어 주석 추가 허용"
        };

        public PromptCustomizationForm(List<string> lines, Func<string, Task<string>> aiGenerator, 
            string targetLang = "한국어", Dictionary<string, string>? glossary = null)
        {
            _allLines = lines.Take(500).ToList();
            _aiGenerator = aiGenerator;
            _targetLang = targetLang;
            _glossary = glossary;

            LoadPresets();
            InitializeComponent();
            LoadData();
        }

        private void InitializeComponent()
        {
            this.Text = "🎨 프롬프트 커스터마이저 - 번역 설정";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = UiTheme.ColorBackground;
            this.Font = new Font("Segoe UI", 9);
            this.KeyPreview = true;
            this.KeyDown += Form_KeyDown;

            splitContainer = new SplitContainer { 
                Dock = DockStyle.Fill, 
                Orientation = Orientation.Vertical, 
                SplitterDistance = 500,
                BackColor = UiTheme.ColorSurface,
                SplitterWidth = 6
            };
            
            // ========== Left Panel - Preview ==========
            var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UiTheme.ColorBackground };
            
            // Header with search
            var pnlLeftHeader = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = UiTheme.ColorBackground };
            var lblPreview = new Label { 
                Text = "📄 파일 미리보기", 
                Location = new Point(0, 0), Height = 25, 
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.White, AutoSize = true
            };
            
            txtSearch = new TextBox {
                Location = new Point(0, 30), Width = 300, Height = 28,
                BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10)
            };
            txtSearch.PlaceholderText = "🔍 검색 (Enter로 다음 결과)";
            txtSearch.KeyDown += TxtSearch_KeyDown;
            
            lblLineCount = new Label {
                Location = new Point(310, 33), AutoSize = true,
                ForeColor = Color.Gray, Font = new Font("Segoe UI", 9)
            };
            
            pnlLeftHeader.Controls.AddRange(new Control[] { lblPreview, txtSearch, lblLineCount });
            
            // Selection buttons
            var pnlSelectionButtons = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = UiTheme.ColorBackground };
            
            var btnSelectAll = CreateStyledButton("전체 선택", UiTheme.ColorSurfaceLight);
            btnSelectAll.Location = new Point(0, 5);
            btnSelectAll.Click += (s, e) => SelectAllLines(true);
            
            var btnSelectNone = CreateStyledButton("전체 해제", UiTheme.ColorSurfaceLight);
            btnSelectNone.Location = new Point(95, 5);
            btnSelectNone.Click += (s, e) => SelectAllLines(false);
            
            var btnSelectFirst10 = CreateStyledButton("처음 10줄", UiTheme.ColorSurfaceLight);
            btnSelectFirst10.Location = new Point(190, 5);
            btnSelectFirst10.Click += (s, e) => SelectFirstN(10);
            
            var btnSelectFirst50 = CreateStyledButton("처음 50줄", UiTheme.ColorSurfaceLight);
            btnSelectFirst50.Location = new Point(285, 5);
            btnSelectFirst50.Click += (s, e) => SelectFirstN(50);
            
            pnlSelectionButtons.Controls.AddRange(new Control[] { btnSelectAll, btnSelectNone, btnSelectFirst10, btnSelectFirst50 });
            
            // Preview List
            lstPreview = new ListBox { 
                Dock = DockStyle.Fill, 
                SelectionMode = SelectionMode.MultiExtended,
                BackColor = UiTheme.ColorSurface,
                ForeColor = UiTheme.ColorText,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9.5f),
                IntegralHeight = false
            };
            lstPreview.DrawMode = DrawMode.OwnerDrawFixed;
            lstPreview.ItemHeight = 22;
            lstPreview.DrawItem += LstPreview_DrawItem;
            lstPreview.SelectedIndexChanged += LstPreview_SelectedIndexChanged;

            pnlLeft.Controls.Add(lstPreview);
            pnlLeft.Controls.Add(pnlSelectionButtons);
            pnlLeft.Controls.Add(pnlLeftHeader);

            // ========== Right Panel - Prompt Configuration ==========
            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UiTheme.ColorBackground };
            
            // Header
            var lblPrompt = new Label { 
                Text = "📝 번역 프롬프트 설정", 
                Dock = DockStyle.Top, Height = 30, 
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.White
            };

            // Glossary Status
            lblGlossaryStatus = new Label {
                Text = _glossary != null && _glossary.Count > 0 
                    ? $"📚 단어장: {_glossary.Count}개 용어 적용됨" 
                    : "📚 단어장: 미설정 (설정 > 단어장에서 추가)",
                Dock = DockStyle.Top, Height = 22,
                ForeColor = _glossary != null && _glossary.Count > 0 ? Color.FromArgb(100, 220, 130) : Color.Gray,
                Font = new Font("Segoe UI", 9)
            };

            // ===== Presets Panel =====
            var pnlPresets = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = UiTheme.ColorBackground };
            
            var lblPresets = new Label { Text = "프리셋:", Location = new Point(0, 10), AutoSize = true, ForeColor = Color.White };
            cboPresets = new ComboBox { 
                Location = new Point(55, 6), Width = 180, 
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = UiTheme.ColorSurface, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cboPresets.Items.Add("-- 선택 --");
            foreach (var preset in _presets.Keys) cboPresets.Items.Add(preset);
            cboPresets.SelectedIndex = 0;
            cboPresets.SelectedIndexChanged += CboPresets_SelectedIndexChanged;
            
            var btnSavePreset = CreateStyledButton("💾 저장", UiTheme.ColorPrimary);
            btnSavePreset.Location = new Point(245, 5);
            btnSavePreset.Width = 70;
            btnSavePreset.Click += BtnSavePreset_Click;
            
            var btnDeletePreset = CreateStyledButton("🗑️", UiTheme.ColorError);
            btnDeletePreset.Location = new Point(320, 5);
            btnDeletePreset.Width = 40;
            btnDeletePreset.Click += BtnDeletePreset_Click;
            
            pnlPresets.Controls.AddRange(new Control[] { lblPresets, cboPresets, btnSavePreset, btnDeletePreset });

            // ===== Genre Templates =====
            var pnlTemplates = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = UiTheme.ColorBackground };
            var lblTemplates = new Label { Text = "템플릿:", Location = new Point(0, 12), AutoSize = true, ForeColor = Color.White };
            
            int templateX = 55;
            foreach (var template in GenreTemplates)
            {
                var btn = CreateStyledButton(template.Key, UiTheme.ColorSurface);
                btn.Location = new Point(templateX, 7);
                btn.Width = 95;
                btn.Height = 28;
                btn.Font = new Font("Segoe UI", 8);
                btn.Tag = template.Value;
                btn.Click += (s, e) => { if (s is Button b && b.Tag is string t) txtPrompt.Text = t; };
                pnlTemplates.Controls.Add(btn);
                templateX += 100;
            }
            pnlTemplates.Controls.Add(lblTemplates);

            // ===== Analysis Buttons Panel =====
            var analysisPanel = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = UiTheme.ColorBackground };
            
            btnAnalyze = CreateStyledButton("⚡ 빠른 분석", UiTheme.ColorPrimary);
            btnAnalyze.Location = new Point(0, 8);
            btnAnalyze.Width = 130;
            btnAnalyze.Height = 35;
            btnAnalyze.Click += BtnQuickAnalyze_Click;

            btnDetailedAnalysis = CreateStyledButton("🔍 상세 분석", UiTheme.ColorPrimary);
            btnDetailedAnalysis.Location = new Point(140, 8);
            btnDetailedAnalysis.Width = 130;
            btnDetailedAnalysis.Height = 35;
            btnDetailedAnalysis.Click += BtnDetailedAnalysis_Click;
            
            btnPreview = CreateStyledButton("👁️ 번역 미리보기", UiTheme.ColorSuccess);
            btnPreview.Location = new Point(280, 8);
            btnPreview.Width = 140;
            btnPreview.Height = 35;
            btnPreview.Click += BtnPreview_Click;
            
            analysisPanel.Controls.AddRange(new Control[] { btnAnalyze, btnDetailedAnalysis, btnPreview });
            
            // ===== Prompt TextBox =====
            txtPrompt = new TextBox { 
                Dock = DockStyle.Fill, 
                Multiline = true, 
                ScrollBars = ScrollBars.Vertical, 
                Font = new Font("Consolas", 10),
                BackColor = UiTheme.ColorSurface,
                ForeColor = UiTheme.ColorText,
                BorderStyle = BorderStyle.FixedSingle,
                AcceptsTab = true
            };
            
            // Preview Result (Bottom)
            var pnlPreviewResult = new Panel { Dock = DockStyle.Bottom, Height = 120, BackColor = UiTheme.ColorSurface };
            var lblPreviewResult = new Label { 
                Text = "🔄 번역 미리보기 결과", Dock = DockStyle.Top, Height = 22,
                ForeColor = Color.Gray, Font = new Font("Segoe UI", 9)
            };
            txtPreviewResult = new TextBox {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                BackColor = UiTheme.ColorSurface, ForeColor = UiTheme.ColorSuccess,
                BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f),
                ScrollBars = ScrollBars.Vertical
            };
            pnlPreviewResult.Controls.Add(txtPreviewResult);
            pnlPreviewResult.Controls.Add(lblPreviewResult);
            
            // ===== Bottom Actions =====
            var pnlBottomActions = new Panel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(0, 10, 0, 0), BackColor = UiTheme.ColorBackground };
            
            btnConfirm = CreateStyledButton("[적용] 프롬프트 적용", UiTheme.ColorSuccess);
            btnConfirm.Dock = DockStyle.Right;
            btnConfirm.Width = 140;
            btnConfirm.Height = 40;
            btnConfirm.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnConfirm.Click += (s, e) => { GeneratedPrompt = txtPrompt.Text; DialogResult = DialogResult.OK; Close(); };
            
            btnCancel = CreateStyledButton("건너뛰기 (기본 프롬프트 사용)", UiTheme.ColorSurfaceLight);
            btnCancel.Dock = DockStyle.Left;
            btnCancel.Width = 200;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Ignore; Close(); };

            pnlBottomActions.Controls.Add(btnConfirm);
            pnlBottomActions.Controls.Add(btnCancel);

            progressBar = new ProgressBar { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, Visible = false, Height = 4 };

            // Assemble Right Panel
            pnlRight.Controls.Add(txtPrompt);
            pnlRight.Controls.Add(pnlPreviewResult);
            pnlRight.Controls.Add(progressBar);
            pnlRight.Controls.Add(analysisPanel);
            pnlRight.Controls.Add(pnlTemplates);
            pnlRight.Controls.Add(pnlPresets);
            pnlRight.Controls.Add(lblGlossaryStatus);
            pnlRight.Controls.Add(lblPrompt);
            pnlRight.Controls.Add(pnlBottomActions);

            splitContainer.Panel1.Controls.Add(pnlLeft);
            splitContainer.Panel2.Controls.Add(pnlRight);

            this.Controls.Add(splitContainer);
        }

        private Button CreateStyledButton(string text, Color backColor)
        {
            return new Button {
                Text = text, Width = 90, Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
        }

        private void LoadData()
        {
            lstPreview.Items.Clear();
            for (int i = 0; i < _allLines.Count; i++)
            {
                var line = _allLines[i];
                var display = $"{i + 1,4}: {(line.Length > 90 ? line.Substring(0, 90) + "..." : line)}";
                lstPreview.Items.Add(display);
            }
            
            lblLineCount.Text = $"총 {_allLines.Count}줄";
            SelectFirstN(10);
        }

        private void LstPreview_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            
            var isSelected = _selectedIndices.Contains(e.Index);
            var backColor = isSelected ? UiTheme.ColorPrimary : 
                            (e.Index % 2 == 0 ? UiTheme.ColorSurface : UiTheme.ColorSurfaceLight);
            
            e.Graphics.FillRectangle(new SolidBrush(backColor), e.Bounds);
            
            var text = lstPreview.Items[e.Index]?.ToString() ?? "";
            var textColor = isSelected ? Color.White : UiTheme.ColorText;
            e.Graphics.DrawString(text, e.Font ?? Font, new SolidBrush(textColor), e.Bounds.X + 5, e.Bounds.Y + 3);
        }

        private void LstPreview_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _selectedIndices.Clear();
            foreach (int idx in lstPreview.SelectedIndices)
                _selectedIndices.Add(idx);
            lstPreview.Invalidate();
        }

        private void SelectAllLines(bool select)
        {
            _selectedIndices.Clear();
            if (select)
            {
                for (int i = 0; i < lstPreview.Items.Count; i++)
                    _selectedIndices.Add(i);
            }
            lstPreview.Invalidate();
        }

        private void SelectFirstN(int n)
        {
            _selectedIndices.Clear();
            for (int i = 0; i < Math.Min(n, lstPreview.Items.Count); i++)
                _selectedIndices.Add(i);
            lstPreview.Invalidate();
        }

        private int _lastSearchIndex = -1;
        private void TxtSearch_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !string.IsNullOrEmpty(txtSearch.Text))
            {
                var search = txtSearch.Text.ToLower();
                for (int i = _lastSearchIndex + 1; i < _allLines.Count; i++)
                {
                    if (_allLines[i].ToLower().Contains(search))
                    {
                        lstPreview.SelectedIndex = i;
                        lstPreview.TopIndex = Math.Max(0, i - 5);
                        _lastSearchIndex = i;
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        return;
                    }
                }
                _lastSearchIndex = -1; // Reset to start
                MessageBox.Show("더 이상 결과가 없습니다.", "검색", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void Form_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            if (e.Control && e.KeyCode == Keys.Enter) { GeneratedPrompt = txtPrompt.Text; DialogResult = DialogResult.OK; Close(); }
        }

        #region Presets

        private void LoadPresets()
        {
            try
            {
                if (File.Exists(PresetsPath))
                    _presets = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(PresetsPath)) ?? new();
            }
            catch { _presets = new(); }
        }

        private void SavePresets()
        {
            try { File.WriteAllText(PresetsPath, JsonConvert.SerializeObject(_presets, Formatting.Indented)); }
            catch { }
        }

        private void CboPresets_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cboPresets.SelectedIndex <= 0) return;
            var name = cboPresets.SelectedItem?.ToString();
            if (name != null && _presets.TryGetValue(name, out var prompt))
                txtPrompt.Text = prompt;
        }

        private void BtnSavePreset_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtPrompt.Text))
            {
                MessageBox.Show("저장할 프롬프트를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            var name = Microsoft.VisualBasic.Interaction.InputBox("프리셋 이름을 입력하세요:", "프리셋 저장", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            
            _presets[name] = txtPrompt.Text;
            SavePresets();
            
            if (!cboPresets.Items.Contains(name))
                cboPresets.Items.Add(name);
            cboPresets.SelectedItem = name;
            
            MessageBox.Show($"프리셋 '{name}'이(가) 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnDeletePreset_Click(object? sender, EventArgs e)
        {
            if (cboPresets.SelectedIndex <= 0) return;
            
            var name = cboPresets.SelectedItem?.ToString();
            if (name != null && MessageBox.Show($"'{name}' 프리셋을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                _presets.Remove(name);
                SavePresets();
                cboPresets.Items.Remove(name);
                cboPresets.SelectedIndex = 0;
            }
        }

        #endregion

        #region Analysis

        private string BuildGlossaryContext()
        {
            if (_glossary == null || _glossary.Count == 0) return "";
            
            var sb = new StringBuilder();
            sb.AppendLine("\n【적용된 단어장】");
            foreach (var entry in _glossary.Take(30))
                sb.AppendLine($"  {entry.Key} → {entry.Value}");
            return sb.ToString();
        }

        private List<string> GetSelectedSamples()
        {
            return _selectedIndices.Where(i => i < _allLines.Count).Select(i => _allLines[i]).ToList();
        }

        private async void BtnQuickAnalyze_Click(object? sender, EventArgs e)
        {
            var samples = GetSelectedSamples();
            if (samples.Count == 0) { ShowNoSampleWarning(); return; }

            var promptInput = string.Join("\n", samples.Take(5));
            var glossaryContext = BuildGlossaryContext();
            
            var analysisPrompt = $@"다음 샘플 텍스트를 분석하고, 이 파일 구조에 맞는 **짧은 번역 지침**(3~5줄)을 작성하세요.
{glossaryContext}
【샘플】
{promptInput}

【요청】
- 파일 형식(TSV/JSON 등)과 필드 구조를 파악하세요.
- 번역 시 유지해야 할 태그나 구분자를 명시하세요.
- 문체와 어조에 대한 간단한 지침을 포함하세요.
- **결과물만 출력** (설명, 인사말 불필요)";

            await ExecuteAnalysis(analysisPrompt);
        }

        private async void BtnDetailedAnalysis_Click(object? sender, EventArgs e)
        {
            var samples = GetSelectedSamples();
            if (samples.Count == 0) { ShowNoSampleWarning(); return; }

            var promptInput = string.Join("\n", samples);
            var glossaryContext = BuildGlossaryContext();
            
            var analysisPrompt = $@"당신은 전문 번역 컨설턴트입니다. 다음 텍스트 샘플을 분석하고, 번역 작업에 대해 자세히 답변해주세요.
{glossaryContext}
【샘플 텍스트】
{promptInput}

【질문 사항】
1. 이 텍스트의 장르/유형은 무엇인가요? (게임 대사, 소설, UI 텍스트 등)
2. 등장인물이 있다면, 각 캐릭터의 말투 특징은?
3. 특수 태그나 변수(@, #, %% 등)가 있다면 어떻게 처리해야 하나요?
4. 이 텍스트를 자연스러운 {_targetLang}로 번역할 때 주의할 점은?
5. 추천하는 번역 스타일과 어조는?

【출력 형식】
위 분석을 바탕으로, 번역가가 사용할 수 있는 **최적화된 시스템 프롬프트**를 작성해주세요.
프롬프트는 명령형으로 작성하고, 10줄 이내로 간결하게 유지하세요.";

            await ExecuteAnalysis(analysisPrompt);
        }

        private async void BtnPreview_Click(object? sender, EventArgs e)
        {
            var samples = GetSelectedSamples();
            if (samples.Count == 0) { ShowNoSampleWarning(); return; }
            if (string.IsNullOrWhiteSpace(txtPrompt.Text))
            {
                MessageBox.Show("프롬프트를 먼저 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sampleText = samples.First();
            var testPrompt = $@"{txtPrompt.Text}

【번역 대상】
{sampleText}

위 내용을 번역해주세요. **번역 결과만 출력**하세요.";

            try
            {
                SetLoading(true);
                txtPreviewResult.Text = "번역 중...";
                var result = await _aiGenerator(testPrompt);
                txtPreviewResult.Text = $"원문: {sampleText}\n\n번역: {result.Trim()}";
            }
            catch (Exception ex)
            {
                txtPreviewResult.Text = $"오류: {ex.Message}";
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void ShowNoSampleWarning()
        {
            MessageBox.Show("분석할 샘플을 하나 이상 선택해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private async Task ExecuteAnalysis(string prompt)
        {
            try
            {
                SetLoading(true);
                var result = await _aiGenerator(prompt);
                txtPrompt.Text = result.Trim();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"분석 실패: {ex.Message}\n\nAPI 연결 상태를 확인해주세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void SetLoading(bool loading)
        {
            progressBar.Visible = loading;
            btnAnalyze.Enabled = !loading;
            btnDetailedAnalysis.Enabled = !loading;
            btnPreview.Enabled = !loading;
            btnConfirm.Enabled = !loading;
            txtPrompt.Enabled = !loading;
            lstPreview.Enabled = !loading;
            
            if (loading) 
            {
                btnAnalyze.Text = "분석 중...";
                btnDetailedAnalysis.Text = "분석 중...";
                btnPreview.Text = "처리 중...";
            }
            else 
            {
                btnAnalyze.Text = "⚡ 빠른 분석";
                btnDetailedAnalysis.Text = "🔍 상세 분석";
                btnPreview.Text = "👁️ 번역 미리보기";
            }
        }

        #endregion
    }
}

