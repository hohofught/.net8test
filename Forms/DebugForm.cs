using System;
using System.Drawing;
using System.Windows.Forms;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms
{
    /// <summary>
    /// 개발자 및 고급 사용자를 위한 디버깅 및 세션 관리 창입니다.
    /// 로그 표시와 브라우저 제어 기능을 통합합니다.
    /// </summary>
    public class DebugForm : Form
    {
        private readonly MainForm _mainForm;

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
            this.BackColor = UiTheme.ColorBackground;
            this.ForeColor = UiTheme.ColorText;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(500, 400);

            // 메인 분할: 상단 버튼 패널 + 하단 로그 영역
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = UiTheme.ColorBackground
            };

            // Panel MinSize와 SplitterDistance를 폼 로드 후 안전하게 설정
            this.Load += (s, e) =>
            {
                try
                {
                    splitContainer.Panel1MinSize = 180;
                    splitContainer.Panel2MinSize = 150;
                    splitContainer.SplitterDistance = Math.Max(180, Math.Min(250, splitContainer.Height - 150));
                }
                catch { }
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
                ForeColor = UiTheme.ColorPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 15)
            };

            // === 브라우저 프로필 제어 ===
            var lblBrowserTitle = new Label { Text = "브라우저 및 세션 제어", Font = UiTheme.FontHeader, ForeColor = UiTheme.ColorTextMuted, AutoSize = true, Margin = new Padding(0, 5, 0, 5) };

            // 1. 현재 실행 중인 WebView 보기
            var btnOpenCurrent = CreateDebugButton("🌐 [현재] WebView 브라우저 보기", UiTheme.ColorPrimary);
            btnOpenCurrent.Click += (s, e) =>
            {
                if (!btnOpenCurrent.Enabled) return;
                btnOpenCurrent.Enabled = false;
                try
                {
                    AppendLocalLog("[Debug] 현재 실행 중인 WebView 창 열기 시도...");
                    _mainForm.ShowBrowserWindow();
                }
                finally
                {
                    btnOpenCurrent.Enabled = true;
                }
            };

            // 2. 로그인 프로필 전용 브라우저
            var btnOpenLogin = CreateDebugButton("🔐 [로그인 프로필] 브라우저 열기", UiTheme.ColorSuccess);
            btnOpenLogin.Click += async (s, e) =>
            {
                btnOpenLogin.Enabled = false;
                AppendLocalLog("[Debug] [로그인 프로필] 브라우저 열기 시도 (gemini_session)...");
                try
                {
                    await _mainForm.ShowDebugBrowserAsync(null); // null = profileDir
                    AppendLocalLog("[Debug] [성공] 로그인 프로필 브라우저 열림");
                }
                catch (Exception ex)
                {
                    AppendLocalLog($"[Debug] [실패] 오류: {ex.Message}");
                }
                finally { btnOpenLogin.Enabled = true; }
            };

            // 3. 비로그인 프로필 전용 브라우저
            var btnOpenNonLogin = CreateDebugButton("🚀 [비로그인 프로필] 브라우저 열기", UiTheme.ColorSurfaceLight);
            btnOpenNonLogin.Click += async (s, e) =>
            {
                btnOpenNonLogin.Enabled = false;
                AppendLocalLog("[Debug] [비로그인 프로필] 브라우저 열기 시도 (gemini_session/webview)...");
                try
                {
                    await _mainForm.ShowDebugBrowserAsync("webview");
                    AppendLocalLog("[Debug] [성공] 비로그인 프로필 브라우저 열림");
                }
                catch (Exception ex)
                {
                    AppendLocalLog($"[Debug] [실패] 오류: {ex.Message}");
                }
                finally { btnOpenNonLogin.Enabled = true; }
            };

            // 4. (기존) SharedWebViewManager 로그인 보조 도구
            var btnWebViewLoginTool = CreateDebugButton("🔑 로그인 관리 도구 (Shared)", UiTheme.ColorSuccess);
            btnWebViewLoginTool.Click += async (s, e) =>
            {
                btnWebViewLoginTool.Enabled = false;
                AppendLocalLog("[Debug] SharedWebViewManager 로그인 도구 활성화...");
                try
                {
                    var manager = SharedWebViewManager.Instance;
                    manager.OnLog += msg => AppendLocalLog(msg);
                    manager.UseLoginMode = true;
                    if (await manager.InitializeAsync(showWindow: true))
                    {
                        manager.ShowBrowserWindow(autoCloseOnLogin: false);
                        AppendLocalLog("[Debug] [성공] Shared 로그인 창 열림");
                    }
                }
                catch (Exception ex) { AppendLocalLog($"[Debug] [실패] 오류: {ex.Message}"); }
                finally { btnWebViewLoginTool.Enabled = true; }
            };

            // 브라우저 캐시 초기화
            var btnClearCache = CreateDebugButton("🧹 브라우저 캐시 초기화", UiTheme.ColorSurfaceLight);
            btnClearCache.Click += (s, e) =>
            {
                AppendLocalLog("[Debug] 브라우저 캐시 초기화 버튼 클릭 (미구현)");
            };

            // 브라우저 서비스 강제 재시작 버튼
            var btnForceRestartBrowser = CreateDebugButton("🔥 브라우저 서비스 강제 재시작", UiTheme.ColorError);
            btnForceRestartBrowser.Click += async (s, e) =>
            {
                btnForceRestartBrowser.Enabled = false;
                AppendLocalLog("[Debug] 브라우저 서비스 강제 재시작 요청됨...");
                try
                {
                    await _mainForm.ForceRestartBrowserServicesAsync();
                    AppendLocalLog("[Debug] [성공] 브라우저 서비스 재시작 완료");
                    MessageBox.Show("브라우저 서비스가 재시작되었습니다.", "성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppendLocalLog($"[Debug] [실패] 재시작 실패: {ex.Message}");
                    MessageBox.Show($"재시작 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    btnForceRestartBrowser.Enabled = true;
                }
            };

            // WebView 재시작
            var btnRestartWebView = CreateDebugButton("🔄 WebView 재시작", UiTheme.ColorSuccess);
            btnRestartWebView.Click += async (s, e) =>
            {
                btnRestartWebView.Enabled = false;
                AppendLocalLog("[Debug] WebView 재시작 요청됨...");
                try
                {
                    await _mainForm.RestartWebViewAsync();
                    AppendLocalLog("[Debug] [성공] WebView 재시작 완료");
                    MessageBox.Show("WebView가 재시작되었습니다.", "성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppendLocalLog($"[Debug] [실패] WebView 재시작 실패: {ex.Message}");
                    MessageBox.Show($"WebView 재시작 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    btnRestartWebView.Enabled = true;
                }
            };

            // WebView 새 채팅 시작
            var btnNewChat = CreateDebugButton("💬 새 채팅 시작", UiTheme.ColorPrimary);
            btnNewChat.Click += async (s, e) =>
            {
                btnNewChat.Enabled = false;
                AppendLocalLog("[Debug] WebView 새 채팅 시작 요청됨...");
                try
                {
                    await _mainForm.StartNewChatAsync();
                    AppendLocalLog("[Debug] [성공] 새 채팅 시작 완료");
                    MessageBox.Show("새 채팅이 시작되었습니다.", "성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppendLocalLog($"[Debug] [실패] 새 채팅 시작 실패: {ex.Message}");
                    MessageBox.Show($"새 채팅 시작 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    btnNewChat.Enabled = true;
                }
            };

            // 로그 폴더 열기 버튼
            var btnOpenLogs = CreateDebugButton("📂 로그 폴더 열기", UiTheme.ColorSurfaceLight);
            btnOpenLogs.Click += (s, e) =>
            {
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
            var btnClearLog = CreateDebugButton("🗑️ 로그 지우기", UiTheme.ColorSurface);
            btnClearLog.Click += (s, e) =>
            {
                txtLog?.Clear();
                _mainForm.ClearLogs();
            };

            buttonPanel.Controls.AddRange(new Control[] {
                lblTitle, lblBrowserTitle, btnOpenCurrent, btnOpenLogin, btnOpenNonLogin, btnWebViewLoginTool,
                btnRestartWebView, btnNewChat, btnClearCache,
                btnForceRestartBrowser, btnOpenLogs, btnClearLog
            });
            splitContainer.Panel1.Controls.Add(buttonPanel);

            // === 하단: 로그 영역 ===
            var logGroup = new GroupBox
            {
                Text = " 실시간 로그 ",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.ColorTextMuted,
                Font = new Font("Segoe UI", 9),
                Padding = new Padding(10)
            };

            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = UiTheme.ColorBackground,
                ForeColor = UiTheme.ColorSuccess,
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
            // 자동 스크롤 비활성화 - 사용자가 직접 스크롤 가능
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
