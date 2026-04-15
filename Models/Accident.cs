using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Models;

[Table("Accident")]
public partial class Accident
{
    [Key]
    [Column("accident_id")]
    public int AccidentId { get; set; }

    [Column("accident_date", TypeName = "date")]
    public DateOnly? AccidentDate { get; set; }

    [Column("accident_time", TypeName = "time")]
    public TimeOnly? AccidentTime { get; set; }

    [Column("location")]
    [StringLength(255)]
    public string? Location { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("accident_type")]
    [StringLength(50)]
    public string? AccidentType { get; set; }

    [Column("status")]
    [StringLength(50)]
    public string? Status { get; set; }

    [Column("latitude", TypeName = "decimal(10,7)")]
    public decimal? Latitude { get; set; }

    [Column("longitude", TypeName = "decimal(10,7)")]
    public decimal? Longitude { get; set; }

    [InverseProperty("Accident")]
    public virtual AccidentReport? AccidentReport { get; set; }

    [InverseProperty("Accident")]
    public virtual ICollection<Answer> Answers { get; set; } = new List<Answer>();

    [InverseProperty("Accident")]
    public virtual ICollection<DriverFeedback> DriverFeedbacks { get; set; } = new List<DriverFeedback>();

    [InverseProperty("Accident")]
    public virtual ICollection<Image> Images { get; set; } = new List<Image>();

    [InverseProperty("Accident")]
    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();

    public virtual ICollection<Involve> Involves { get; set; } = new List<Involve>();
}