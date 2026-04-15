using System.Text.Json.Serialization;

namespace Aoun.ViewModels
{
    public class SinglePredictionResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("prediction_type")]
        public string? PredictionType { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("model_name")]
        public string? ModelName { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}