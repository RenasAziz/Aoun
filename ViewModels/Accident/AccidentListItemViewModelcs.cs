namespace Aoun.ViewModels.Accident
{
    public class AccidentListItemViewModel
    {
        public int AccidentId { get; set; }

        public string AccidentNumber { get; set; } = string.Empty;

        public DateOnly? AccidentDate { get; set; }

        public int FaultPercentage { get; set; }

        public string Status { get; set; } = "قيد المراجعة";

        public string StatusCssClass { get; set; } = "st-pending";

        public string AccidentClassification { get; set; } = string.Empty;
    }
}
