using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Models;

[Table("Vehicle")]
[Index("LicensePlate", Name = "UQ__Vehicle__F72CD56EA546ED93", IsUnique = true)]
public partial class Vehicle
{
    [Key]
    [Column("vehicle_id")]
    public int VehicleId { get; set; }

    [Column("license_plate")]
    [StringLength(20)]
    public string LicensePlate { get; set; } = null!;

    // Arabic: اجعليها Unicode لدعم العربية
    // English: Make it Unicode to support Arabic
    [Column("model")]
    [StringLength(50)]
    public string? Model { get; set; }

    [Column("year")]
    public int? Year { get; set; }

    [Column("color")]
    [StringLength(50)]
    public string? Color { get; set; }


    // Arabic: العمود في الداتابيس الجديدة driver_user_id
    // English: Column name in new DB is driver_user_id
    [Column("driver_user_id")]
    public int DriverUserId { get; set; }

    // Arabic: علاقة Involves كما هي
    // English: Involves relation as-is
    public virtual ICollection<Involve> Involves { get; set; } = new List<Involve>();
}
