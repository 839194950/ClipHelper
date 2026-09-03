using System.ComponentModel;
using LocalClipboard.Infrastructure.Settings;

namespace LocalClipboard.App.UI;

internal sealed class ClearHistoryDialog : Form
{
    private readonly CheckBox includeFavoritesCheck = new();

    public ClearHistoryDialog(AppLanguage language = AppLanguage.Chinese)
    {
        bool english = language == AppLanguage.English;
        Text = english ? "Clear history" : "清空历史";
        ClientSize = new Size(390, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);

        var message = new Label
        {
            AutoSize = false,
            Location = new Point(22, 22),
            Size = new Size(346, 48),
            Text = english ? "Regular clipboard history will be permanently deleted. This cannot be undone." : "普通剪贴板历史将被永久删除。此操作无法撤销。"
        };
        includeFavoritesCheck.AutoSize = true;
        includeFavoritesCheck.Location = new Point(22, 82);
        includeFavoritesCheck.Text = english ? "Also delete favorites" : "同时删除收藏内容";
        includeFavoritesCheck.Checked = false;

        var clearButton = new Button
        {
            BackColor = Color.Firebrick,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Location = new Point(196, 137),
            Size = new Size(80, 32),
            Text = english ? "Clear" : "清空",
            UseVisualStyleBackColor = false
        };
        var cancelButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(288, 137),
            Size = new Size(80, 32),
            Text = english ? "Cancel" : "取消"
        };

        AcceptButton = clearButton;
        CancelButton = cancelButton;
        Controls.AddRange([message, includeFavoritesCheck, clearButton, cancelButton]);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IncludeFavorites => DialogResult == DialogResult.OK && includeFavoritesCheck.Checked;
}
