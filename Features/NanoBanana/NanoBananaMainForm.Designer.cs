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
        this.settingsPanel = new System.Windows.Forms.Panel();
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
        this.panelOptions = new System.Windows.Forms.Panel();
        this.chkProMode = new System.Windows.Forms.CheckBox();
        this.chkImageGen = new System.Windows.Forms.CheckBox();
        this.grpImageList = new System.Windows.Forms.GroupBox();
        this.dgvImages = new System.Windows.Forms.DataGridView();
        this.colFileName = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.panelControls = new System.Windows.Forms.Panel();
        this.btnStart = new System.Windows.Forms.Button();
        this.btnStop = new System.Windows.Forms.Button();
        this.btnReset = new System.Windows.Forms.Button();
        this.btnRefresh = new System.Windows.Forms.Button();
        this.progressBar = new System.Windows.Forms.ProgressBar();
        this.lblProgress = new System.Windows.Forms.Label();
        this.grpLog = new System.Windows.Forms.GroupBox();
        this.txtLog = new System.Windows.Forms.RichTextBox();
        
        ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).BeginInit();
        this.mainSplit.Panel1.SuspendLayout();
        this.mainSplit.Panel2.SuspendLayout();
        this.mainSplit.SuspendLayout();
        this.settingsPanel.SuspendLayout();
        this.grpSettings.SuspendLayout();
        this.layoutSettings.SuspendLayout();
        this.panelOptions.SuspendLayout();
        this.grpImageList.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.dgvImages)).BeginInit();
        this.panelControls.SuspendLayout();
        this.grpLog.SuspendLayout();
        this.SuspendLayout();

        // 
        // Form Setup
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.BackColor = System.Drawing.Color.FromArgb(30, 30, 35);
        this.ForeColor = System.Drawing.Color.FromArgb(220, 220, 225);
        this.ClientSize = new System.Drawing.Size(950, 750);
        this.Controls.Add(this.mainSplit);
        this.Name = "NanoBananaMainForm";
        this.Text = "🍌 NanoBanana Pro - 배치 이미지 처리";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

        // 
        // mainSplit
        // 
        this.mainSplit.Dock = System.Windows.Forms.DockStyle.Fill;
        this.mainSplit.Orientation = System.Windows.Forms.Orientation.Horizontal;
        this.mainSplit.SplitterDistance = 450;
        this.mainSplit.BackColor = System.Drawing.Color.FromArgb(45, 45, 50);
        
        // 
        // Panel 1 (Top) - Settings & List
        // 
        this.mainSplit.Panel1.Controls.Add(this.grpImageList);
        this.mainSplit.Panel1.Controls.Add(this.panelControls);
        this.mainSplit.Panel1.Controls.Add(this.settingsPanel);
        this.mainSplit.Panel1.Padding = new System.Windows.Forms.Padding(10);

        // 
        // settingsPanel
        // 
        this.settingsPanel.Controls.Add(this.grpSettings);
        this.settingsPanel.Dock = System.Windows.Forms.DockStyle.Top;
        this.settingsPanel.Height = 220;
        this.settingsPanel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 10);

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
        this.layoutSettings.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 40F));
        this.layoutSettings.RowCount = 5;
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 45F)); // Browser (Higher for button)
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F)); // Input
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F)); // Output
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F)); // Options
        this.layoutSettings.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F)); // Prompt

        // Row 1: Browser Management (Isolated Browser)
        this.lblBrowserMode.Text = "브라우저 제어:";
        this.lblBrowserMode.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.lblBrowserMode.Dock = System.Windows.Forms.DockStyle.Fill;
        
        var panelBrowser = new System.Windows.Forms.FlowLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, Margin = new System.Windows.Forms.Padding(0) };
        
        this.btnLaunchIsolated.Text = "🚀 Chrome for Testing 실행/설치";
        this.btnLaunchIsolated.Width = 250;
        this.btnLaunchIsolated.Height = 35;
        this.btnLaunchIsolated.BackColor = System.Drawing.Color.FromArgb(60, 120, 210);
        this.btnLaunchIsolated.ForeColor = System.Drawing.Color.White;
        this.btnLaunchIsolated.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnLaunchIsolated.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnLaunchIsolated.Margin = new Padding(0, 5, 10, 0);

        var lblStatusInfo = new System.Windows.Forms.Label
        {
            Text = "(독립 브라우저를 통해 구글 로그인이 수행됩니다)",
            AutoSize = true,
            ForeColor = System.Drawing.Color.FromArgb(150, 150, 160),
            Margin = new Padding(0, 12, 0, 0),
            Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic)
        };

        panelBrowser.Controls.AddRange(new System.Windows.Forms.Control[] { 
            this.btnLaunchIsolated, lblStatusInfo
        });
        
        this.layoutSettings.Controls.Add(this.lblBrowserMode, 0, 0);
        this.layoutSettings.Controls.Add(panelBrowser, 1, 0);
        this.layoutSettings.SetColumnSpan(panelBrowser, 2);

        // Row 2: Input
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

        // Row 3: Output
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

        // Row 4: Options
        this.panelOptions.Dock = System.Windows.Forms.DockStyle.Fill;
        this.chkProMode.Text = "Pro 모드";
        this.chkProMode.AutoSize = true;
        this.chkProMode.ForeColor = System.Drawing.Color.White;
        this.chkProMode.Location = new System.Drawing.Point(0, 5);
        
        this.chkImageGen.Text = "이미지 생성 모드";
        this.chkImageGen.AutoSize = true;
        this.chkImageGen.ForeColor = System.Drawing.Color.White;
        this.chkImageGen.Location = new System.Drawing.Point(100, 5);
        
        this.chkUseOcr = new System.Windows.Forms.CheckBox();
        this.chkUseOcr.Text = "OCR 텍스트 추출";
        this.chkUseOcr.AutoSize = true;
        this.chkUseOcr.ForeColor = System.Drawing.Color.White;
        this.chkUseOcr.Location = new System.Drawing.Point(230, 5);
        this.chkUseOcr.Checked = true;

        this.chkHideBrowser = new System.Windows.Forms.CheckBox();
        this.chkHideBrowser.Text = "브라우저 숨기기";
        this.chkHideBrowser.AutoSize = true;
        this.chkHideBrowser.ForeColor = System.Drawing.Color.White;
        this.chkHideBrowser.Location = new System.Drawing.Point(360, 5);
        this.chkHideBrowser.Checked = false;
        
        this.panelOptions.Controls.AddRange(new System.Windows.Forms.Control[] { this.chkProMode, this.chkImageGen, this.chkUseOcr, this.chkHideBrowser });
        this.layoutSettings.Controls.Add(new System.Windows.Forms.Label(), 0, 3); // Empty label
        this.layoutSettings.Controls.Add(this.panelOptions, 1, 3);
        this.layoutSettings.SetColumnSpan(this.panelOptions, 2);

        // Row 5: Prompt
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
        // Panel Controls
        //
        this.panelControls.Dock = System.Windows.Forms.DockStyle.Bottom;
        this.panelControls.Height = 50;
        this.panelControls.Padding = new System.Windows.Forms.Padding(0, 5, 0, 0);
        
        this.btnStart.Text = "▶️ 시작";
        this.btnStart.Width = 100;
        this.btnStart.Height = 40;
        this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnStart.BackColor = System.Drawing.Color.FromArgb(52, 199, 89);
        this.btnStart.ForeColor = System.Drawing.Color.White;
        this.btnStart.Location = new System.Drawing.Point(0, 5);
        
        this.btnStop.Text = "⏹️ 중지";
        this.btnStop.Width = 80;
        this.btnStop.Height = 40;
        this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnStop.BackColor = System.Drawing.Color.FromArgb(255, 69, 58);
        this.btnStop.ForeColor = System.Drawing.Color.White;
        this.btnStop.Enabled = false;
        this.btnStop.Location = new System.Drawing.Point(110, 5);
        
        this.btnReset.Text = "🔄 리셋";
        this.btnReset.Width = 80;
        this.btnReset.Height = 40;
        this.btnReset.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnReset.BackColor = System.Drawing.Color.FromArgb(80, 80, 80);
        this.btnReset.ForeColor = System.Drawing.Color.White;
        this.btnReset.Location = new System.Drawing.Point(200, 5);
        
        this.btnRefresh.Text = "📁 새로고침";
        this.btnRefresh.Width = 100;
        this.btnRefresh.Height = 40;
        this.btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnRefresh.BackColor = System.Drawing.Color.FromArgb(80, 80, 100);
        this.btnRefresh.ForeColor = System.Drawing.Color.White;
        this.btnRefresh.Location = new System.Drawing.Point(290, 5);
        
        this.progressBar.Location = new System.Drawing.Point(400, 10);
        this.progressBar.Size = new System.Drawing.Size(300, 30);
        
        this.lblProgress.Text = "0/0";
        this.lblProgress.AutoSize = true;
        this.lblProgress.Location = new System.Drawing.Point(710, 18);
        this.lblProgress.ForeColor = System.Drawing.Color.White;
        
        this.panelControls.Controls.AddRange(new System.Windows.Forms.Control[] { 
            this.btnStart, this.btnStop, this.btnReset, this.btnRefresh, this.progressBar, this.lblProgress 
        });

        // 
        // Group Image List
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
        this.dgvImages.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(30, 30, 35);
        this.dgvImages.DefaultCellStyle.ForeColor = System.Drawing.Color.White;
        this.dgvImages.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(140, 80, 180);
        this.dgvImages.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(40, 40, 45);
        this.dgvImages.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.White;
        this.dgvImages.EnableHeadersVisualStyles = false;
        
        this.colFileName.Name = "FileName";
        this.colFileName.HeaderText = "파일명";
        this.colFileName.Width = 400;
        
        this.colStatus.Name = "Status";
        this.colStatus.HeaderText = "상태";
        this.colStatus.Width = 150;
        
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
        this.mainSplit.Panel1.ResumeLayout(false);
        this.mainSplit.Panel2.ResumeLayout(false);
        this.mainSplit.ResumeLayout(false);
        this.settingsPanel.ResumeLayout(false);
        this.grpSettings.ResumeLayout(false);
        this.layoutSettings.ResumeLayout(false);
        this.layoutSettings.PerformLayout();
        this.panelOptions.ResumeLayout(false);
        this.panelOptions.PerformLayout();
        this.grpImageList.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.dgvImages)).EndInit();
        this.panelControls.ResumeLayout(false);
        this.panelControls.PerformLayout();
        this.grpLog.ResumeLayout(false);
        this.ResumeLayout(false);
    }
    
    #endregion

    private System.Windows.Forms.SplitContainer mainSplit;
    private System.Windows.Forms.Panel settingsPanel;
    private System.Windows.Forms.GroupBox grpSettings;
    private System.Windows.Forms.TableLayoutPanel layoutSettings;
    private System.Windows.Forms.Panel panelOptions;
    private System.Windows.Forms.GroupBox grpImageList;
    private System.Windows.Forms.Panel panelControls;
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
    private System.Windows.Forms.CheckBox chkUseOcr; // Added
    private System.Windows.Forms.CheckBox chkHideBrowser;
    private System.Windows.Forms.Button btnLaunchIsolated; // Unified button
    
    private System.Windows.Forms.DataGridView dgvImages;
    private System.Windows.Forms.DataGridViewTextBoxColumn colFileName;
    private System.Windows.Forms.DataGridViewTextBoxColumn colStatus;
    
    private System.Windows.Forms.Button btnStart;
    private System.Windows.Forms.Button btnStop;
    private System.Windows.Forms.Button btnReset;
    private System.Windows.Forms.Button btnRefresh;
    private System.Windows.Forms.ProgressBar progressBar;
    private System.Windows.Forms.Label lblProgress;
    
    private System.Windows.Forms.RichTextBox txtLog;
}
