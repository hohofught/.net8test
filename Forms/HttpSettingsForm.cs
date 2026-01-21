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

namespace GeminiWebTranslator.Forms
{
    /// <summary>
    /// HTTP API 모드를 위한 통합 설정 화면입니다.
    /// 독립된 브라우저를 실행하여 쿠키를 자동으로 추출하거나, 사용자가 직접 수동으로 입력할 수 있는 기능을 제공합니다.
    /// </summary>
    public class HttpSettingsForm : Form
    {
        // UI 컨트롤 선언
        private Button? btnAutoExtract;  // WebView에서 쿠키 추출 버튼
        private Button? btnManualLoad;   // 파일 로드 버튼
        private Button? btnSave;         // 저장 및 적용 버튼
        private TextBox? txtPSID;        // __Secure-1PSID 입력 칸
        private TextBox? txtPSIDTS;      // __Secure-1PSIDTS 입력 칸
        private TextBox? txtUserAgent;   // User-Agent 입력 칸
        private Button? btnReconnectApi; // API 재연결 버튼
        private Label? lblStatus;        // 상태 표시 레이블
        
        // 상태 필드
        private readonly string _cookiePath; // 쿠키 파일 저장 경로
        private readonly string _profileDir; // 브라우저 프로필 디렉토리
        
        // 경로 도우미 속성
        private static string BasePath => AppContext.BaseDirectory;
        private static string BrowserFolder => Path.Combine(BasePath, "chrome_bin"); // 크롬 실행 파일 경로
        private static string UserDataFolder => Path.Combine(BasePath, "TopSecretProfile"); // 전용 사용자 데이터 경로
        
        // 외부 연동 이벤트
        public event Action<string>? OnLog; // 로그 메시지 전달
        public event Action<string, string>? OnCookiesUpdated; // 쿠키 정보 업데이트 알림

        public HttpSettingsForm(string cookiePath, string profileDir)
        {
            _cookiePath = cookiePath;
            _profileDir = profileDir;
            
            this.Text = "HTTP API 및 쿠키 통합 설정";
            this.MinimizeBox = false;
            this.Size = new Size(560, 560); // 간소화된 UI에 맞게 높이 축소
            this.BackColor = UiTheme.ColorBackground;
            
            InitializeComponents();
            LoadExistingCookies(); 
        }
        
        /// <summary>
        /// 사용자 인터페이스(UI) 구성 요소를 초기화하고 배치합니다.
        /// </summary>
        private void InitializeComponents()
        {
            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30), BackColor = UiTheme.ColorBackground };
            
            // 제목 섹션
            var lblTitle = new Label
            {
                Text = "HTTP API & 쿠키 설정",
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
            
            // --- 자동 추출 및 파일 로드 그룹 ---
            var gbAuto = new GroupBox
            {
                Text = " 쿠키 추출 ",
                Location = new Point(30, 105),
                Size = new Size(485, 90),
                ForeColor = UiTheme.ColorPrimary,
                Font = new Font("Segoe UI Semibold", 9)
            };

            btnAutoExtract = new Button
            {
                Text = "� WebView에서 쿠키 추출",
                Location = new Point(15, 30),
                Size = new Size(250, 45),
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
                Text = "📁 쿠키 파일 열기",
                Location = new Point(280, 30),
                Size = new Size(190, 45),
                BackColor = UiTheme.ColorSurface,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            btnManualLoad.FlatAppearance.BorderSize = 0;
            btnManualLoad.Click += BtnManualLoad_Click;
            
            // 툴팁 설정
            var toolTip2 = new ToolTip();
            toolTip2.SetToolTip(btnAutoExtract, "MainForm의 WebView에서 로그인된 세션의 쿠키를 추출합니다.");
            
            gbAuto.Controls.AddRange(new Control[] { btnAutoExtract, btnManualLoad });

            // --- 수동 입력 그룹 ---
            var gbManual = new GroupBox
            {
                Text = " 상세 설정 (수동 편집) ",
                Location = new Point(30, 210),
                Size = new Size(485, 230),
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI Semibold", 9)
            };

            var lblPSID = new Label { Text = "__Secure-1PSID:", Location = new Point(15, 30), AutoSize = true, ForeColor = UiTheme.ColorText };
            txtPSID = new TextBox { Location = new Point(15, 52), Width = 455, BackColor = UiTheme.ColorSurface, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };
            
            var lblPSIDTS = new Label { Text = "__Secure-1PSIDTS (선택사항):", Location = new Point(15, 90), AutoSize = true, ForeColor = UiTheme.ColorText };
            txtPSIDTS = new TextBox { Location = new Point(15, 112), Width = 455, BackColor = UiTheme.ColorSurface, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };

            var lblUA = new Label { Text = "User-Agent (선택사항):", Location = new Point(15, 150), AutoSize = true, ForeColor = UiTheme.ColorText };
            txtUserAgent = new TextBox { Location = new Point(15, 172), Width = 455, BackColor = UiTheme.ColorSurface, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9) };
            
            gbManual.Controls.AddRange(new Control[] { lblPSID, txtPSID, lblPSIDTS, txtPSIDTS, lblUA, txtUserAgent });
            
            // 상태 안내문
            lblStatus = new Label
            {
                Text = "설정값을 입력하거나 WebView에서 추출하세요.",
                Location = new Point(30, 450),
                ForeColor = Color.FromArgb(255, 200, 100),
                AutoSize = true,
                Width = 485,
                Font = new Font("Segoe UI", 9)
            };
            
            // 저장 버튼
            btnSave = new Button
            {
                Text = "💾 설정 저장 및 API 적용",
                Location = new Point(30, 480),
                Size = new Size(320, 50),
                BackColor = UiTheme.ColorPrimary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;
            
            // API 재연결 버튼
            btnReconnectApi = new Button
            {
                Text = "🔄 재연결",
                Location = new Point(360, 480),
                Size = new Size(155, 50),
                BackColor = UiTheme.ColorSuccess,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10),
                Cursor = Cursors.Hand
            };
            btnReconnectApi.FlatAppearance.BorderSize = 0;
            btnReconnectApi.Click += BtnReconnectApi_Click;
            
            mainPanel.Controls.AddRange(new Control[]
            {
                lblTitle, lblDesc, gbAuto, gbManual,
                lblStatus, btnSave, btnReconnectApi
            });
            
            this.Controls.Add(mainPanel);
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
                    manager.HideBrowserWindow();
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
                            
                            // 로그인 창 닫기
                            manager.HideBrowserWindow();
                            
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
            }
            finally
            {
                btnReconnectApi.Enabled = true;
                btnReconnectApi.Text = "🔄 재연결";
            }
        }
    }
}

