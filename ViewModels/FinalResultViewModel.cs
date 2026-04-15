namespace Aoun.ViewModels
{
    public class FinalResultViewModel
    {
        public int AccidentId { get; set; }
        public int Role { get; set; }

        public string AccidentCode { get; set; } = "";
        public DateOnly? AccidentDate { get; set; }
        public TimeOnly? AccidentTime { get; set; }
        public string Location { get; set; } = "";

        public string RuleId { get; set; } = "";
        public string AccidentClassification { get; set; } = "";

        public int FaultPercentDriver1 { get; set; }
        public int FaultPercentDriver2 { get; set; }

        public decimal FinalConfidenceScore { get; set; }
        public string FinalConfidenceLabel { get; set; } = "";

        public string DecisionExplanation { get; set; } = "";
        public bool HasConflicts { get; set; }

        public string? Damage1PredictedLabel { get; set; }
        public double? Damage1PredictionConfidence { get; set; }

        public string? Damage2PredictedLabel { get; set; }
        public double? Damage2PredictionConfidence { get; set; }

        public string? Damage1SegmentationResultPath { get; set; }
        public bool? Damage1SegmentationHasDamage { get; set; }
        public List<SegmentationDetectionDisplayItem> Damage1SegmentationDetections { get; set; } = new();

        public string? Damage2SegmentationResultPath { get; set; }
        public bool? Damage2SegmentationHasDamage { get; set; }
        public List<SegmentationDetectionDisplayItem> Damage2SegmentationDetections { get; set; } = new();

        public bool HasDamageImages
            => !string.IsNullOrWhiteSpace(Damage1PredictedLabel)
            || !string.IsNullOrWhiteSpace(Damage2PredictedLabel)
            || Damage1SegmentationHasDamage == true
            || Damage2SegmentationHasDamage == true
            || !string.IsNullOrWhiteSpace(Damage1SegmentationResultPath)
            || !string.IsNullOrWhiteSpace(Damage2SegmentationResultPath);

        public int CurrentDriverFaultPercent
            => Role == 2 ? FaultPercentDriver2 : FaultPercentDriver1;

        public int OtherDriverFaultPercent
            => Role == 2 ? FaultPercentDriver1 : FaultPercentDriver2;

        public string CurrentDriverLabel
            => Role == 2 ? "الطرف الثاني (أنت)" : "الطرف الأول (أنت)";

        public string OtherDriverLabel
            => Role == 2 ? "الطرف الأول" : "الطرف الثاني";

        public string ConfidencePercentText
            => $"{FinalConfidenceScore * 100:0}%";

        public string FormattedDate
            => AccidentDate.HasValue ? AccidentDate.Value.ToString("yyyy-MM-dd") : "—";

        public string FormattedTime
            => AccidentTime.HasValue ? AccidentTime.Value.ToString("hh\\:mm") : "—";
    }

    public class SegmentationDetectionDisplayItem
    {
        public string Label { get; set; } = "";
        public double? Confidence { get; set; }
    }
}