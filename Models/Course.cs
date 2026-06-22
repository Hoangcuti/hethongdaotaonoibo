using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KhoaHoc.Models;

[Index("CourseCode", Name = "UQ__Courses__FC00E000C4E3229A", IsUnique = true)]
public partial class Course
{
    [Key]
    [Column("CourseID")]
    public int CourseId { get; set; }

    [Column("CategoryID")]
    public int? CategoryId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? CourseCode { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    [StringLength(500)]
    public string? Thumbnail { get; set; }

    public bool? IsMandatory { get; set; }

    [StringLength(50)]
    public string? Status { get; set; }

    public int? Level { get; set; }

    public int? CreatedBy { get; set; }

    [Column("TargetDepartmentID")]
    public int? TargetDepartmentId { get; set; }

    [Column("OwnerDepartmentID")]
    public int? OwnerDepartmentId { get; set; }

    public bool? IsForAllDepartments { get; set; }

    [StringLength(255)]
    public string? TargetDepartmentIds { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? StartDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? EndDate { get; set; }

    [ForeignKey("CategoryId")]
    [InverseProperty("Courses")]
    public virtual Category? Category { get; set; }

    [InverseProperty("Course")]
    public virtual ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();

    [InverseProperty("Course")]
    public virtual CourseCost? CourseCost { get; set; }

    [InverseProperty("Course")]
    public virtual ICollection<CourseFeedback> CourseFeedbacks { get; set; } = new List<CourseFeedback>();

    [InverseProperty("Course")]
    public virtual ICollection<CourseModule> CourseModules { get; set; } = new List<CourseModule>();

    [ForeignKey("CreatedBy")]
    [InverseProperty("Courses")]
    public virtual User? CreatedByNavigation { get; set; }

    [ForeignKey("TargetDepartmentId")]
    [InverseProperty("TargetedCourses")]
    public virtual Department? TargetDepartment { get; set; }

    [ForeignKey("OwnerDepartmentId")]
    [InverseProperty("OwnedCourses")]
    public virtual Department? OwnerDepartment { get; set; }

    [InverseProperty("Course")]
    public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();

    [InverseProperty("Course")]
    public virtual ICollection<Exam> Exams { get; set; } = new List<Exam>();

    [InverseProperty("Course")]
    public virtual ICollection<OfflineTrainingEvent> OfflineTrainingEvents { get; set; } = new List<OfflineTrainingEvent>();

    [InverseProperty("Course")]
    public virtual ICollection<PathCourse> PathCourses { get; set; } = new List<PathCourse>();

    [InverseProperty("Course")]
    public virtual ICollection<TrainingAssignment> TrainingAssignments { get; set; } = new List<TrainingAssignment>();
}
