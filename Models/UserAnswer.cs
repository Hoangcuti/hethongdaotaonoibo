using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KhoaHoc.Models;

[PrimaryKey(nameof(UserExamId), nameof(QuestionId))]
public partial class UserAnswer
{
    [Key]
    [Column("UserExamID")]
    public int UserExamId { get; set; }

    [Key]
    [Column("QuestionID")]
    public int QuestionId { get; set; }

    [Column("OptionID")]
    public int? OptionId { get; set; }

    public bool? IsCorrect { get; set; }

    public string? TextResponse { get; set; }

    [ForeignKey("UserExamId")]
    public virtual UserExam? UserExam { get; set; }
}
