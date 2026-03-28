using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels.Auth
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "اسم السائق مطلوب")]
        public string DriverName { get; set; }

        [Required(ErrorMessage = "البريد الإلكتروني مطلوب"), EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "رقم الجوال مطلوب")]
        public string PhoneNumber { get; set; }

        [Required(ErrorMessage = "رقم رخصة القيادة مطلوب")]
        public string LicenseNumber { get; set; }

        [Required(ErrorMessage = "كلمة المرور مطلوبة")]
        public string Password { get; set; }

        [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب"), Compare("Password")]
        public string ConfirmPassword { get; set; }
    }
}
