using System.Text.Json.Serialization;

namespace Aoun.ViewModels
{
    public class SegmentationPredictionResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("prediction_type")]
        public string? PredictionType { get; set; }

        [JsonPropertyName("has_damage")]
        public bool HasDamage { get; set; }

        [JsonPropertyName("result_image_url")]
        public string? ResultImageUrl { get; set; }

        [JsonPropertyName("model_name")]
        public string? ModelName { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("detections")]
        public List<SegmentationDetectionItem> Detections { get; set; } = new();
    }

    public class SegmentationDetectionItem
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }
}