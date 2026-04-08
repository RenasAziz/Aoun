namespace Aoun.ViewModels
{
    public class SinglePredictionResponse
    {
        public bool Success { get; set; }
        public string? PredictionType { get; set; }
        public string? Label { get; set; }
        public double Confidence { get; set; }
        public string? ModelName { get; set; }
        public string? Error { get; set; }
    }
}