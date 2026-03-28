using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aoun.Models
{
    [Table("Answer")]
    public class Answer
    {
        // =========================
        // Composite Key
        // PK: (accident_id, driver_user_id, question_id)
        // =========================

        [Column("accident_id")]
        public int AccidentId { get; set; }

        [Column("driver_user_id")]
        public int DriverUserId { get; set; }

        [Column("question_id")]
        public int QuestionId { get; set; }

        // =========================
        // Core fields
        // =========================

        [Column("answered_at")]
        public DateTime? AnsweredAt { get; set; }

        // Arabic: كود الخيار المختار (المصدر الرئيسي للّوجيك)
        // English: Selected option code (main source for logic)
        [Column("selected_option_code")]
        [StringLength(50)]
        public string? SelectedOptionCode { get; set; }

        // Arabic: نص حر (اختياري لأسئلة مستقبلية)
        // English: Optional free text for future open questions
        [Column("free_text")]
        [StringLength(1000)]
        public string? FreeText { get; set; }

        // =========================
        // Legacy column (optional)
        // =========================

        // Arabic: عمود قديم (لو لسه موجود في DB)
        // English: Legacy response column (keep nullable)
        [Column("response")]
        [StringLength(255)]
        public string? Response { get; set; }

        // =========================
        // Navigation
        // =========================

        public virtual Accident Accident { get; set; } = null!;
        public virtual Question Question { get; set; } = null!;
    }
}