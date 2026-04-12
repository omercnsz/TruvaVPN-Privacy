using System;

namespace TruvaDesktop.Models
{
    /// <summary>
    /// Nitro modunda izlenecek uygulamaların veri modeli.
    /// </summary>
    public class NitroApp
    {
        public string Name { get; set; } = string.Empty;
        public string ExeName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool IsProtected { get; set; } = false; // Blacklisted ise true (Vanguard vb.)

        // ListBox'ta düzgün görünmesi için Override
        public override string ToString() => Name;

        public override bool Equals(object? obj)
        {
            if (obj is NitroApp other)
                return ExeName.Equals(other.ExeName, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        public override int GetHashCode() => ExeName.ToLower().GetHashCode();
    }
}
