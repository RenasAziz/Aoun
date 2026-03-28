using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aoun.Models
{
    [Table("AccidentSessionParticipants")]
    public class AccidentSessionParticipant
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("accident_id")]
        public int AccidentId { get; set; }

        [Column("driver_user_id")]
        public int DriverUserId { get; set; }

        [Column("role")]
        public byte Role { get; set; } // 1 or 2

        [Column("is_joined")]
        public bool IsJoined { get; set; } = true;

        [Column("joined_at")]
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        [Column("vehicle_id")]
        public int? VehicleId { get; set; }

        [Column("current_step")]
        [StringLength(50)]
        public string CurrentStep { get; set; } = "Waiting";

        [Column("is_completed")]
        public bool IsCompleted { get; set; } = false;

        // Navigation (اختياري)
        public Accident Accident { get; set; } = null!;
    }
}
