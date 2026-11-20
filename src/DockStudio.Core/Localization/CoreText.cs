namespace DockStudio.Core.Localization
{
    /// <summary>Testi del nucleo (Lang/*.json), condivisi da tutte le app della suite.</summary>
    public static class CoreText
    {
        public static readonly Localizer Strings = new(typeof(CoreText).Assembly, "DockStudio.Core.Lang.");
    }
}
