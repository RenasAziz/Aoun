using Aoun.Models;
using Aoun.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Controllers
{
    public class InspectorReportsController : Controller
    {
        private readonly AounDbContext _context;

        public InspectorReportsController(AounDbContext context)
        {
            _context = context;
        }

        private int? GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId");
        }

        private async Task<bool> CurrentUserIsInspectorAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return false;

            return await _context.Users
                .AnyAsync(u => u.UserId == userId.Value &&
                               u.Role != null &&
                               u.Role.ToLower() == "inspector");
        }

        public async Task<IActionResult> Index(string? search, string? statusFilter)
        {
            if (!await CurrentUserIsInspectorAsync())
                return Forbid();

            var query = _context.AccidentReports
                .AsNoTracking()
                .Include(r => r.Accident)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                query = query.Where(r => r.ApprovalStatus == statusFilter);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                int parsedAccidentId;
                bool isAccidentId = int.TryParse(search, out parsedAccidentId);

                query = query.Where(r =>
                    (r.AccidentClassification != null && r.AccidentClassification.Contains(search)) ||
                    (r.RuleId != null && r.RuleId.Contains(search)) ||
                    (r.Accident.Location != null && r.Accident.Location.Contains(search)) ||
                    (isAccidentId && r.AccidentId == parsedAccidentId));
            }

            var reports = await query
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new InspectorReportListItemViewModel
                {
                    ReportId = r.ReportId,
                    AccidentId = r.AccidentId,
                    Status = r.ApprovalStatus ?? "قيد المراجعة",
                    AccidentClassification = r.AccidentClassification ?? r.Summary ?? "—",
                    Location = r.Accident.Location ?? "—",
                    AccidentDate = r.Accident.AccidentDate,
                    AccidentTime = r.Accident.AccidentTime,
                    FaultPercentDriver1 = r.FaultPercentDriver1 ?? 0,
                    FaultPercentDriver2 = r.FaultPercentDriver2 ?? 0,
                    FinalConfidenceScore = r.FinalConfidenceScore ?? 0,
                    FinalConfidenceLabel = r.FinalConfidenceLabel ?? "—"
                })
                .ToListAsync();

            var vm = new InspectorReportsIndexViewModel
            {
                Search = search,
                StatusFilter = statusFilter,
                Reports = reports,
                TotalCount = await _context.AccidentReports.CountAsync(),
                PendingCount = await _context.AccidentReports.CountAsync(r => r.ApprovalStatus == "قيد المراجعة"),
                AcceptedCount = await _context.AccidentReports.CountAsync(r => r.ApprovalStatus == "مقبول"),
                RejectedCount = await _context.AccidentReports.CountAsync(r => r.ApprovalStatus == "مرفوض")
            };

            return View(vm);
        }

        public async Task<IActionResult> Details(int accidentId)
        {
            if (!await CurrentUserIsInspectorAsync())
                return Forbid();

            var report = await _context.AccidentReports
                .AsNoTracking()
                .Include(r => r.Accident)
                .Include(r => r.ReviewedByUser)
                .FirstOrDefaultAsync(r => r.AccidentId == accidentId);

            if (report == null)
                return NotFound();

            var participants = await _context.AccidentSessionParticipants
                .AsNoTracking()
                .Where(p => p.AccidentId == accidentId)
                .OrderBy(p => p.Role)
                .ToListAsync();

            var userIds = participants
                .Select(p => p.DriverUserId)
                .Distinct()
                .ToList();

            var users = await _context.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.UserId))
                .ToListAsync();

            var drivers = await _context.Drivers
                .AsNoTracking()
                .Where(d => userIds.Contains(d.UserId))
                .ToListAsync();

            var vehicleIds = participants
                .Where(p => p.VehicleId.HasValue)
                .Select(p => p.VehicleId!.Value)
                .Distinct()
                .ToList();

            var vehicles = await _context.Vehicles
                .AsNoTracking()
                .Where(v => vehicleIds.Contains(v.VehicleId))
                .ToListAsync();

            var freeTextQuestionId = await _context.Questions
                .AsNoTracking()
                .Where(q => q.QuestionCode == "FREE_TEXT_ACCIDENT_DESC")
                .Select(q => q.QuestionId)
                .FirstOrDefaultAsync();

            var freeTextAnswers = await _context.Answers
                .AsNoTracking()
                .Where(a => a.AccidentId == accidentId && a.QuestionId == freeTextQuestionId)
                .ToListAsync();

            InspectorPartyDetailsViewModel? BuildParty(byte role)
            {
                var participant = participants.FirstOrDefault(p => p.Role == role);
                if (participant == null) return null;

                var user = users.FirstOrDefault(u => u.UserId == participant.DriverUserId);
                var driver = drivers.FirstOrDefault(d => d.UserId == participant.DriverUserId);
                var vehicle = participant.VehicleId.HasValue
                    ? vehicles.FirstOrDefault(v => v.VehicleId == participant.VehicleId.Value)
                    : null;
                var freeText = freeTextAnswers
                    .FirstOrDefault(a => a.DriverUserId == participant.DriverUserId)?.FreeText;

                return new InspectorPartyDetailsViewModel
                {
                    UserId = participant.DriverUserId,
                    Role = role,
                    Name = driver?.DriverName ?? "—",
                    Email = user?.Email ?? "—",
                    PhoneNumber = user?.PhoneNumber ?? "—",
                    LicenseNumber = driver?.LicenseNumber ?? "—",
                    VehiclePlate = vehicle?.LicensePlate ?? "—",
                    VehicleModel = vehicle?.Model ?? "—",
                    VehicleColor = vehicle?.Color ?? "—",
                    VehicleYear = vehicle?.Year,
                    FreeText = freeText
                };
            }

            var questionRows = await (
                from q in _context.Questions.AsNoTracking()
                join a in _context.Answers.AsNoTracking().Where(x => x.AccidentId == accidentId)
                    on q.QuestionId equals a.QuestionId into answerGroup
                from a in answerGroup.DefaultIfEmpty()
                select new
                {
                    q.QuestionId,
                    q.QuestionCode,
                    q.QuestionTextAr,
                    q.QuestionType,
                    q.PackName,
                    DriverUserId = a != null ? a.DriverUserId : 0,
                    a.SelectedOptionCode,
                    a.FreeText
                }
            ).ToListAsync();

            string GetAnswerCode(string questionCode, int driverUserId)
            {
                return questionRows
                    .Where(x => x.QuestionCode == questionCode && x.DriverUserId == driverUserId)
                    .Select(x => x.SelectedOptionCode)
                    .FirstOrDefault() ?? "—";
            }

            string GetFreeText(string questionCode, int driverUserId)
            {
                return questionRows
                    .Where(x => x.QuestionCode == questionCode && x.DriverUserId == driverUserId)
                    .Select(x => x.FreeText)
                    .FirstOrDefault() ?? "";
            }

            var questionMeta = questionRows
                .GroupBy(x => new { x.QuestionId, x.QuestionCode, x.QuestionTextAr, x.QuestionType, x.PackName })
                .Select(g => g.Key)
                .OrderBy(x => x.QuestionType)
                .ThenBy(x => x.QuestionId)
                .ToList();

            var role1UserId = participants.FirstOrDefault(p => p.Role == 1)?.DriverUserId ?? 0;
            var role2UserId = participants.FirstOrDefault(p => p.Role == 2)?.DriverUserId ?? 0;

            var allAnswers = questionMeta
                .Select(q => new InspectorAnswerCompareItemViewModel
                {
                    QuestionCode = q.QuestionCode ?? "",
                    QuestionTextAr = q.QuestionTextAr ?? "",
                    QuestionType = q.QuestionType ?? "",
                    PackName = q.PackName,
                    Driver1AnswerCode = role1UserId > 0 ? GetAnswerCode(q.QuestionCode ?? "", role1UserId) : "—",
                    Driver2AnswerCode = role2UserId > 0 ? GetAnswerCode(q.QuestionCode ?? "", role2UserId) : "—",
                    Driver1FreeText = role1UserId > 0 ? GetFreeText(q.QuestionCode ?? "", role1UserId) : "",
                    Driver2FreeText = role2UserId > 0 ? GetFreeText(q.QuestionCode ?? "", role2UserId) : ""
                })
                .ToList();

            var conflicts = await _context.AccidentConflicts
                .AsNoTracking()
                .Where(c => c.AccidentId == accidentId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new InspectorConflictItemViewModel
                {
                    AccidentConflictId = c.AccidentConflictId,
                    ConflictType = c.ConflictType.ToString(),
                    Severity = c.Severity.ToString(),
                    Summary = c.Summary ?? "—",
                    IsResolved = c.IsResolved,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            var images = await _context.Images
                .AsNoTracking()
                .Where(i => i.AccidentId == accidentId)
                .OrderBy(i => i.DriverUserId)
                .ThenBy(i => i.ImageId)
                .ToListAsync();

            var vm = new InspectorReportDetailsViewModel
            {
                ReportId = report.ReportId,
                AccidentId = report.AccidentId,
                ApprovalStatus = report.ApprovalStatus ?? "قيد المراجعة",
                InspectorNote = report.InspectorNote,
                ReviewedAt = report.ReviewedAt,
                ReviewedByUserId = report.ReviewedByUserId,
                ReviewedByName = report.ReviewedByUser != null ? report.ReviewedByUser.Email : null,

                Location = report.Accident.Location ?? "—",
                Latitude = report.Accident.Latitude,
                Longitude = report.Accident.Longitude,
                AccidentDate = report.Accident.AccidentDate,
                AccidentTime = report.Accident.AccidentTime,
                AccidentType = report.Accident.AccidentType ?? "—",
                AccidentStatus = report.Accident.Status ?? "—",

                RuleId = report.RuleId ?? "—",
                AccidentClassification = report.AccidentClassification ?? report.Summary ?? "—",
                FaultPercentDriver1 = report.FaultPercentDriver1 ?? 0,
                FaultPercentDriver2 = report.FaultPercentDriver2 ?? 0,
                BaseConfidenceScore = report.BaseConfidenceScore ?? 0,
                ConflictPenaltyScore = report.ConflictPenaltyScore ?? 0,
                EvidenceBonusScore = report.EvidenceBonusScore ?? 0,
                FinalConfidenceScore = report.FinalConfidenceScore ?? 0,
                FinalConfidenceLabel = report.FinalConfidenceLabel ?? "—",
                DecisionExplanation = report.DecisionExplanation ?? "—",

                Party1 = BuildParty(1),
                Party2 = BuildParty(2),

                CoreAnswers = allAnswers.Where(a => a.QuestionType == "Core").ToList(),
                MirrorAnswers = allAnswers.Where(a => a.QuestionType == "Mirror").ToList(),
                ConflictBackAnswers = allAnswers.Where(a => a.QuestionType == "ConflictBack").ToList(),

                Conflicts = conflicts,

                Party1Images = images
                    .Where(i => i.DriverUserId == role1UserId)
                    .Select(i => new InspectorImageItemViewModel
                    {
                        ImageId = i.ImageId,
                        DriverUserId = i.DriverUserId,
                        Label = i.Label ?? "",
                        ImagePath = i.ImagePath ?? "",
                        PredictedLabel = i.PredictedLabel,
                        PredictionConfidence = i.PredictionConfidence,
                        PredictionModel = i.PredictionModel,
                        UploadDate = i.UploadDate
                    }).ToList(),

                Party2Images = images
                    .Where(i => i.DriverUserId == role2UserId)
                    .Select(i => new InspectorImageItemViewModel
                    {
                        ImageId = i.ImageId,
                        DriverUserId = i.DriverUserId,
                        Label = i.Label ?? "",
                        ImagePath = i.ImagePath ?? "",
                        PredictedLabel = i.PredictedLabel,
                        PredictionConfidence = i.PredictionConfidence,
                        PredictionModel = i.PredictionModel,
                        UploadDate = i.UploadDate
                    }).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReview(InspectorReviewInputViewModel vm)
        {
            if (!await CurrentUserIsInspectorAsync())
                return Forbid();

            if (vm.ApprovalStatus != "مقبول" && vm.ApprovalStatus != "مرفوض")
            {
                TempData["ReviewError"] = "حالة المراجعة غير صحيحة.";
                return RedirectToAction(nameof(Details), new { accidentId = vm.AccidentId });
            }

            var report = await _context.AccidentReports
                .FirstOrDefaultAsync(r => r.AccidentId == vm.AccidentId);

            if (report == null)
                return NotFound();

            report.ApprovalStatus = vm.ApprovalStatus;
            report.InspectorNote = string.IsNullOrWhiteSpace(vm.InspectorNote) ? null : vm.InspectorNote.Trim();
            report.ReviewedAt = DateTime.Now;
            report.ReviewedByUserId = GetCurrentUserId();

            await _context.SaveChangesAsync();

            TempData["ReviewSuccess"] = "تم تحديث حالة التقرير بنجاح.";
            return RedirectToAction(nameof(Details), new { accidentId = vm.AccidentId });
        }
    }
}