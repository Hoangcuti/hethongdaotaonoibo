using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KhoaHoc.Models;

[Table("QuestionBank")]
public partial class QuestionBank
{
    [Key]
    [Column("QuestionID")]
    public int QuestionId { get; set; }

    [Column("CategoryID")]
    public int? CategoryId { get; set; }

    public string? QuestionText { get; set; }

    [StringLength(50)]
    public string? Difficulty { get; set; }

    [StringLength(50)]
    public string? QuestionType { get; set; }

    [ForeignKey("CategoryId")]
    [InverseProperty("QuestionBanks")]
    public virtual Category? Category { get; set; }

    [InverseProperty("Question")]
    public virtual ICollection<ExamQuestion> ExamQuestions { get; set; } = new List<ExamQuestion>();

    [InverseProperty("Question")]
    public virtual ICollection<QuestionOption> QuestionOptions { get; set; } = new List<QuestionOption>();
}
