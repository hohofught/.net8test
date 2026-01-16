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
        private Button? btnAutoExtract;  // 자동 추출 버튼
        private Button? btnResetBrowser; // 브라우저 초기화 버튼
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
            this.Size = new Size(560, 560); // 모델 선택 제거로 높이 줄임
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
                Text = " 자동 추출 및 파일 로드 ",
                Location = new Point(30, 105),
                Size = new Size(485, 95),
                ForeColor = UiTheme.ColorPrimary,
                Font = new Font("Segoe UI Semibold", 9)
            };

            btnAutoExtract = new Button
            {
                Text = "🚀 독립 브라우저 실행",
                Location = new Point(15, 30),
                Size = new Size(165, 45),
                BackColor = UiTheme.ColorSuccess,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAutoExtract.FlatAppearance.BorderSize = 0;
            btnAutoExtract.Click += BtnAutoExtract_Click;

            btnResetBrowser = new Button
            {
                Text = "🔄 초기화",
                Location = new Point(185, 30),
                Size = new Size(80, 45),
                BackColor = UiTheme.ColorSurfaceLight,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            var toolTip = new ToolTip();
            toolTip.SetToolTip(btnResetBrowser, "브라우저 파일을 삭제하고 다시 설치합니다. (오류 발생 시 권장)");
            btnResetBrowser.FlatAppearance.BorderSize = 0;
            btnResetBrowser.Click += BtnResetBrowser_Click;
            
            btnManualLoad = new Button
            {
                Text = "📁 쿠키 파일 열기",
                Location = new Point(270, 30),
                Size = new Size(200, 45),
                BackColor = UiTheme.ColorSurface,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            btnManualLoad.FlatAppearance.BorderSize = 0;
            btnManualLoad.Click += BtnManualLoad_Click;
            gbAuto.Controls.AddRange(new Control[] { btnAutoExtract, btnResetBrowser, btnManualLoad });

            // --- 수동 입력 그룹 ---
            var gbManual = new GroupBox
            {
                Text = " 상세 설정 (수동 편집) ",
                Location = new Point(30, 215),
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
                Text = "설정값을 입력하거나 브라우저에서 추출하세요.",
                Location = new Point(30, 455),
                ForeColor = Color.FromArgb(255, 200, 100),
                AutoSize = true,
                Width = 485,
                Font = new Font("Segoe UI", 9)
            };
            
            // 저장 버튼
            btnSave = new Button
            {
                Text = "💾 설정 저장 및 API 적용",
                Location = new Point(30, 485),
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
                Location = new Point(360, 485),
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
        /// 독립 브라우저 실행 버튼 클릭 이벤트
        /// </summary>
        private async void BtnAutoExtract_Click(object? sender, EventArgs e)
        {
            if (btnAutoExtract == null || lblStatus == null || txtPSID == null) return;
            
            btnAutoExtract.Enabled = false;
            lblStatus.Text = "독립 브라우저 실행 중... 로그인을 진행해 주세요.";
            lblStatus.ForeColor = Color.Orange;
            
            try
            {
                Log("[HTTP] 독립 브라우저 실행 시도");
                // SharedWebViewManager를 사용하여 WebView2 브라우저를 띄우고 쿠키 추출
                var (psid, psidts, userAgent) = await ExtractCookiesFromIsolatedBrowserAsync();
                
                if (!string.IsNullOrEmpty(psid))
                {
                    // 추출된 정보를 화면의 입력칸에 자동 채움
                    txtPSID.Text = psid;
                    txtPSIDTS!.Text = psidts ?? "";
                    txtUserAgent!.Text = userAgent ?? "";
                    
                    lblStatus.Text = "[성공] 쿠키 추출 성공! 이제 '저장'을 눌러주세요.";
                    lblStatus.ForeColor = Color.Lime;
                    Log("[HTTP] 쿠키 추출 완료");
                }
                else
                {
                    lblStatus.Text = "[실패] 쿠키를 찾을 수 없습니다. 로그인이 필요합니다.";
                    lblStatus.ForeColor = Color.Red;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"[실패] 오류: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
            finally
            {
                btnAutoExtract.Enabled = true;
            }
        }

        /// <summary>
        /// 브라우저 초기화 버튼 클릭 이벤트
        /// </summary>
        private async void BtnResetBrowser_Click(object? sender, EventArgs e)
        {
            if (btnResetBrowser == null || lblStatus == null) return;
            
            if (MessageBox.Show("WebView2 세션을 초기화하시겠습니까?\n로그인 상태가 초기화됩니다.", "세션 초기화", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            btnResetBrowser.Enabled = false;
            btnAutoExtract!.Enabled = false;
            lblStatus.Text = "WebView2 세션 초기화 중...";
            
            try
            {
                // gemini_session 폴더 삭제
                var sessionPath = Path.Combine(_profileDir, "gemini_session");
                if (Directory.Exists(sessionPath))
                {
                    Directory.Delete(sessionPath, true);
                    lblStatus.Text = "[성공] 세션 초기화 완료!";
                    lblStatus.ForeColor = Color.Lime;
                    Log("[HTTP] WebView2 세션 초기화 완료");
                }
                else
                {
                    lblStatus.Text = "[알림] 초기화할 세션이 없습니다.";
                    lblStatus.ForeColor = Color.Yellow;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"[실패] 초기화 실패: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
            finally
            {
                btnResetBrowser.Enabled = true;
                btnAutoExtract.Enabled = true;
            }

            await Task.CompletedTask;
        }
        
        /// <summary>
        /// SharedWebViewManager를 사용하여 WebView2를 실행하고 쿠키를 추출합니다.
        /// </summary>
        private async Task<(string? psid, string? psidts, string? userAgent)> ExtractCookiesFromIsolatedBrowserAsync()
        {
            try
            {
                // SharedWebViewManager 싱글톤 사용
                var manager = SharedWebViewManager.Instance;
                manager.OnLog += msg => Log(msg);
                
                lblStatus?.Invoke(() => lblStatus.Text = "WebView2 초기화 중...");
                
                // WebView2 초기화 (창 표시)
                if (!await manager.InitializeAsync(showWindow: true))
                {
                    return (null, null, null);
                }
                
                lblStatus?.Invoke(() => lblStatus.Text = "Gemini에 로그인해 주세요... (최대 3분 대기)");
                
                // 사용자가 로그인할 시간을 주기 위해 쿠키가 나타날 때까지 반복 감시 (최대 3분)
                string? psid = null;
                string? psidts = null;
                
                for (int i = 0; i < 180; i++)
                {
                    try
                    {
                        var loginStatus = await manager.CheckLoginStatusAsync();
                        if (loginStatus)
                        {
                            // 쿠키 추출을 위한 스크립트 실행
                            var cookieScript = @"
                            (function() {
                                var cookies = document.cookie.split(';');
                                var result = {};
                                for (var i = 0; i < cookies.length; i++) {
                                    var cookie = cookies[i].trim();
                                    var parts = cookie.split('=');
                                    if (parts.length >= 2) {
                                        result[parts[0]] = parts.slice(1).join('=');
                                    }
                                }
                                return JSON.stringify(result);
                            })()";
                            
                            var cookieJson = await manager.ExecuteScriptAsync(cookieScript);
                            if (!string.IsNullOrEmpty(cookieJson) && cookieJson != "null")
                            {
                                var cookies = Newtonsoft.Json.Linq.JObject.Parse(cookieJson.Trim('"').Replace("\\\"", "\""));
                                psid = cookies["__Secure-1PSID"]?.ToString();
                                psidts = cookies["__Secure-1PSIDTS"]?.ToString();
                                
                                if (!string.IsNullOrEmpty(psid))
                                {
                                    Log($"[HTTP] 쿠키 추출 성공: PSID 발견");
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                    
                    await Task.Delay(1000);
                }
                
                // User-Agent 추출
                var userAgent = await manager.ExecuteScriptAsync("navigator.userAgent");
                userAgent = userAgent?.Trim('"');
                
                // WebView 창 숨기기
                manager.HideBrowserWindow();
                
                return (psid, psidts, userAgent);
            }
            catch (Exception ex)
            {
                Log($"[HTTP] 쿠키 추출 오류: {ex.Message}");
                return (null, null, null);
            }
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

