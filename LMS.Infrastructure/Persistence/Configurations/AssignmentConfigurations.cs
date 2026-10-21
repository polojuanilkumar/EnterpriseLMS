using LMS.Domain.Entities;
using LMS.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace LMS.Infrastructure.Persistence.Configurations;

public sealed class AssignmentConfiguration:IEntityTypeConfiguration<LessonAssignment>
{
    public void Configure(EntityTypeBuilder<LessonAssignment> b)
    {
        b.ToTable("LessonAssignments",t=>t.HasCheckConstraint("CK_LessonAssignments_MaximumMarks","[MaximumMarks] > 0"));b.HasKey(x=>x.Id);
        b.Property(x=>x.Title).HasMaxLength(200).IsRequired();b.Property(x=>x.Instructions).HasMaxLength(20000).IsRequired();b.Property(x=>x.MaximumMarks).HasPrecision(18,2);
        b.HasOne<Lesson>().WithMany().HasForeignKey(x=>x.LessonId).OnDelete(DeleteBehavior.Restrict);b.HasIndex(x=>x.LessonId);
    }
}
public sealed class SubmissionConfiguration:IEntityTypeConfiguration<AssignmentSubmission>
{
    public void Configure(EntityTypeBuilder<AssignmentSubmission> b)
    {
        b.ToTable("AssignmentSubmissions",t=>{
            t.HasCheckConstraint("CK_AssignmentSubmissions_Content","([Text] IS NOT NULL AND LEN(LTRIM(RTRIM([Text]))) > 0) OR [AttachmentId] IS NOT NULL");
            t.HasCheckConstraint("CK_AssignmentSubmissions_Attachment","([AttachmentId] IS NULL AND [Filename] IS NULL AND [AttachmentSize] IS NULL) OR ([AttachmentId] IS NOT NULL AND [Filename] IS NOT NULL AND [AttachmentSize] IS NOT NULL AND [AttachmentSize] > 0 AND [AttachmentSize] <= 26214400)");
            t.HasCheckConstraint("CK_AssignmentSubmissions_MaximumMarks","[MaximumMarksSnapshot] > 0");
        });b.HasKey(x=>x.Id);b.Property(x=>x.Text).HasMaxLength(100000);b.Property(x=>x.InstructionsSnapshot).HasMaxLength(20000).IsRequired();b.Property(x=>x.Filename).HasMaxLength(200);b.Property(x=>x.MaximumMarksSnapshot).HasPrecision(18,2);
        b.HasIndex(x=>new{x.AssignmentId,x.UserId}).IsUnique().HasDatabaseName("IX_AssignmentSubmissions_AssignmentId_UserId");b.HasIndex(x=>x.AttachmentId).IsUnique().HasFilter("[AttachmentId] IS NOT NULL");
        b.HasOne<LessonAssignment>().WithMany().HasForeignKey(x=>x.AssignmentId).OnDelete(DeleteBehavior.Restrict);b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class GradeConfiguration:IEntityTypeConfiguration<AssignmentGrade>
{
    public void Configure(EntityTypeBuilder<AssignmentGrade> b)
    {
        b.ToTable("AssignmentGrades",t=>t.HasCheckConstraint("CK_AssignmentGrades_Marks","[Marks] >= 0"));b.HasKey(x=>x.Id);
        b.Property(x=>x.Marks).HasPrecision(18,2);b.Property(x=>x.Feedback).HasMaxLength(20000).IsRequired();
        b.HasIndex(x=>x.SubmissionId).IsUnique().HasDatabaseName("IX_AssignmentGrades_SubmissionId");
        b.HasOne<AssignmentSubmission>().WithMany().HasForeignKey(x=>x.SubmissionId).OnDelete(DeleteBehavior.Restrict);b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.GraderId).OnDelete(DeleteBehavior.Restrict);
    }
}
