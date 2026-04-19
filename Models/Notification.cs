using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aoun.Models
{
    [Table("Notifications")]
    public class Notification
    {
        [Key]
        [Column("notification_id")]
        public int NotificationId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Required]
        [StringLength(150)]
        [Column("title")]
        public string Title { get; set; } = null!;

        [Required]
        [StringLength(500)]
        [Column("message")]
        public string Message { get; set; } = null!;

        [StringLength(50)]
        [Column("type")]
        public string? Type { get; set; }

        [Column("reference_id")]
        public int? ReferenceId { get; set; }

        [Column("is_read")]
        public bool IsRead { get; set; }

        [Column("created_at", TypeName = "datetime")]
        public DateTime CreatedAt { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
    }
}