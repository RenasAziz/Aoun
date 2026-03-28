using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels.Auth
{
    public class OtpViewModel
    {
        [Required]
        public string D1 { get; set; }

        [Required]
        public string D2 { get; set; }

        [Required]
        public string D3 { get; set; }

        [Required]
        public string D4 { get; set; }

        public string FullOtp => $"{D1}{D2}{D3}{D4}";
    }
}

