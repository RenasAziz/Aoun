using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Models;

[Table("Driver")]
[Index("LicenseNumber", Name = "UQ__Driver__D482A0036CD618F0", IsUnique = true)]
public partial class Driver
{
    // Arabic: المفتاح الأساسي في الجدول الجديد هو user_id وليس driver_id
    // English: The primary key in the new schema is user_id (not driver_id)
    [Key]
    [Column("user_id")]
    public int UserId { get; set; }

    // Arabic: اسم السائق (يفضل جعله Unicode لدعم العربية)
    // English: Driver name (prefer Unicode to support Arabic)
    [Column("driver_name")]
    [StringLength(100)]
    public string DriverName { get; set; } = null!;

    // Arabic: رقم الرخصة
    // English: License number
    [Column("license_number")]
    [StringLength(50)]
    public string LicenseNumber { get; set; } = null!;

    // Arabic: علاقات (إن كانت لازالت موجودة عندك في الداتابيس)
    // English: Relationships (if still present in DB)
    [InverseProperty("Driver")]
    public virtual ICollection<DriverFeedback> DriverFeedbacks { get; set; } = new List<DriverFeedback>();

    [InverseProperty("Driver")]
    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();
}
