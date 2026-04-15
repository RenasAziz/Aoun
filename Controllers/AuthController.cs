using Aoun.Models;
using Aoun.ViewModels.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Scripting;
using BCrypt.Net;
using System.Linq;
using Aoun.Services;
using System.Security.Cryptography;


namespace Aoun.Controllers
{

/*
===============================================================================
AuthController
===============================================================================
Handles all authentication-related functionality in the system:

- User Login 
- OTP Verification 
- User Registration
- Forgot Password flow
- Reset Password with OTP verification
- Session management (login/logout) --> Temporarily store OTP codes and logged-in user data
- Password hashing using BCrypt for security
===============================================================================
*/

    public class AuthController : Controller
    {
        private readonly AounDbContext _context; // Entity Framework to access the database
        private readonly EmailService _emailService;

        public AuthController(AounDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // =========================
        // LOGIN
        // =========================

        // GET: /Auth/Login
        // Displays the login page.
        public IActionResult Login()
        {
            return View();
        }

        // POST: /Auth/Login
        /*
        - Validates user input. 
        - Searches for the user by email.
        - Verifies password using BCrypt hash comparison.
        - If valid → generates a 4-digit OTP.
        - Stores OTP and UserId temporarily in Session.
        - Redirects to OTP verification page.
         */

        [HttpPost]
        public async Task <IActionResult> Login(LoginViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var user = _context.Users
                .FirstOrDefault(u => u.Email == vm.Email);

            if (user == null ||
                !BCrypt.Net.BCrypt.Verify(vm.Password, user.PasswordHash)) // Securely hash and verify passwords
            {
                ViewBag.Error = "خطأ في البريد الإلكتروني أو كلمة المرور";
                return View(vm);
            }

            // Generate OTP
            var otp = RandomNumberGenerator.GetInt32(1000, 9999).ToString();

            // Store OTP temporarily in SESSION
            HttpContext.Session.SetString("OtpCode", otp);
            HttpContext.Session.SetInt32("OtpUserId", user.UserId);

            // Send real email
            await _emailService.SendOtpEmail(user.Email, otp);

            return RedirectToAction("Otp");
        }


        // =========================
        // REGISTER
        // =========================

        // GET: /Auth/Register
        // Displays registration page.
        public IActionResult Register()
        {
            return View();
        }

        // POST: /Auth/Register
        /*
        - Validates form data.
        - Checks if email already exists.
        - Hashes password using BCrypt.
        - Creates a new User record.
        - Creates a related Driver record.
        - Saves both into the database.
         */

        [HttpPost]
        public IActionResult Register(RegisterViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            if (_context.Users.Any(u => u.Email == vm.Email))
            {
                ViewBag.Error = "Email already exists";
                return View(vm);
            }

            var user = new User
            {
                Email = vm.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
                PhoneNumber = vm.PhoneNumber,
                Role = "driver"
            };

            _context.Users.Add(user);
            _context.SaveChanges();

            var driver = new Driver
            {
                UserId = user.UserId,
                DriverName = vm.DriverName,
                LicenseNumber = vm.LicenseNumber
            };

            _context.Drivers.Add(driver);
            _context.SaveChanges();

            return RedirectToAction("Login");
        }

        // =========================
        // OTP
        // =========================

        // GET: /Auth/Otp
        // Ensures OTP session exists before showing page.
        public IActionResult Otp()
        {
            if (HttpContext.Session.GetInt32("OtpUserId") == null)
                return RedirectToAction("Login");

            return View();
        }

        // POST: /Auth/Otp
        /*
        - Compares entered OTP with session OTP.
        - If correct:
            • Clears OTP session data.
            • Stores UserId and Role in session.
            • Stores driver name for display.
            • Redirects to Home page.

        - If incorrect:
            • Shows error message.
         */

        [HttpPost]
        public IActionResult Otp(OtpViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var sessionOtp = HttpContext.Session.GetString("OtpCode");
            var userId = HttpContext.Session.GetInt32("OtpUserId");

            if (sessionOtp == null || userId == null || vm.FullOtp != sessionOtp)
            {
                ViewBag.Error = "رمز التحقق غير صحيح";
                return View(vm);
            }

            var user = _context.Users.Find(userId);
            if (user == null)
                return RedirectToAction("Login");

            HttpContext.Session.Remove("OtpCode");
            HttpContext.Session.Remove("OtpUserId");

            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("Role", user.Role);

            // Driver name
            if (user.Role != null && user.Role.Equals("driver", StringComparison.OrdinalIgnoreCase))
            {
                var driver = _context.Drivers
                    .FirstOrDefault(d => d.UserId == user.UserId);

                if (driver != null)
                {
                    HttpContext.Session.SetString("UserName", driver.DriverName);
                }

                return RedirectToAction("HomePage", "Home");
            }

            // Inspector name
            if (user.Role != null && user.Role.Equals("Inspector", StringComparison.OrdinalIgnoreCase))
            {
                HttpContext.Session.SetString("UserName", "محقق الحوادث");
                return RedirectToAction("Index", "InspectorReports");
            }

            // fallback
            return RedirectToAction("HomePage", "Home");

        }

        // =========================
        // Resend OTP
        // =========================

        [HttpPost]
        public async Task <IActionResult> ResendOtp()
        {
            var userId = HttpContext.Session.GetInt32("OtpUserId");
            if (userId == null)
                return Unauthorized();

            // Retrieve user from database
            var user = _context.Users.Find(userId);

            if (user == null)
                return Unauthorized();
            
            // Generate secure OTP
            var otp = RandomNumberGenerator.GetInt32(1000, 9999).ToString();

            HttpContext.Session.SetString("OtpCode", otp);

            // Send real email
            await _emailService.SendOtpEmail(user.Email, otp);

            return Ok(new { success = true });
        }

        // =========================
        // Forgot Password
        // =========================

        /*
        1. User enters registered email.
        2. System verifies email exists.
        3. Generates temporary OTP (2-minute expiry).
        4. Stores OTP + expiry + email in session.
        5. Redirects user to OTP verification page.
         */

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public async Task <IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = _context.Users.FirstOrDefault(u => u.Email == model.Email);
            if (user == null)
            {
                ViewBag.Error = "البريد الإلكتروني غير مسجل";
                return View(model);
            }

            var otp = RandomNumberGenerator.GetInt32(1000, 9999).ToString();

            HttpContext.Session.SetString("ResetOtp", otp);
            HttpContext.Session.SetString("ResetEmail", user.Email);
            HttpContext.Session.SetString("ResetOtpExpiry",
                DateTime.UtcNow.AddMinutes(2).ToString());

            await _emailService.SendOtpEmail(user.Email, otp);

            return RedirectToAction("ResetPasswordOtp");
        }

        // =========================
        // Reset Password Otp
        // =========================

        [HttpGet]
        public IActionResult ResetPasswordOtp()
        {
            return View();
        }

        [HttpPost]
        public IActionResult ResetPasswordOtp(OtpViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var sessionOtp = HttpContext.Session.GetString("ResetOtp");
            var expiryString = HttpContext.Session.GetString("ResetOtpExpiry");

            if (sessionOtp == null || expiryString == null)
                return RedirectToAction("Login");

            var expiry = DateTime.Parse(expiryString);

            if (DateTime.UtcNow > expiry)
            {
                ViewBag.Error = "انتهت صلاحية الرمز، اطلب رمز جديد";
                return View(vm);
            }

            if (vm.FullOtp != sessionOtp)
            {
                ViewBag.Error = "رمز التحقق غير صحيح";
                return View(vm);
            }

            return RedirectToAction("ResetPassword");
        }

        // ===============================
        // Resend Reset Password OTP
        // ===============================

        [HttpPost]
        public async Task<IActionResult> ResendResetOtp()
        {
            var email = HttpContext.Session.GetString("ResetEmail");
            if (email == null)
                return Unauthorized();

            var otp = RandomNumberGenerator.GetInt32(1000, 9999).ToString();

            HttpContext.Session.SetString("ResetOtp", otp);
            HttpContext.Session.SetString("ResetOtpExpiry",
                DateTime.UtcNow.AddMinutes(2).ToString());

            await _emailService.SendOtpEmail(email, otp);

            return Ok(new { success = true });
        }


        // =========================
        // Reset Password
        // =========================

        /*
        1. OTP must be validated first.
        2. User enters new password.
        3. Password is hashed using BCrypt.
        4. Database is updated.
        5. Reset session data is cleared.
        6. User is redirected to success page.
         */

        [HttpGet]
        public IActionResult ResetPassword()
        {
            if (HttpContext.Session.GetString("ResetEmail") == null)
                return RedirectToAction("Login");

            return View();
        }


        [HttpPost]
        public IActionResult ResetPassword(ResetPasswordViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var email = HttpContext.Session.GetString("ResetEmail");
            if (string.IsNullOrEmpty(email))
                return RedirectToAction("Login");

            var user = _context.Users.FirstOrDefault(u => u.Email == email);
            if (user == null)
                return RedirectToAction("Login");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.NewPassword);

            user.OtpCode = null;
            user.OtpExpiry = null;

            _context.SaveChanges();
            HttpContext.Session.Remove("ResetEmail");

            return RedirectToAction("Success");
        }

        // =========================
        // Reset password successful page
        // =========================

        [HttpGet]
        public IActionResult Success()
        {
            return View();
        }


        // =========================
        // LOGOUT
        // =========================

        /*
        - Clears all session data and redirects user to login page.
        - Ends the authenticated session completely.
         */

        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();

            // Ensures session cookie is gone
            Response.Cookies.Delete(".AspNetCore.Session");

            return RedirectToAction("Login");
        }
    }
}