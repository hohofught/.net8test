#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PuppeteerSharp;
using Point = System.Drawing.Point;

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
        private static string BasePath => Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        private static string BrowserFolder => Path.Combine(BasePath, "chrome_bin"); // 크롬 실행 파일 경로
        private static string UserDataFolder => Path.Combine(BasePath, "TopSecretProfile"); // 전용 사용자 데이터 경로
        
        // 외부 연동 이벤트
        public event Action<string>? OnLog; // 로그 메시지 전달
        public event Action<string, string>? OnCookiesUpdated; // 쿠키 정보 업데이트 알림
        
        // UI 색상 테마 설정 (Premium Dark Mode)
        private readonly Color darkBg = Color.FromArgb(18, 18, 20);      // 더 깊고 현대적인 검정
        private readonly Color darkPanel = Color.FromArgb(28, 28, 32);   // 요소용 짙은 회색
        private readonly Color accentBlue = Color.FromArgb(60, 180, 255); // 활기찬 파랑
        private readonly Color accentGreen = Color.FromArgb(80, 200, 120);// 에메랄드 그린
        private readonly Color darkText = Color.FromArgb(220, 220, 220); // 부드러운 흰색
        private readonly Color borderColor = Color.FromArgb(45, 45, 50);  // 세련된 구분선

        public HttpSettingsForm(string cookiePath, string profileDir)
        {
            _cookiePath = cookiePath;
            _profileDir = profileDir;
            
            this.Text = "HTTP API 및 쿠키 통합 설정";
            this.MinimizeBox = false;
            this.Size = new Size(560, 560); // 모델 선택 제거로 높이 줄임
            this.BackColor = darkBg;
            
            InitializeComponents();
            LoadExistingCookies(); 
        }
        
        /// <summary>
        /// 사용자 인터페이스(UI) 구성 요소를 초기화하고 배치합니다.
        /// </summary>
        private void InitializeComponents()
        {
            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30), BackColor = darkBg };
            
            // 제목 섹션
            var lblTitle = new Label
            {
                Text = "HTTP API & 쿠키 설정",
                Font = new Font("Segoe UI Variable Display", 18, FontStyle.Bold),
                ForeColor = accentBlue,
                Location = new Point(30, 25),
                AutoSize = true
            };

            var lblDesc = new Label
            {
                Text = "독립 브라우저 자동 추출 또는 수동 입력을 지원합니다.",
                Location = new Point(30, 65),
                ForeColor = Color.FromArgb(150, 150, 150),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            
            // --- 자동 추출 및 파일 로드 그룹 ---
            var gbAuto = new GroupBox
            {
                Text = " 자동 추출 및 파일 로드 ",
                Location = new Point(30, 105),
                Size = new Size(485, 95),
                ForeColor = accentBlue,
                Font = new Font("Segoe UI Semibold", 9)
            };

            btnAutoExtract = new Button
            {
                Text = "🚀 독립 브라우저 실행",
                Location = new Point(15, 30),
                Size = new Size(165, 45),
                BackColor = accentGreen,
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
                BackColor = Color.FromArgb(70, 70, 75),
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
                BackColor = Color.FromArgb(50, 50, 60),
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

            var lblPSID = new Label { Text = "__Secure-1PSID:", Location = new Point(15, 30), AutoSize = true, ForeColor = darkText };
            txtPSID = new TextBox { Location = new Point(15, 52), Width = 455, BackColor = darkPanel, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };
            
            var lblPSIDTS = new Label { Text = "__Secure-1PSIDTS (선택사항):", Location = new Point(15, 90), AutoSize = true, ForeColor = darkText };
            txtPSIDTS = new TextBox { Location = new Point(15, 112), Width = 455, BackColor = darkPanel, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };

            var lblUA = new Label { Text = "User-Agent (선택사항):", Location = new Point(15, 150), AutoSize = true, ForeColor = darkText };
            txtUserAgent = new TextBox { Location = new Point(15, 172), Width = 455, BackColor = darkPanel, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9) };
            
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
                BackColor = accentBlue,
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
                BackColor = accentGreen,
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
                // PuppeteerSharp을 사용하여 실제 브라우저를 띄우고 쿠키 낚아채기
                var (psid, psidts, userAgent) = await ExtractCookiesFromIsolatedBrowserAsync();
                
                if (!string.IsNullOrEmpty(psid))
                {
                    // 추출된 정보를 화면의 입력칸에 자동 채움
                    txtPSID.Text = psid;
                    txtPSIDTS!.Text = psidts ?? "";
                    txtUserAgent!.Text = userAgent ?? "";
                    
                    lblStatus.Text = "✅ 쿠키 추출 성공! 이제 '저장'을 눌러주세요.";
                    lblStatus.ForeColor = Color.Lime;
                    Log("[HTTP] 쿠키 추출 완료");
                }
                else
                {
                    lblStatus.Text = "❌ 쿠키를 찾을 수 없습니다. 로그인이 필요합니다.";
                    lblStatus.ForeColor = Color.Red;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"❌ 오류: {ex.Message}";
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
            
            if (MessageBox.Show("브라우저 실행 파일을 삭제하고 다시 다운로드하시겠습니까?\n이 작업은 시간이 다소 걸릴 수 있습니다.", "브라우저 초기화", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            btnResetBrowser.Enabled = false;
            btnAutoExtract!.Enabled = false;
            lblStatus.Text = "브라우저 초기화 중...";
            
            try
            {
                var manager = new Services.IsolatedBrowserManager();
                manager.OnStatusUpdate += msg => {
                    lblStatus.Invoke(() => lblStatus.Text = msg);
                    Log(msg);
                };
                await manager.ResetBrowserAsync();
                lblStatus.Text = "✅ 초기화 및 재설치 완료!";
                lblStatus.ForeColor = Color.Lime;
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"❌ 초기화 실패: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
            finally
            {
                btnResetBrowser.Enabled = true;
                btnAutoExtract.Enabled = true;
            }
        }
        
        /// <summary>
        /// IsolatedBrowserManager를 사용하여 Chrome for Testing을 실행하고 쿠키를 추출합니다.
        /// </summary>
        private async Task<(string? psid, string? psidts, string? userAgent)> ExtractCookiesFromIsolatedBrowserAsync()
        {
            var manager = new Services.IsolatedBrowserManager();
            IBrowser? browser = null;
            
            try
            {
                // 상태 업데이트를 UI에 반영
                manager.OnStatusUpdate += msg =>
                {
                    lblStatus?.Invoke(() => lblStatus.Text = msg);
                    Log(msg);
                };
                
                // Chrome for Testing 실행 (필요시 자동 다운로드)
                browser = await manager.LaunchBrowserAsync(headless: false);
                
                var pages = await browser.PagesAsync();
                var page = pages.FirstOrDefault() ?? await browser.NewPageAsync();
                
                // 페이지가 Gemini가 아닌 경우 이동
                var currentUrl = page.Url;
                if (!currentUrl.Contains("gemini.google.com"))
                {
                    await page.GoToAsync("https://gemini.google.com", WaitUntilNavigation.Networkidle2);
                }
                
                // 사용자가 로그인할 시간을 주기 위해 쿠키가 나타날 때까지 반복 감시 (최대 3분)
                string? psid = null;
                string? psidts = null;
                for (int i = 0; i < 180; i++)
                {
                    var cookies = await page.GetCookiesAsync("https://gemini.google.com");
                    psid = cookies.FirstOrDefault(c => c.Name == "__Secure-1PSID")?.Value;
                    psidts = cookies.FirstOrDefault(c => c.Name == "__Secure-1PSIDTS")?.Value;
                    
                    if (!string.IsNullOrEmpty(psid)) break;
                    await Task.Delay(1000);
                    
                    if (browser.IsClosed) break;
                }
                
                // User-Agent 일관성을 위해 브라우저 엔진의 UA 정보 추출
                var userAgent = await page.EvaluateExpressionAsync<string>("navigator.userAgent");
                return (psid, psidts, userAgent);
            }
            finally
            {
                if (browser != null && !browser.IsClosed)
                {
                    await manager.CloseBrowserAsync(); // IsolatedBrowserManager 통해 종료
                }
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
                        
                        lblStatus!.Text = "✅ 파일 로드 성공!";
                        lblStatus.ForeColor = Color.Lime;
                        Log("[HTTP] 쿠키 파일 로드됨");
                    }
                    else
                    {
                        lblStatus!.Text = "⚠️ JSON 형식이 아닙니다.";
                        lblStatus.ForeColor = Color.Orange;
                    }
                }
                catch (Exception ex)
                {
                    lblStatus!.Text = $"❌ 파일 읽기 오류: {ex.Message}";
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
                    lblStatus.Text = "✅ API 재연결 성공";
                    lblStatus.ForeColor = accentGreen;
                    OnLog?.Invoke("[HTTP] API 재연결 성공");
                }
                else
                {
                    lblStatus.Text = "⚠️ 재연결 핸들러 없음";
                    lblStatus.ForeColor = Color.Yellow;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"❌ 재연결 실패: {ex.Message}";
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

