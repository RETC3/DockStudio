using DockStudio.UI.Windows;

namespace DockPost.Views
{
    /// <summary>Domanda di conferma con due pulsanti; ShowDialog&lt;bool&gt; restituisce true se si conferma.</summary>
    public sealed class ConfirmWindow : DockWindow
    {
        public ConfirmWindow(string title, string message, string confirm, string? detail = null) : base(AppInfo.Branding)
        {
            Title = title;
            Icon = AppInfo.Icon;
            var body = new System.Collections.Generic.List<Avalonia.Controls.Control> { Text(title, "heading"), Text(message) };
            if (detail != null) body.Add(Text(detail, "muted"));
            body.Add(Buttons(
                ButtonFor(DockPost.Text.T("common.cancel"), () => Close(false)),
                ButtonFor(confirm, () => Close(true), accent: true)));
            SetBody(body.ToArray());
        }
    }
}
