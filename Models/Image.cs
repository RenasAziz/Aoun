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

    [Column("predicted_label")]
    public string? PredictedLabel { get; set; }

    [Column("prediction_confidence")]
    public double? PredictionConfidence { get; set; }

    [Column("prediction_model")]
    public string? PredictionModel { get; set; }

    [Column("prediction_date")]
    public DateTime? PredictionDate { get; set; }

    [Column("segmentation_result_path")]
    public string? SegmentationResultPath { get; set; }

    [Column("segmentation_model")]
    public string? SegmentationModel { get; set; }

    [Column("segmentation_date")]
    public DateTime? SegmentationDate { get; set; }

    [Column("segmentation_has_damage")]
    public bool? SegmentationHasDamage { get; set; }

    [InverseProperty("Image")]
    public virtual ICollection<ImageSegmentationDetection> ImageSegmentationDetections { get; set; }
        = new List<ImageSegmentationDetection>();

    public virtual Accident Accident { get; set; } = null!;
}
 