namespace HardwareTestApp
{
    partial class FormSplash
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
            if (disposing && (components != null))
            {
                components.Dispose();
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
            progressBar1 = new ProgressBar();
            lblStatus = new Label();
            SuspendLayout();
            // 
            // progressBar1
            // 
            progressBar1.Location = new Point(24, 118);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(312, 23);
            progressBar1.TabIndex = 0;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.ForeColor = SystemColors.ControlLight;
            lblStatus.Location = new Point(41, 66);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(38, 15);
            lblStatus.TabIndex = 1;
            lblStatus.Text = "label1";
            // 
            // FormSplash
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(64, 64, 64);
            ClientSize = new Size(365, 203);
            Controls.Add(lblStatus);
            Controls.Add(progressBar1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "FormSplash";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "FormSplash";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ProgressBar progressBar1;
        private Label lblStatus;
    }
}