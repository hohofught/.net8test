#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using Point = System.Drawing.Point;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// MainForm - UI Initialization and Layout
/// </summary>
public partial class MainForm
{
    private void InitializeComponent()
    {
        Text = "🌐 Gemini Web Translator";
        Size = new Size(1600, 900);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular); // Win11 Standard Font
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = darkBg;

        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 110 };
        CreateControlPanel(splitContainer.Panel1);
        CreateMainArea(splitContainer.Panel2);

        Controls.Add(splitContainer);
        FormClosing += MainForm_FormClosing;
        Load += MainForm_Load;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            // 1. [핵심] WebView 모드 백그라운드 자동 시작
            // - BtnModeWebView_Click 내부에서 EnsureWebViewSettingsForm()을 호출하여
            //   창을 띄우지 않고(Invisible) 백그라운드에서 WebView2 엔진과 Gemini 페이지를 미리 로딩합니다.
            // - 이를 통해 사용자가 번역을 요청할 때 '세션 없음' 오류 없이 즉시 응답할 수 있습니다.
            BtnModeWebView_Click(null, EventArgs.Empty);

            // [추가 요청] 프로그램 시작 시 WebView 창을 자동으로 열었다가 1초 뒤에 닫음
            // 이는 WebView2가 확실하게 렌더링되고 초기화되도록 강제하는 역할을 함
            _ = Task.Run(async () => {
                await Task.Delay(500); // UI 안정화 대기
                if (!IsHandleCreated || IsDisposed) return;
                
                this.Invoke((MethodInvoker)delegate {
                    try
                    {
                        var browser = ShowBrowserWindow();
                        if (browser != null)
                        {
                            // 1. 창을 매우 작고 거의 안 보이게 설정
                            browser.Size = new Size(10, 10);
                            browser.StartPosition = FormStartPosition.Manual;
                            browser.Location = new Point(0, 0);
                            browser.Opacity = 0.01; 
                            
                            // 2. 로딩 완료 이벤트 대기
                            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null!;
                            handler = (s, args) => {
                                if (args.IsSuccess) {
                                    // 로딩 성공 시 닫기
                                    this.Invoke((MethodInvoker)(() => {
                                        if (!browser.IsDisposed && browser.Visible) browser.Close();
                                    }));
                                    webView.NavigationCompleted -= handler;
                                }
                            };
                            webView.NavigationCompleted += handler;

                            // 3. 타임아웃 (15초) - 혹시 로딩이 너무 오래 걸리거나 이벤트 누락 대비
                            Task.Delay(15000).ContinueWith(t => {
                                try {
                                    this.Invoke((MethodInvoker)(() => {
                                        if (browser != null && !browser.IsDisposed && browser.Visible) 
                                            browser.Close();
                                        webView.NavigationCompleted -= handler;
                                    }));
                                } catch { }
                            });
                        }
                    }
                    catch { }
                });
            });

            // 2. 쿠키 파일이 존재할 경우에만 백그라운드에서 HTTP API 초기화 시도
            // 2. 쿠키 파일이 존재할 경우에만 백그라운드에서 HTTP API 초기화 시도
            // 사용자 요청: HTTP 모드는 기본적으로 꺼짐 상태여야 함 (자동 시작 제거)
            /*
            if (File.Exists(cookiePath))
            {
                await InitializeHttpApiAsync(silent: true);
            }
            */
        }
        catch (Exception ex)
        {
            AppendLog($"초기화 중 오류: {ex.Message}");
        }
    }


    private void CreateControlPanel(Control parent)
    {
        controlPanel = new Panel { Dock = DockStyle.Fill, BackColor = darkPanel, Padding = new Padding(5) };
        
        // Top panel with status and mode buttons
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = darkPanel };
        var titleLabel = new Label { Text = "Gemini", Font = new Font("Segoe UI Variable Display", 14, FontStyle.Bold), ForeColor = accentBlue, AutoSize = true, Location = new Point(10, 10) };
        
        var rightFlow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 5, 10, 0), BackColor = darkPanel };
        
        // 1. HTTP 상태
        pnlStatusHttp = new Panel { Size = new Size(10, 10), Margin = new Padding(0, 10, 5, 0), BackColor = Color.Gray };
        lblStatusHttp = new Label { Text = "HTTP", ForeColor = Color.Gray, Font = new Font("Segoe UI", 9), AutoSize = true, Margin = new Padding(0, 8, 10, 0) };
        
        // 2. Browser 상태
        pnlStatusBrowser = new Panel { Size = new Size(10, 10), Margin = new Padding(0, 10, 5, 0), BackColor = Color.Gray };
        lblStatusBrowser = new Label { Text = "Browser", ForeColor = Color.Gray, Font = new Font("Segoe UI", 9), AutoSize = true, Margin = new Padding(0, 8, 10, 0) };

        // 3. WebView 상태
        pnlStatusWebView = new Panel { Size = new Size(10, 10), Margin = new Padding(0, 10, 5, 0), BackColor = Color.Gray };
        lblStatusWebView = new Label { Text = "WebView", ForeColor = Color.Gray, Font = new Font("Segoe UI", 9), AutoSize = true, Margin = new Padding(0, 8, 15, 0) };

        // Debug Button
        var btnDebug = new Button 
        { 
            Text = "🐞", 
            Width = 40, Height = 32, 
            BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 10, 0)
        };
        btnDebug.Click += (s, e) => { new Forms.DebugForm(this).ShowDialog(this); };
        
        // HTTP Mode Checkbox (controls access to HTTP settings)
        chkHttpMode = new CheckBox {
            Text = "HTTP",
            ForeColor = Color.FromArgb(100, 180, 255),
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Margin = new Padding(0, 8, 5, 0),
            Checked = false
        };
        chkHttpMode.CheckedChanged += ChkHttpMode_CheckedChanged;

        // Mode Buttons
        btnModeHttp = new Button { 
            Text = "HTTP 설정", Width = 90, Height = 32, 
            BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.Gray, FlatStyle = FlatStyle.Flat, 
            Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 0, 8, 0),
            Enabled = false // 기본 비활성화 - 체크박스로 활성화
        };
        btnModeHttp.Click += BtnModeHttpSettings_Click;

        btnModeBrowser = new Button { 
            Text = "브라우저 모드", Width = 100, Height = 32, 
            BackColor = Color.FromArgb(255, 140, 0), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, 
            Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 0, 8, 0)
        };
        btnModeBrowser.Click += BtnModeBrowser_Click;
        
        btnModeWebView = new Button { 
            Text = "WebView 모드", Width = 100, Height = 32, 
            BackColor = Color.FromArgb(0, 150, 136), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, 
            Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 0, 8, 0)
        };
        btnModeWebView.Click += BtnModeWebView_Click;

        // Gemini Model Selection
        cmbGeminiModel = new ComboBox
        {
            Width = 120, Height = 32, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(40, 40, 45), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 5, 8, 0)
        };
        cmbGeminiModel.Items.AddRange(new object[] { "Gemini 1.5 Flash", "Gemini 1.5 Pro" });
        cmbGeminiModel.SelectedIndex = 0;

        // 레이아웃 추가: 상태 3종 세트 + 버튼들
        rightFlow.Controls.AddRange(new Control[] { 
            pnlStatusHttp, lblStatusHttp,
            pnlStatusBrowser, lblStatusBrowser,
            pnlStatusWebView, lblStatusWebView,
            cmbGeminiModel, 
            chkHttpMode, // HTTP 모드 체크박스
            btnModeHttp, 
            btnModeBrowser, 
            btnModeWebView,
            btnDebug
        });
        topPanel.Controls.AddRange(new Control[] { rightFlow, titleLabel });
        topPanel.Controls.AddRange(new Control[] { rightFlow, titleLabel });



        // Bottom panel with options
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = darkPanel };
        var lblLang = new Label { Text = "언어:", Location = new Point(5, 8), AutoSize = true, ForeColor = darkText };
        cmbTargetLang = new ComboBox { Location = new Point(50, 5), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = darkBg, ForeColor = darkText };
        cmbTargetLang.Items.AddRange(new object[] { "한국어 (ko)", "English (en)", "日本語 (ja)", "中文 (zh)" });
        cmbTargetLang.SelectedIndex = 0;

        var lblStyle = new Label { Text = "스타일:", Location = new Point(160, 8), AutoSize = true, ForeColor = darkText };
        cmbStyle = new ComboBox { Location = new Point(210, 5), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = darkBg, ForeColor = darkText };
        cmbStyle.Items.AddRange(new object[] { "자연스럽게", "게임 번역", "소설 번역", "대화체", "공식 문서" });
        cmbStyle.SelectedIndex = 0;

        // Game selector
        var lblGame = new Label { Text = "게임:", Location = new Point(310, 8), AutoSize = true, ForeColor = darkText };
        var cmbGame = new ComboBox { Location = new Point(350, 5), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = darkBg, ForeColor = darkText };
        cmbGame.Items.AddRange(new object[] { "(없음)", "붕괴학원2", "원신", "붕괴: 스타레일" });
        cmbGame.SelectedIndex = 0;
        cmbGame.SelectedIndexChanged += (s, e) => {
            var game = cmbGame.SelectedItem?.ToString() ?? "";
            if (game != "(없음)") { currentSettings = TranslationSettings.GetGamePreset(game); AppendLog($"[설정] 게임: {game}"); }
            else { currentSettings = new TranslationSettings(); }
        };
        
        // Glossary button - Adjusted position
        var btnGlossary = new Button { Text = "단어장", Location = new Point(470, 3), Width = 70, Height = 28, BackColor = Color.FromArgb(80, 80, 100), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        var lblGlossary = new Label { Text = "", Location = new Point(545, 8), AutoSize = true, ForeColor = accentGreen, Font = new Font("Segoe UI", 9) };
        
        // Hide Browser Checkbox - Adjusted position
        chkHideBrowser = new CheckBox { 
            Text = "브라우저 숨기기", 
            Location = new Point(610, 5), 
            AutoSize = true, 
            ForeColor = darkText,
            Checked = false // 기본값: 보임
        };

        // Custom Prompt Checkbox (for non-file mode) - Adjusted position
        chkUseCustomPrompt = new CheckBox {
            Text = "커스텀 설정",
            Location = new Point(730, 5), // Reduced gap
            AutoSize = true,
            ForeColor = Color.FromArgb(100, 200, 255),
            Checked = false
        };
        chkUseCustomPrompt.CheckedChanged += ChkUseCustomPrompt_CheckedChanged;

        // Always on Top Checkbox
        var chkAlwaysOnTop = new CheckBox {
            Text = "항상 위",
            Location = new Point(830, 5),
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 200, 100),
            Checked = false
        };
        chkAlwaysOnTop.CheckedChanged += (s, e) => {
            this.TopMost = chkAlwaysOnTop.Checked;
            AppendLog(chkAlwaysOnTop.Checked ? "[설정] 항상 위 모드 활성화" : "[설정] 항상 위 모드 비활성화");
        };

        btnGlossary.Click += (s, e) => {
            var ofd = new OpenFileDialog { Filter = "JSON|*.json", Title = "단어장 파일 선택" };
            if (ofd.ShowDialog() == DialogResult.OK) {
                currentSettings.Glossary = TranslationSettings.LoadGlossary(ofd.FileName);
                loadedGlossaryPath = ofd.FileName;
                lblGlossary.Text = $"✓ {currentSettings.Glossary.Count}개";
                AppendLog($"[단어장] {Path.GetFileName(ofd.FileName)} 로드: {currentSettings.Glossary.Count}개 용어");
            }
        };
        // OCR Toggle Checkbox
        bottomPanel.Controls.AddRange(new Control[] { lblLang, cmbTargetLang, lblStyle, cmbStyle, lblGame, cmbGame, btnGlossary, lblGlossary, chkHideBrowser, chkUseCustomPrompt, chkAlwaysOnTop });

        controlPanel.Controls.AddRange(new Control[] { topPanel, bottomPanel });
        parent.Controls.Add(controlPanel);
    }


    private void CreateMainArea(Control parent)
    {
        var actionPanel = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = darkPanel, Padding = new Padding(5) };
        
        // 1. File Operations Group (Left)
        var panelFile = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        btnLoadFile = CreateActionButton("파일", 85, Color.FromArgb(60, 60, 70));
        btnLoadFile.Click += BtnLoadFile_Click;
        btnSaveFile = CreateActionButton("저장", 75, Color.FromArgb(60, 60, 70));
        btnSaveFile.Enabled = false;
        btnSaveFile.Click += BtnSaveFile_Click;
        btnClear = CreateActionButton("초기화", 80, Color.FromArgb(70, 50, 50));
        btnClear.ForeColor = Color.FromArgb(255, 150, 150);
        btnClear.Click += (s, e) => { txtInput.Clear(); txtOutput.Clear(); if (isFileMode) BtnLoadFile_Click(null, EventArgs.Empty); httpClient?.ResetSession(); UpdateStatus("초기화됨", Color.Orange); };
        
        panelFile.Controls.AddRange(new Control[] { btnLoadFile, btnSaveFile, btnClear });

        // Separator 1
        var sep1 = new Label { Dock = DockStyle.Left, Width = 2, BackColor = Color.FromArgb(80, 80, 90), Margin = new Padding(8, 10, 8, 10) };

        // 2. Nano Banana Pro Group (Center-Left) - Opens new window
        var panelTools = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(5, 5, 0, 0) };
        btnNanoBanana = CreateActionButton("NanoBanana", 130, Color.FromArgb(130, 70, 160));
        btnNanoBanana.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        btnNanoBanana.Click += BtnNanoBanana_Click; // MainForm.cs에 구현된 핸들러 연결
        panelTools.Controls.Add(btnNanoBanana);

        // 3. Translation Controls Group (Right)
        var panelTranslation = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        btnTranslate = CreateActionButton("번역", 100, accentGreen);
        btnTranslate.Font = new Font("Segoe UI Semibold", 11);
        btnTranslate.Click += BtnTranslate_Click;
        

        btnReviewPrompt = CreateActionButton("프롬프트 검토", 110, Color.FromArgb(70, 70, 80));
        btnReviewPrompt.Font = new Font("Segoe UI", 9.5f);
        btnReviewPrompt.Click += BtnReviewPrompt_Click;

        // [New Button] Custom Prompt Check/Edit
        btnCheckCustomPrompt = CreateActionButton("🔧 커스텀 설정", 110, Color.FromArgb(60, 90, 100));
        btnCheckCustomPrompt.Font = new Font("Segoe UI", 9.5f);
        btnCheckCustomPrompt.Click += BtnCheckCustomPrompt_Click;

        btnStop = CreateActionButton("중지", 80, Color.FromArgb(200, 80, 80));
        btnStop.Enabled = false;
        btnStop.Click += BtnStop_Click;
        
        btnCopy = CreateActionButton("복사", 85, accentBlue);
        btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(txtOutput.Text)) { Clipboard.SetText(txtOutput.Text); UpdateStatus("클립보드 복사됨", Color.Lime); } };
        
        lblProgress = new Label { 
            Text = "", 
            AutoSize = true, 
            ForeColor = accentGreen, 
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin = new Padding(0, 12, 10, 0) 
        };

        panelTranslation.Controls.AddRange(new Control[] { btnTranslate, btnCheckCustomPrompt, btnReviewPrompt, btnStop, btnCopy, lblProgress });

        // Assemble Action Panel
        actionPanel.Controls.Add(panelTranslation); // Right docked first
        actionPanel.Controls.Add(panelTools);       // Left docked
        actionPanel.Controls.Add(sep1);             // Left docked
        actionPanel.Controls.Add(panelFile);        // Left docked

        progressBar = new ProgressBar { Dock = DockStyle.Bottom, Style = ProgressBarStyle.Marquee, Height = 4, Visible = false };
        actionPanel.Controls.Add(progressBar);

        // Main split - 40% input : 60% output
        var mainSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 1200, BackColor = darkPanel, SplitterWidth = 2 };
        mainSplit.SplitterMoved += (s, e) => { }; // Allow resizing
        
        var inputGroup = new GroupBox { Text = "  입력 본문  ", Dock = DockStyle.Fill, Padding = new Padding(5), ForeColor = darkText, BackColor = darkPanel, Font = new Font("Segoe UI Semibold", 10) };
        txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 11), BackColor = Color.FromArgb(36, 36, 36), ForeColor = darkText, BorderStyle = BorderStyle.FixedSingle };
        inputGroup.Controls.Add(txtInput);
        
        // 번역 결과 (탭 없이 심플하게)
        var outputGroup = new GroupBox { Text = "  번역 결과  ", Dock = DockStyle.Fill, Padding = new Padding(5), ForeColor = darkText, BackColor = darkPanel, Font = new Font("Segoe UI Semibold", 10) };
        txtOutput = new RichTextBox { 
            Dock = DockStyle.Fill, 
            ReadOnly = true, 
            BackColor = Color.FromArgb(36, 36, 36),
            ForeColor = Color.FromArgb(240, 240, 240), 
            Font = new Font("맑은 고딕", 11), 
            BorderStyle = BorderStyle.None 
        };
        outputGroup.Controls.Add(txtOutput);

        // Log TextBox (hidden, for internal logging only - visible in DebugForm)
        txtLog = new RichTextBox { Visible = false };

        // WebView (Hidden but Active for Background Processing)
        // Visible=false로 하면 브라우저가 절전 모드(JS 중지)에 들어가므로,
        // Visible=true로 유지하되 1x1 크기로 만들고 맨 뒤로 숨깁니다(Stealth Mode).
        webView = new WebView2 { 
            Visible = true, 
            Dock = DockStyle.None, 
            Size = new Size(1, 1), 
            Location = new Point(0, 0) 
        };
        btnWebNewChat = new Button { Visible = false };
        btnWebRefresh = new Button { Visible = false };

        mainSplit.Panel1.Controls.Add(inputGroup);
        mainSplit.Panel2.Controls.Add(outputGroup);
        
        // 컨트롤 추가 순서 중요: 번역창(mainSplit)과 하단바(actionPanel)를 추가하고 webView는 뒤로 숨깁니다.
        parent.Controls.Add(mainSplit);
        parent.Controls.Add(actionPanel);
        parent.Controls.Add(webView);
        
        webView.SendToBack(); // 스텔스 모드: 다른 UI 뒤에 숨기기
    }

    private Button btnNanoBanana = null!;
    // WebView Controls (hidden, for automation)
    private WebView2 webView = null!;
    private Button btnWebNewChat = null!;
    private Button btnWebRefresh = null!;


    private Button CreateModeButton(string text, Color bg) => new Button { Text = text, ForeColor = Color.White, BackColor = bg, Width = 90, Height = 32, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f), Margin = new Padding(0, 0, 8, 0) };
    private Button CreateActionButton(string text, int width, Color bg) => new Button { Text = text, Width = width + 10, Height = 45, BackColor = bg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5f), Margin = new Padding(0, 0, 10, 0), Cursor = Cursors.Hand };



    private void BtnStop_Click(object? sender, EventArgs e)
    {
        if (isTranslating && !isPaused)
        {
            translationCancellation?.Cancel();
            isPaused = true;
            btnStop.Text = "▶️ 계속";
            btnStop.BackColor = accentGreen;
            UpdateStatus("⏸️ 일시정지됨", Color.Yellow);
        }
        else if (isPaused)
        {
            isPaused = false;
            btnStop.Text = "⏹️ 중지";
            btnStop.BackColor = Color.FromArgb(200, 80, 80);
            ResumeTranslation();
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e) => httpClient?.Dispose();
}
