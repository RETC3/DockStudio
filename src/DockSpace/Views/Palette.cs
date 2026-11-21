using Avalonia.Media;
using DockSpace.Engine;

namespace DockSpace.Views
{
    /// <summary>Colori delle famiglie di file: abbastanza pieni da leggersi sia sul tema chiaro sia su quello scuro.</summary>
    public static class Palette
    {
        public static Color For(FileCategory category) => category switch
        {
            FileCategory.Image => Color.Parse("#E8A33D"),
            FileCategory.Video => Color.Parse("#D9534F"),
            FileCategory.Audio => Color.Parse("#B565C9"),
            FileCategory.Document => Color.Parse("#3D8FE0"),
            FileCategory.Archive => Color.Parse("#8C6D4F"),
            FileCategory.Code => Color.Parse("#4FB06A"),
            FileCategory.Program => Color.Parse("#5B6BD6"),
            FileCategory.System => Color.Parse("#7F8C99"),
            _ => Color.Parse("#A3AAB3"),
        };

        public static IBrush Brush(FileCategory category) => new SolidColorBrush(For(category));

        public static string Key(FileCategory category) => "category." + category.ToString().ToLowerInvariant();
    }
}
