#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace GeminiWebTranslator;

/// <summary>
/// Nano Banana Pro 설정 및 실행 창
/// </summary>
public class NanoBananaForm : Form
{
    private readonly GeminiImageProcessor _processor;
    private TextBox txtPrompt = null!;
    private TextBox txtLog = null!;
    private Button btnStart = null!;
    private Button btnDownload = null!;
    private CheckBox chkProMode = null!;
    private CheckBox chkImageGen = null!;
    private ProgressBar progressBar = null!;

    // 다크모드 색상
    private readonly Color darkBg = Color.FromArgb(30, 30, 35);
    private readonly Color darkPanel = Color.FromArgb(40, 40, 45);
    private readonly Color darkText = Color.FromArgb(220, 220, 225);
    private readonly Color accentPurple = Color.FromArgb(140, 80, 180);

    public NanoBananaForm(GeminiImageProcessor processor)
    {
        _processor = processor;
        _processor.OnLog += msg => AppendLog(msg);
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Text = "🍌 Nano Banana Pro - 이미지 워터마크 제거";
        Size = new Size(600, 500);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = darkBg;
        ForeColor = darkText;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        // Header
        var lblHeader = new Label {
            Text = "🍌 Nano Banana Pro",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = accentPurple,
            Dock = DockStyle.Top,
            Height = 50,
            TextAlign = ContentAlignment.MiddleCenter
        };

        // Settings Panel
        var panelSettings = new Panel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(15) };
        
        chkProMode = new CheckBox {
            Text = "Pro 모드 사용",
            Checked = true,
            Location = new Point(15, 10),
            ForeColor = darkText,
            AutoSize = true
        };
        
        chkImageGen = new CheckBox {
            Text = "이미지 생성 모드 활성화",
            Checked = true,
            Location = new Point(15, 35),
            ForeColor = darkText,
            AutoSize = true
        };
        
        panelSettings.Controls.AddRange(new Control[] { chkProMode, chkImageGen });

        // Prompt Group
        var grpPrompt = new GroupBox {
            Text = "  📝 프롬프트  ",
            Dock = DockStyle.Top,
            Height = 120,
            ForeColor = darkText,
            Padding = new Padding(10)
        };
        
        txtPrompt = new TextBox {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(25, 25, 30),
            ForeColor = darkText,
            Font = new Font("맑은 고딕", 10),
            Text = _processor.DefaultPrompt
        };
        grpPrompt.Controls.Add(txtPrompt);

        // Log Group
        var grpLog = new GroupBox {
            Text = "  📋 로그  ",
            Dock = DockStyle.Fill,
            ForeColor = darkText,
            Padding = new Padding(10)
        };
        
        txtLog = new TextBox {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(20, 20, 25),
            ForeColor = Color.FromArgb(180, 255, 180),
            Font = new Font("Consolas", 9)
        };
        grpLog.Controls.Add(txtLog);

        // Bottom Panel
        var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(15, 10, 15, 10) };
        
        progressBar = new ProgressBar {
            Dock = DockStyle.Top,
            Height = 5,
            Style = ProgressBarStyle.Marquee,
            Visible = false
        };
        
        btnStart = new Button {
            Text = "▶️ 시작",
            Width = 120,
            Height = 40,
            BackColor = accentPurple,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Dock = DockStyle.Right,
            Cursor = Cursors.Hand
        };
        btnStart.Click += BtnStart_Click;
        
        btnDownload = new Button {
            Text = "⬇️ 다운로드",
            Width = 110,
            Height = 40,
            BackColor = Color.FromArgb(60, 120, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10),
            Dock = DockStyle.Right,
            Enabled = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 0)
        };
        btnDownload.Click += async (s, e) => await _processor.DownloadResultAsync();

        var btnClose = new Button {
            Text = "닫기",
            Width = 80,
            Height = 40,
            BackColor = Color.FromArgb(60, 60, 65),
            ForeColor = darkText,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Left,
            Cursor = Cursors.Hand
        };
        btnClose.Click += (s, e) => Close();

        panelBottom.Controls.AddRange(new Control[] { btnStart, btnDownload, btnClose, progressBar });

        // Assemble
        Controls.Add(grpLog);
        Controls.Add(grpPrompt);
        Controls.Add(panelSettings);
        Controls.Add(lblHeader);
        Controls.Add(panelBottom);
    }

    private void AppendLog(string msg)
    {
        if (InvokeRequired)
        {
            Invoke(() => AppendLog(msg));
            return;
        }
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
    }

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        btnStart.Enabled = false;
        progressBar.Visible = true;
        
        try
        {
            AppendLog("=== 워터마크 제거 시작 ===");
            
            // 설정 적용
            if (chkProMode.Checked)
                await _processor.SelectProModeAsync();
                
            if (chkImageGen.Checked)
                await _processor.EnableImageGenerationAsync();
            
            // 새 채팅
            await _processor.StartNewChatAsync();
            
            // 업로드 메뉴 (수동 선택 필요)
            await _processor.OpenUploadMenuAsync();
            AppendLog("⚠️ 파일 다이얼로그에서 이미지를 선택하세요");
            
            // 대기
            await Task.Delay(5000);
            
            // 프롬프트
            await _processor.SendPromptAsync(txtPrompt.Text);
            
            // 응답 대기
            await _processor.WaitForResponseAsync();
            
            btnDownload.Enabled = true;
            AppendLog("✅ 완료! [다운로드] 버튼을 클릭하세요");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ 오류: {ex.Message}");
        }
        finally
        {
            btnStart.Enabled = true;
            progressBar.Visible = false;
        }
    }
}
