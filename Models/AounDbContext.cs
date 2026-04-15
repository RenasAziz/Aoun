using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Aoun.Models;


namespace Aoun.Models;

public partial class AounDbContext : DbContext
{
    public AounDbContext()
    {
    }

    public AounDbContext(DbContextOptions<AounDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Accident> Accidents { get; set; }
    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<AccidentSessionParticipant> AccidentSessionParticipants { get; set; }

    public virtual DbSet<AccidentReport> AccidentReports { get; set; }

    public virtual DbSet<Answer> Answers { get; set; }

    public virtual DbSet<Driver> Drivers { get; set; }

    public virtual DbSet<DriverFeedback> DriverFeedbacks { get; set; }

    public virtual DbSet<Image> Images { get; set; }

    public virtual DbSet<Question> Questions { get; set; }

    public virtual DbSet<Report> Reports { get; set; }

    public virtual DbSet<Vehicle> Vehicles { get; set; }
    public virtual DbSet<Involve> Involves { get; set; }
    public virtual DbSet<QuestionOption> QuestionOptions { get; set; }
    public DbSet<AccidentConflict> AccidentConflicts { get; set; }
    public virtual DbSet<ImageSegmentationDetection> ImageSegmentationDetections { get; set; }



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("SQL_Latin1_General_CP1_CI_AS");

        modelBuilder.Entity<Accident>(entity =>
        {
            entity.HasKey(e => e.AccidentId).HasName("PK__Accident__A27CA62BCAA43B20");

          
        });

        modelBuilder.Entity<AccidentReport>(entity =>
        {
            entity.HasKey(e => e.ReportId).HasName("PK__Accident__779B7C58890D5B1E");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");

            entity.Property(e => e.ApprovalStatus)
                  .HasMaxLength(50)
                  .IsUnicode(true)
                  .HasDefaultValue("قيد المراجعة");

            entity.Property(e => e.InspectorNote)
                  .HasColumnName("inspector_note")
                  .HasMaxLength(1000)
                  .IsUnicode(true);

            entity.Property(e => e.ReviewedAt)
                  .HasColumnName("reviewed_at")
                  .HasColumnType("datetime");

            entity.Property(e => e.ReviewedByUserId)
                  .HasColumnName("reviewed_by_user_id");

            entity.HasOne(d => d.Accident)
                .WithOne(p => p.AccidentReport)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Report_Accident");

            entity.HasOne(d => d.ReviewedByUser)
                .WithMany()
                .HasForeignKey(d => d.ReviewedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AccidentReport_ReviewedByUser");
        });


        modelBuilder.Entity<Driver>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Driver__..."); // الاسم لا يهم كثير

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.DriverName).HasColumnName("driver_name");
            entity.Property(e => e.LicenseNumber).HasColumnName("license_number");

            // Arabic: دعم العربية لاسم السائق (اختياري لكنه مفيد)
            // English: Arabic support for DriverName (optional but recommended)
            entity.Property(e => e.DriverName).IsUnicode(true);
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.HasKey(e => e.VehicleId).HasName("PK__Vehicle__...");

            entity.Property(e => e.DriverUserId).HasColumnName("driver_user_id");

            // Arabic: دعم العربية للموديل (اختياري لكنه مفيد)
            // English: Arabic support for Model (optional but recommended)
            entity.Property(e => e.Model).IsUnicode(true);
            entity.Property(e => e.Color).IsUnicode(true);

        });


        modelBuilder.Entity<DriverFeedback>(entity =>
        {
            entity.Property(e => e.FeedbackDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Accident).WithMany(p => p.DriverFeedbacks)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Feedback_Accident");

            entity.HasOne(d => d.Driver).WithMany(p => p.DriverFeedbacks)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Feedback_Driver");
        });

        modelBuilder.Entity<Image>(entity =>
        {
            entity.Property(e => e.ImageId).ValueGeneratedOnAdd();
            entity.Property(e => e.UploadDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Accident).WithMany(p => p.Images)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Image_Accident");
        });

        modelBuilder.Entity<Question>(entity =>
        {
            entity.ToTable("Question");

            entity.HasKey(e => e.QuestionId).HasName("PK__Question__2EC215495087E83F");

            entity.Property(e => e.QuestionId).HasColumnName("question_id");

            entity.Property(e => e.QuestionCode)
                  .HasColumnName("question_code")
                  .HasMaxLength(30)
                  .IsUnicode(true);

            entity.Property(e => e.QuestionType)
                  .HasColumnName("question_type")
                  .HasMaxLength(20)
                  .IsUnicode(true);

            entity.Property(e => e.QuestionTextAr)
                  .HasColumnName("question_text_ar")
                  .IsUnicode(true);

            entity.Property(e => e.SortOrder)
                  .HasColumnName("sort_order");

            entity.Property(e => e.PackName)
      .HasColumnName("pack_name")
      .HasMaxLength(100)
      .IsUnicode(true);
        });

        modelBuilder.Entity<Report>(entity =>
        {
            entity.Property(e => e.ReportTime).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Accident).WithMany(p => p.Reports)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Reports_Accident");

            entity.HasOne(d => d.Driver).WithMany(p => p.Reports)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Reports_Driver");
        });

    

        OnModelCreatingPartial(modelBuilder);

        modelBuilder.Entity<QuestionOption>(entity =>
        {
            entity.ToTable("QuestionOption");

            entity.HasKey(e => e.OptionId);

            entity.Property(e => e.OptionId).HasColumnName("option_id");

            entity.Property(e => e.QuestionId).HasColumnName("question_id");

            entity.Property(e => e.OptionCode)
                  .HasColumnName("option_code")
                  .HasMaxLength(50)
                  .IsUnicode(true);

            entity.Property(e => e.OptionTextAr)
                  .HasColumnName("option_text_ar")
                  .HasMaxLength(400)
                  .IsUnicode(true);

            entity.Property(e => e.SortOrder).HasColumnName("sort_order");

            entity.HasOne(d => d.Question)
                  .WithMany(p => p.Options)
                  .HasForeignKey(d => d.QuestionId)
                  .OnDelete(DeleteBehavior.Cascade)
                  .HasConstraintName("FK_QuestionOption_Question");
        });

        modelBuilder.Entity<Involve>(entity =>
        {
            entity.HasKey(e => new { e.AccidentId, e.VehicleId });

            entity.ToTable("Involves");

            entity.Property(e => e.AccidentId).HasColumnName("accident_id");
            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");
            entity.Property(e => e.VehicleRole).HasColumnName("vehicle_role");

            entity.HasOne(d => d.Accident)
                  .WithMany(p => p.Involves)
                  .HasForeignKey(d => d.AccidentId)
                  .OnDelete(DeleteBehavior.ClientSetNull)
                  .HasConstraintName("FK_Involves_Accident");

            entity.HasOne(d => d.Vehicle)
                  .WithMany(p => p.Involves)
                  .HasForeignKey(d => d.VehicleId)
                  .OnDelete(DeleteBehavior.ClientSetNull)
                  .HasConstraintName("FK_Involves_Vehicle");
        });


        modelBuilder.Entity<ImageSegmentationDetection>(entity =>
        {
            entity.HasKey(e => e.DetectionId);

            entity.HasOne(d => d.Image)
                .WithMany(p => p.ImageSegmentationDetections)
                .HasForeignKey(d => new { d.AccidentId, d.ImageId })
                .OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<AccidentSessionParticipant>(entity =>
        {
            entity.ToTable("AccidentSessionParticipants");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccidentId).HasColumnName("accident_id");
            entity.Property(e => e.DriverUserId).HasColumnName("driver_user_id");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.IsJoined).HasColumnName("is_joined").HasDefaultValue(true);
            entity.Property(e => e.JoinedAt).HasColumnName("joined_at").HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");
            entity.Property(e => e.CurrentStep).HasColumnName("current_step").HasMaxLength(50).HasDefaultValue("Waiting");
            entity.Property(e => e.IsCompleted).HasColumnName("is_completed").HasDefaultValue(false);

            entity.HasOne(e => e.Accident)
                  .WithMany() // إذا ما تبين تضيفين Navigation داخل Accident
                  .HasForeignKey(e => e.AccidentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<Answer>(entity =>
        {
            entity.ToTable("Answer");

            entity.HasKey(e => new { e.AccidentId, e.DriverUserId, e.QuestionId });

            entity.Property(e => e.AccidentId).HasColumnName("accident_id");
            entity.Property(e => e.DriverUserId).HasColumnName("driver_user_id");
            entity.Property(e => e.QuestionId).HasColumnName("question_id");

            entity.Property(e => e.AnsweredAt)
                  .HasColumnName("answered_at")
                  .HasDefaultValueSql("(sysdatetime())");

            entity.Property(e => e.SelectedOptionCode)
                  .HasColumnName("selected_option_code")
                  .HasMaxLength(50)
                  .IsUnicode(true);

            entity.Property(e => e.FreeText)
                  .HasColumnName("free_text")
                  .HasMaxLength(1000)
                  .IsUnicode(true);

            entity.Property(e => e.Response)
                  .HasColumnName("response")
                  .HasMaxLength(255)
                  .IsUnicode(true);

            entity.HasOne(d => d.Accident)
                  .WithMany(p => p.Answers)
                  .HasForeignKey(d => d.AccidentId)
                  .OnDelete(DeleteBehavior.ClientSetNull)
                  .HasConstraintName("FK_Answer_Accident");

            entity.HasOne(d => d.Question)
                  .WithMany(p => p.Answers)
                  .HasForeignKey(d => d.QuestionId)
                  .OnDelete(DeleteBehavior.ClientSetNull)
                  .HasConstraintName("FK_Answer_Question");
        });


       

    

        // Arabic: ممنوع يتكرر نفس نوع التعارض لنفس الحادث (سجل واحد لكل Type)
        // English: Prevent duplicates per AccidentId + ConflictType
        modelBuilder.Entity<AccidentConflict>()
            .HasIndex(x => new { x.AccidentId, x.ConflictType })
            .IsUnique();


    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
