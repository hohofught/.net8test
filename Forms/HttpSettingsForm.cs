#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Point = System.Drawing.Point;
using GeminiWebTranslator.Services;
using GeminiWebTranslator.Models;

namespace GeminiWebTranslator.Forms
{
    /// <summary>
    /// HTTP API 모드를 위한 통합 설정 화면입니다.
    /// 쿠키 관리, 채팅 관리, 모델 선택, 이미지 생성, 디버그 로그 기능을 제공합니다.
    /// </summary>
    public class HttpSettingsForm : Form
    {
        // 쿠키 탭 컨트롤
        private Button? btnAutoExtract;
        private Button? btnManualLoad;
        private Button? btnSave;
        private TextBox? txtPSID;
        private TextBox? txtPSIDTS;
        private TextBox? txtUserAgent;
        private Button? btnReconnectApi;
        private Button? btnRefreshCookies;
        private Label? lblStatus;
        
        // 채팅 관리 탭 컨트롤
        private ListBox? lstChats;
        private Button? btnLoadChats;
        private Button? btnDeleteChat;
        private Label? lblChatStatus;
        
        // 모델 선택 (메인 폼에 통합됨)
        private ComboBox? cmbModel;
        
        // 이미지 생성은 NanoBanana에 통합됨
        
        // 디버그 탭 컨트롤
        private TextBox? txtDebugLog;
        private Button? btnClearLog;
        private CheckBox? chkHttpAutoDelete;
        
        // 상태 필드
        private readonly string _cookiePath;
        private readonly string _profileDir;
        private GeminiHttpClient? _httpClient;
        private GeminiChatService? _chatService;
        
        // 경로 도우미 속성
        private static string BasePath => AppContext.BaseDirectory;
        private static string BrowserFolder => Path.Combine(BasePath, "chrome_bin");
        private static string UserDataFolder => Path.Combine(BasePath, "TopSecretProfile");
        
        // 외부 연동 이벤트
        public event Action<string>? OnLog;
        public event Action<string, string>? OnCookiesUpdated;
        
        /// <summary>
        /// 전역 HTTP 자동 삭제 활성화 상태 (모든 HTTP 모드에서 공유)
        /// </summary>
        public static bool GlobalHttpAutoDeleteEnabled { get; set; } = true;
        
        /// <summary>
        /// HTTP 자동 삭제 활성화 상태 (인스턴스)
        /// </summary>
        public bool HttpAutoDeleteEnabled => chkHttpAutoDelete?.Checked ?? GlobalHttpAutoDeleteEnabled;

        public HttpSettingsForm(string cookiePath, string profileDir, GeminiHttpClient? httpClient = null)
        {
            _cookiePath = cookiePath;
            _profileDir = profileDir;
            _httpClient = httpClient;
            
            if (_httpClient != null)
            {
                _chatService = new GeminiChatService(_httpClient);
                _chatService.OnLog += msg => DebugLog(msg);
            }
            
            this.Text = "HTTP API 통합 관리";
            this.MinimizeBox = false;
            this.Size = new Size(700, 700);
            this.BackColor = UiTheme.ColorBackground;
            
            InitializeComponents();
            LoadExistingCookies(); 
        }

        
        /// <summary>
        /// 사용자 인터페이스(UI) 구성 요소를 초기화하고 배치합니다. (Mica 스타일 단일 페이지)
        /// </summary>
        private void InitializeComponents()
        {
            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30), BackColor = UiTheme.ColorBackground };
            
            // 제목 섹션
            var lblTitle = new Label
            {
                Text = "HTTP API 쿠키 설정",
                Font = new Font("Segoe UI Variable Display", 18, FontStyle.Bold),
                ForeColor = UiTheme.ColorPrimary,
                Location = new Point(30, 25),
                AutoSize = true
            };

            var lblDesc = new Label
            {
                Text = "독립 브라우저 자동 추출 또는 수동 입력을 지원합니다.",
                Location = new Point(30, 65),
                ForeColor = UiTheme.ColorTextMuted,
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            
            // === 쿠키 추출 그룹 ===
            var gbAuto = new GroupBox
            {
                Text = " 쿠키 추출 ",
                Location = new Point(30, 105),
                Size = new Size(485, 100),
                ForeColor = UiTheme.ColorPrimary,
                Font = new Font("Segoe UI Semibold", 9)
            };

            btnAutoExtract = new Button
            {
                Text = "✨ WebView 쿠키 추출",
                Location = new Point(15, 35),
                Size = new Size(170, 45),
                BackColor = UiTheme.ColorSuccess,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAutoExtract.FlatAppearance.BorderSize = 0;
            btnAutoExtract.Click += BtnAutoExtract_Click;

            btnManualLoad = new Button
            {
                Text = "📁 파일 열기",
                Location = new Point(195, 35),
                Size = new Size(130, 45),
                BackColor = UiTheme.ColorSurface,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            btnManualLoad.FlatAppearance.BorderSize = 0;
            btnManualLoad.Click += BtnManualLoad_Click;
            
            btnRefreshCookies = new Button
            {
                Text = "🔄 갱신",
                Location = new Point(335, 35),
                Size = new Size(130, 45),
                BackColor = Color.FromArgb(100, 100, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            btnRefreshCookies.FlatAppearance.BorderSize = 0;
            btnRefreshCookies.Click += BtnRefreshCookies_Click;
            
            var toolTip = new ToolTip();
            toolTip.SetToolTip(btnAutoExtract, "WebView2에서 로그인된 세션의 쿠키를 추출합니다.");
            toolTip.SetToolTip(btnRefreshCookies, "__Secure-1PSIDTS 토큰을 갱신합니다.");
            
            gbAuto.Controls.AddRange(new Control[] { btnAutoExtract, btnManualLoad, btnRefreshCookies });

            // === 수동 입력 그룹 ===
            var gbManual = new GroupBox
            {
                Text = " 상세 설정 (수동 편집) ",
                Location = new Point(30, 210),
                Size = new Size(485, 260),   // 높이 증가
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI Semibold", 9)
            };

            var lblPSID = new Label { Text = "__Secure-1PSID:", Location = new Point(15, 30), AutoSize = true, ForeColor = UiTheme.ColorText };
            txtPSID = new TextBox { Location = new Point(15, 52), Width = 455, BackColor = UiTheme.ColorSurface, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };
            
            var lblPSIDTS = new Label { Text = "__Secure-1PSIDTS (선택사항):", Location = new Point(15, 85), AutoSize = true, ForeColor = UiTheme.ColorText };
            txtPSIDTS = new TextBox { Location = new Point(15, 107), Width = 455, BackColor = UiTheme.ColorSurface, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };

            var lblUA = new Label { Text = "User-Agent (선택사항):", Location = new Point(15, 140), AutoSize = true, ForeColor = UiTheme.ColorText };
            txtUserAgent = new TextBox { Location = new Point(15, 162), Width = 455, BackColor = UiTheme.ColorSurface, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9) };
            
            // 모델 선택 ComboBox (GeminiModelConstants 참조)
            var lblModel = new Label { Text = "Gemini 모델:", Location = new Point(15, 195), AutoSize = true, ForeColor = UiTheme.ColorText };
            cmbModel = new ComboBox
            {
                Location = new Point(15, 217),
                Width = 250,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = UiTheme.ColorSurface,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            // 모델 목록: GeminiModelConstants.ModelHeaders 기준
            cmbModel.Items.AddRange(new[] { 
                "gemini-3.0-flash (빠른 모드)", 
                "gemini-3.0-pro (Pro)", 
                "gemini-3.0-pro-thinking (사고 모드)",
                "gemini-2.5-flash",
                "gemini-2.5-pro"
            });
            cmbModel.SelectedIndex = 0;
            
            gbManual.Controls.AddRange(new Control[] { lblPSID, txtPSID, lblPSIDTS, txtPSIDTS, lblUA, txtUserAgent, lblModel, cmbModel });
            
            // 상태 안내문
            lblStatus = new Label
            {
                Text = "설정값을 입력하거나 WebView에서 추출하세요.",
                Location = new Point(30, 480),
                ForeColor = Color.FromArgb(255, 200, 100),
                AutoSize = true,
                Font = new Font("Segoe UI", 9)
            };
            
            // === 하단 버튼 그룹 (1줄: 저장/재연결, 2줄: 채팅/모델/로그) ===
            btnSave = new Button
            {
                Text = "💾 설정 저장 및 API 적용",
                Location = new Point(30, 510),
                Size = new Size(210, 50),
                BackColor = UiTheme.ColorPrimary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;
            
            btnReconnectApi = new Button
            {
                Text = "🔄 재연결",
                Location = new Point(250, 510),
                Size = new Size(80, 50),
                BackColor = UiTheme.ColorSuccess,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9),
                Cursor = Cursors.Hand
            };
            btnReconnectApi.FlatAppearance.BorderSize = 0;
            btnReconnectApi.Click += BtnReconnectApi_Click;
            
            // 개별 기능 버튼들 (오른쪽에 배치)
            var btnChatManage = new Button
            {
                Text = "💬 채팅",
                Location = new Point(340, 510),
                Size = new Size(80, 50),
                BackColor = Color.FromArgb(80, 80, 120),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9),
                Cursor = Cursors.Hand
            };
            btnChatManage.FlatAppearance.BorderSize = 0;
            btnChatManage.Click += (s, e) => ShowChatManageForm();
            toolTip.SetToolTip(btnChatManage, "채팅 목록 조회 및 삭제");
            
            var btnDebugLog = new Button
            {
                Text = "📋 로그",
                Location = new Point(430, 510),
                Size = new Size(80, 50),
                BackColor = Color.FromArgb(60, 60, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9),
                Cursor = Cursors.Hand
            };
            btnDebugLog.FlatAppearance.BorderSize = 0;
            btnDebugLog.Click += (s, e) => ShowDebugLogForm();
            toolTip.SetToolTip(btnDebugLog, "디버그 로그");

            // === HTTP 자동 삭제 체크박스 ===
            chkHttpAutoDelete = new CheckBox
            {
                Text = "🗑️ HTTP 자동 삭제",
                Location = new Point(30, 570),
                AutoSize = true,
                ForeColor = Color.FromArgb(255, 150, 150),
                Font = new Font("Segoe UI", 9),
                Checked = GlobalHttpAutoDeleteEnabled
            };
            chkHttpAutoDelete.CheckedChanged += (s, e) => 
            {
                GlobalHttpAutoDeleteEnabled = chkHttpAutoDelete.Checked;
            };
            toolTip.SetToolTip(chkHttpAutoDelete, "HTTP 모드 전체: 10회 사용 후 채팅 자동 삭제");
            
            mainPanel.Controls.AddRange(new Control[]
            {
                lblTitle, lblDesc, gbAuto, gbManual,
                lblStatus, btnSave, btnReconnectApi, btnChatManage, btnDebugLog, chkHttpAutoDelete
            });
            
            this.Controls.Add(mainPanel);
        }
        
        /// <summary>
        /// 채팅 관리 폼을 표시합니다.
        /// </summary>
        private void ShowChatManageForm()
        {
            using var form = new Form
            {
                Text = "💬 채팅 관리",
                Size = new Size(600, 500),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = UiTheme.ColorBackground
            };
            
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
            InitializeChatTab(panel);
            form.Controls.Add(panel);
            form.ShowDialog();
        }
        
        // 모델 선택은 메인 폼에서 직접 가능
        
        /// <summary>
        /// 디버그 로그 폼을 표시합니다.
        /// </summary>
        private void ShowDebugLogForm()
        {
            using var form = new Form
            {
                Text = "📋 HTTP 디버그 로그",
                Size = new Size(700, 550),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = UiTheme.ColorBackground
            };
            
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
            InitializeDebugTab(panel);
            form.Controls.Add(panel);
            form.ShowDialog();
        }
        
        private void InitializeChatTab(Control container)
        {
            // === 채팅 관리 그룹 ===
            var gbChat = new GroupBox
            {
                Text = " 채팅 관리 ",
                Location = new Point(15, 15),
                Size = new Size(630, 480),
                ForeColor = UiTheme.ColorPrimary,
                Font = new Font("Segoe UI Semibold", 9)
            };
            
            var lblInfo = new Label { Text = "기존 채팅 목록을 불러오거나 삭제할 수 있습니다.", Location = new Point(15, 25), AutoSize = true, ForeColor = UiTheme.ColorTextMuted };
            
            btnLoadChats = new Button
            {
                Text = "📥 채팅 목록 불러오기",
                Location = new Point(15, 55),
                Size = new Size(200, 40),
                BackColor = UiTheme.ColorPrimary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnLoadChats.FlatAppearance.BorderSize = 0;
            btnLoadChats.Click += BtnLoadChats_Click;
            
            btnDeleteChat = new Button
            {
                Text = "🗑️ 선택 채팅 삭제",
                Location = new Point(230, 55),
                Size = new Size(160, 40),
                BackColor = Color.FromArgb(180, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            btnDeleteChat.FlatAppearance.BorderSize = 0;
            btnDeleteChat.Click += BtnDeleteChat_Click;
            
            lstChats = new ListBox
            {
                Location = new Point(15, 105),
                Size = new Size(600, 320),
                BackColor = UiTheme.ColorSurface,
                ForeColor = Color.White,
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.FixedSingle
            };
            
            lblChatStatus = new Label { Text = "", Location = new Point(15, 435), AutoSize = true, ForeColor = UiTheme.ColorSuccess };
            
            gbChat.Controls.AddRange(new Control[] { lblInfo, btnLoadChats, btnDeleteChat, lstChats, lblChatStatus });
            container.Controls.Add(gbChat);
        }
        
        // 모델 테스트 기능 제거됨 - 모델 선택은 메인 폼에서 직접 수행
        
        // 이미지 생성 기능은 NanoBanana에 통합되었습니다
        
        private void InitializeDebugTab(Control container)
    {
        // === 디버그 로그 그룹 ===
        var gbDebug = new GroupBox
        {
            Text = " HTTP 디버그 로그 ",
            Location = new Point(15, 15),
            Size = new Size(630, 430),
            ForeColor = UiTheme.ColorPrimary,
            Font = new Font("Segoe UI Semibold", 9)
        };
        
        txtDebugLog = new TextBox
        {
            Location = new Point(15, 25),
            Size = new Size(595, 350),
            BackColor = Color.FromArgb(20, 20, 30),
            ForeColor = Color.LightGreen,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true
        };
        
        btnClearLog = new Button
        {
            Text = "🗑️ 로그 지우기",
            Location = new Point(15, 385),
            Size = new Size(130, 35),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9),
            Cursor = Cursors.Hand
        };
        btnClearLog.FlatAppearance.BorderSize = 0;
        btnClearLog.Click += (s, e) => { if (txtDebugLog != null) txtDebugLog.Text = ""; };
        
        gbDebug.Controls.AddRange(new Control[] { txtDebugLog, btnClearLog });
        container.Controls.Add(gbDebug);
        
        // === 항상 위에 체크박스 ===
        var chkAlwaysOnTop = new CheckBox
        {
            Text = "📌 항상 위에",
            Location = new Point(15, 455),
            AutoSize = true,
            ForeColor = UiTheme.ColorText,
            Font = new Font("Segoe UI", 9),
            Checked = MainForm.IsAlwaysOnTop
        };
        chkAlwaysOnTop.CheckedChanged += (s, e) =>
        {
            MainForm.IsAlwaysOnTop = chkAlwaysOnTop.Checked;
            if (container.FindForm() is Form parentForm)
            {
                parentForm.TopMost = chkAlwaysOnTop.Checked;
            }
        };
        container.Controls.Add(chkAlwaysOnTop);
    }

        
        private void DebugLog(string message)
        {
            if (txtDebugLog == null) return;
            if (txtDebugLog.InvokeRequired)
            {
                txtDebugLog.Invoke(new Action(() => DebugLog(message)));
                return;
            }
            txtDebugLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }


        /// <summary>
        /// 기존에 저장된 쿠키 파일에서 정보를 읽어와 화면에 표시합니다.
        /// </summary>
        private void LoadExistingCookies()
        {
            try
            {
                if (File.Exists(_cookiePath))
                {
                    var json = File.ReadAllText(_cookiePath);
                    var cookies = Newtonsoft.Json.Linq.JObject.Parse(json);
                    if (txtPSID != null) txtPSID.Text = cookies["Secure_1PSID"]?.ToString() ?? "";
                    if (txtPSIDTS != null) txtPSIDTS.Text = cookies["Secure_1PSIDTS"]?.ToString() ?? "";
                    if (txtUserAgent != null) txtUserAgent.Text = cookies["UserAgent"]?.ToString() ?? "";
                    Log("기존 설정을 불러왔습니다.");
                }
            }
            catch (Exception ex)
            {
                Log($"기존 설정 로드 실패: {ex.Message}");
            }
        }

        
        private void Log(string msg) => OnLog?.Invoke(msg);

        /// <summary>
        /// WebView에서 쿠키 추출 버튼 클릭 이벤트
        /// SharedWebViewManager의 로그인 모드 WebView를 사용하여 쿠키를 추출합니다.
        /// </summary>
        private async void BtnAutoExtract_Click(object? sender, EventArgs e)
        {
            if (btnAutoExtract == null || lblStatus == null || txtPSID == null) return;
            
            btnAutoExtract.Enabled = false;
            lblStatus.Text = "로그인 전용 WebView 초기화 중...";
            lblStatus.ForeColor = Color.Orange;
            
            try
            {
                Log("[HTTP] SharedWebViewManager 로그인 모드로 쿠키 추출 시도");
                
                // SharedWebViewManager를 로그인 모드로 설정
                var manager = SharedWebViewManager.Instance;
                manager.UseLoginMode = true; // 로그인 모드 강제 설정
                manager.OnLog += msg => Log(msg);
                
                // WebView 초기화 (창 표시)
                lblStatus.Text = "WebView 로그인 창 열기 중...";
                if (!await manager.InitializeAsync(showWindow: true))
                {
                    lblStatus.Text = "[실패] WebView 초기화 실패";
                    lblStatus.ForeColor = Color.Red;
                    return;
                }
                
                // 현재 쿠키 확인
                var (psid, psidts, userAgent) = await manager.ExtractCookiesAsync();
                
                if (!string.IsNullOrEmpty(psid))
                {
                    // 이미 로그인되어 있음 - 바로 추출
                    FillCookieFields(psid, psidts, userAgent);
                    lblStatus.Text = "[성공] 쿠키 추출 성공! 이제 '저장'을 눌러주세요.";
                    lblStatus.ForeColor = Color.Lime;
                    Log("[HTTP] SharedWebViewManager에서 쿠키 추출 완료");
                    
                    // HTTP 모드: 쿠키 추출 후 WebView 세션 완전 종료 (로그아웃 URL 방지)
                    manager.HideBrowserWindow();
                    manager.Dispose();
                    Log("[HTTP] WebView 세션 종료됨");
                    return;
                }
                
                // 로그인 필요 - 브라우저 창 표시
                Log("[HTTP] 로그인이 필요합니다. 로그인 창을 엽니다.");
                lblStatus.Text = "로그인 창에서 Google 계정으로 로그인하세요...";
                lblStatus.ForeColor = Color.Yellow;
                manager.ShowBrowserWindow(autoCloseOnLogin: false);
                
                
                // 최대 3분간 로그인 모니터링
                for (int i = 0; i < 180; i++)
                {
                    await Task.Delay(1000);
                    
                    try
                    {
                        // SharedWebViewManager에서 쿠키 확인
                        var (extractedPsid, extractedPsidts, extractedUa) = await manager.ExtractCookiesAsync();
                        
                        if (!string.IsNullOrEmpty(extractedPsid))
                        {
                            Log($"[HTTP] 로그인 감지! 쿠키 추출 성공 (PSID 길이: {extractedPsid.Length})");
                            FillCookieFields(extractedPsid, extractedPsidts, extractedUa);
                            lblStatus.Text = "[성공] 로그인 감지! 쿠키가 자동으로 추출되었습니다.";
                            lblStatus.ForeColor = Color.Lime;
                            
                            // HTTP 모드: 쿠키 추출 후 WebView 세션 완전 종료 (로그아웃 URL 방지)
                            manager.HideBrowserWindow();
                            manager.Dispose();
                            Log("[HTTP] WebView 세션 종료됨");
                            
                            // 이 창을 다시 앞으로
                            this.BringToFront();
                            this.Activate();
                            return;
                        }
                        
                        // 10초마다 상태 업데이트
                        if (i > 0 && i % 10 == 0)
                        {
                            lblStatus.Text = $"로그인 대기 중... ({180 - i}초 남음)";
                            Log($"[HTTP] 로그인 대기 중... ({i}초 경과)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[HTTP] 쿠키 확인 오류: {ex.Message}");
                    }
                }
                
                // 타임아웃 - 브라우저 창은 열어둠
                lblStatus.Text = "[타임아웃] 3분이 지났습니다. 로그인 후 다시 시도하세요.";
                lblStatus.ForeColor = Color.Red;
                Log("[HTTP] 로그인 대기 타임아웃 (3분)");
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"[실패] 오류: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
                Log($"[HTTP] 쿠키 추출 오류: {ex.Message}");
            }
            finally
            {
                btnAutoExtract.Enabled = true;
            }
        }
        
        /// <summary>
        /// 쿠키 필드에 값을 채웁니다.
        /// </summary>
        private void FillCookieFields(string? psid, string? psidts, string? userAgent)
        {
            if (txtPSID != null) txtPSID.Text = psid ?? "";
            if (txtPSIDTS != null) txtPSIDTS.Text = psidts ?? "";
            if (txtUserAgent != null) txtUserAgent.Text = userAgent ?? "";
        }
        
        /// <summary>
        /// 기존에 저장된 JSON 쿠키 파일을 수동으로 선택하여 불러옵니다.
        /// </summary>
        private void BtnManualLoad_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Title = "쿠키 파일 선택",
                Filter = "JSON 파일|*.json|모든 파일|*.*"
            };
            
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var content = File.ReadAllText(ofd.FileName);
                    if (content.TrimStart().StartsWith("{"))
                    {
                        var json = Newtonsoft.Json.Linq.JObject.Parse(content);
                        if (txtPSID != null) txtPSID.Text = json["Secure_1PSID"]?.ToString() ?? json["__Secure-1PSID"]?.ToString() ?? "";
                        if (txtPSIDTS != null) txtPSIDTS.Text = json["Secure_1PSIDTS"]?.ToString() ?? json["__Secure-1PSIDTS"]?.ToString() ?? "";
                        if (txtUserAgent != null) txtUserAgent.Text = json["UserAgent"]?.ToString() ?? "";
                        
                        lblStatus!.Text = "[성공] 파일 로드 성공!";
                        lblStatus.ForeColor = Color.Lime;
                        Log("[HTTP] 쿠키 파일 로드됨");
                    }
                    else
                    {
                        lblStatus!.Text = "[경고] JSON 형식이 아닙니다.";
                        lblStatus.ForeColor = Color.Orange;
                    }
                }
                catch (Exception ex)
                {
                    lblStatus!.Text = $"[실패] 파일 읽기 오류: {ex.Message}";
                    lblStatus.ForeColor = Color.Red;
                }
            }
        }
        
        /// <summary>
        /// 현재 입력된 설정값들을 저장하고 메인 화면에 반영합니다.
        /// </summary>
        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtPSID?.Text))
            {
                MessageBox.Show("__Secure-1PSID 값은 필수입니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // OnCookiesUpdated에 psid|psidts 형태로 합쳐서 전달 (GeminiHttpClient의 파싱 규칙 준수)
            var psid = txtPSID.Text.Trim();
            var psidts = txtPSIDTS?.Text.Trim();
            var ua = txtUserAgent?.Text.Trim();
            
            var combinedCookie = string.IsNullOrEmpty(psidts) ? psid : $"{psid}|{psidts}";
            
            OnCookiesUpdated?.Invoke(combinedCookie, ua ?? "");
            OnLog?.Invoke("[HTTP] 쿠키 설정 저장됨");
            
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        
        /// <summary>
        /// API 재연결 요청 이벤트 (MainForm에서 구독)
        /// </summary>
        public event Func<Task>? OnReconnectRequested;
        
        /// <summary>
        /// API 재연결 버튼 클릭 핸들러
        /// </summary>
        private async void BtnReconnectApi_Click(object? sender, EventArgs e)
        {
            if (btnReconnectApi == null) return;
            
            btnReconnectApi.Enabled = false;
            btnReconnectApi.Text = "연결 중...";
            lblStatus!.Text = "API 재연결 시도 중...";
            lblStatus.ForeColor = Color.Orange;
            
            try
            {
                if (OnReconnectRequested != null)
                {
                    await OnReconnectRequested.Invoke();
                    lblStatus.Text = "[성공] API 재연결 성공";
                    lblStatus.ForeColor = UiTheme.ColorSuccess;
                    OnLog?.Invoke("[HTTP] API 재연결 성공");
                    DebugLog("API 재연결 성공");
                }
                else
                {
                    lblStatus.Text = "[경고] 재연결 핸들러 없음";
                    lblStatus.ForeColor = Color.Yellow;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"[실패] 재연결 실패: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
                OnLog?.Invoke($"[HTTP] API 재연결 실패: {ex.Message}");
                DebugLog($"API 재연결 실패: {ex.Message}");
            }
            finally
            {
                btnReconnectApi.Enabled = true;
                btnReconnectApi.Text = "🔄 재연결";
            }
        }
        
        // ==================== 쿠키 갱신 ====================
        private async void BtnRefreshCookies_Click(object? sender, EventArgs e)
        {
            if (_httpClient == null || !_httpClient.IsInitialized)
            {
                MessageBox.Show("먼저 HTTP 클라이언트를 초기화하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            if (btnRefreshCookies != null) btnRefreshCookies.Enabled = false;
            DebugLog("쿠키 갱신 시작...");
            
            try
            {
                var newPsidts = await _httpClient.RotateCookiesAsync();
                if (!string.IsNullOrEmpty(newPsidts))
                {
                    if (txtPSIDTS != null) txtPSIDTS.Text = newPsidts;
                    if (lblStatus != null)
                    {
                        lblStatus.Text = "[성공] 쿠키 갱신 완료!";
                        lblStatus.ForeColor = Color.Lime;
                    }
                    DebugLog($"쿠키 갱신 성공: 새 PSIDTS 적용됨");
                }
                else
                {
                    if (lblStatus != null)
                    {
                        lblStatus.Text = "[경고] 새 쿠키를 받지 못했습니다.";
                        lblStatus.ForeColor = Color.Orange;
                    }
                    DebugLog("쿠키 갱신: 새 PSIDTS 없음");
                }
            }
            catch (Exception ex)
            {
                if (lblStatus != null)
                {
                    lblStatus.Text = $"[실패] 쿠키 갱신 오류: {ex.Message}";
                    lblStatus.ForeColor = Color.Red;
                }
                DebugLog($"쿠키 갱신 오류: {ex.Message}");
            }
            finally
            {
                if (btnRefreshCookies != null) btnRefreshCookies.Enabled = true;
            }
        }
        
        // ==================== 채팅 관리 ====================
        private async void BtnLoadChats_Click(object? sender, EventArgs e)
        {
            if (_chatService == null)
            {
                if (lblChatStatus != null) lblChatStatus.Text = "HTTP 클라이언트가 초기화되지 않았습니다.";
                return;
            }
            
            if (btnLoadChats != null) btnLoadChats.Enabled = false;
            if (lblChatStatus != null) { lblChatStatus.Text = "채팅 목록 불러오는 중..."; lblChatStatus.ForeColor = Color.Yellow; }
            DebugLog("채팅 목록 불러오기 시작...");
            
            try
            {
                await _chatService.LoadChatsAsync(30);
                
                if (lstChats != null)
                {
                    lstChats.Items.Clear();
                    foreach (var chat in _chatService.Chats)
                    {
                        lstChats.Items.Add($"{chat.ChatId} | {chat.Title}");
                    }
                }
                
                if (lblChatStatus != null)
                {
                    lblChatStatus.Text = $"[성공] {_chatService.Chats.Count}개의 채팅을 불러왔습니다.";
                    lblChatStatus.ForeColor = Color.Lime;
                }
                DebugLog($"채팅 목록 불러오기 완료: {_chatService.Chats.Count}개");
            }
            catch (Exception ex)
            {
                if (lblChatStatus != null)
                {
                    lblChatStatus.Text = $"[실패] {ex.Message}";
                    lblChatStatus.ForeColor = Color.Red;
                }
                DebugLog($"채팅 목록 불러오기 실패: {ex.Message}");
            }
            finally
            {
                if (btnLoadChats != null) btnLoadChats.Enabled = true;
            }
        }
        
        private async void BtnDeleteChat_Click(object? sender, EventArgs e)
        {
            if (_chatService == null || lstChats == null || lstChats.SelectedItem == null)
            {
                MessageBox.Show("삭제할 채팅을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            var selected = lstChats.SelectedItem.ToString();
            var chatId = selected?.Split('|')[0].Trim();
            
            if (string.IsNullOrEmpty(chatId))
            {
                MessageBox.Show("채팅 ID를 파싱할 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            var confirm = MessageBox.Show($"채팅 '{chatId}'를 삭제하시겠습니까?", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
            
            if (btnDeleteChat != null) btnDeleteChat.Enabled = false;
            DebugLog($"채팅 삭제 시작: {chatId}");
            
            try
            {
                var success = await _chatService.DeleteChatAsync(chatId);
                if (success)
                {
                    lstChats.Items.Remove(lstChats.SelectedItem);
                    if (lblChatStatus != null)
                    {
                        lblChatStatus.Text = "[성공] 채팅이 삭제되었습니다.";
                        lblChatStatus.ForeColor = Color.Lime;
                    }
                    DebugLog($"채팅 삭제 성공: {chatId}");
                }
                else
                {
                    if (lblChatStatus != null)
                    {
                        lblChatStatus.Text = "[실패] 채팅 삭제에 실패했습니다.";
                        lblChatStatus.ForeColor = Color.Red;
                    }
                    DebugLog($"채팅 삭제 실패: {chatId}");
                }
            }
            catch (Exception ex)
            {
                if (lblChatStatus != null)
                {
                    lblChatStatus.Text = $"[오류] {ex.Message}";
                    lblChatStatus.ForeColor = Color.Red;
                }
                DebugLog($"채팅 삭제 오류: {ex.Message}");
            }
            finally
            {
                if (btnDeleteChat != null) btnDeleteChat.Enabled = true;
            }
        }
        
        // 모델 테스트 기능 제거됨 - 모델 선택은 메인 폼에서 직접 수행
        
        // 이미지 생성 기능은 NanoBanana에 통합되었습니다

    }
}

