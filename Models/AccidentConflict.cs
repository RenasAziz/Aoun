using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aoun.Models
{
    [Table("AccidentConflicts")]
    public class AccidentConflict
    {
        [Key]
        public int AccidentConflictId { get; set; }

        [Column("AccidentId")]
        public int AccidentId { get; set; }

        public Accident? Accident { get; set; }

        [Column("ConflictType")]
        public ConflictType ConflictType { get; set; }

        [Column("Severity")]
        public ConflictSeverity Severity { get; set; }

        [Column("Summary")]
        public string? Summary { get; set; }

        [Column("IsResolved")]
        public bool IsResolved { get; set; } = false;

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}