using DockStudio.Core.Localization;

namespace DockStudio.UI
{
    /// <summary>Testi dell'interfaccia comune (Lang/*.json).</summary>
    public static class UiText
    {
        public static readonly Localizer Strings = new(typeof(UiText).Assembly, "DockStudio.UI.Lang.");
    }
}
