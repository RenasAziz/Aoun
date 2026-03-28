using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels
{
    public class UploadPhotosViewModel
    {
        public int AccidentId { get; set; }
        public int Role { get; set; } // 1 or 2


        // Arabic: صور موقع الضرر (2 صور)
        // English: Damage photos (2 photos)
        [Required(ErrorMessage = "الرجاء رفع صورة الضرر الأولى")]
        public IFormFile? DamagePhoto1 { get; set; }

        [Required(ErrorMessage = "الرجاء رفع صورة الضرر الثانية")]
        public IFormFile? DamagePhoto2 { get; set; }

        // Arabic: صور جوانب السيارة (4 صور)
        // English: Car sides (4 photos)
        [Required(ErrorMessage = "الرجاء رفع صورة الواجهة الأمامية")]
        public IFormFile? FrontPhoto { get; set; }

        [Required(ErrorMessage = "الرجاء رفع صورة الواجهة الخلفية")]
        public IFormFile? BackPhoto { get; set; }

        [Required(ErrorMessage = "الرجاء رفع صورة الجانب الأيسر")]
        public IFormFile? LeftPhoto { get; set; }

        [Required(ErrorMessage = "الرجاء رفع صورة الجانب الأيمن")]
        public IFormFile? RightPhoto { get; set; }

        // Arabic: صورة اللوحة
        // English: Plate photo
        [Required(ErrorMessage = "الرجاء رفع صورة لوحة السيارة")]
        public IFormFile? PlatePhoto { get; set; }

        // Arabic: صورة عامة لموقع الحادث
        // English: Wide scene photo
        [Required(ErrorMessage = "الرجاء رفع صورة عامة لموقع الحادث")]
        public IFormFile? ScenePhoto { get; set; }
    }
}
