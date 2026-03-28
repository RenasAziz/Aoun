using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Models;

[Table("Users")]
[Index(nameof(Email), IsUnique = true)]
public partial class User
{
    // Arabic: المفتاح الأساسي
    // English: Primary Key
    [Key]
    [Column("user_id")]
    public int UserId { get; set; }

    // Arabic: البريد الإلكتروني
    // English: Email
    [Column("email")]
    [StringLength(100)]
    public string Email { get; set; } = null!;

    // Arabic: كلمة المرور
    // English: Password
    [Column("password")]
    [StringLength(255)]
    public string PasswordHash { get; set; } = null!;

    // Arabic: رقم الهاتف
    // English: Phone number
    [Column("phone_number")]
    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    // Arabic: الدور (Driver / Inspector)
    // English: User role
    [Column("role")]
    [StringLength(50)]
    public string Role { get; set; } = null!;

    // OTP code for login verification
    [Column("otp_code")]
    [StringLength(10)]
    public string? OtpCode { get; set; }

    // OTP expiry time
    [Column("otp_expiry")]
    public DateTime? OtpExpiry { get; set; }
}
