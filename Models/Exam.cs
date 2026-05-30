using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KhoaHoc.Models;

public partial class Exam
{
    [Key]
    [Column("ExamID")]
    public int ExamId { get; set; }

    [Column("CourseID")]
    public int? CourseId { get; set; }

    [StringLength(255)]
    public string? ExamTitle { get; set; }

    public int? DurationMinutes { get; set; }

    [Column(TypeName = "decimal(5, 2)")]
    public decimal? PassScore { get; set; }

    public int? Level { get; set; }

    /// <summary>Số lần làm tối đa (null = không giới hạn)</summary>
    public int? MaxAttempts { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>Phòng ban được làm bài kiểm tra (null = tất cả)</summary>
    [Column("TargetDepartmentId")]
    public int? TargetDepartmentId { get; set; }

    [Column("ModuleID")]
    public int? ModuleId { get; set; }

    [ForeignKey("CourseId")]
    [InverseProperty("Exams")]
    public virtual Course? Course { get; set; }

    [ForeignKey("ModuleId")]
    public virtual CourseModule? Module { get; set; }

    [InverseProperty("Exam")]
    public virtual ICollection<ExamQuestion> ExamQuestions { get; set; } = new List<ExamQuestion>();

    [InverseProperty("Exam")]
    public virtual ICollection<UserExam> UserExams { get; set; } = new List<UserExam>();
}
