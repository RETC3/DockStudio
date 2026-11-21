using System;
using System.Globalization;

namespace DockSpace.Engine
{
    /// <summary>Dimensioni leggibili: "512 byte", "1,4 MB", "23,7 GB" (unità da 1024, come Esplora file).</summary>
    public static class Sizes
    {
        private static readonly string[] Units = { "KB", "MB", "GB", "TB", "PB" };

        public static string Format(long bytes, CultureInfo? culture = null)
        {
            culture ??= CultureInfo.CurrentCulture;
            if (bytes < 1024) return bytes.ToString("N0", culture) + " byte";
            double value = bytes;
            int unit = -1;
            while (value >= 1024 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            string format = value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
            return value.ToString(format, culture) + " " + Units[unit];
        }

        /// <summary>Percentuale con una cifra decimale ("12,3 %").</summary>
        public static string Percent(double fraction, CultureInfo? culture = null) =>
            (Math.Clamp(fraction, 0, 1) * 100).ToString("0.0", culture ?? CultureInfo.CurrentCulture) + " %";
    }
}
