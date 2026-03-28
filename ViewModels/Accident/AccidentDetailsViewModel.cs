namespace Aoun.ViewModels.Accident
{
    public class AccidentDetailsViewModel
    {
        public int AccidentId { get; set; }

        public string AccidentNumber { get; set; } = string.Empty;

        public DateOnly? AccidentDate { get; set; }

        public TimeOnly? AccidentTime { get; set; }

        public string? Location { get; set; }

        public string? AccidentType { get; set; }

        public string Status { get; set; } = "قيد المراجعة";

        public int FaultPercentage { get; set; }

        public List<string> ImagePaths { get; set; } = new();
    }
}
