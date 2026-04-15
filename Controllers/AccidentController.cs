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
    public class AccidentController : Controller
    {
        private readonly AounDbContext _context;

        private readonly QuestionnaireService _questionnaireService;
        private readonly ConflictService _conflictService;
        private readonly ConflictPackService _conflictPackService;
        private readonly LiabilityRuleEngineService _liabilityRuleEngineService;
        private readonly IHttpClientFactory _httpClientFactory;

        private const string FreeTextQuestionCode = "FREE_TEXT_ACCIDENT_DESC";

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
        // Session / Participant Helpers
        // =========================================================
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
        // 1) Screening
        // =========================================================
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
        // 2) Location (Create Accident)
        // =========================================================
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
        // 3) Success (Shows Code + QR)
        // =========================================================
        [AuthorizeUser]
        [HttpGet]
        public IActionResult Success(int accidentId)
        {
            var code = $"ACC-{accidentId:000000}";
            var vm = new AccidentSuccessViewModel
            {
                AccidentId = accidentId,
                AccidentCode = code
            };

            return View(vm);
        }

        // =========================================================
        // 4) Waiting Page + Polling API
        // =========================================================
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> Waiting(int accidentId, int role)
        {
            var accident = await _context.Accidents.FirstOrDefaultAsync(a => a.AccidentId == accidentId);
            if (accident == null) return NotFound();

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
                    redirectUrl = Url.Action("Login", "Auth")
                });
            }

            var currentParticipant = await _context.AccidentSessionParticipants
                .FirstOrDefaultAsync(p => p.AccidentId == accidentId && p.DriverUserId == currentUserId.Value);

            if (currentParticipant == null)
            {
                return Json(new
                {
                    roomReady = false,
                    redirectUrl = Url.Action("Join", "Accident")
                });
            }

            var joinedCount = await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == accidentId && p.IsJoined)
                .CountAsync();

            bool roomReady = joinedCount >= 2;

            if (roomReady)
            {
                var accident = await _context.Accidents.FirstOrDefaultAsync(a => a.AccidentId == accidentId);
                if (accident != null && accident.Status != "فعال")
                {
                    accident.Status = "فعال";
                    await _context.SaveChangesAsync();
                }
            }

            return Json(new
            {
                roomReady,
                redirectUrl = Url.Action("UploadPhotos", "Accident", new
                {
                    accidentId,
                    role = currentParticipant.Role
                })
            });
        }

        // =========================================================
        // 5) Join UI Page (Enter Code / Scan QR)
        // =========================================================
        [AuthorizeUser]
        [HttpGet]
        public IActionResult Join()
        {
            return View();
        }

        // =========================================================
        // 6) JoinByCode (POST) - used by manual code and QR redirect
        // =========================================================
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
                TempData["JoinError"] = "الرجاء إدخال رمز الحادث.";
                return RedirectToAction("Join");
            }

            code = code.Trim();

            int accidentId = ExtractAccidentId(code);

            if (accidentId <= 0)
            {
                TempData["JoinError"] = "رمز الحادث غير صحيح.";
                return RedirectToAction("Join");
            }

            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null)
            {
                TempData["JoinError"] = "لا يوجد حادث بهذا الرقم.";
                return RedirectToAction("Join");
            }

            int driverUserId = currentUserId.Value;

            bool alreadyJoined = await _context.AccidentSessionParticipants
                .AnyAsync(p => p.AccidentId == accidentId && p.DriverUserId == driverUserId);

            if (!alreadyJoined)
            {
                int count = await _context.AccidentSessionParticipants
                    .CountAsync(p => p.AccidentId == accidentId);

                if (count >= 2)
                {
                    TempData["JoinError"] = "تم اكتمال أطراف الحادث، لا يمكن الانضمام.";
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
            }
            else
            {
                var existingParticipant = await _context.AccidentSessionParticipants
                    .FirstOrDefaultAsync(p => p.AccidentId == accidentId && p.DriverUserId == driverUserId);

                if (existingParticipant != null && existingParticipant.Role == 1)
                {
                    TempData["JoinError"] = "أنتِ منشئة هذا الحادث بالفعل ولا يمكنك الانضمام إليه كطرف ثانٍ.";
                    return RedirectToAction("Join");
                }

                if (accident.Status != "فعال")
                {
                    accident.Status = "فعال";
                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToAction("JoinSuccess", new { accidentId = accidentId });
        }

        // =========================================================
        // 7) Join Success Page (Shows accident details + role)
        // =========================================================
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
        // Helpers
        // =========================================================
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

        // =========================================================
        // Upload Photos (GET)
        // =========================================================
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
        // Upload Photos (POST)
        // =========================================================
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

            var requiredFiles = new Dictionary<string, (IFormFile? File, string DisplayName)>
            {
                { "Front", (vm.FrontPhoto, "صورة الواجهة الأمامية") },
                { "Back",  (vm.BackPhoto,  "صورة الواجهة الخلفية") },
                { "Left",  (vm.LeftPhoto,  "صورة الجانب الأيسر") },
                { "Right", (vm.RightPhoto, "صورة الجانب الأيمن") },
                { "Plate", (vm.PlatePhoto, "صورة لوحة السيارة") },
                { "Scene", (vm.ScenePhoto, "صورة عامة لموقع الحادث") }
            };

            var optionalFiles = new Dictionary<string, (IFormFile? File, string DisplayName)>
            {
                { "Damage1", (vm.DamagePhoto1, "صورة الضرر الأولى") },
                { "Damage2", (vm.DamagePhoto2, "صورة الضرر الثانية") }
            };
            string? damage1Url = null;
            string? damage2Url = null;

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

                if (file != null && file.Length > 0 && !IsAllowedImage(file))
                {
                    ModelState.AddModelError("", $"صيغة {display} غير مدعومة. ارفعي JPG أو PNG فقط.");
                    ViewBag.FromPost = true;
                    return View(vm);
                }
            }

            var allFiles = requiredFiles
                .Concat(optionalFiles)
                .ToDictionary(x => x.Key, x => x.Value);

            var labels = allFiles.Keys.ToList();

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

            return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = role });
        }

        // =========================================================
        // Helpers (Images)
        // =========================================================
        private static bool IsAllowedImage(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png";
        }

        private static async Task<string> SaveImageAsync(IFormFile file, int accidentId, int role, string fileBaseName)
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

        private async Task<SinglePredictionResponse?> PredictSingleImageAsync(string physicalPath)
        {
            if (!System.IO.File.Exists(physicalPath))
                return null;

            var client = _httpClientFactory.CreateClient();

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

            return JsonSerializer.Deserialize<SinglePredictionResponse>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }

        private async Task<SegmentationPredictionResponse?> PredictSegmentationAsync(string physicalPath)
        {
            if (!System.IO.File.Exists(physicalPath))
                return null;

            var client = _httpClientFactory.CreateClient();

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

            return JsonSerializer.Deserialize<SegmentationPredictionResponse>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }

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
        // Select Vehicle
        // =========================================================
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

            participant.CurrentStep = "SelectVehicle";
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
                TempData["VehicleError"] = "تعذر العثور على مشاركتك في الحادث.";
                return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = vm.Role });
            }

            if (string.IsNullOrWhiteSpace(vm.NewLicensePlate))
            {
                TempData["VehicleError"] = "يرجى إدخال رقم اللوحة.";
                return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = participant.Role });
            }

            var vehicle = new Vehicle
            {
                DriverUserId = currentUserId.Value,
                LicensePlate = vm.NewLicensePlate.Trim(),
                Model = string.IsNullOrWhiteSpace(vm.NewModel) ? null : vm.NewModel.Trim(),
                Year = vm.NewYear
            };

            _context.Vehicles.Add(vehicle);
            await _context.SaveChangesAsync();

            return RedirectToAction("SelectVehicle", new { accidentId = vm.AccidentId, role = participant.Role });
        }

        // =========================================================
        // Questions
        // =========================================================
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> Questions(int accidentId, int role, int? index)
        {
            ViewData["WizardAction"] = "Questions";
            ViewData["WizardTitle"] = "الأسئلة الأساسية";

            if (role != 1 && role != 2) return BadRequest("Role invalid.");

            int i = index ?? 1;

            var vm = await _questionnaireService.GetCoreQuestionAsync(accidentId, role, i);
            if (vm == null)
            {
                TempData["JoinError"] = "لا توجد أسئلة Core في قاعدة البيانات.";
                return RedirectToAction("HomePage", "Home");
            }

            return View(vm);
        }

        [AuthorizeUser]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Questions(QuestionsWizardViewModel vm, string nav)
        {
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
                return RedirectToAction(nameof(MirrorQuestions), new { accidentId = vm.AccidentId, role = vm.Role, index = 1 });
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
        // MirrorQuestions Wizard
        // =========================================================
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> MirrorQuestions(int accidentId, int role, int? index)
        {
            ViewData["WizardAction"] = "MirrorQuestions";
            ViewData["WizardTitle"] = "أسئلة التحقق";

            if (role != 1 && role != 2) return BadRequest("Role invalid.");

            int i = index ?? 1;

            var vm = await _questionnaireService.GetMirrorQuestionAsync(accidentId, role, i);
            if (vm == null)
            {
                TempData["JoinError"] = "لا توجد أسئلة Mirror في قاعدة البيانات.";
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
            bool bothDone = await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == accidentId && p.IsJoined)
                .AllAsync(p => p.CurrentStep == "MirrorDone");

            return Json(new
            {
                bothDone,
                redirectUrl = Url.Action(nameof(Reviewing), "Accident", new { accidentId, role })
            });
        }

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
            var accident = await _context.Accidents
                .FirstOrDefaultAsync(a => a.AccidentId == accidentId);

            if (accident == null) return NotFound();

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

            return RedirectToAction(nameof(FreeText), new { accidentId, role });
        }
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

            int i = index ?? 1;

            var vm = await _conflictPackService.GetPackQuestionAsync(accidentId, role, packName, i);
            if (vm == null)
            {
                TempData["JoinError"] = "لا توجد أسئلة لهذا الباك.";
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
        // FreeText Page (Optional)
        // =========================================================
        [AuthorizeUser]
        [HttpGet]
        public async Task<IActionResult> FreeText(int accidentId, int role)
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

            var question = await _context.Questions
                .FirstOrDefaultAsync(q => q.QuestionCode == FreeTextQuestionCode);

            if (question == null)
            {
                TempData["JoinError"] = "لم يتم العثور على سؤال FreeText في قاعدة البيانات.";
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
                TempData["JoinError"] = "تعذر حفظ النص الحر لعدم العثور على السؤال المرتبط.";
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

            var ruleResult = await _liabilityRuleEngineService.EvaluateAsync(vm.AccidentId);
            await _liabilityRuleEngineService.SaveResultAsync(vm.AccidentId, ruleResult);

            return RedirectToAction(nameof(FinalResult), new
            {
                accidentId = vm.AccidentId,
                role = vm.Role
            });
        }

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
                TempData["JoinError"] = "لم يتم إنشاء التقرير بعد.";
                return RedirectToAction(nameof(Reviewing), new { accidentId, role });
            }

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

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
        // Feedback
        // =========================================================
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

            TempData["FeedbackSuccess"] = "تم إرسال تقييمك بنجاح. شكرًا لك.";

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