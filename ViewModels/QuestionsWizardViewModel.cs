using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels
{
    public class QuestionsWizardViewModel
    {
        // Context
        public int AccidentId { get; set; }
        public int Role { get; set; }

        // Question Info
        public int QuestionId { get; set; }
        public string QuestionCode { get; set; } = "";
        public string QuestionTextAr { get; set; } = "";

        // Options
        public List<OptionItemViewModel> Options { get; set; } = new();

        [Required(ErrorMessage = "الرجاء اختيار إجابة.")]
        public string? SelectedOptionCode { get; set; }

        // Wizard Navigation
        public int Index { get; set; }   // 1-based
        public int Total { get; set; }

        public bool CanGoBack => Index > 1;
        public bool IsLast => Index >= Total;
    }
}