using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
        private CheckedListBox lstPreview = null!;
        private TextBox txtPrompt = null!;
        private Button btnAnalyze = null!;
        private Button btnDetailedAnalysis = null!;
        private Button btnConfirm = null!;
        private Button btnCancel = null!;
        private ProgressBar progressBar = null!;
        private SplitContainer splitContainer = null!;
        private Label lblGlossaryStatus = null!;

        public PromptCustomizationForm(List<string> lines, Func<string, Task<string>> aiGenerator, 
            string targetLang = "한국어", Dictionary<string, string>? glossary = null)
        {
            _allLines = lines.Take(400).ToList();
            _aiGenerator = aiGenerator;
            _targetLang = targetLang;
            _glossary = glossary;

            InitializeComponent();
            LoadData();
        }

        private void InitializeComponent()
        {
            this.Text = "프롬프트 커스텀 (단어장 + AI 분석)";
            this.Size = new Size(1100, 750);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(30, 30, 35);

            splitContainer = new SplitContainer { 
                Dock = DockStyle.Fill, 
                Orientation = Orientation.Vertical, 
                SplitterDistance = 450,
                BackColor = Color.FromArgb(40, 40, 45)
            };
            
            // Left Panel - Preview
            var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Color.FromArgb(30, 30, 35) };
            var lblPreview = new Label { 
                Text = "📄 파일 미리보기 (분석할 샘플 선택)", 
                Dock = DockStyle.Top, Height = 30, 
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White
            };
            
            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 35, BackColor = Color.FromArgb(30, 30, 35) };
            var btnSelectAll = new Button { Text = "전체 선택", Location = new Point(0, 2), Width = 80, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White };
            btnSelectAll.Click += (s, e) => SetAllChecked(true);
            var btnSelectNone = new Button { Text = "전체 해제", Location = new Point(85, 2), Width = 80, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White };
            btnSelectNone.Click += (s, e) => SetAllChecked(false);
            btnPanel.Controls.AddRange(new Control[] { btnSelectAll, btnSelectNone });
            
            lstPreview = new CheckedListBox { 
                Dock = DockStyle.Fill, 
                CheckOnClick = true,
                BackColor = Color.FromArgb(40, 40, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            pnlLeft.Controls.Add(lstPreview);
            pnlLeft.Controls.Add(btnPanel);
            pnlLeft.Controls.Add(lblPreview);

            // Right Panel - Prompt Configuration
            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Color.FromArgb(30, 30, 35) };
            var lblPrompt = new Label { 
                Text = "📝 커스텀 번역 프롬프트", 
                Dock = DockStyle.Top, Height = 30, 
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White
            };

            // Glossary Status
            lblGlossaryStatus = new Label {
                Text = _glossary != null && _glossary.Count > 0 
                    ? $"📚 단어장: {_glossary.Count}개 용어 적용됨" 
                    : "📚 단어장: 미설정",
                Dock = DockStyle.Top, Height = 25,
                ForeColor = _glossary != null && _glossary.Count > 0 ? Color.LightGreen : Color.Gray,
                Font = new Font("Segoe UI", 9)
            };

            // Analysis Buttons Panel
            var analysisPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.FromArgb(30, 30, 35) };
            
            btnAnalyze = new Button { 
                Text = "⚡ 빠른 분석 (간단 프롬프트)", 
                Location = new Point(0, 5), Width = 200, Height = 35, 
                BackColor = Color.FromArgb(60, 150, 200), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnAnalyze.Click += BtnQuickAnalyze_Click;

            btnDetailedAnalysis = new Button { 
                Text = "🔍 상세 분석 (AI에게 번역 방법 질문)", 
                Location = new Point(210, 5), Width = 250, Height = 35, 
                BackColor = Color.FromArgb(100, 80, 180), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnDetailedAnalysis.Click += BtnDetailedAnalysis_Click;
            
            analysisPanel.Controls.AddRange(new Control[] { btnAnalyze, btnDetailedAnalysis });
            
            txtPrompt = new TextBox { 
                Dock = DockStyle.Fill, 
                Multiline = true, 
                ScrollBars = ScrollBars.Vertical, 
                Font = new Font("Consolas", 10),
                BackColor = Color.FromArgb(40, 40, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            
            var pnlBottomActions = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 10, 0, 0), BackColor = Color.FromArgb(25, 25, 30) };
            btnConfirm = new Button { Text = "✅ 적용", Dock = DockStyle.Right, Width = 120, BackColor = Color.FromArgb(80, 200, 120), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnConfirm.Click += (s, e) => { GeneratedPrompt = txtPrompt.Text; DialogResult = DialogResult.OK; Close(); };
            
            btnCancel = new Button { Text = "건너뛰기", Dock = DockStyle.Left, Width = 100, BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Ignore; Close(); };

            pnlBottomActions.Controls.Add(btnConfirm);
            pnlBottomActions.Controls.Add(btnCancel);

            progressBar = new ProgressBar { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, Visible = false, Height = 5 };

            pnlRight.Controls.Add(txtPrompt);
            pnlRight.Controls.Add(progressBar);
            pnlRight.Controls.Add(analysisPanel);
            pnlRight.Controls.Add(lblGlossaryStatus);
            pnlRight.Controls.Add(lblPrompt);
            pnlRight.Controls.Add(pnlBottomActions);

            splitContainer.Panel1.Controls.Add(pnlLeft);
            splitContainer.Panel2.Controls.Add(pnlRight);

            this.Controls.Add(splitContainer);
        }

        private void LoadData()
        {
            lstPreview.Items.Clear();
            foreach (var line in _allLines)
            {
                var display = line.Length > 100 ? line.Substring(0, 100) + "..." : line;
                lstPreview.Items.Add(display);
            }
            
            int count = Math.Min(lstPreview.Items.Count, 10);
            for (int i = 0; i < count; i++) lstPreview.SetItemChecked(i, true);
        }

        private void SetAllChecked(bool state)
        {
            for (int i = 0; i < lstPreview.Items.Count; i++)
                lstPreview.SetItemChecked(i, state);
        }

        private string BuildGlossaryContext()
        {
            if (_glossary == null || _glossary.Count == 0) return "";
            
            var sb = new StringBuilder();
            sb.AppendLine("\n【적용된 단어장】");
            foreach (var entry in _glossary.Take(30))
            {
                sb.AppendLine($"  {entry.Key} → {entry.Value}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 빠른 분석 - 짧고 간결한 프롬프트 생성
        /// </summary>
        private async void BtnQuickAnalyze_Click(object? sender, EventArgs e)
        {
            var samples = GetSelectedSamples();
            if (samples.Count == 0) { ShowNoSampleWarning(); return; }

            var promptInput = string.Join("\n", samples.Take(5)); // 최대 5개만
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

        /// <summary>
        /// 상세 분석 - AI에게 번역 방법을 자세히 질문
        /// </summary>
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

        private List<string> GetSelectedSamples()
        {
            var samples = new List<string>();
            foreach (int index in lstPreview.CheckedIndices)
            {
                if (index < _allLines.Count)
                    samples.Add(_allLines[index]);
            }
            return samples;
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
            btnConfirm.Enabled = !loading;
            txtPrompt.Enabled = !loading;
            lstPreview.Enabled = !loading;
            
            if (loading) 
            {
                btnAnalyze.Text = "분석 중...";
                btnDetailedAnalysis.Text = "분석 중...";
            }
            else 
            {
                btnAnalyze.Text = "⚡ 빠른 분석 (간단 프롬프트)";
                btnDetailedAnalysis.Text = "🔍 상세 분석 (AI에게 번역 방법 질문)";
            }
        }
    }
}
