using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Models;

[Table("Accident_Report")]
[Index("AccidentId", Name = "UQ__Accident__A27CA62AE269DDD4", IsUnique = true)]
public partial class AccidentReport
{
    [Key]
    [Column("report_id")]
    public int ReportId { get; set; }

    [Column("fault_percent_driver1")]
    public int? FaultPercentDriver1 { get; set; }

    [Column("fault_percent_driver2")]
    public int? FaultPercentDriver2 { get; set; }

    [Column("created_at", TypeName = "datetime")]
    public DateTime? CreatedAt { get; set; }

    [Column("approval_status")]
    [StringLength(50)]
    [Unicode(false)]
    public string? ApprovalStatus { get; set; }

    [Column("pdf_path")]
    [StringLength(255)]
    [Unicode(false)]
    public string? PdfPath { get; set; }

    [Column("summary")]
    [Unicode(false)]
    public string? Summary { get; set; }

    [Column("accident_id")]
    public int AccidentId { get; set; }

    // =========================
    // New rule engine fields
    // =========================

    [Column("rule_id")]
    [StringLength(20)]
    [Unicode(false)]
    public string? RuleId { get; set; }

    [Column("accident_classification")]
    [StringLength(200)]
    public string? AccidentClassification { get; set; }

    [Column("base_confidence_score", TypeName = "decimal(4,2)")]
    public decimal? BaseConfidenceScore { get; set; }

    [Column("conflict_penalty_score", TypeName = "decimal(4,2)")]
    public decimal? ConflictPenaltyScore { get; set; }

    [Column("evidence_bonus_score", TypeName = "decimal(4,2)")]
    public decimal? EvidenceBonusScore { get; set; }

    [Column("final_confidence_score", TypeName = "decimal(4,2)")]
    public decimal? FinalConfidenceScore { get; set; }

    [Column("final_confidence_label")]
    [StringLength(50)]
    [Unicode(false)]
    public string? FinalConfidenceLabel { get; set; }

    [Column("decision_explanation")]
    public string? DecisionExplanation { get; set; }

    [ForeignKey("AccidentId")]
    [InverseProperty("AccidentReport")]
    public virtual Accident Accident { get; set; } = null!;
}