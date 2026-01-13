#nullable disable
using System.Drawing;
using System.Windows.Forms;

namespace GeminiWebTranslator;

partial class NanoBananaMainForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _cts?.Cancel();
            _cts?.Dispose();
            _edgeCdpAutomation?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        this.mainSplit = new System.Windows.Forms.SplitContainer();
        this.innerSplit = new System.Windows.Forms.SplitContainer();
        this.grpSettings = new System.Windows.Forms.GroupBox();
        this.layoutSettings = new System.Windows.Forms.TableLayoutPanel();
        this.lblBrowserMode = new System.Windows.Forms.Label();
        this.btnLaunchIsolated = new System.Windows.Forms.Button();
        this.lblInput = new System.Windows.Forms.Label();
        this.txtInputFolder = new System.Windows.Forms.TextBox();
        this.btnBrowseInput = new System.Windows.Forms.Button();
        this.lblOutput = new System.Windows.Forms.Label();
        this.txtOutputFolder = new System.Windows.Forms.TextBox();
        this.btnBrowseOutput = new System.Windows.Forms.Button();
        this.lblPrompt = new System.Windows.Forms.Label();
        this.txtPrompt = new System.Windows.Forms.TextBox();
        this.flowOptions = new System.Windows.Forms.FlowLayoutPanel();
        this.chkProMode = new System.Windows.Forms.CheckBox();
        this.chkImageGen = new System.Windows.Forms.CheckBox();
        this.grpImageList = new System.Windows.Forms.GroupBox();
        this.dgvImages = new System.Windows.Forms.DataGridView();
        this.colFileName = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.flowControls = new System.Windows.Forms.FlowLayoutPanel();
        this.btnStart = new System.Windows.Forms.Button();
        this.btnStop = new System.Windows.Forms.Button();
        this.btnReset = new System.Windows.Forms.Button();
        this.btnRefresh = new System.Windows.Forms.Button();
        this.btnShowBrowser = new System.Windows.Forms.Button();
        this.progressBar = new System.Windows.Forms.ProgressBar();
        this.lblProgress = new System.Windows.Forms.Label();
        this.grpLog = new System.Windows.Forms.GroupBox();
        this.txtLog = new System.Windows.Forms.RichTextBox();
        
        ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).BeginInit();
        this.mainSplit.Panel1.SuspendLayout();
        this.mainSplit.Panel2.SuspendLayout();
        this.mainSplit.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.innerSplit)).BeginInit();
        this.innerSplit.Panel1.SuspendLayout();
        this.innerSplit.Panel2.SuspendLayout();
        this.innerSplit.SuspendLayout();
        this.grpSettings.SuspendLayout();
        this.layoutSettings.SuspendLayout();
        this.flowOptions.SuspendLayout();
        this.grpImageList.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.dgvImages)).BeginInit();
        this.flowControls.SuspendLayout();
        this.grpLog.SuspendLayout();
        this.SuspendLayout();

        // 
        // Form Setup
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.BackColor = System.Drawing.Color.FromArgb(30, 30, 35);
        this.ForeColor = System.Drawing.Color.FromArgb(220, 220, 225);
        this.ClientSize = new System.Drawing.Size(1000, 800);
        this.MinimumSize = new System.Drawing.Size(800, 600);
        this.Controls.Add(this.mainSplit);
        this.Name = "NanoBananaMainForm";
        this.Text = "🍌 NanoBanana Pro - 배치 이미지 처리";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

        // 
        // mainSplit (Top/Bottom: Content/Log)
        // 
        this.mainSplit.Dock = System.Windows.Forms.DockStyle.Fill;
        this.mainSplit.Orientation = System.Windows.Forms.Orientation.Horizontal;
        this.mainSplit.SplitterDistance = 500;
        this.mainSplit.SplitterWidth = 6;
        this.mainSplit.BackColor = System.Drawing.Color.FromArgb(45, 45, 50);
        this.mainSplit.Panel1MinSize = 300;
        this.mainSplit.Panel2MinSize = 100;
        
        // 
        // Panel 1 (Top) - Settings, List, Controls
        // 
        this.mainSplit.Panel1.Controls.Add(this.innerSplit);
        this.mainSplit.Panel1.Controls.Add(this.flowControls);
        this.mainSplit.Panel1.Padding = new System.Windows.Forms.Padding(10);

        // 
        // flowControls (Bottom bar with buttons)
        // 
        this.flowControls.Dock = System.Windows.Forms.DockStyle.Bottom;
        this.flowControls.Height = 55;
        this.flowControls.Padding = new System.Windows.Forms.Padding(0, 5, 0, 5);
        this.flowControls.WrapContents = true;
        this.flowControls.AutoSize = false;
        
        this.btnStart.Text = "▶️ 시작";
        this.btnStart.Size = new System.Drawing.Size(100, 40);
        this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnStart.BackColor = System.Drawing.Color.FromArgb(52, 199, 89);
        this.btnStart.ForeColor = System.Drawing.Color.White;
        this.btnStart.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
        
        this.btnStop.Text = "⏹️ 중지";
        this.btnStop.Size = new System.Drawing.Size(80, 40);
        this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnStop.BackColor = System.Drawing.Color.FromArgb(255, 69, 58);
        this.btnStop.ForeColor = System.Drawing.Color.White;
        this.btnStop.Enabled = false;
        this.btnStop.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
        
        this.btnReset.Text = "🔄 리셋";
        this.btnReset.Size = new System.Drawing.Size(80, 40);
        this.btnReset.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnReset.BackColor = System.Drawing.Color.FromArgb(80, 80, 80);
        this.btnReset.ForeColor = System.Drawing.Color.White;
        this.btnReset.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
        
        this.btnRefresh.Text = "📁 새로고침";
        this.btnRefresh.Size = new System.Drawing.Size(100, 40);
        this.btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnRefresh.BackColor = System.Drawing.Color.FromArgb(80, 80, 100);
        this.btnRefresh.ForeColor = System.Drawing.Color.White;
        this.btnRefresh.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
        
        this.btnShowBrowser.Text = "🔳 창 키우기";
        this.btnShowBrowser.Size = new System.Drawing.Size(100, 40);
        this.btnShowBrowser.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnShowBrowser.BackColor = System.Drawing.Color.FromArgb(100, 80, 140);
        this.btnShowBrowser.ForeColor = System.Drawing.Color.White;
        this.btnShowBrowser.Margin = new System.Windows.Forms.Padding(0, 0, 20, 0);
        
        this.progressBar.Size = new System.Drawing.Size(200, 30);
        this.progressBar.Margin = new System.Windows.Forms.Padding(0, 5, 10, 0);
        
        this.lblProgress.Text = "0/0";
        this.lblProgress.AutoSize = true;
        this.lblProgress.ForeColor = System.Drawing.Color.White;
        this.lblProgress.Margin = new System.Windows.Forms.Padding(0, 12, 0, 0);
        
        this.btnHideBrowser = new System.Windows.Forms.Button();
        this.btnHideBrowser.Text = "🔽 숨기기";
        this.btnHideBrowser.Size = new System.Drawing.Size(90, 40);
        this.btnHideBrowser.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnHideBrowser.BackColor = System.Drawing.Color.FromArgb(70, 70, 80);
        this.btnHideBrowser.ForeColor = System.Drawing.Color.White;
        this.btnHideBrowser.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
        
        this.flowControls.Controls.AddRange(new System.Windows.Forms.Control[] { 
            this.btnStart, this.btnStop, this.btnReset, this.btnRefresh, this.btnShowBrowser, this.btnHideBrowser, this.progressBar, this.lblProgress 
        });

        // 
        // innerSplit (Settings / Image List)
        // 
        this.innerSplit.Dock = System.Windows.Forms.DockStyle.Fill;
        this.innerSplit.Orientation = System.Windows.Forms.Orientation.Horizontal;
        this.innerSplit.SplitterDistance = 230;
        this.innerSplit.SplitterWidth = 6;
        this.innerSplit.BackColor = System.Drawing.Color.FromArgb(45, 45, 50);
        this.innerSplit.Panel1MinSize = 180;
        this.innerSplit.Panel2MinSize = 100;

        // 
        // innerSplit.Panel1 - Settings
        // 
        this.innerSplit.Panel1.Controls.Add(this.grpSettings);
        this.innerSplit.Panel1.Padding = new System.Windows.Forms.Padding(0, 0, 0, 5);

        // 
        // grpSettings
        // 
        this.grpSettings.Controls.Add(this.layoutSettings);
        this.grpSettings.Dock = System.Windows.Forms.DockStyle.Fill;
        this.grpSettings.ForeColor = System.Drawing.Color.WhiteSmoke;
        this.grpSettings.Text = "설정";
        this.grpSettings.Padding = new System.Windows.Forms.Padding(10);

        // 
        // layoutSettings
        // 
        this.layoutSettings.Dock = System.Windows.Forms.DockStyle.Fill;
        this.layoutSettings.ColumnCount = 3;
        this.layoutSettings.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
        this.layoutSettings.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        this.layoutSettings.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 45F));
        this.layoutSettings.RowCount = 5;
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 42F)); // Browser
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F)); // Input
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F)); // Output
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F)); // Options
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F)); // Prompt

        // Row 0: Browser Management
        this.lblBrowserMode.Text = "브라우저:";
        this.lblBrowserMode.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.lblBrowserMode.Dock = System.Windows.Forms.DockStyle.Fill;
        
        var panelBrowser = new System.Windows.Forms.FlowLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, Margin = new System.Windows.Forms.Padding(0) };
        
        this.btnLaunchIsolated.Text = "🚀 Chrome 실행/설치";
        this.btnLaunchIsolated.Size = new System.Drawing.Size(180, 32);
        this.btnLaunchIsolated.BackColor = System.Drawing.Color.FromArgb(60, 120, 210);
        this.btnLaunchIsolated.ForeColor = System.Drawing.Color.White;
        this.btnLaunchIsolated.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnLaunchIsolated.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnLaunchIsolated.Margin = new Padding(0, 3, 10, 0);

        var lblStatusInfo = new System.Windows.Forms.Label
        {
            Text = "(독립 브라우저로 Google 로그인)",
            AutoSize = true,
            ForeColor = System.Drawing.Color.FromArgb(150, 150, 160),
            Margin = new Padding(0, 10, 0, 0),
            Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Italic)
        };

        panelBrowser.Controls.AddRange(new System.Windows.Forms.Control[] { this.btnLaunchIsolated, lblStatusInfo });
        
        this.layoutSettings.Controls.Add(this.lblBrowserMode, 0, 0);
        this.layoutSettings.Controls.Add(panelBrowser, 1, 0);
        this.layoutSettings.SetColumnSpan(panelBrowser, 2);

        // Row 1: Input
        this.lblInput.Text = "입력 폴더:";
        this.lblInput.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.lblInput.Dock = System.Windows.Forms.DockStyle.Fill;
        
        this.txtInputFolder.Dock = System.Windows.Forms.DockStyle.Fill;
        this.txtInputFolder.BackColor = System.Drawing.Color.FromArgb(40, 40, 45);
        this.txtInputFolder.ForeColor = System.Drawing.Color.White;
        this.txtInputFolder.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        
        this.btnBrowseInput.Text = "...";
        this.btnBrowseInput.Dock = System.Windows.Forms.DockStyle.Fill;
        this.btnBrowseInput.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnBrowseInput.BackColor = System.Drawing.Color.FromArgb(60, 60, 65);
        
        this.layoutSettings.Controls.Add(this.lblInput, 0, 1);
        this.layoutSettings.Controls.Add(this.txtInputFolder, 1, 1);
        this.layoutSettings.Controls.Add(this.btnBrowseInput, 2, 1);

        // Row 2: Output
        this.lblOutput.Text = "출력 폴더:";
        this.lblOutput.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.lblOutput.Dock = System.Windows.Forms.DockStyle.Fill;
        
        this.txtOutputFolder.Dock = System.Windows.Forms.DockStyle.Fill;
        this.txtOutputFolder.BackColor = System.Drawing.Color.FromArgb(40, 40, 45);
        this.txtOutputFolder.ForeColor = System.Drawing.Color.White;
        this.txtOutputFolder.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        
        this.btnBrowseOutput.Text = "...";
        this.btnBrowseOutput.Dock = System.Windows.Forms.DockStyle.Fill;
        this.btnBrowseOutput.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnBrowseOutput.BackColor = System.Drawing.Color.FromArgb(60, 60, 65);
        
        this.layoutSettings.Controls.Add(this.lblOutput, 0, 2);
        this.layoutSettings.Controls.Add(this.txtOutputFolder, 1, 2);
        this.layoutSettings.Controls.Add(this.btnBrowseOutput, 2, 2);

        // Row 3: Options (FlowLayoutPanel)
        this.flowOptions.Dock = System.Windows.Forms.DockStyle.Fill;
        this.flowOptions.Margin = new System.Windows.Forms.Padding(0);
        this.flowOptions.WrapContents = true;
        
        this.chkProMode.Text = "Pro 모드";
        this.chkProMode.AutoSize = true;
        this.chkProMode.ForeColor = System.Drawing.Color.White;
        this.chkProMode.Margin = new System.Windows.Forms.Padding(0, 5, 15, 0);
        
        this.chkImageGen.Text = "이미지 생성";
        this.chkImageGen.AutoSize = true;
        this.chkImageGen.ForeColor = System.Drawing.Color.White;
        this.chkImageGen.Margin = new System.Windows.Forms.Padding(0, 5, 15, 0);
        
        this.chkUseOcr = new System.Windows.Forms.CheckBox();
        this.chkUseOcr.Text = "OCR 사용";
        this.chkUseOcr.AutoSize = true;
        this.chkUseOcr.ForeColor = System.Drawing.Color.White;
        this.chkUseOcr.Checked = true;
        this.chkUseOcr.Margin = new System.Windows.Forms.Padding(0, 5, 15, 0);

        
        this.flowOptions.Controls.AddRange(new System.Windows.Forms.Control[] { this.chkProMode, this.chkImageGen, this.chkUseOcr });
        this.layoutSettings.Controls.Add(new System.Windows.Forms.Label { Text = "옵션:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 3);
        this.layoutSettings.Controls.Add(this.flowOptions, 1, 3);
        this.layoutSettings.SetColumnSpan(this.flowOptions, 2);

        // Row 4: Prompt
        this.lblPrompt.Text = "프롬프트:";
        this.lblPrompt.TextAlign = System.Drawing.ContentAlignment.TopLeft;
        this.lblPrompt.Padding = new System.Windows.Forms.Padding(0, 5, 0, 0);
        this.lblPrompt.Dock = System.Windows.Forms.DockStyle.Fill;
        
        this.txtPrompt.Dock = System.Windows.Forms.DockStyle.Fill;
        this.txtPrompt.Multiline = true;
        this.txtPrompt.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        this.txtPrompt.BackColor = System.Drawing.Color.FromArgb(40, 40, 45);
        this.txtPrompt.ForeColor = System.Drawing.Color.White;
        this.txtPrompt.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        
        this.layoutSettings.Controls.Add(this.lblPrompt, 0, 4);
        this.layoutSettings.Controls.Add(this.txtPrompt, 1, 4);
        this.layoutSettings.SetColumnSpan(this.txtPrompt, 2);

        // 
        // innerSplit.Panel2 - Image List
        // 
        this.innerSplit.Panel2.Controls.Add(this.grpImageList);
        this.innerSplit.Panel2.Padding = new System.Windows.Forms.Padding(0, 5, 0, 0);

        // 
        // grpImageList
        // 
        this.grpImageList.Controls.Add(this.dgvImages);
        this.grpImageList.Dock = System.Windows.Forms.DockStyle.Fill;
        this.grpImageList.Text = "이미지 목록";
        this.grpImageList.ForeColor = System.Drawing.Color.WhiteSmoke;
        
        this.dgvImages.Dock = System.Windows.Forms.DockStyle.Fill;
        this.dgvImages.BackgroundColor = System.Drawing.Color.FromArgb(30, 30, 35);
        this.dgvImages.BorderStyle = System.Windows.Forms.BorderStyle.None;
        this.dgvImages.RowHeadersVisible = false;
        this.dgvImages.AllowUserToAddRows = false;
        this.dgvImages.AllowUserToDeleteRows = false;
        this.dgvImages.ReadOnly = true;
        this.dgvImages.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        this.dgvImages.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
        this.dgvImages.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(30, 30, 35);
        this.dgvImages.DefaultCellStyle.ForeColor = System.Drawing.Color.White;
        this.dgvImages.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(140, 80, 180);
        this.dgvImages.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(40, 40, 45);
        this.dgvImages.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.White;
        this.dgvImages.EnableHeadersVisualStyles = false;
        
        this.colFileName.Name = "FileName";
        this.colFileName.HeaderText = "파일명";
        this.colFileName.FillWeight = 70;
        
        this.colStatus.Name = "Status";
        this.colStatus.HeaderText = "상태";
        this.colStatus.FillWeight = 30;
        
        this.dgvImages.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { this.colFileName, this.colStatus });

        // 
        // Panel 2 (Bottom) - Log
        // 
        this.mainSplit.Panel2.Controls.Add(this.grpLog);
        this.mainSplit.Panel2.Padding = new System.Windows.Forms.Padding(10);
        
        this.grpLog.Dock = System.Windows.Forms.DockStyle.Fill;
        this.grpLog.Text = "로그";
        this.grpLog.ForeColor = System.Drawing.Color.WhiteSmoke;
        this.grpLog.Controls.Add(this.txtLog);
        
        this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
        this.txtLog.BackColor = System.Drawing.Color.FromArgb(20, 20, 25);
        this.txtLog.ForeColor = System.Drawing.Color.FromArgb(180, 255, 180);
        this.txtLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
        this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
        this.txtLog.ReadOnly = true;

        // Finish
        this.innerSplit.Panel1.ResumeLayout(false);
        this.innerSplit.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.innerSplit)).EndInit();
        this.innerSplit.ResumeLayout(false);
        this.mainSplit.Panel1.ResumeLayout(false);
        this.mainSplit.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).EndInit();
        this.mainSplit.ResumeLayout(false);
        this.grpSettings.ResumeLayout(false);
        this.layoutSettings.ResumeLayout(false);
        this.layoutSettings.PerformLayout();
        this.flowOptions.ResumeLayout(false);
        this.flowOptions.PerformLayout();
        this.grpImageList.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.dgvImages)).EndInit();
        this.flowControls.ResumeLayout(false);
        this.flowControls.PerformLayout();
        this.grpLog.ResumeLayout(false);
        this.ResumeLayout(false);
    }
    
    #endregion

    private System.Windows.Forms.SplitContainer mainSplit;
    private System.Windows.Forms.SplitContainer innerSplit;
    private System.Windows.Forms.GroupBox grpSettings;
    private System.Windows.Forms.TableLayoutPanel layoutSettings;
    private System.Windows.Forms.FlowLayoutPanel flowOptions;
    private System.Windows.Forms.GroupBox grpImageList;
    private System.Windows.Forms.FlowLayoutPanel flowControls;
    private System.Windows.Forms.GroupBox grpLog;
    
    private System.Windows.Forms.Label lblBrowserMode;
    
    private System.Windows.Forms.Label lblInput;
    private System.Windows.Forms.TextBox txtInputFolder;
    private System.Windows.Forms.Button btnBrowseInput;
    
    private System.Windows.Forms.Label lblOutput;
    private System.Windows.Forms.TextBox txtOutputFolder;
    private System.Windows.Forms.Button btnBrowseOutput;
    
    private System.Windows.Forms.Label lblPrompt;
    private System.Windows.Forms.TextBox txtPrompt;
    
    private System.Windows.Forms.CheckBox chkProMode;
    private System.Windows.Forms.CheckBox chkImageGen;
    private System.Windows.Forms.CheckBox chkUseOcr;
    private System.Windows.Forms.Button btnLaunchIsolated;
    private System.Windows.Forms.Button btnHideBrowser;
    
    private System.Windows.Forms.DataGridView dgvImages;
    private System.Windows.Forms.DataGridViewTextBoxColumn colFileName;
    private System.Windows.Forms.DataGridViewTextBoxColumn colStatus;
    
    private System.Windows.Forms.Button btnStart;
    private System.Windows.Forms.Button btnStop;
    private System.Windows.Forms.Button btnReset;
    private System.Windows.Forms.Button btnRefresh;
    private System.Windows.Forms.Button btnShowBrowser;
    private System.Windows.Forms.ProgressBar progressBar;
    private System.Windows.Forms.Label lblProgress;
    
    private System.Windows.Forms.RichTextBox txtLog;
}
