using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    internal class AboutForm : Form
    {
        private Label lblStatus;
        private ProgressBar progressBar;
        private Button btnCheckUpdate;
        private Button btnClose;

        public AboutForm ()
        {
            InitializeComponent();
        }

        private void InitializeComponent ()
        {
            this.Text = Localization.AboutTitle;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(580, 550);
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
            Panel bottomPanel = new Panel {
                Dock = DockStyle.Bottom,
                Height = 70,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(16, 10, 16, 10)
            };

            lblStatus = new Label {
                Text = "",
                Location = new Point(16, 16),
                Size = new Size(310, 20),
                ForeColor = Color.FromArgb(70, 70, 70),
                AutoEllipsis = true
            };

            progressBar = new ProgressBar {
                Location = new Point(16, 40),
                Size = new Size(310, 14),
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };

            btnCheckUpdate = new Button {
                Text = Localization.CheckForUpdates,
                Size = new Size(136, 38),
                Location = new Point(338, 16),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCheckUpdate.FlatAppearance.BorderSize = 0;
            btnCheckUpdate.Click += OnCheckUpdateClick;

            btnClose = new Button {
                Text = Localization.Close,
                Size = new Size(84, 38),
                Location = new Point(482, 16),
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
            lblStatus.ForeColor = Color.FromArgb(70, 70, 70);

            UpdateCheckResult result = await AppUpdateManager.CheckForUpdatesAsync();

            if (!result.Success) {
                lblStatus.Text = Localization.UpdateFailed + ": " + result.ErrorMessage;
                lblStatus.ForeColor = Color.FromArgb(200, 30, 30);
                btnCheckUpdate.Enabled = true;
                return;
            }

            if (!result.HasUpdate) {
                lblStatus.Text = string.Format(Localization.AlreadyLatest + " (v{0})", BuildInfo.Version);
                lblStatus.ForeColor = Color.FromArgb(0, 130, 50);
                btnCheckUpdate.Enabled = true;
                return;
            }

            // New version found
            lblStatus.Text = string.Format(Localization.NewVersionFound, result.RemoteVersion);
            lblStatus.ForeColor = Color.FromArgb(0, 102, 204);

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
                                lblStatus.ForeColor = Color.FromArgb(200, 30, 30);
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
