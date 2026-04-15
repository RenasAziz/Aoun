using System;
using System.Collections.Generic;

namespace Aoun.ViewModels
{
    public class InspectorReportsIndexViewModel
    {
        public string? Search { get; set; }
        public string? StatusFilter { get; set; }

        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int AcceptedCount { get; set; }
        public int RejectedCount { get; set; }

        public List<InspectorReportListItemViewModel> Reports { get; set; } = new();
    }

    public class InspectorReportListItemViewModel
    {
        public int AccidentId { get; set; }
        public int ReportId { get; set; }

        public string AccidentCode => $"ACC-{AccidentId:000000}";

        public string Status { get; set; } = "";
        public string AccidentClassification { get; set; } = "";
        public string Location { get; set; } = "—";

        public DateOnly? AccidentDate { get; set; }
        public TimeOnly? AccidentTime { get; set; }

        public int FaultPercentDriver1 { get; set; }
        public int FaultPercentDriver2 { get; set; }

        public decimal FinalConfidenceScore { get; set; }
        public string FinalConfidenceLabel { get; set; } = "";

        public string FormattedDate
            => AccidentDate.HasValue ? AccidentDate.Value.ToString("yyyy-MM-dd") : "—";

        public string FormattedTime
            => AccidentTime.HasValue ? AccidentTime.Value.ToString("hh\\:mm") : "—";

        public string ConfidencePercentText
            => $"{FinalConfidenceScore * 100:0}%";
    }

    public class InspectorReportDetailsViewModel
    {
        public int AccidentId { get; set; }
        public int ReportId { get; set; }

        public string AccidentCode => $"ACC-{AccidentId:000000}";

        public string ApprovalStatus { get; set; } = "";
        public string? InspectorNote { get; set; }

        public DateTime? ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string? ReviewedByName { get; set; }

        public string Location { get; set; } = "—";
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        public DateOnly? AccidentDate { get; set; }
        public TimeOnly? AccidentTime { get; set; }
        public string AccidentType { get; set; } = "—";
        public string AccidentStatus { get; set; } = "—";

        public string RuleId { get; set; } = "—";
        public string AccidentClassification { get; set; } = "—";
        public int FaultPercentDriver1 { get; set; }
        public int FaultPercentDriver2 { get; set; }

        public decimal BaseConfidenceScore { get; set; }
        public decimal ConflictPenaltyScore { get; set; }
        public decimal EvidenceBonusScore { get; set; }
        public decimal FinalConfidenceScore { get; set; }
        public string FinalConfidenceLabel { get; set; } = "—";
        public string DecisionExplanation { get; set; } = "—";

        public InspectorPartyDetailsViewModel? Party1 { get; set; }
        public InspectorPartyDetailsViewModel? Party2 { get; set; }

        public List<InspectorAnswerCompareItemViewModel> CoreAnswers { get; set; } = new();
        public List<InspectorAnswerCompareItemViewModel> MirrorAnswers { get; set; } = new();
        public List<InspectorAnswerCompareItemViewModel> ConflictBackAnswers { get; set; } = new();

        public List<InspectorConflictItemViewModel> Conflicts { get; set; } = new();

        public List<InspectorImageItemViewModel> Party1Images { get; set; } = new();
        public List<InspectorImageItemViewModel> Party2Images { get; set; } = new();

        public string FormattedDate
            => AccidentDate.HasValue ? AccidentDate.Value.ToString("yyyy-MM-dd") : "—";

        public string FormattedTime
            => AccidentTime.HasValue ? AccidentTime.Value.ToString("hh\\:mm") : "—";

        public string ConfidencePercentText
            => $"{FinalConfidenceScore * 100:0}%";
    }

    public class InspectorPartyDetailsViewModel
    {
        public int UserId { get; set; }
        public byte Role { get; set; }

        public string PartyLabel => Role == 1 ? "الطرف الأول" : "الطرف الثاني";

        public string Name { get; set; } = "—";
        public string Email { get; set; } = "—";
        public string PhoneNumber { get; set; } = "—";
        public string LicenseNumber { get; set; } = "—";

        public string VehiclePlate { get; set; } = "—";
        public string VehicleModel { get; set; } = "—";
        public string VehicleColor { get; set; } = "—";
        public int? VehicleYear { get; set; }

        public string? FreeText { get; set; }
    }

    public class InspectorAnswerCompareItemViewModel
    {
        public string QuestionCode { get; set; } = "";
        public string QuestionTextAr { get; set; } = "";
        public string QuestionType { get; set; } = "";

        public string? PackName { get; set; }

        public string Driver1AnswerCode { get; set; } = "—";
        public string Driver2AnswerCode { get; set; } = "—";

        public string Driver1FreeText { get; set; } = "";
        public string Driver2FreeText { get; set; } = "";
    }

    public class InspectorConflictItemViewModel
    {
        public int AccidentConflictId { get; set; }
        public string ConflictType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Summary { get; set; } = "";
        public bool IsResolved { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class InspectorImageItemViewModel
    {
        public int ImageId { get; set; }
        public int? DriverUserId { get; set; }

        public string Label { get; set; } = "";
        public string ImagePath { get; set; } = "";

        public string? PredictedLabel { get; set; }
        public double? PredictionConfidence { get; set; }
        public string? PredictionModel { get; set; }

        public DateTime? UploadDate { get; set; }

        public string PredictionConfidenceText
            => PredictionConfidence.HasValue ? $"{PredictionConfidence.Value * 100:0.0}%" : "—";
    }

    public class InspectorReviewInputViewModel
    {
        public int AccidentId { get; set; }

        public string ApprovalStatus { get; set; } = "";
        public string? InspectorNote { get; set; }
    }
}