using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels
{
    public class DriverFeedbackViewModel
    {
        public int AccidentId { get; set; }
        public int Role { get; set; }

        [Required(ErrorMessage = "يرجى اختيار مستوى الرضا.")]
        [Range(1, 5, ErrorMessage = "يرجى اختيار مستوى رضا صحيح.")]
        public int? SatisfactionLevel { get; set; }

        [Required(ErrorMessage = "يرجى كتابة رأيك أو ملاحظاتك.")]
        [StringLength(1000, ErrorMessage = "التعليق طويل جدًا.")]
        public string? Comment { get; set; }
    }
}