using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Models;

[PrimaryKey("AccidentId", "ImageId")]
[Table("Image")]
public partial class Image
{
    [Key]
    [Column("accident_id")]
    public int AccidentId { get; set; }

    [Key]
    [Column("image_id")]
    public int ImageId { get; set; }

    [Column("image_path")]
    [StringLength(255)]
    [Unicode(false)]
    public string? ImagePath { get; set; }

    [Column("upload_date", TypeName = "datetime")]
    public DateTime? UploadDate { get; set; }

    [Column("label")]
    [StringLength(100)]
    [Unicode(false)]
    public string? Label { get; set; }

    [ForeignKey("AccidentId")]
    [InverseProperty("Images")]

    [Column("driver_user_id")]
    public int? DriverUserId { get; set; }
    [ForeignKey("DriverUserId")]
    public virtual User? User { get; set; }

    public virtual Accident Accident { get; set; } = null!;
}
