using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aoun.Models
{
    [Table("ImageSegmentationDetection")]
    public class ImageSegmentationDetection
    {
        [Key]
        [Column("detection_id")]
        public int DetectionId { get; set; }

        [Column("accident_id")]
        public int AccidentId { get; set; }

        [Column("image_id")]
        public int ImageId { get; set; }

        [Column("damage_label")]
        public string? DamageLabel { get; set; }

        [Column("confidence")]
        public double? Confidence { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [ForeignKey(nameof(AccidentId) + "," + nameof(ImageId))]
        public virtual Image Image { get; set; } = null!;
    }
}