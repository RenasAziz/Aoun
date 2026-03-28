using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aoun.Models
{
    [Table("Question")]
    public class Question
    {
        [Key]
        [Column("question_id")]
        public int QuestionId { get; set; }

        [Column("question_code")]
        [StringLength(30)]
        public string QuestionCode { get; set; } = "";

        [Column("question_type")]
        [StringLength(20)]
        public string QuestionType { get; set; } = "";

        [Column("question_text_ar")]
        public string QuestionTextAr { get; set; } = "";

        [Column("sort_order")]
        public int SortOrder { get; set; }

        // legacy column (اختياري تخليه لو موجود عندك)
        [Column("question_text")]
        public string? QuestionText { get; set; }
        
        [Column("pack_name")]
        [StringLength(100)]
        public string? PackName { get; set; }

        // Navigation
        public virtual ICollection<QuestionOption> Options { get; set; } = new List<QuestionOption>();
        public virtual ICollection<Answer> Answers { get; set; } = new List<Answer>();
    }
}