using System;
using System.Drawing;
using System.Windows.Forms;

namespace GeminiWebTranslator.Forms
{
    /// <summary>
    /// 개발자 및 고급 사용자를 위한 디버깅 및 세션 관리 창입니다.
    /// 로그 표시와 브라우저 제어 기능을 통합합니다.
    /// </summary>
    public class DebugForm : Form
    {
        private readonly MainForm _mainForm;
        private readonly Color darkBg = Color.FromArgb(15, 15, 15);
        private readonly Color darkPanel = Color.FromArgb(25, 25, 25);
        private readonly Color accentBlue = Color.FromArgb(60, 180, 255);
        private readonly Color accentGreen = Color.FromArgb(80, 200, 120);
        private readonly Color darkText = Color.FromArgb(230, 230, 230);
        
        private RichTextBox? txtLog;

        public DebugForm(MainForm mainForm)
        {
            _mainForm = mainForm;
            InitializeComponent();
            LoadLogs();
        }

        private void InitializeComponent()
        {
            this.Text = "🛠️ 디버그 및 로그";
            this.Size = new Size(700, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = darkBg;
            this.ForeColor = darkText;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(500, 400);

            // 메인 분할: 상단 버튼 패널 + 하단 로그 영역
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 250,
                BackColor = darkBg,
                Panel1MinSize = 180,
                Panel2MinSize = 150
            };

            // === 상단: 버튼 패널 ===
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            var lblTitle = new Label
            {
                Text = "디버그 도구",
                Font = new Font("Segoe UI Semibold", 14, FontStyle.Bold),
                ForeColor = accentBlue,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 15)
            };

            // WebView 브라우저 열기 (별도 창)
            var btnOpenWebView = CreateDebugButton("🌐 WebView 브라우저 창 열기", accentBlue);
            btnOpenWebView.Click += (s, e) =>
            {
                _mainForm.ShowBrowserWindow();
            };

            // 브라우저 캐시 초기화
            var btnClearCache = CreateDebugButton("🧹 브라우저 캐시 초기화", Color.FromArgb(70, 70, 75));
            btnClearCache.Click += (s, e) => {
                AppendLocalLog("[Debug] 브라우저 캐시 초기화 버튼 클릭 (미구현)");
            };

            // 브라우저 서비스 강제 재시작 버튼
            var btnForceRestartBrowser = CreateDebugButton("🔥 브라우저 서비스 강제 재시작", Color.FromArgb(180, 70, 70));
            btnForceRestartBrowser.Click += async (s, e) => {
                btnForceRestartBrowser.Enabled = false;
                AppendLocalLog("[Debug] 브라우저 서비스 강제 재시작 요청됨...");
                try
                {
                    await _mainForm.ForceRestartBrowserServicesAsync();
                    AppendLocalLog("[Debug] ✅ 브라우저 서비스 재시작 완료");
                    MessageBox.Show("브라우저 서비스가 재시작되었습니다.", "성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppendLocalLog($"[Debug] ❌ 재시작 실패: {ex.Message}");
                    MessageBox.Show($"재시작 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    btnForceRestartBrowser.Enabled = true;
                }
            };

            // WebView 개발자 도구 열기
            var btnDevTools = CreateDebugButton("🛠️ WebView 개발자 도구 (F12)", Color.FromArgb(0, 150, 136));
            btnDevTools.Click += (s, e) => _mainForm.OpenWebViewDevTools();

            // 로그 폴더 열기 버튼
            var btnOpenLogs = CreateDebugButton("📂 로그 폴더 열기", Color.FromArgb(60, 60, 80));
            btnOpenLogs.Click += (s, e) => {
                try 
                {
                    var logsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    if (System.IO.Directory.Exists(logsPath))
                        System.Diagnostics.Process.Start("explorer.exe", logsPath);
                    else
                        MessageBox.Show("로그 폴더가 존재하지 않습니다.", "알림");
                } 
                catch { }
            };

            // 로그 지우기 버튼
            var btnClearLog = CreateDebugButton("🗑️ 로그 지우기", Color.FromArgb(50, 50, 55));
            btnClearLog.Click += (s, e) => {
                txtLog?.Clear();
                _mainForm.ClearLogs();
            };

            buttonPanel.Controls.AddRange(new Control[] { 
                lblTitle, btnOpenWebView, btnDevTools, btnClearCache, 
                btnForceRestartBrowser, btnOpenLogs, btnClearLog 
            });
            splitContainer.Panel1.Controls.Add(buttonPanel);

            // === 하단: 로그 영역 ===
            var logGroup = new GroupBox
            {
                Text = " 실시간 로그 ",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 9),
                Padding = new Padding(10)
            };

            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.FromArgb(80, 255, 100),
                Font = new Font("Cascadia Code", 10),
                BorderStyle = BorderStyle.None,
                WordWrap = false
            };
            logGroup.Controls.Add(txtLog);
            splitContainer.Panel2.Controls.Add(logGroup);

            this.Controls.Add(splitContainer);
            
            // MainForm의 로그 이벤트 구독
            _mainForm.OnLogMessage += AppendLocalLog;
            this.FormClosed += (s, e) => _mainForm.OnLogMessage -= AppendLocalLog;
        }

        private void LoadLogs()
        {
            // MainForm의 기존 로그를 로드
            var existingLogs = _mainForm.GetLogHistory();
            if (txtLog != null && !string.IsNullOrEmpty(existingLogs))
            {
                txtLog.Text = existingLogs;
                txtLog.SelectionStart = txtLog.Text.Length;
                txtLog.ScrollToCaret();
            }
        }

        private void AppendLocalLog(string message)
        {
            if (txtLog == null || txtLog.IsDisposed) return;
            
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(() => AppendLocalLog(message));
                return;
            }
            
            txtLog.AppendText($"{message}\n");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }

        private Button CreateDebugButton(string text, Color color)
        {
            var btn = new Button
            {
                Text = text,
                Width = 320,
                Height = 42,
                Margin = new Padding(0, 3, 0, 3),
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
    }
}
