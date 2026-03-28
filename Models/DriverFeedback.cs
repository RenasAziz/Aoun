using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Models;

// Arabic: مفتاح مركب (accident_id + driver_user_id)
// English: Composite primary key (accident_id + driver_user_id)
[PrimaryKey(nameof(AccidentId), nameof(DriverUserId))]
[Table("Driver_Feedback")]
public partial class DriverFeedback
{
    [Column("accident_id")]
    public int AccidentId { get; set; }

    [Column("driver_user_id")]
    public int DriverUserId { get; set; }

    [Column("satisfaction_level")]
    public int? SatisfactionLevel { get; set; }

    [Column("comment")]
    public string? Comment { get; set; }

    [Column("feedback_date", TypeName = "datetime")]
    public DateTime? FeedbackDate { get; set; }

    // Arabic: علاقة مع Accident
    // English: Relation with Accident
    [ForeignKey(nameof(AccidentId))]
    [InverseProperty("DriverFeedbacks")]
    public virtual Accident Accident { get; set; } = null!;

    // Arabic: علاقة مع Driver عبر user_id
    // English: Relation with Driver via user_id
    [ForeignKey(nameof(DriverUserId))]
    [InverseProperty("DriverFeedbacks")]
    public virtual Driver Driver { get; set; } = null!;
}
