using Aoun.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Aoun.Models;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using System.IO;
using Aoun.Services;
using System.Data;
using Aoun.Filters;
using System.Net.Http.Headers;
using System.Text.Json;


namespace Aoun.Controllers
{
    /// <summary>
    /// AccidentController manages the complete driver accident workflow in Aoun.
    /// يدير هذا الكنترولر مسار حادث السائقين بالكامل داخل نظام عون.
    ///
    /// Main responsibilities:
    /// - Screening accident eligibility before creating a session.
    /// - Creating the accident and registering the first driver.
    /// - Allowing the second driver to join using an accident code.
    /// - Handling photo upload, vehicle selection, questionnaires, conflict resolution, final result, and feedback.
    ///
    /// المسؤوليات الأساسية:
    /// - التحقق من أهلية الحادث قبل إنشاء الجلسة.
    /// - إنشاء الحادث وتسجيل السائق الأول.
    /// - السماح للسائق الثاني بالانضمام باستخدام رمز الحادث.
    /// - إدارة رفع الصور، اختيار المركبة، الأسئلة، حل التعارضات، النتيجة النهائية، والتقييم.
    /// </summary>
    public class AccidentController : Controller
    {
        // =========================================================
        // Dependencies / الاعتماديات
        // =========================================================
        // EF Core database context used to access Aoun database tables.
        // سياق قاعدة البيانات المستخدم للوصول إلى جداول نظام عون.
        private readonly AounDbContext _context;

        // Business services used by the accident workflow.
        // خدمات منطق الأعمال المستخدمة داخل مسار الحادث.
        private readonly QuestionnaireService _questionnaireService;
        private readonly ConflictService _conflictService;
        private readonly ConflictPackService _conflictPackService;
        private readonly LiabilityRuleEngineService _liabilityRuleEngineService;
        private readonly IHttpClientFactory _httpClientFactory;

        // Fixed database code for the optional free-text question.
        // كود ثابت للسؤال النصي الاختياري المخزن في قاعدة البيانات.
        private const string FreeTextQuestionCode = "FREE_TEXT_ACCIDENT_DESC";

        /// <summary>
        /// Injects the database context and all services needed by the accident workflow.
        /// يحقن سياق قاعدة البيانات والخدمات اللازمة لتشغيل مسار الحادث.
        /// </summary>
        public AccidentController(
           AounDbContext context,
           QuestionnaireService questionnaireService,
           ConflictService conflictService,
           ConflictPackService conflictPackService,
           LiabilityRuleEngineService liabilityRuleEngineService,
           IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _questionnaireService = questionnaireService;
            _conflictService = conflictService;
            _conflictPackService = conflictPackService;
            _liabilityRuleEngineService = liabilityRuleEngineService;
            _httpClientFactory = httpClientFactory;
        }

        // =========================================================
        // Session and Participant Helpers
        // مساعدات الجلسة والمشاركين
        // =========================================================
        // These helper methods centralize access to the currently logged-in user
        // and the driver's role inside a specific accident session.
        // هذه الدوال تجمع منطق الوصول للمستخدم الحالي ودوره داخل جلسة حادث محددة.
        // Reads the logged-in user id from session.
        // يقرأ رقم المستخدم الحالي من الجلسة.
        private int? GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId");
        }

        private async Task<AccidentSessionParticipant?> GetCurrentParticipantAsync(int accidentId)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return null;

            return await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p => p.AccidentId == accidentId && p.DriverUserId == currentUserId.Value);
        }

        private async Task<byte?> GetCurrentUserRoleInAccidentAsync(int accidentId)
        {
            var participant = await GetCurrentParticipantAsync(accidentId);
            return participant?.Role;
        }

        private async Task<int?> GetDriverUserIdByRoleAsync(int accidentId, int role)
        {
            if (role != 1 && role != 2)
                return null;

            return await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == accidentId && p.Role == role)
                .OrderBy(p => p.Id)
                .Select(p => (int?)p.DriverUserId)
                .FirstOrDefaultAsync();
        }

        // =========================================================
        // 1) Accident Eligibility Screening
        // ١) فحص أهلية الحادث
        // =========================================================
        // This step prevents unsupported cases from entering the automated workflow,
        // such as accidents with injuries, more than two vehicles, missing parties, or invalid insurance.
        // هذه الخطوة تمنع الحالات غير المدعومة من دخول المسار الآلي،
        // مثل وجود إصابات، أكثر من مركبتين، غياب أحد الأطراف، أو عدم وجود تأمين صالح.
        [HttpGet]
        public IActionResult Screening()
        {
            return View(new ScreeningViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Screening(ScreeningViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // The accident is rejected if it falls outside the supported minor-accident scope.
            // يتم رفض الحادث إذا خرج عن نطاق الحوادث البسيطة المدعومة في النظام.
            bool reject =
                vm.HasInjuries == true ||
                vm.VehiclesCount != "Two" ||
                vm.BothPartiesPresent == false ||
                vm.HasValidInsurance == false;

            if (reject)
            {
                ViewBag.ShowRejectModal = true;
                return View(vm);
            }

            return RedirectToAction(nameof(Location));
        }

        // =========================================================
        // 2) Location and Accident Creation
        // ٢) تحديد الموقع وإنشاء الحادث
        // =========================================================
        // This section receives the accident location, date, and time from the UI,
        // then creates the accident record and registers the logged-in driver as Driver 1.
        // يستقبل هذا الجزء موقع الحادث والتاريخ والوقت من الواجهة،
        // ثم ينشئ سجل الحادث ويسجل المستخدم الحالي كسائق أول.
        [AuthorizeUser]
        [HttpGet]
        public IActionResult Location()
        {
            return View(new AccidentLocationViewModel());
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Location(AccidentLocationViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // The date/time values come from hidden inputs in the UI, so they are parsed explicitly.
            // تأتي قيم التاريخ والوقت من حقول مخفية في الواجهة، لذلك يتم تحليلها بشكل صريح.
            if (!DateOnly.TryParseExact(vm.AccidentDateIso, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                ModelState.AddModelError("", "صيغة التاريخ غير صحيحة.");
                return View(vm);
            }

            if (!TimeOnly.TryParseExact(vm.AccidentTimeIso, "HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time))
            {
                ModelState.AddModelError("", "صيغة الوقت غير صحيحة.");
                return View(vm);
            }

            // The system allows manual location selection, but it should not create an accident without a location.
            // يسمح النظام بتحديد الموقع يدويًا، لكن لا يجب إنشاء حادث بدون موقع.
            if (string.IsNullOrWhiteSpace(vm.LocationText))
            {
                ModelState.AddModelError("LocationText", "يرجى تحديد موقع الحادث أو إدخاله يدويًا.");
                return View(vm);
            }

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");
            var accident = new Accident
            {
                Location = vm.LocationText,
                AccidentDate = date,
                AccidentTime = time,
                AccidentType = "تصادم",
                Status = "انتظار",
                Description = null,
                Latitude = vm.Latitude.HasValue ? Convert.ToDecimal(vm.Latitude.Value) : null,
                Longitude = vm.Longitude.HasValue ? Convert.ToDecimal(vm.Longitude.Value) : null
            };

            // Save the accident first to generate AccidentId, then use it to register the creator as Driver 1.
            // نحفظ الحادث أولاً لتوليد رقم الحادث، ثم نستخدمه لتسجيل المنشئ كسائق أول.
            _context.Accidents.Add(accident);
            await _context.SaveChangesAsync();

            // Driver 1 = creator
            int driverUserId = currentUserId.Value;

            bool exists = await _context.AccidentSessionParticipants
                .AnyAsync(p => p.AccidentId == accident.AccidentId && p.DriverUserId == driverUserId);

            if (!exists)
            {
                _context.AccidentSessionParticipants.Add(new AccidentSessionParticipant
                {
                    AccidentId = accident.AccidentId,
                    DriverUserId = driverUserId,
                    Role = 1,
                    IsJoined = true,
                    JoinedAt = DateTime.UtcNow,
                    CurrentStep = "Waiting",
                    IsCompleted = false
                });

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Success", new { accidentId = accident.AccidentId });
        }

        // =========================================================
        // 3) Success Page: Accident Code, QR, and Join Timer
        // ٣) صفحة النجاح: رمز الحادث، QR، ومؤقت الانضمام
        // =========================================================
        // The creator sees the accident code and QR code here.
        // The remaining join time is calculated on the server to prevent timer reset after refresh.
        // يرى منشئ الحادث رمز الحادث ورمز QR هنا.
        // يتم حساب الوقت المتبقي من الخادم حتى لا يبدأ المؤقت من جديد عند تحديث الصفحة.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> Success(int accidentId)
        {
            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null)
                return NotFound();

            var currentRole = await GetCurrentUserRoleInAccidentAsync(accidentId);
            if (currentRole == null)
                return RedirectToAction("Join");

            var code = $"ACC-{accidentId:000000}";

            int remainingSeconds = 5 * 60;

            if (accident.AccidentDate.HasValue && accident.AccidentTime.HasValue)
            {
                var accidentDateTime = accident.AccidentDate.Value.ToDateTime(accident.AccidentTime.Value);
                var expiresAt = accidentDateTime.AddMinutes(5);

                remainingSeconds = (int)(expiresAt - DateTime.Now).TotalSeconds;

                if (remainingSeconds < 0)
                    remainingSeconds = 0;
            }

            ViewBag.JoinRemainingSeconds = remainingSeconds;

            var vm = new AccidentSuccessViewModel
            {
                AccidentId = accidentId,
                AccidentCode = code
            };

            return View(vm);
        }

        // =========================================================
        // 4) Waiting and Polling
        // ٤) الانتظار والتحديث التلقائي
        // =========================================================
        // WaitingStatus is called by JavaScript polling to check whether both drivers joined.
        // It also expires the accident if the second driver does not join within the allowed time.
        // يتم استدعاء WaitingStatus من JavaScript للتحقق من انضمام الطرفين.
        // كما ينهي صلاحية الحادث إذا لم ينضم الطرف الثاني خلال المهلة المحددة.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> Waiting(int accidentId, int role)
        {
            var accident = await _context.Accidents.FirstOrDefaultAsync(a => a.AccidentId == accidentId);
            if (accident == null) return NotFound();

            var joinedCount = await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == accidentId && p.IsJoined)
                .CountAsync();

            if (joinedCount < 2 &&
                accident.AccidentDate.HasValue &&
                accident.AccidentTime.HasValue)
            {
                var accidentDateTime = accident.AccidentDate.Value.ToDateTime(accident.AccidentTime.Value);

                if (DateTime.Now > accidentDateTime.AddMinutes(5))
                {
                    accident.Status = "منتهي";
                    await _context.SaveChangesAsync();

                    TempData["ToastWarning"] = "انتهت مهلة انضمام الطرف الآخر. يرجى إنشاء حادث جديد.";
                    return RedirectToAction("Join");
                }
            }

            var currentRole = await GetCurrentUserRoleInAccidentAsync(accidentId);
            if (currentRole == null)
                return RedirectToAction("Join");

            var vm = new AccidentWaitingViewModel
            {
                AccidentId = accidentId,
                AccidentCode = $"ACC-{accidentId:000000}",
                Role = currentRole.Value
            };

            return View(vm);
        }

        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> WaitingStatus(int accidentId)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return Json(new
                {
                    roomReady = false,
                    expired = false,
                    redirectUrl = Url.Action("Login", "Auth")
                });
            }

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null)
            {
                return Json(new
                {
                    roomReady = false,
                    expired = true,
                    redirectUrl = Url.Action("Join", "Accident")
                });
            }

            var currentParticipant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p => p.AccidentId == accidentId && p.DriverUserId == currentUserId.Value);

            if (currentParticipant == null)
            {
                return Json(new
                {
                    roomReady = false,
                    expired = false,
                    redirectUrl = Url.Action("Join", "Accident")
                });
            }

            var joinedCount = await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == accidentId && p.IsJoined)
                .CountAsync();

            bool roomReady = joinedCount >= 2;

            if (roomReady)
            {
                if (accident.Status != "فعال")
                {
                    accident.Status = "فعال";
                    await _context.SaveChangesAsync();
                }

                return Json(new
                {
                    roomReady = true,
                    expired = false,
                    redirectUrl = Url.Action("UploadPhotos", "Accident", new
                    {
                        accidentId,
                        role = currentParticipant.Role
                    })
                });
            }

            // If the second party has not joined within 5 minutes, expire the accident session.
            // إذا لم ينضم الطرف الآخر خلال 5 دقائق، تنتهي مهلة الانضمام.
            if (accident.AccidentDate.HasValue && accident.AccidentTime.HasValue)
            {
                var accidentDateTime = accident.AccidentDate.Value.ToDateTime(accident.AccidentTime.Value);

                if (DateTime.Now > accidentDateTime.AddMinutes(5))
                {
                    accident.Status = "منتهي";
                    await _context.SaveChangesAsync();

                    TempData["ToastWarning"] = "انتهت مهلة انضمام الطرف الآخر. يرجى إنشاء حادث جديد.";

                    return Json(new
                    {
                        roomReady = false,
                        expired = true,
                        redirectUrl = Url.Action("Join", "Accident")
                    });
                }
            }

            return Json(new
            {
                roomReady = false,
                expired = false,
                redirectUrl = Url.Action("UploadPhotos", "Accident", new
                {
                    accidentId,
                    role = currentParticipant.Role
                })
            });
        }

        // =========================================================
        // 5) Join Page
        // ٥) صفحة الانضمام
        // =========================================================
        // Displays the page where the second driver enters the accident code or scans the QR code.
        // تعرض الصفحة التي يستخدمها الطرف الثاني لإدخال رمز الحادث أو مسح رمز QR.
        [AuthorizeUser]
        [HttpGet]
        public IActionResult Join()
        {
            return View();
        }

        // =========================================================
        // 6) Join by Accident Code
        // ٦) الانضمام باستخدام رمز الحادث
        // =========================================================
        // This action validates the accident code, checks expiry/completion rules,
        // prevents third-party access, and resumes already registered drivers from their current step.
        // يتحقق هذا الإجراء من رمز الحادث، وقواعد الانتهاء أو الاكتمال،
        // ويمنع دخول طرف ثالث، كما يعيد السائق المسجل سابقًا إلى خطوته الحالية.
        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> JoinByCode(string code)
        {
            var currentUserId = GetCurrentUserId();

            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["ToastError"] = "يرجى إدخال رمز الحادث للمتابعة.";
                return RedirectToAction("Join");
            }

            code = code.Trim();

            int accidentId = ExtractAccidentId(code);

            if (accidentId <= 0)
            {
                TempData["ToastError"] = "رمز الحادث غير صحيح. يرجى التأكد من كتابته بالشكل الصحيح.";
                return RedirectToAction("Join");
            }

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null)
            {
                TempData["ToastError"] = "لا يوجد حادث بهذا الرقم. يرجى التأكد من الرمز والمحاولة مرة أخرى.";
                return RedirectToAction("Join");
            }

            int driverUserId = currentUserId.Value;

            var existingParticipant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p => p.AccidentId == accidentId && p.DriverUserId == driverUserId);

            // If a report already exists, the workflow is complete and should not return to questions.
            // إذا كان التقرير موجودًا، فهذا يعني أن المسار اكتمل ولا يجب الرجوع للأسئلة.
            var existingReport = await _context.AccidentReports
                .FirstOrDefaultAsync(r => r.AccidentId == accidentId);

            // If a report already exists, the accident workflow is completed.
            // إذا كان التقرير موجودًا، فهذا يعني أن مسار الحادث اكتمل ولا يجب الرجوع للأسئلة.
            if (existingReport != null)
            {
                if (existingParticipant != null)
                {
                    existingParticipant.CurrentStep = "FinalResult";
                    await _context.SaveChangesAsync();

                    TempData["ToastInfo"] = "هذا الحادث مكتمل وتم إنشاء تقريره مسبقًا.";
                    return RedirectToAction(nameof(FinalResult), new
                    {
                        accidentId = accidentId,
                        role = existingParticipant.Role
                    });
                }

                TempData["ToastError"] = "لا يمكن الانضمام لهذا الحادث لأنه مكتمل وتم إنشاء تقريره.";
                return RedirectToAction("Join");
            }

            if (accident.Status == "مكتمل" || accident.Status == "منتهي" || accident.Status == "ملغي")
            {
                TempData["ToastError"] = "لا يمكن الانضمام لهذا الحادث لأنه مكتمل أو غير نشط.";
                return RedirectToAction("Join");
            }

            // Existing participants are not treated as new joiners; they are resumed from their saved step.
            // المشاركون الموجودون سابقًا لا يعاملون كمنضمين جدد؛ بل يتم إعادتهم إلى خطوتهم المحفوظة.
            if (existingParticipant != null)
            {
                if (accident.Status != "فعال")
                {
                    accident.Status = "فعال";
                    await _context.SaveChangesAsync();
                }

                TempData["ToastInfo"] = "أنتِ مسجلة مسبقًا في هذا الحادث، تم إعادتك إلى خطوتك الحالية.";
                return RedirectToCurrentStep(existingParticipant);
            }

            // Check join timeout only for new participants.
            // التحقق من انتهاء مهلة الانضمام يكون فقط للمستخدم الجديد، وليس للطرف المسجل مسبقًا.
            if (accident.AccidentDate.HasValue && accident.AccidentTime.HasValue)
            {
                var accidentDateTime = accident.AccidentDate.Value.ToDateTime(accident.AccidentTime.Value);

                if (DateTime.Now > accidentDateTime.AddMinutes(5))
                {
                    accident.Status = "منتهي";
                    await _context.SaveChangesAsync();

                    TempData["ToastWarning"] = "انتهت مهلة الانضمام لهذا الحادث. يمكن للطرف الأول إنشاء حادث جديد ومشاركة رمز جديد.";
                    return RedirectToAction("Join");
                }
            }

            int count = await _context.AccidentSessionParticipants
                .CountAsync(p => p.AccidentId == accidentId);

            if (count >= 2)
            {
                TempData["ToastError"] = "تم اكتمال أطراف الحادث، لا يمكن الانضمام.";
                return RedirectToAction("Join");
            }

            _context.AccidentSessionParticipants.Add(new AccidentSessionParticipant
            {
                AccidentId = accidentId,
                DriverUserId = driverUserId,
                Role = 2,
                IsJoined = true,
                JoinedAt = DateTime.UtcNow,
                CurrentStep = "Waiting",
                IsCompleted = false
            });

            accident.Status = "فعال";
            await _context.SaveChangesAsync();

            return RedirectToAction("JoinSuccess", new { accidentId = accidentId });
        }
        // =========================================================
        // 7) Join Success
        // ٧) نجاح الانضمام
        // =========================================================
        // Shows the joined driver basic accident details and their assigned role.
        // يعرض للطرف المنضم بيانات الحادث الأساسية والدور المخصص له.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> JoinSuccess(int accidentId)
        {
            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null) return NotFound();

            var currentRole = await GetCurrentUserRoleInAccidentAsync(accidentId);
            if (currentRole == null)
                return RedirectToAction("Join");

            string dateText = accident.AccidentDate.HasValue
                ? accident.AccidentDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "—";

            string timeText = accident.AccidentTime.HasValue
                ? accident.AccidentTime.Value.ToString("hh\\:mm", CultureInfo.InvariantCulture)
                : "—";

            var vm = new JoinSuccessViewModel
            {
                AccidentId = accidentId,
                AccidentCode = $"ACC-{accidentId:000000}",
                Location = accident.Location ?? "—",
                AccidentDate = dateText,
                AccidentTime = timeText,
                Role = currentRole.Value
            };

            return View(vm);
        }

        // =========================================================
        // Navigation and Workflow Helpers
        // مساعدات التنقل ومسار العمل
        // =========================================================
        // These helpers keep routing decisions consistent across the controller,
        // especially when resuming an interrupted accident session.
        // تساعد هذه الدوال في توحيد قرارات الانتقال بين الصفحات،
        // خصوصًا عند استكمال جلسة حادث لم تكتمل سابقًا.
        // Extracts the numeric accident id from codes like ACC-000132 or ACC-2024-00856.
        // يستخرج رقم الحادث من صيغ مثل ACC-000132 أو ACC-2024-00856.
        private static int ExtractAccidentId(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return 0;

            var parts = code.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return 0;

            var last = parts[^1];
            var digitsOnly = new string(last.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(digitsOnly)) return 0;

            return int.TryParse(digitsOnly, out int id) ? id : 0;
        }


        // Redirects a returning participant to the last saved workflow step.
        // يعيد المشارك العائد إلى آخر خطوة محفوظة في مسار الحادث.
        private IActionResult RedirectToCurrentStep(AccidentSessionParticipant participant)
        {
            int accidentId = participant.AccidentId;
            int role = participant.Role;

            return participant.CurrentStep switch
            {
                "Waiting" => RedirectToAction("Waiting", new { accidentId, role }),

                "UploadPhotos" => RedirectToAction("UploadPhotos", new { accidentId, role }),

                "SelectVehicle" => RedirectToAction("SelectVehicle", new { accidentId, role }),

                "Questions" => RedirectToAction("Questions", new { accidentId, role, index = 1 }),
                "MirrorQuestions" => RedirectToAction("MirrorQuestions", new { accidentId, role, index = 1 }),

                "MirrorDone" => RedirectToAction("MirrorDone", new { accidentId, role }),

                "FreeText" => RedirectToAction("FreeText", new { accidentId, role }),

                "FinalResult" => RedirectToAction("FinalResult", new { accidentId, role }),

                _ => RedirectToAction("Waiting", new { accidentId, role })
            };
        }

        // Prevents access to old workflow pages after the report has already been generated.
        // يمنع الرجوع لصفحات الخطوات القديمة بعد إنشاء التقرير.
        private async Task<IActionResult?> RedirectIfReportExistsAsync(int accidentId, int role)
        {
            if (role != 1 && role != 2)
                return BadRequest("Role invalid.");

            var currentUserId = GetCurrentUserId();

            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var participant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p =>
                    p.AccidentId == accidentId &&
                    p.DriverUserId == currentUserId.Value &&
                    p.Role == role);

            if (participant == null)
                return RedirectToAction("Join");

            bool reportExists = await _context.AccidentReports
                .AnyAsync(r => r.AccidentId == accidentId);

            if (!reportExists)
                return null;

            participant.CurrentStep = "FinalResult";
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(FinalResult), new
            {
                accidentId,
                role
            });
        }

        // =========================================================
        // 8) Upload Photos - Display Page
        // ٨) رفع الصور - عرض الصفحة
        // =========================================================
        // The upload page is only available after both drivers join the same accident session.
        // لا تظهر صفحة رفع الصور إلا بعد انضمام الطرفين إلى نفس جلسة الحادث.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> UploadPhotos(int accidentId, int role)
        {
            var accident = await _context.Accidents.FirstOrDefaultAsync(a => a.AccidentId == accidentId);
            if (accident == null) return NotFound();

            var participant = await GetCurrentParticipantAsync(accidentId);
            if (participant == null)
                return RedirectToAction("Join");

            var joinedCount = await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == accidentId && p.IsJoined)
                .CountAsync();

            if (joinedCount < 2)
                return RedirectToAction("Waiting", new { accidentId, role = participant.Role });

            var vm = new UploadPhotosViewModel
            {
                AccidentId = accidentId,
                Role = participant.Role
            };

            return View(vm);
        }

        // =========================================================
        // 9) Upload Photos - Validation, Storage, and AI Processing
        // ٩) رفع الصور - التحقق، الحفظ، والتحليل الآلي
        // =========================================================
        // This action validates required and optional images, stores them,
        // then calls the classification and segmentation FastAPI services when damage photos exist.
        // يتحقق هذا الإجراء من الصور المطلوبة والاختيارية، ويحفظها،
        // ثم يستدعي خدمات التصنيف والتقسيم في FastAPI عند وجود صور ضرر.
        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPhotos(UploadPhotosViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.FromPost = true;
                return View(vm);
            }

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var accident = await _context.Accidents.FirstOrDefaultAsync(a => a.AccidentId == vm.AccidentId);
            if (accident == null) return NotFound();

            var participant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p => p.AccidentId == vm.AccidentId && p.DriverUserId == currentUserId.Value);

            if (participant == null)
            {
                ModelState.AddModelError("", "لا يمكن تحديد مشاركتك في هذا الحادث.");
                ViewBag.FromPost = true;
                return View(vm);
            }

            var joinedCount = await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == vm.AccidentId && p.IsJoined)
                .CountAsync();

            if (joinedCount < 2)
                return RedirectToAction("Waiting", new { accidentId = vm.AccidentId, role = participant.Role });

            int role = participant.Role;
            int driverUserId = currentUserId.Value;

            // Required images are mandatory evidence for both vehicles and the accident scene.
            // الصور المطلوبة تمثل أدلة أساسية للمركبة وموقع الحادث.
            var requiredFiles = new Dictionary<string, (IFormFile? File, string DisplayName)>
            {
                { "Front", (vm.FrontPhoto, "صورة الواجهة الأمامية") },
                { "Back",  (vm.BackPhoto,  "صورة الواجهة الخلفية") },
                { "Left",  (vm.LeftPhoto,  "صورة الجانب الأيسر") },
                { "Right", (vm.RightPhoto, "صورة الجانب الأيمن") },
                { "Plate", (vm.PlatePhoto, "صورة لوحة السيارة") },
                { "Scene", (vm.ScenePhoto, "صورة عامة لموقع الحادث") }
            };

            // Damage photos are optional because not every accident has visible damage photos uploaded by the driver.
            // صور الضرر اختيارية لأن السائق قد لا يرفع صور ضرر ظاهرة في كل حادث.
            var optionalFiles = new Dictionary<string, (IFormFile? File, string DisplayName)>
            {
                { "Damage1", (vm.DamagePhoto1, "صورة الضرر الأولى") },
                { "Damage2", (vm.DamagePhoto2, "صورة الضرر الثانية") }
            };
            string? damage1Url = null;
            string? damage2Url = null;

            // Validate required images before saving any files to avoid partial uploads.
            // نتحقق من الصور المطلوبة قبل حفظ أي ملف لتجنب الرفع الجزئي.
            foreach (var kv in requiredFiles)
            {
                var file = kv.Value.File;
                var display = kv.Value.DisplayName;

                if (file == null || file.Length == 0)
                {
                    ModelState.AddModelError("", $"يرجى رفع {display}.");
                    ViewBag.FromPost = true;
                    return View(vm);
                }

                if (file.Length > MaxImageSizeBytes)
                {
                    ModelState.AddModelError("", $"{display} حجمها كبير جدًا. الحد الأقصى المسموح هو 5MB.");
                    ViewBag.FromPost = true;
                    return View(vm);
                }

                if (!IsAllowedImage(file))
                {
                    ModelState.AddModelError("", $"صيغة {display} غير مدعومة. ارفعي JPG أو PNG فقط.");
                    ViewBag.FromPost = true;
                    return View(vm);
                }
            }

            foreach (var kv in optionalFiles)
            {
                var file = kv.Value.File;
                var display = kv.Value.DisplayName;

                if (file != null && file.Length > 0)
                {
                    if (file.Length > MaxImageSizeBytes)
                    {
                        ModelState.AddModelError("", $"{display} حجمها كبير جدًا. الحد الأقصى المسموح هو 5MB.");
                        ViewBag.FromPost = true;
                        return View(vm);
                    }

                    if (!IsAllowedImage(file))
                    {
                        ModelState.AddModelError("", $"صيغة {display} غير مدعومة. ارفعي JPG أو PNG فقط.");
                        ViewBag.FromPost = true;
                        return View(vm);
                    }
                }
            }

            var allFiles = requiredFiles
                .Concat(optionalFiles)
                .ToDictionary(x => x.Key, x => x.Value);

            var labels = allFiles.Keys.ToList();

            // Replace existing images for the same driver and labels to keep the latest upload only.
            // نستبدل الصور القديمة لنفس السائق ونفس التصنيفات حتى تبقى آخر نسخة مرفوعة فقط.
            var oldImages = await _context.Images
                .Where(i => i.AccidentId == vm.AccidentId
                            && i.DriverUserId == driverUserId
                            && i.Label != null
                            && labels.Contains(i.Label))
                .ToListAsync();

            if (oldImages.Count > 0)
                _context.Images.RemoveRange(oldImages);

            foreach (var kv in requiredFiles)
            {
                string label = kv.Key;
                IFormFile file = kv.Value.File!;

                var url = await SaveImageAsync(file, vm.AccidentId, role, label);

                if (string.IsNullOrWhiteSpace(url))
                {
                    ModelState.AddModelError("", $"تعذر حفظ {kv.Value.DisplayName}. يرجى المحاولة مرة أخرى.");
                    ViewBag.FromPost = true;
                    return View(vm);
                }

                _context.Images.Add(new Image
                {
                    AccidentId = vm.AccidentId,
                    ImagePath = url,
                    Label = label,
                    UploadDate = DateTime.Now,
                    DriverUserId = driverUserId
                });
            }
            foreach (var kv in optionalFiles)
            {
                string label = kv.Key;
                IFormFile? file = kv.Value.File;

                if (file == null || file.Length == 0)
                    continue;

                var url = await SaveImageAsync(file, vm.AccidentId, role, label);

                if (string.IsNullOrWhiteSpace(url))
                {
                    ModelState.AddModelError("", $"تعذر حفظ {kv.Value.DisplayName}. يرجى المحاولة مرة أخرى.");
                    ViewBag.FromPost = true;
                    return View(vm);
                }

                if (label == "Damage1")
                    damage1Url = url;

                if (label == "Damage2")
                    damage2Url = url;

                _context.Images.Add(new Image
                {
                    AccidentId = vm.AccidentId,
                    ImagePath = url,
                    Label = label,
                    UploadDate = DateTime.Now,
                    DriverUserId = driverUserId
                });
            }

            await _context.SaveChangesAsync();

            Image? damage1Image = null;
            Image? damage2Image = null;

            if (!string.IsNullOrWhiteSpace(damage1Url))
            {
                damage1Image = await _context.Images.FirstOrDefaultAsync(i =>
                    i.AccidentId == vm.AccidentId &&
                    i.DriverUserId == driverUserId &&
                    i.ImagePath == damage1Url);
            }

            if (!string.IsNullOrWhiteSpace(damage2Url))
            {
                damage2Image = await _context.Images.FirstOrDefaultAsync(i =>
                    i.AccidentId == vm.AccidentId &&
                    i.DriverUserId == driverUserId &&
                    i.ImagePath == damage2Url);
            }

            // Run damage-side classification for the first optional damage image.
            // يتم تشغيل تصنيف جهة الضرر لصورة الضرر الاختيارية الأولى.
            if (damage1Image != null && !string.IsNullOrWhiteSpace(damage1Url))
            {
                var physicalPath1 = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    damage1Url.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString())
                );

                var prediction1 = await PredictSingleImageAsync(physicalPath1);

                if (prediction1 != null && prediction1.Success)
                {
                    damage1Image.PredictedLabel = prediction1.Label;
                    damage1Image.PredictionConfidence = prediction1.Confidence;
                    damage1Image.PredictionModel = prediction1.ModelName;
                    damage1Image.PredictionDate = DateTime.Now;
                }
            }

            // Run damage-side classification for the second optional damage image.
            // يتم تشغيل تصنيف جهة الضرر لصورة الضرر الاختيارية الثانية.
            if (damage2Image != null && !string.IsNullOrWhiteSpace(damage2Url))
            {
                var physicalPath2 = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    damage2Url.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString())
                );

                var prediction2 = await PredictSingleImageAsync(physicalPath2);

                if (prediction2 != null && prediction2.Success)
                {
                    damage2Image.PredictedLabel = prediction2.Label;
                    damage2Image.PredictionConfidence = prediction2.Confidence;
                    damage2Image.PredictionModel = prediction2.ModelName;
                    damage2Image.PredictionDate = DateTime.Now;
                }
            }

            // Run segmentation/detection for the first damage image to identify damage regions and labels.
            // يتم تشغيل التقسيم/الكشف للصورة الأولى لتحديد مناطق وأنواع الضرر.
            if (damage1Image != null && !string.IsNullOrWhiteSpace(damage1Url))
            {
                var physicalPath1 = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    damage1Url.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString())
                );

                var segmentation1 = await PredictSegmentationAsync(physicalPath1);

                if (segmentation1 != null && segmentation1.Success)
                {
                    damage1Image.SegmentationResultPath = segmentation1.ResultImageUrl;
                    damage1Image.SegmentationModel = segmentation1.ModelName;
                    damage1Image.SegmentationDate = DateTime.Now;
                    damage1Image.SegmentationHasDamage = segmentation1.HasDamage;

                    await _context.SaveChangesAsync();
                    await SaveSegmentationDetectionsAsync(damage1Image, segmentation1);
                }
            }

            // Run segmentation/detection for the second damage image to identify damage regions and labels.
            // يتم تشغيل التقسيم/الكشف للصورة الثانية لتحديد مناطق وأنواع الضرر.
            if (damage2Image != null && !string.IsNullOrWhiteSpace(damage2Url))
            {
                var physicalPath2 = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    damage2Url.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString())
                );

                var segmentation2 = await PredictSegmentationAsync(physicalPath2);

                if (segmentation2 != null && segmentation2.Success)
                {
                    damage2Image.SegmentationResultPath = segmentation2.ResultImageUrl;
                    damage2Image.SegmentationModel = segmentation2.ModelName;
                    damage2Image.SegmentationDate = DateTime.Now;
                    damage2Image.SegmentationHasDamage = segmentation2.HasDamage;

                    await _context.SaveChangesAsync();
                    await SaveSegmentationDetectionsAsync(damage2Image, segmentation2);
                }
            }

            await _context.SaveChangesAsync();

            participant.CurrentStep = "SelectVehicle";
            await _context.SaveChangesAsync();

            return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = role });

        }

        // =========================================================
        // Image Helpers and AI Service Calls
        // مساعدات الصور واستدعاءات خدمات الذكاء الاصطناعي
        // =========================================================
        // These methods validate images, save files safely, and call external AI APIs with error handling.
        // هذه الدوال تتحقق من الصور، تحفظ الملفات بشكل آمن، وتستدعي خدمات الذكاء الاصطناعي مع معالجة الأخطاء.

        // Maximum file size accepted for each uploaded image.
        // الحد الأقصى لحجم كل صورة مرفوعة.
        private const long MaxImageSizeBytes = 5 * 1024 * 1024; // 5 MB
        // Checks the uploaded file extension against the allowed image formats.
        // يتحقق من امتداد الملف المرفوع مقارنة بصيغ الصور المسموحة.
        private static bool IsAllowedImage(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png";
        }

        // Saves the uploaded image under wwwroot/uploads using a stable accident/driver folder structure.
        // يحفظ الصورة المرفوعة داخل wwwroot/uploads باستخدام هيكلة ثابتة حسب الحادث والسائق.
        private static async Task<string?> SaveImageAsync(IFormFile file, int accidentId, int role, string fileBaseName)
        {
            try
            {
                var folderName = $"accident_{accidentId}";
                var driverFolder = $"driver_{role}";
                var root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folderName, driverFolder);

                if (!Directory.Exists(root))
                    Directory.CreateDirectory(root);

                var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
                var fileName = $"{fileBaseName}{ext}";
                var fullPath = Path.Combine(root, fileName);

                using var stream = new FileStream(fullPath, FileMode.Create);
                await file.CopyToAsync(stream);

                return $"/uploads/{folderName}/{driverFolder}/{fileName}";
            }
            catch
            {
                return null;
            }
        }

        // Calls the FastAPI classification endpoint to predict the damage side: front, back, or side.
        // يستدعي نقطة التصنيف في FastAPI لتحديد جهة الضرر: أمامي، خلفي، أو جانبي.
        private async Task<SinglePredictionResponse?> PredictSingleImageAsync(string physicalPath)
        {
            if (!System.IO.File.Exists(physicalPath))
                return new SinglePredictionResponse
                {
                    Success = false,
                    Error = "Image file was not found."
                };

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(20);

                using var content = new MultipartFormDataContent();
                await using var fs = new FileStream(physicalPath, FileMode.Open, FileAccess.Read);
                using var fileContent = new StreamContent(fs);

                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                content.Add(fileContent, "image", Path.GetFileName(physicalPath));

                var response = await client.PostAsync("http://127.0.0.1:8000/predict-single", content);

                if (!response.IsSuccessStatusCode)
                {
                    return new SinglePredictionResponse
                    {
                        Success = false,
                        Error = $"Prediction API failed: {response.StatusCode}"
                    };
                }

                var json = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<SinglePredictionResponse>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (result == null)
                {
                    return new SinglePredictionResponse
                    {
                        Success = false,
                        Error = "Prediction API returned an empty response."
                    };
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                return new SinglePredictionResponse
                {
                    Success = false,
                    Error = "Prediction API timeout."
                };
            }
            catch (HttpRequestException ex)
            {
                return new SinglePredictionResponse
                {
                    Success = false,
                    Error = $"Prediction API connection error: {ex.Message}"
                };
            }
            catch (JsonException)
            {
                return new SinglePredictionResponse
                {
                    Success = false,
                    Error = "Prediction API returned invalid JSON."
                };
            }
            catch (Exception ex)
            {
                return new SinglePredictionResponse
                {
                    Success = false,
                    Error = $"Unexpected prediction error: {ex.Message}"
                };
            }
        }

        // Calls the FastAPI segmentation/detection endpoint to detect visible damage and return mask/labels.
        // يستدعي نقطة التقسيم/الكشف في FastAPI لاكتشاف الضرر الظاهر وإرجاع الصورة والأنواع.
        private async Task<SegmentationPredictionResponse?> PredictSegmentationAsync(string physicalPath)
        {
            if (!System.IO.File.Exists(physicalPath))
                return new SegmentationPredictionResponse
                {
                    Success = false,
                    Error = "Image file was not found."
                };

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                using var content = new MultipartFormDataContent();
                await using var fs = new FileStream(physicalPath, FileMode.Open, FileAccess.Read);
                using var fileContent = new StreamContent(fs);

                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                content.Add(fileContent, "image", Path.GetFileName(physicalPath));

                var response = await client.PostAsync("http://127.0.0.1:8000/predict-segmentation", content);

                if (!response.IsSuccessStatusCode)
                {
                    return new SegmentationPredictionResponse
                    {
                        Success = false,
                        Error = $"Segmentation API failed: {response.StatusCode}"
                    };
                }

                var json = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<SegmentationPredictionResponse>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (result == null)
                {
                    return new SegmentationPredictionResponse
                    {
                        Success = false,
                        Error = "Segmentation API returned an empty response."
                    };
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                return new SegmentationPredictionResponse
                {
                    Success = false,
                    Error = "Segmentation API timeout."
                };
            }
            catch (HttpRequestException ex)
            {
                return new SegmentationPredictionResponse
                {
                    Success = false,
                    Error = $"Segmentation API connection error: {ex.Message}"
                };
            }
            catch (JsonException)
            {
                return new SegmentationPredictionResponse
                {
                    Success = false,
                    Error = "Segmentation API returned invalid JSON."
                };
            }
            catch (Exception ex)
            {
                return new SegmentationPredictionResponse
                {
                    Success = false,
                    Error = $"Unexpected segmentation error: {ex.Message}"
                };
            }
        }

        // Stores segmentation detection labels in the database and replaces old detections for the same image.
        // يحفظ أنواع الضرر المكتشفة في قاعدة البيانات ويستبدل النتائج القديمة لنفس الصورة.
        private async Task SaveSegmentationDetectionsAsync(Image image, SegmentationPredictionResponse segmentation)
        {
            var oldDetections = await _context.ImageSegmentationDetections
                .Where(d => d.AccidentId == image.AccidentId && d.ImageId == image.ImageId)
                .ToListAsync();

            if (oldDetections.Count > 0)
                _context.ImageSegmentationDetections.RemoveRange(oldDetections);

            if (segmentation.Detections != null && segmentation.Detections.Count > 0)
            {
                foreach (var det in segmentation.Detections)
                {
                    _context.ImageSegmentationDetections.Add(new ImageSegmentationDetection
                    {
                        AccidentId = image.AccidentId,
                        ImageId = image.ImageId,
                        DamageLabel = det.Label,
                        Confidence = det.Confidence,
                        CreatedAt = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();
        }
        // =========================================================
        // 10) Select or Add Vehicle
        // ١٠) اختيار أو إضافة المركبة
        // =========================================================
        // Each driver must select the vehicle involved in the accident before answering liability questions.
        // يجب على كل سائق اختيار المركبة المرتبطة بالحادث قبل الإجابة على أسئلة تحديد المسؤولية.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> SelectVehicle(int accidentId, int role)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var accident = await _context.Accidents.FirstOrDefaultAsync(a => a.AccidentId == accidentId);
            if (accident == null) return NotFound();

            var participant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p => p.AccidentId == accidentId && p.DriverUserId == currentUserId.Value);

            if (participant == null)
                return RedirectToAction("Waiting", new { accidentId, role });

            // Load only vehicles owned by the current logged-in driver.
            // يتم تحميل مركبات المستخدم الحالي فقط.
            var vehicles = await _context.Vehicles
                .Where(v => v.DriverUserId == currentUserId.Value)
                .OrderByDescending(v => v.VehicleId)
                .Select(v => new VehicleOption
                {
                    VehicleId = v.VehicleId,
                    Title = $"{(v.Model ?? "مركبة")} {(v.Year.HasValue ? v.Year.Value.ToString() : "")}".Trim(),
                    Sub = $"{v.LicensePlate} • {v.Color ?? "—"}"
                })
                .ToListAsync();

            var vm = new SelectVehicleViewModel
            {
                AccidentId = accidentId,
                Role = participant.Role,
                SelectedVehicleId = participant.VehicleId,
                Vehicles = vehicles
            };

            return View(vm);
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectVehicle(SelectVehicleViewModel vm)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            if (!ModelState.IsValid)
            {
                ViewBag.FromPost = true;

                vm.Vehicles = await _context.Vehicles
                    .Where(v => v.DriverUserId == currentUserId.Value)
                    .OrderByDescending(v => v.VehicleId)
                    .Select(v => new VehicleOption
                    {
                        VehicleId = v.VehicleId,
                        Title = $"{(v.Model ?? "مركبة")} {(v.Year.HasValue ? v.Year.Value.ToString() : "")}".Trim(),
                        Sub = $"{v.LicensePlate}"
                    })
                    .ToListAsync();

                return View(vm);
            }

            var participant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p => p.AccidentId == vm.AccidentId && p.DriverUserId == currentUserId.Value);

            if (participant == null)
            {
                ModelState.AddModelError("", "تعذر العثور على مشاركتك في الحادث.");
                ViewBag.FromPost = true;
                return View(vm);
            }

            participant.VehicleId = vm.SelectedVehicleId;
            vm.Role = participant.Role;

            // Link the selected vehicle to the accident if the relationship does not already exist.
            // نربط المركبة المختارة بالحادث إذا لم تكن العلاقة موجودة مسبقًا.
            bool involveExists = await _context.Involves
                .AnyAsync(i => i.AccidentId == vm.AccidentId
                            && i.VehicleId == vm.SelectedVehicleId);

            if (!involveExists)
            {
                _context.Involves.Add(new Involve
                {
                    AccidentId = vm.AccidentId,
                    VehicleId = vm.SelectedVehicleId!.Value,
                    VehicleRole = participant.Role
                });
            }

            participant.CurrentStep = "Questions";
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Questions), new { accidentId = vm.AccidentId, role = participant.Role, index = 1 });
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddVehicle(SelectVehicleViewModel vm)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var participant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p => p.AccidentId == vm.AccidentId && p.DriverUserId == currentUserId.Value);

            if (participant == null)
            {
                TempData["ToastError"] = "تعذر العثور على مشاركتك في الحادث.";
                return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = vm.Role });
            }

            if (string.IsNullOrWhiteSpace(vm.NewLicensePlate))
            {
                TempData["ToastError"] = "يرجى إدخال رقم اللوحة.";
                return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = participant.Role });
            }

            // Normalize plate before checking duplicate.
            // توحيد صيغة اللوحة قبل فحص التكرار.
            // Normalize the license plate to prevent duplicates caused by letter casing or extra spaces.
            // نوحد صيغة اللوحة لمنع التكرار بسبب اختلاف الحروف أو المسافات.
            var normalizedPlate = vm.NewLicensePlate.Trim().ToUpper();

            bool plateExists = await _context.Vehicles
                .AnyAsync(v => v.LicensePlate != null &&
                               v.LicensePlate.Trim().ToUpper() == normalizedPlate);

            if (plateExists)
            {
                TempData["ToastError"] = "رقم اللوحة مسجل مسبقًا. يرجى اختيار المركبة من القائمة أو إدخال لوحة مختلفة.";
                return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = participant.Role });
            }

            var vehicle = new Vehicle
            {
                DriverUserId = currentUserId.Value,
                LicensePlate = normalizedPlate,
                Model = string.IsNullOrWhiteSpace(vm.NewModel) ? null : vm.NewModel.Trim(),
                Year = vm.NewYear
            };

            _context.Vehicles.Add(vehicle);
            await _context.SaveChangesAsync();



            return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = participant.Role });
        }

        // =========================================================
        // 11) Core Questions
        // ١١) الأسئلة الأساسية
        // =========================================================
        // Core questions collect each driver's own description of the accident circumstances.
        // تجمع الأسئلة الأساسية وصف كل سائق لظروف الحادث من وجهة نظره.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> Questions(int accidentId, int role, int? index)
        {
            ViewData["WizardAction"] = "Questions";
            ViewData["WizardTitle"] = "الأسئلة الأساسية";

            if (role != 1 && role != 2) return BadRequest("Role invalid.");

            var reportRedirect = await RedirectIfReportExistsAsync(accidentId, role);
            if (reportRedirect != null)
                return reportRedirect;

            int i = index ?? 1;

            var vm = await _questionnaireService.GetCoreQuestionAsync(accidentId, role, i);
            if (vm == null)
            {
                TempData["ToastError"] = "لا توجد أسئلة Core في قاعدة البيانات.";
                return RedirectToAction("HomePage", "Home");
            }

            return View(vm);
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Questions(QuestionsWizardViewModel vm, string nav)
        {
            var reportRedirect = await RedirectIfReportExistsAsync(vm.AccidentId, vm.Role);
            if (reportRedirect != null)
                return reportRedirect;

            if (nav == "back")
            {
                int prev = vm.Index - 1;
                if (prev < 1) prev = 1;
                return RedirectToAction(nameof(Questions), new { accidentId = vm.AccidentId, role = vm.Role, index = prev });
            }

            if (!ModelState.IsValid)
            {
                var reload = await _questionnaireService.GetCoreQuestionAsync(vm.AccidentId, vm.Role, vm.Index);
                if (reload == null) return RedirectToAction("HomePage", "Home");

                reload.SelectedOptionCode = vm.SelectedOptionCode;
                return View(reload);
            }

            await _questionnaireService.SaveAnswerAsync(vm.AccidentId, vm.Role, vm.QuestionId, vm.SelectedOptionCode!);

            // CQ6 affects the routing logic; if the other driver has not answered it yet, wait before continuing.
            // سؤال CQ6 يؤثر على مسار الأسئلة؛ إذا لم يجب الطرف الآخر عليه بعد، ننتظر قبل المتابعة.
            if (string.Equals(vm.QuestionCode, "CQ6", StringComparison.OrdinalIgnoreCase))
            {
                int otherRole = vm.Role == 1 ? 2 : 1;
                int? otherDriverUserId = await GetDriverUserIdByRoleAsync(vm.AccidentId, otherRole);

                bool otherAnsweredCQ6 = false;

                if (otherDriverUserId.HasValue)
                {
                    otherAnsweredCQ6 = await _context.Answers
                        .Where(a => a.AccidentId == vm.AccidentId && a.DriverUserId == otherDriverUserId.Value)
                        .Join(_context.Questions,
                              a => a.QuestionId,
                              q => q.QuestionId,
                              (a, q) => new { q.QuestionCode })
                        .AnyAsync(x => x.QuestionCode == "CQ6");
                }

                if (!otherAnsweredCQ6)
                {
                    return RedirectToAction(nameof(WaitOnRouting), new
                    {
                        accidentId = vm.AccidentId,
                        role = vm.Role,
                        currentQuestionId = vm.QuestionId,
                        routingCode = "CQ6"
                    });
                }
            }

            int next = vm.Index + 1;

            if (vm.Index >= vm.Total)
            {
                await SetStep(vm.AccidentId, vm.Role, "MirrorQuestions");

                return RedirectToAction(nameof(MirrorQuestions), new
                {
                    accidentId = vm.AccidentId,
                    role = vm.Role,
                    index = 1
                });
            }

            return RedirectToAction(nameof(Questions), new { accidentId = vm.AccidentId, role = vm.Role, index = next });
        }

        [AuthorizeUser]
        [HttpGet]
        public IActionResult WaitOnRouting(int accidentId, int role, int currentQuestionId, string routingCode)
        {
            ViewData["RoutingCode"] = routingCode;
            ViewData["CurrentQuestionId"] = currentQuestionId;

            return View("MirrorDone", new AccidentWaitingViewModel
            {
                AccidentId = accidentId,
                AccidentCode = $"ACC-{accidentId:000000}",
                Role = role
            });
        }

        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> WaitOnRoutingStatus(int accidentId, int role, int currentQuestionId, string routingCode)
        {
            int otherRole = role == 1 ? 2 : 1;
            int? otherDriverUserId = await GetDriverUserIdByRoleAsync(accidentId, otherRole);

            bool otherAnswered = false;

            if (otherDriverUserId.HasValue)
            {
                otherAnswered = await _context.Answers
                    .Where(a => a.AccidentId == accidentId && a.DriverUserId == otherDriverUserId.Value)
                    .Join(_context.Questions,
                          a => a.QuestionId,
                          q => q.QuestionId,
                          (a, q) => new { q.QuestionCode })
                    .AnyAsync(x => x.QuestionCode == routingCode);
            }

            if (!otherAnswered)
            {
                return Json(new { ready = false });
            }

            int nextIndex = await _questionnaireService.GetNextCoreIndexAsync(accidentId, role, currentQuestionId);

            return Json(new
            {
                ready = true,
                redirectUrl = Url.Action(nameof(Questions), "Accident", new { accidentId, role, index = nextIndex })
            });
        }

        // =========================================================
        // 12) Mirror Questions
        // ١٢) أسئلة التحقق
        // =========================================================
        // Mirror questions ask each driver about the other party to help detect contradictions.
        // تسأل أسئلة التحقق كل سائق عن الطرف الآخر للمساعدة في اكتشاف التناقضات.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> MirrorQuestions(int accidentId, int role, int? index)
        {
            ViewData["WizardAction"] = "MirrorQuestions";
            ViewData["WizardTitle"] = "أسئلة التحقق";

            if (role != 1 && role != 2) return BadRequest("Role invalid.");

            var reportRedirect = await RedirectIfReportExistsAsync(accidentId, role);
            if (reportRedirect != null)
                return reportRedirect;

            int i = index ?? 1;

            var vm = await _questionnaireService.GetMirrorQuestionAsync(accidentId, role, i);
            if (vm == null)
            {
                TempData["ToastError"] = "لا توجد أسئلة Mirror في قاعدة البيانات.";
                return RedirectToAction("HomePage", "Home");
            }

            ViewData["StageTitle"] = "الأسئلة الأساسية (Mirror)";
            return View("Questions", vm);
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MirrorQuestions(QuestionsWizardViewModel vm, string nav)
        {
            var reportRedirect = await RedirectIfReportExistsAsync(vm.AccidentId, vm.Role);
            if (reportRedirect != null)
                return reportRedirect;

            if (nav == "back")
            {
                int prev = vm.Index - 1;
                if (prev < 1) prev = 1;
                return RedirectToAction(nameof(MirrorQuestions), new { accidentId = vm.AccidentId, role = vm.Role, index = prev });
            }

            if (!ModelState.IsValid)
            {
                var reload = await _questionnaireService.GetMirrorQuestionAsync(vm.AccidentId, vm.Role, vm.Index);
                if (reload == null) return RedirectToAction("HomePage", "Home");

                reload.SelectedOptionCode = vm.SelectedOptionCode;
                return View("Questions", reload);
            }

            await _questionnaireService.SaveAnswerAsync(vm.AccidentId, vm.Role, vm.QuestionId, vm.SelectedOptionCode!);

            int next = vm.Index + 1;

            if (vm.Index >= vm.Total)
            {
                await SetStep(vm.AccidentId, vm.Role, "MirrorDone");

                bool bothDone = await _context.AccidentSessionParticipants
                    .Where(p => p.AccidentId == vm.AccidentId && p.IsJoined)
                    .AllAsync(p => p.CurrentStep == "MirrorDone");

                if (bothDone)
                {
                    return RedirectToAction(nameof(Reviewing), new { accidentId = vm.AccidentId, role = vm.Role });
                }

                return RedirectToAction(nameof(MirrorDone), new { accidentId = vm.AccidentId, role = vm.Role });
            }

            return RedirectToAction(nameof(MirrorQuestions), new { accidentId = vm.AccidentId, role = vm.Role, index = next });
        }

        [AuthorizeUser]
        [HttpGet]
        public IActionResult MirrorDone(int accidentId, int role)
        {
            return View(new AccidentWaitingViewModel
            {
                AccidentId = accidentId,
                AccidentCode = $"ACC-{accidentId:000000}",
                Role = role
            });
        }

        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> MirrorDoneStatus(int accidentId, int role)
        {
            // A driver is considered done with mirror questions if they reached MirrorDone or any later step.
            // يعتبر السائق منتهيًا من أسئلة التحقق إذا وصل إلى MirrorDone أو أي خطوة بعدها.
            var completedMirrorSteps = new[]
            {
                "MirrorDone",
                "FreeText",
                "FinalResult"
            };

            bool bothDone = await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == accidentId && p.IsJoined)
                .AllAsync(p => p.CurrentStep != null && completedMirrorSteps.Contains(p.CurrentStep));

            return Json(new
            {
                bothDone,
                redirectUrl = Url.Action(nameof(Reviewing), "Accident", new { accidentId, role })
            });
        }

        // =========================================================
        // 13) Conflict Back Questions
        // ١٣) أسئلة الرجوع عند وجود تعارض
        // =========================================================
        // If contradictions are detected, the system asks additional pack-based questions
        // to clarify the conflicting answers before generating the final rule-based result.
        // إذا تم اكتشاف تعارضات، يطرح النظام أسئلة إضافية مجمعة حسب نوع التعارض
        // لتوضيح الإجابات المتضاربة قبل توليد نتيجة القواعد النهائية.
        [AuthorizeUser]
        [HttpGet]
        public IActionResult ConflictBackEntry(int accidentId, int role, string packName)
        {
            if (role != 1 && role != 2)
                return BadRequest("Role invalid.");

            return RedirectToAction(nameof(ConflictBack), new
            {
                accidentId,
                role,
                packName,
                index = 1
            });
        }

        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> Reviewing(int accidentId, int role)
        {
            if (role != 1 && role != 2)
                return BadRequest("Role invalid.");

            var reportRedirect = await RedirectIfReportExistsAsync(accidentId, role);
            if (reportRedirect != null)
                return reportRedirect;

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null) return NotFound();

            // Conflict detection is executed once unless conflicts already exist for this accident.
            // يتم تشغيل اكتشاف التعارضات مرة واحدة ما لم تكن التعارضات موجودة مسبقًا لهذا الحادث.
            bool hasExistingConflicts = await _context.AccidentConflicts
                .AnyAsync(c => c.AccidentId == accidentId);

            if (!hasExistingConflicts)
            {
                await _conflictService.DetectAndUpsertConflictsAsync(accidentId);
                await _conflictPackService.ClearConflictBackAnswersAsync(accidentId);
            }

            var nextPack = await _conflictPackService.GetNextPendingPackAsync(accidentId);

            if (!string.IsNullOrWhiteSpace(nextPack))
            {
                return RedirectToAction(nameof(ConflictBackEntry), new
                {
                    accidentId,
                    role,
                    packName = nextPack
                });
            }

            await SetStep(accidentId, role, "FreeText");
            return RedirectToAction(nameof(FreeText), new { accidentId, role });
        }
        // Saves the current workflow step for resume support.
        // يحفظ الخطوة الحالية لدعم استكمال المسار لاحقًا.
        private async Task SetStep(int accidentId, int role, string step)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return;

            var participant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(x =>
                    x.AccidentId == accidentId &&
                    x.DriverUserId == currentUserId.Value &&
                    x.Role == role);

            if (participant == null) return;

            participant.CurrentStep = step;
            await _context.SaveChangesAsync();
        }

        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> ConflictBack(int accidentId, int role, string packName, int? index)
        {
            if (role != 1 && role != 2) return BadRequest("Role invalid.");

            var reportRedirect = await RedirectIfReportExistsAsync(accidentId, role);
            if (reportRedirect != null)
                return reportRedirect;

            int i = index ?? 1;

            var vm = await _conflictPackService.GetPackQuestionAsync(accidentId, role, packName, i);
            if (vm == null)
            {
                TempData["ToastError"] = "لا توجد أسئلة لهذا الباك.";
                return RedirectToAction(nameof(Reviewing), new { accidentId, role });
            }

            ViewData["WizardAction"] = "ConflictBack";
            ViewData["WizardTitle"] = "أسئلة التحقق الإضافية";

            return View(vm);
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConflictBack(ConflictBackWizardViewModel vm, string nav)
        {
            var reportRedirect = await RedirectIfReportExistsAsync(vm.AccidentId, vm.Role);
            if (reportRedirect != null)
                return reportRedirect;

            if (nav == "back")
            {
                int prev = vm.Index - 1;
                if (prev < 1) prev = 1;

                return RedirectToAction(nameof(ConflictBack), new
                {
                    accidentId = vm.AccidentId,
                    role = vm.Role,
                    packName = vm.PackName,
                    index = prev
                });
            }

            if (!ModelState.IsValid)
            {
                var reload = await _conflictPackService.GetPackQuestionAsync(vm.AccidentId, vm.Role, vm.PackName, vm.Index);
                if (reload == null)
                    return RedirectToAction(nameof(Reviewing), new { accidentId = vm.AccidentId, role = vm.Role });

                reload.SelectedOptionCode = vm.SelectedOptionCode;
                return View(reload);
            }

            await _conflictPackService.SavePackAnswerAsync(vm.AccidentId, vm.Role, vm.QuestionId, vm.SelectedOptionCode!);

            int next = vm.Index + 1;

            if (vm.Index >= vm.Total)
            {
                bool bothDone = await _conflictPackService
                    .IsPackCompletedByBothDriversAsync(vm.AccidentId, vm.PackName);

                if (!bothDone)
                {
                    return RedirectToAction(nameof(ConflictBackDone), new
                    {
                        accidentId = vm.AccidentId,
                        role = vm.Role,
                        packName = vm.PackName
                    });
                }

                await _conflictPackService.MarkPackConflictsResolvedAsync(vm.AccidentId, vm.PackName);

                var nextPack = await _conflictPackService.GetNextPendingPackAsync(vm.AccidentId);

                if (!string.IsNullOrWhiteSpace(nextPack))
                {
                    return RedirectToAction(nameof(ConflictBackEntry), new
                    {
                        accidentId = vm.AccidentId,
                        role = vm.Role,
                        packName = nextPack
                    });
                }

                return RedirectToAction(nameof(Reviewing), new
                {
                    accidentId = vm.AccidentId,
                    role = vm.Role
                });
            }

            return RedirectToAction(nameof(ConflictBack), new
            {
                accidentId = vm.AccidentId,
                role = vm.Role,
                packName = vm.PackName,
                index = next
            });
        }

        [AuthorizeUser]
        [HttpGet]
        public IActionResult ConflictBackDone(int accidentId, int role, string packName)
        {
            ViewData["WaitMode"] = "ConflictBack";
            ViewData["PackName"] = packName;

            return View("MirrorDone", new AccidentWaitingViewModel
            {
                AccidentId = accidentId,
                AccidentCode = $"ACC-{accidentId:000000}",
                Role = role
            });
        }

        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> ConflictBackDoneStatus(int accidentId, int role, string packName)
        {
            bool bothDone = await _conflictPackService.IsPackCompletedByBothDriversAsync(accidentId, packName);

            if (!bothDone)
            {
                return Json(new { ready = false });
            }

            await _conflictPackService.MarkPackConflictsResolvedAsync(accidentId, packName);

            var nextPack = await _conflictPackService.GetNextPendingPackAsync(accidentId);

            return Json(new
            {
                ready = true,
                redirectUrl = !string.IsNullOrWhiteSpace(nextPack)
                    ? Url.Action(nameof(ConflictBackEntry), "Accident", new
                    {
                        accidentId,
                        role,
                        packName = nextPack
                    })
                    : Url.Action(nameof(Reviewing), "Accident", new { accidentId, role })
            });
        }

        // =========================================================
        // 14) Free Text and Rule Engine Result
        // ١٤) النص الحر ونتيجة محرك القواعد
        // =========================================================
        // The optional free-text description is saved, then the liability rule engine evaluates the accident
        // and stores the preliminary report result.
        // يتم حفظ الوصف النصي الاختياري، ثم يقوم محرك قواعد المسؤولية بتقييم الحادث
        // وحفظ نتيجة التقرير الأولي.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> FreeText(int accidentId, int role)
        {
            if (role != 1 && role != 2)
                return BadRequest("Role invalid.");

            var reportRedirect = await RedirectIfReportExistsAsync(accidentId, role);
            if (reportRedirect != null)
                return reportRedirect;

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null)
                return NotFound();

            var question = await _context.Questions
                .FirstOrDefaultAsync(q => q.QuestionCode == FreeTextQuestionCode);

            if (question == null)
            {
                TempData["ToastError"] = "لم يتم العثور على سؤال FreeText في قاعدة البيانات.";
                return RedirectToAction(nameof(Reviewing), new { accidentId, role });
            }

            var existingAnswer = await _context.Answers
                .FirstOrDefaultAsync(a =>
                    a.AccidentId == accidentId &&
                    a.DriverUserId == currentUserId.Value &&
                    a.QuestionId == question.QuestionId);

            var vm = new DynamicQuestionsViewModel
            {
                AccidentId = accidentId,
                Role = role,
                QuestionId = question.QuestionId,
                FreeText = existingAnswer?.FreeText
            };

            return View(vm);
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FreeText(DynamicQuestionsViewModel vm)
        {
            if (vm.Role != 1 && vm.Role != 2)
                return BadRequest("Role invalid.");

            var reportRedirect = await RedirectIfReportExistsAsync(vm.AccidentId, vm.Role);
            if (reportRedirect != null)
                return reportRedirect;

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == vm.AccidentId);

            if (accident == null)
                return NotFound();

            var question = await _context.Questions
                .FirstOrDefaultAsync(q => q.QuestionId == vm.QuestionId && q.QuestionCode == FreeTextQuestionCode);

            if (question == null)
            {
                TempData["ToastError"] = "تعذر حفظ النص الحر لعدم العثور على السؤال المرتبط.";
                return RedirectToAction(nameof(Reviewing), new { accidentId = vm.AccidentId, role = vm.Role });
            }

            string? normalizedText = string.IsNullOrWhiteSpace(vm.FreeText)
                ? null
                : vm.FreeText.Trim();

            var answer = await _context.Answers
                .FirstOrDefaultAsync(a =>
                    a.AccidentId == vm.AccidentId &&
                    a.DriverUserId == currentUserId.Value &&
                    a.QuestionId == vm.QuestionId);

            if (answer == null)
            {
                answer = new Answer
                {
                    AccidentId = vm.AccidentId,
                    DriverUserId = currentUserId.Value,
                    QuestionId = vm.QuestionId,
                    SelectedOptionCode = null,
                    FreeText = normalizedText
                };

                _context.Answers.Add(answer);
            }
            else
            {
                answer.FreeText = normalizedText;
            }

            await _context.SaveChangesAsync();

            // After saving the final narrative, evaluate liability using the expert rule engine.
            // بعد حفظ الوصف النهائي، يتم تقييم المسؤولية باستخدام محرك القواعد الخبير.
            var ruleResult = await _liabilityRuleEngineService.EvaluateAsync(vm.AccidentId);
            await _liabilityRuleEngineService.SaveResultAsync(vm.AccidentId, ruleResult);

            await SetStep(vm.AccidentId, vm.Role, "FinalResult");

            return RedirectToAction(nameof(FinalResult), new
            {
                accidentId = vm.AccidentId,
                role = vm.Role
            });
        }

        // =========================================================
        // 15) Final Result
        // ١٥) النتيجة النهائية
        // =========================================================
        // Builds the final result view model using the saved report, accident information,
        // damage image predictions, segmentation outputs, and conflict status.
        // يبني نموذج عرض النتيجة النهائية بالاعتماد على التقرير المحفوظ، وبيانات الحادث،
        // ونتائج تصنيف صور الضرر، ومخرجات التقسيم، وحالة التعارضات.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> FinalResult(int accidentId, int role)
        {
            if (role != 1 && role != 2)
                return BadRequest("Role invalid.");

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null)
                return NotFound();

            var report = await _context.AccidentReports
                .FirstOrDefaultAsync(r => r.AccidentId == accidentId);

            if (report == null)
            {
                TempData["ToastError"] = "لم يتم إنشاء التقرير بعد.";
                return RedirectToAction(nameof(Reviewing), new { accidentId, role });
            }

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            // Load only the current driver's damage images to display their own AI results.
            // نحمل صور الضرر الخاصة بالسائق الحالي فقط لعرض نتائج التحليل الخاصة به.
            var damageImages = await _context.Images
                .Include(i => i.ImageSegmentationDetections)
                .Where(i => i.AccidentId == accidentId
                         && i.DriverUserId == currentUserId.Value
                         && (i.Label == "Damage1" || i.Label == "Damage2"))
                .OrderBy(i => i.ImageId)
                .ToListAsync();

            var damage1 = damageImages.FirstOrDefault(i => i.Label == "Damage1");
            var damage2 = damageImages.FirstOrDefault(i => i.Label == "Damage2");

            var vm = new FinalResultViewModel
            {
                AccidentId = accidentId,
                Role = role,
                AccidentCode = $"ACC-{accidentId:000000}",
                AccidentDate = accident.AccidentDate,
                AccidentTime = accident.AccidentTime,
                Location = accident.Location ?? "—",

                RuleId = report.RuleId ?? "—",
                AccidentClassification = report.AccidentClassification ?? report.Summary ?? "—",
                FaultPercentDriver1 = report.FaultPercentDriver1 ?? 0,
                FaultPercentDriver2 = report.FaultPercentDriver2 ?? 0,
                FinalConfidenceScore = report.FinalConfidenceScore ?? 0,
                FinalConfidenceLabel = report.FinalConfidenceLabel ?? "—",
                DecisionExplanation = report.DecisionExplanation ?? "—",

                Damage1PredictedLabel = damage1?.PredictedLabel,
                Damage1PredictionConfidence = damage1?.PredictionConfidence,

                Damage2PredictedLabel = damage2?.PredictedLabel,
                Damage2PredictionConfidence = damage2?.PredictionConfidence,

                Damage1SegmentationResultPath = damage1?.SegmentationResultPath,
                Damage1SegmentationHasDamage = damage1?.SegmentationHasDamage,
                Damage1SegmentationDetections = damage1?.ImageSegmentationDetections
                    .Select(d => new SegmentationDetectionDisplayItem
                    {
                        Label = d.DamageLabel ?? "",
                        Confidence = d.Confidence
                    }).ToList() ?? new List<SegmentationDetectionDisplayItem>(),

                Damage2SegmentationResultPath = damage2?.SegmentationResultPath,
                Damage2SegmentationHasDamage = damage2?.SegmentationHasDamage,
                Damage2SegmentationDetections = damage2?.ImageSegmentationDetections
                    .Select(d => new SegmentationDetectionDisplayItem
                    {
                        Label = d.DamageLabel ?? "",
                        Confidence = d.Confidence
                    }).ToList() ?? new List<SegmentationDetectionDisplayItem>(),

                HasConflicts = await _context.AccidentConflicts.AnyAsync(c => c.AccidentId == accidentId)
            };

            return View(vm);
        }
        // =========================================================
        // 16) Driver Feedback
        // ١٦) تقييم السائق
        // =========================================================
        // Drivers can submit their satisfaction level and comment after viewing the final result.
        // يستطيع السائق إرسال مستوى الرضا والتعليق بعد مشاهدة النتيجة النهائية.
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> Feedback(int accidentId, int role)
        {
            if (role != 1 && role != 2)
                return BadRequest("Role invalid.");

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null)
                return NotFound();

            var existingFeedback = await _context.DriverFeedbacks
                .FirstOrDefaultAsync(f => f.AccidentId == accidentId && f.DriverUserId == currentUserId.Value);

            var vm = new DriverFeedbackViewModel
            {
                AccidentId = accidentId,
                Role = role,
                SatisfactionLevel = existingFeedback?.SatisfactionLevel,
                Comment = existingFeedback?.Comment
            };

            return View(vm);
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Feedback(DriverFeedbackViewModel vm)
        {
            if (vm.Role != 1 && vm.Role != 2)
                return BadRequest("Role invalid.");

            if (!ModelState.IsValid)
                return View(vm);

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == vm.AccidentId);

            if (accident == null)
                return NotFound();

            // If feedback already exists, update it instead of creating duplicate feedback for the same accident and driver.
            // إذا كان التقييم موجودًا مسبقًا، يتم تحديثه بدل إنشاء تقييم مكرر لنفس الحادث والسائق.
            var feedback = await _context.DriverFeedbacks
                .FirstOrDefaultAsync(f => f.AccidentId == vm.AccidentId && f.DriverUserId == currentUserId.Value);

            if (feedback == null)
            {
                feedback = new DriverFeedback
                {
                    AccidentId = vm.AccidentId,
                    DriverUserId = currentUserId.Value,
                    SatisfactionLevel = vm.SatisfactionLevel!.Value,
                    Comment = vm.Comment!.Trim(),
                    FeedbackDate = DateTime.Now
                };

                _context.DriverFeedbacks.Add(feedback);
            }
            else
            {
                feedback.SatisfactionLevel = vm.SatisfactionLevel!.Value;
                feedback.Comment = vm.Comment!.Trim();
                feedback.FeedbackDate = DateTime.Now;
            }

            await _context.SaveChangesAsync();

            TempData["ToastSuccess"] = "تم إرسال تقييمك بنجاح. شكرًا لك.";

            return RedirectToAction(nameof(FeedbackSubmitted));
        }


        [AuthorizeUser]
        [HttpGet]
        public IActionResult FeedbackSubmitted()
        {
            return View();
        }
    }
}