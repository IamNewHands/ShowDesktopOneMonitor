using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    internal class AboutForm : Form
    {
        private Panel bottomPanel;
        private Label lblStatus;
        private ProgressBar progressBar;
        private Button btnCheckUpdate;
        private Button btnClose;

        public AboutForm (bool justUpdated = false)
        {
            InitializeComponent();
            if (justUpdated) {
                SetUpdateSuccess();
            }
        }

        public void SetUpdateSuccess ()
        {
            lblStatus.Text = string.Format(Localization.UpdateCompleted, BuildInfo.Version);
            lblStatus.ForeColor = Color.FromArgb(16, 124, 65);
        }

        private void InitializeComponent ()
        {
            this.Text = Localization.AboutTitle;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(580, 560);
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            try {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch {
            }

            // Top Header Panel
            Panel topPanel = new Panel {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(0, 115),
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(16, 14, 16, 14)
            };

            PictureBox picIcon = new PictureBox {
                Size = new Size(48, 48),
                Location = new Point(16, 18),
                SizeMode = PictureBoxSizeMode.StretchImage
            };
            if (this.Icon != null) {
                picIcon.Image = this.Icon.ToBitmap();
            }

            Label lblTitle = new Label {
                Text = "ShowDesktopOneMonitor",
                Font = new Font("Segoe UI", 12.5F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            Label lblVersion = new Label {
                Text = Localization.CurrentVersion + "v" + BuildInfo.Version,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(90, 90, 90),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            LinkLabel linkGitHub = new LinkLabel {
                Text = "GitHub Repository",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0)
            };
            linkGitHub.LinkClicked += (s, e) =>
            {
                try {
                    Process.Start("https://github.com/IamNewHands/ShowDesktopOneMonitor");
                }
                catch { }
            };

            FlowLayoutPanel headerTextPanel = new FlowLayoutPanel {
                Location = new Point(74, 14),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            headerTextPanel.Controls.Add(lblTitle);
            headerTextPanel.Controls.Add(lblVersion);
            headerTextPanel.Controls.Add(linkGitHub);

            topPanel.Controls.Add(picIcon);
            topPanel.Controls.Add(headerTextPanel);

            // Bottom Action Panel
            bottomPanel = new Panel {
                Dock = DockStyle.Bottom,
                Height = 88,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(16, 8, 16, 8)
            };

            lblStatus = new Label {
                Text = "",
                Location = new Point(16, 8),
                Size = new Size(548, 24),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(60, 60, 60),
                AutoEllipsis = true
            };

            progressBar = new ProgressBar {
                Location = new Point(16, 46),
                Size = new Size(306, 22),
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };

            btnCheckUpdate = new Button {
                Text = Localization.CheckForUpdates,
                Size = new Size(136, 36),
                Location = new Point(338, 40),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCheckUpdate.FlatAppearance.BorderSize = 0;
            btnCheckUpdate.Click += OnCheckUpdateClick;

            btnClose = new Button {
                Text = Localization.Close,
                Size = new Size(84, 36),
                Location = new Point(482, 40),
                BackColor = Color.FromArgb(235, 236, 239),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => this.Close();

            bottomPanel.Controls.Add(lblStatus);
            bottomPanel.Controls.Add(progressBar);
            bottomPanel.Controls.Add(btnCheckUpdate);
            bottomPanel.Controls.Add(btnClose);

            // Content Panel (scrollable feature guide)
            Panel contentPanel = new Panel {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(16, 12, 16, 12),
                BackColor = Color.White
            };

            int y = 10;
            y = AddFeatureCard(contentPanel, y, Localization.FeatureWinDTitle, Localization.FeatureWinDDesc);
            y = AddFeatureCard(contentPanel, y, Localization.FeatureMoveTitle, Localization.FeatureMoveDesc);
            y = AddFeatureCard(contentPanel, y, Localization.FeatureAutoStartTitle, Localization.FeatureAutoStartDesc);
            y = AddFeatureCard(contentPanel, y, Localization.FeatureLogTitle, Localization.FeatureLogDesc);

            this.Controls.Add(contentPanel);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(topPanel);
        }

        // GDI+ draws the text at the monitor's DPI while this hand-built layout keeps its
        // 96-DPI pixel numbers: the 9.5 pt status line is 17 px tall at 96 DPI but 34 px at
        // 200%. WinForms does not rescale a hand-built layout in a PerMonitorV2 process -
        // the window is created on the primary monitor, so it never receives
        // WM_DPICHANGED - so the fixed 24 px status row cut the glyphs off at the bottom on
        // a 200% monitor. Size the rows of the bottom panel from the font they hold instead,
        // here, where the monitor the window landed on is known.
        protected override void OnHandleCreated (EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyBottomPanelMetrics();
        }

        private void ApplyBottomPanelMetrics ()
        {
            float statusLineHeight;
            float buttonLineHeight;
            using (Graphics g = Graphics.FromHwnd(this.Handle)) {
                statusLineHeight = lblStatus.Font.GetHeight(g);
                buttonLineHeight = btnCheckUpdate.Font.GetHeight(g);
            }

            // 24, 36, 88 and the offsets are the 96-DPI design values; the maxima keep the
            // dialog identical on a 100% monitor and only grow the rows that would clip.
            int statusHeight = Math.Max(24, (int)Math.Ceiling(statusLineHeight) + 6);
            int buttonHeight = Math.Max(36, (int)Math.Ceiling(buttonLineHeight) + 10);
            int buttonTop = lblStatus.Location.Y + statusHeight + 8;
            int progressTop = lblStatus.Location.Y + statusHeight + 14;
            int panelHeight = Math.Max(88, Math.Max(progressTop + progressBar.Height, buttonTop + buttonHeight) + 12);

            lblStatus.Size = new Size(lblStatus.Width, statusHeight);
            progressBar.Location = new Point(progressBar.Location.X, progressTop);
            btnCheckUpdate.Location = new Point(btnCheckUpdate.Location.X, buttonTop);
            btnCheckUpdate.Size = new Size(btnCheckUpdate.Width, buttonHeight);
            btnClose.Location = new Point(btnClose.Location.X, buttonTop);
            btnClose.Size = new Size(btnClose.Width, buttonHeight);
            bottomPanel.Height = panelHeight;
        }

        private int AddFeatureCard (Panel container, int y, string title, string desc)
        {
            Label lblCardTitle = new Label {
                Text = title,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                Location = new Point(16, y),
                AutoSize = true
            };
            container.Controls.Add(lblCardTitle);
            y += Math.Max(lblCardTitle.Height, lblCardTitle.PreferredHeight) + 6;

            Label lblCardDesc = new Label {
                Text = desc,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(20, y),
                Width = 510,
                AutoSize = true,
                MaximumSize = new Size(510, 0)
            };
            container.Controls.Add(lblCardDesc);
            y += Math.Max(lblCardDesc.Height, lblCardDesc.PreferredHeight) + 16;

            return y;
        }

        private async void OnCheckUpdateClick (object sender, EventArgs e)
        {
            btnCheckUpdate.Enabled = false;
            lblStatus.Text = Localization.CheckingUpdates;
            lblStatus.ForeColor = Color.FromArgb(60, 60, 60);

            UpdateCheckResult result = await AppUpdateManager.CheckForUpdatesAsync();

            if (!result.Success) {
                lblStatus.Text = Localization.UpdateFailed + ": " + result.ErrorMessage;
                lblStatus.ForeColor = Color.FromArgb(196, 43, 28);
                btnCheckUpdate.Enabled = true;
                return;
            }

            if (!result.HasUpdate) {
                lblStatus.Text = string.Format(Localization.AlreadyLatest + " (v{0})", BuildInfo.Version);
                lblStatus.ForeColor = Color.FromArgb(16, 124, 65);
                btnCheckUpdate.Enabled = true;
                return;
            }

            // New version found
            lblStatus.Text = string.Format(Localization.NewVersionFound, result.RemoteVersion);
            lblStatus.ForeColor = Color.FromArgb(0, 90, 190);

            string promptMsg = string.Format(Localization.NewVersionFound, result.RemoteVersion);
            DialogResult dr = MessageBox.Show(this, promptMsg, Localization.AboutTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (dr == DialogResult.Yes) {
                progressBar.Visible = true;
                progressBar.Value = 0;
                btnCheckUpdate.Enabled = false;
                btnClose.Enabled = false;

                AppUpdateManager.DownloadAndApplyUpdateAsync(
                    progress =>
                    {
                        if (this.IsHandleCreated && !this.IsDisposed) {
                            this.BeginInvoke((Action)(() =>
                            {
                                progressBar.Value = progress;
                                lblStatus.Text = string.Format(Localization.DownloadingUpdate, progress);
                            }));
                        }
                    },
                    error =>
                    {
                        if (this.IsHandleCreated && !this.IsDisposed) {
                            this.BeginInvoke((Action)(() =>
                            {
                                progressBar.Visible = false;
                                lblStatus.Text = Localization.UpdateFailed + ": " + error.Message;
                                lblStatus.ForeColor = Color.FromArgb(196, 43, 28);
                                btnCheckUpdate.Enabled = true;
                                btnClose.Enabled = true;
                            }));
                        }
                    }
                );
            }
            else {
                btnCheckUpdate.Enabled = true;
            }
        }
    }
}
