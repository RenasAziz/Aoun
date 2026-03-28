using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Models;

// Arabic: مفتاح مركب (accident_id + driver_user_id)
// English: Composite primary key (accident_id + driver_user_id)
[PrimaryKey(nameof(AccidentId), nameof(DriverUserId))]
public partial class Report
{
    [Column("accident_id")]
    public int AccidentId { get; set; }

    [Column("driver_user_id")]
    public int DriverUserId { get; set; }

    [Column("report_time", TypeName = "datetime")]
    public DateTime? ReportTime { get; set; }

    // Arabic: علاقة مع Accident
    // English: Relation with Accident
    [ForeignKey(nameof(AccidentId))]
    [InverseProperty("Reports")]
    public virtual Accident Accident { get; set; } = null!;

    // Arabic: علاقة مع Driver عبر user_id
    // English: Relation with Driver via user_id
    [ForeignKey(nameof(DriverUserId))]
    [InverseProperty("Reports")]
    public virtual Driver Driver { get; set; } = null!;
}
