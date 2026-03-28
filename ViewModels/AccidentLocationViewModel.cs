using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels
{
    public class AccidentLocationViewModel
    {
        // Arabic: نص الموقع المعروض (مثلاً: جدة - شارع...)
        // English: Display address text
        [Required]
        public string LocationText { get; set; } = null!;

        // Arabic: إحداثيات (اختياري لكن مفيد)
        // English: Coordinates
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // Arabic: التاريخ بصيغة ISO للتخزين (yyyy-MM-dd)
        // English: ISO date for storing
        [Required]
        public string AccidentDateIso { get; set; } = null!;

        // Arabic: الوقت بصيغة ISO للتخزين (HH:mm:ss)
        // English: ISO time for storing
        [Required]
        public string AccidentTimeIso { get; set; } = null!;

        // Arabic: للعرض فقط (dd/MM/yyyy)
        // English: for display only
        public string? AccidentDateDisplay { get; set; }

        // Arabic: للعرض فقط (hh:mm ص/م)
        // English: for display only
        public string? AccidentTimeDisplay { get; set; }
    }
}
