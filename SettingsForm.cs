using System;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public class SettingsForm : Form
    {
        private readonly TextBox baseUrl;
        private readonly TextBox apiKey;
        private readonly TextBox model;
        private readonly NumericUpDown maxTokens;
        private readonly NumericUpDown timeout;

        public PluginSettings Settings { get; private set; }

        public SettingsForm(PluginSettings settings)
        {
            Settings = Copy(settings);

            Text = "MusicBee AI Agent Settings (Alpha)";
            Width = 520;
            Height = 300;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            int labelWidth = 130;
            Label alpha = new Label();
            alpha.Text = "ALPHA: experimental agent. Review every action before confirming.";
            alpha.Left = 16;
            alpha.Top = 14;
            alpha.Width = 458;
            alpha.Height = 32;
            alpha.ForeColor = Color.DarkRed;
            Controls.Add(alpha);

            int top = 56;

            baseUrl = AddTextBox("Base URL", Settings.BaseUrl, labelWidth, ref top);
            apiKey = AddTextBox("API Key", Settings.ApiKey, labelWidth, ref top);
            apiKey.PasswordChar = '*';
            model = AddTextBox("Model", Settings.Model, labelWidth, ref top);
            maxTokens = AddNumber("Max tokens", 1, 1000000, 0, Settings.MaxTokens, labelWidth, ref top);
            timeout = AddNumber("Timeout seconds", 5, 300, 0, Settings.RequestTimeoutSeconds, labelWidth, ref top);

            Button ok = new Button();
            ok.Text = "OK";
            ok.Left = 294;
            ok.Top = top + 8;
            ok.Width = 86;
            ok.Click += OkClicked;

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Left = 388;
            cancel.Top = top + 8;
            cancel.Width = 86;
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private TextBox AddTextBox(string text, string value, int labelWidth, ref int top)
        {
            Controls.Add(AddLabel(text, labelWidth, top));
            TextBox box = new TextBox();
            box.Left = labelWidth + 20;
            box.Top = top - 3;
            box.Width = 320;
            box.Text = value ?? "";
            Controls.Add(box);
            top += 34;
            return box;
        }

        private NumericUpDown AddNumber(string text, decimal min, decimal max, int decimals, decimal value, int labelWidth, ref int top)
        {
            Controls.Add(AddLabel(text, labelWidth, top));
            NumericUpDown box = new NumericUpDown();
            box.Left = labelWidth + 20;
            box.Top = top - 3;
            box.Width = 120;
            box.Minimum = min;
            box.Maximum = max;
            box.DecimalPlaces = decimals;
            box.Increment = decimals == 0 ? 1 : (decimal)0.1;
            box.Value = Math.Min(max, Math.Max(min, value));
            Controls.Add(box);
            top += 34;
            return box;
        }

        private Label AddLabel(string text, int labelWidth, int top)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = 16;
            label.Top = top;
            label.Width = labelWidth;
            label.Height = 20;
            label.TextAlign = ContentAlignment.MiddleRight;
            return label;
        }

        private void OkClicked(object sender, EventArgs e)
        {
            Settings.BaseUrl = baseUrl.Text.Trim();
            Settings.ApiKey = apiKey.Text;
            Settings.Model = model.Text.Trim();
            Settings.MaxTokens = (int)maxTokens.Value;
            Settings.RequestTimeoutSeconds = (int)timeout.Value;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static PluginSettings Copy(PluginSettings source)
        {
            PluginSettings copy = new PluginSettings();
            if (source == null)
            {
                return copy;
            }

            copy.BaseUrl = source.BaseUrl;
            copy.ApiKey = source.ApiKey;
            copy.Model = source.Model;
            copy.MaxTokens = source.MaxTokens;
            copy.RequestTimeoutSeconds = source.RequestTimeoutSeconds;
            copy.DockPanelTarget = source.DockPanelTarget;
            return copy;
        }
    }
}
