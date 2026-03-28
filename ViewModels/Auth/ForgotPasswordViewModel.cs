using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels.Auth
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
    }
}

