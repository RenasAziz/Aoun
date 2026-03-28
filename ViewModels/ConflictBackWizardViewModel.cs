using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels
{
    public class ConflictBackWizardViewModel
    {
        // Context
        public int AccidentId { get; set; }
        public int Role { get; set; }

        // Pack info
        public string PackName { get; set; } = "";

        // Question info
        public int QuestionId { get; set; }
        public string QuestionCode { get; set; } = "";
        public string QuestionTextAr { get; set; } = "";

        // Options
        public List<OptionItemViewModel> Options { get; set; } = new();

        [Required(ErrorMessage = "الرجاء اختيار إجابة.")]
        public string? SelectedOptionCode { get; set; }

        // Wizard
        public int Index { get; set; }
        public int Total { get; set; }

        public bool CanGoBack => Index > 1;
        public bool IsLast => Index >= Total;
    }
}