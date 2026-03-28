using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aoun.Models
{
    [Table("QuestionOption")]
    public class QuestionOption
    {
        [Key]
        [Column("option_id")]
        public int OptionId { get; set; }

        [Column("question_id")]
        public int QuestionId { get; set; }

        [Column("option_code")]
        [StringLength(50)]
        public string OptionCode { get; set; } = "";

        [Column("option_text_ar")]
        [StringLength(400)]
        public string OptionTextAr { get; set; } = "";

        [Column("sort_order")]
        public int SortOrder { get; set; }

        // Navigation
        public virtual Question Question { get; set; } = null!;
    }
}