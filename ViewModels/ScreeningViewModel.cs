using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels
{
    public class ScreeningViewModel
    {
        // Arabic: هل هناك إصابات؟
        // English: Any injuries?
        [Required]
        public bool? HasInjuries { get; set; }

        // Arabic: عدد المركبات (2 أو أكثر فقط مقبول)
        // English: Vehicles count (must be 2 or more)
        [Required]
        public string? VehiclesCount { get; set; } // "LessThanTwo" | "Two" | "MoreThanTwo"

        // Arabic: هل الطرفان موجودان في موقع الحادث؟
        // English: Are both parties present?
        [Required]
        public bool? BothPartiesPresent { get; set; }

        // Arabic: هل لدى أحد الأطراف تأمين ساري؟
        // English: Is there valid insurance for at least one party?
        [Required]
        public bool? HasValidInsurance { get; set; }
    }
}
