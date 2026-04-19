using Aoun.Models;
using Aoun.Services;
using Aoun.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Controllers
{
    public class InspectorReportsController : Controller
    {
        private readonly AounDbContext _context;
        private readonly NotificationService _notificationService;

        public InspectorReportsController(AounDbContext context, NotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
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

        private HashSet<string> GetRuleEvidenceCodes(string ruleId)
        {
            return ruleId switch
            {
                "R2" => new() { "CQ2", "M2" },
                "R3" => new() { "CQ3", "M3" },
                "R4" => new() { "CQ3", "M3" },
                "R1" => new() { "CQ1", "M1" },
                "R10" => new() { "CQ6", "CQ7", "CQ8" },
                "R11" => new() { "CQ6", "CQ9" },
                "R12" => new() { "CQ6" },
                "R5" => new() { "CQ5" },
                "R9" => new() { "CQ10", "CQ1" },
                "R7" => new() { "CQ10", "M5" },
                "R8" => new() { "CQ5", "CQ11" },
                _ => new()
            };
        }

        private bool IsMirrorMatch(string core, string mirror)
        {
            return (core, mirror) switch
            {
                ("CQ1", "M1") => true,
                ("CQ2", "M2") => true,
                ("CQ3", "M3") => true,
                ("CQ6", "M4") => true,
                ("CQ10", "M5") => true,
                _ => false
            };
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
                    FinalConfidenceLabel = r.FinalConfidenceLabel ?? "—",
                    HasConflicts = _context.AccidentConflicts.Any(c => c.AccidentId == r.AccidentId)
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

            var evidenceCodes = GetRuleEvidenceCodes(report.RuleId);

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

            var feedbacks = await _context.DriverFeedbacks
    .AsNoTracking()
    .Where(f => f.AccidentId == accidentId)
    .ToListAsync();

            string? MapConflictTypeToPackName(ConflictType type)
            {
                return type switch
                {
                    ConflictType.LaneChange => "Pack-LaneChange",
                    ConflictType.EnteringRoad => "Pack-EnteringRoad",
                    ConflictType.SpecialMove => "Pack-SpecialMove",
                    ConflictType.IntersectionControl => "Pack-Intersection",
                    ConflictType.IntersectionCompliance => "Pack-Intersection",
                    ConflictType.IntersectionEntryFirst => "Pack-Intersection",
                    ConflictType.Position => "Pack-Position",
                    ConflictType.Overtake => "Pack-OvertakeVsLeftTurn",
                    _ => null
                };
            }



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

            var questionOptions = await _context.QuestionOptions
    .AsNoTracking()
    .Select(o => new
    {
        o.QuestionId,
        o.OptionCode,
        o.OptionTextAr
    })
    .ToListAsync();

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



            string GetAnswerTextAr(string questionCode, int driverUserId)
            {
                var answerRow = questionRows
                    .FirstOrDefault(x => x.QuestionCode == questionCode && x.DriverUserId == driverUserId);

                if (answerRow == null)
                    return "—";

                if (!string.IsNullOrWhiteSpace(answerRow.FreeText))
                    return answerRow.FreeText;

                if (string.IsNullOrWhiteSpace(answerRow.SelectedOptionCode))
                    return "—";

                var optionText = questionOptions
                    .FirstOrDefault(o => o.QuestionId == answerRow.QuestionId &&
                                         o.OptionCode == answerRow.SelectedOptionCode)
                    ?.OptionTextAr;

                return string.IsNullOrWhiteSpace(optionText) ? answerRow.SelectedOptionCode ?? "—" : optionText;
            }

            var questionMeta = questionRows
                .GroupBy(x => new { x.QuestionId, x.QuestionCode, x.QuestionTextAr, x.QuestionType, x.PackName })
                .Select(g => g.Key)
                .OrderBy(x => x.QuestionType)
                .ThenBy(x => x.QuestionId)
                .ToList();

            var role1UserId = participants.FirstOrDefault(p => p.Role == 1)?.DriverUserId ?? 0;
            var role2UserId = participants.FirstOrDefault(p => p.Role == 2)?.DriverUserId ?? 0;

            InspectorDriverFeedbackViewModel? BuildFeedback(int driverUserId)
            {
                if (driverUserId <= 0) return null;

                var feedback = feedbacks.FirstOrDefault(f => f.DriverUserId == driverUserId);
                if (feedback == null) return null;

                return new InspectorDriverFeedbackViewModel
                {
                    DriverUserId = feedback.DriverUserId,
                    SatisfactionLevel = feedback.SatisfactionLevel,
                    Comment = feedback.Comment,
                    FeedbackDate = feedback.FeedbackDate
                };
            }

            var allAnswers = questionMeta
               .Select(q => new InspectorAnswerCompareItemViewModel
               {
                   QuestionCode = q.QuestionCode ?? "",
                   QuestionTextAr = q.QuestionTextAr ?? "",
                   QuestionType = q.QuestionType ?? "",
                   PackName = q.PackName,

                   Driver1AnswerCode = role1UserId > 0 ? GetAnswerCode(q.QuestionCode ?? "", role1UserId) : "—",
                   Driver2AnswerCode = role2UserId > 0 ? GetAnswerCode(q.QuestionCode ?? "", role2UserId) : "—",

                   Driver1AnswerTextAr = role1UserId > 0 ? GetAnswerTextAr(q.QuestionCode ?? "", role1UserId) : "—",
                   Driver2AnswerTextAr = role2UserId > 0 ? GetAnswerTextAr(q.QuestionCode ?? "", role2UserId) : "—",

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

            var relevantPackNames = conflicts
    .Select(c =>
    {
        if (Enum.TryParse<ConflictType>(c.ConflictType, out var parsedType))
            return MapConflictTypeToPackName(parsedType);

        return null;
    })
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Distinct()
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var images = await _context.Images
                .AsNoTracking()
                .Include(i => i.ImageSegmentationDetections)
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

                Party1Feedback = BuildFeedback(role1UserId),
                Party2Feedback = BuildFeedback(role2UserId),


                CoreAnswers = allAnswers
    .Where(a => a.QuestionType == "Core")
    .Select(core =>
    {
        var mirror = allAnswers.FirstOrDefault(m =>
            m.QuestionType == "Mirror" &&
            IsMirrorMatch(core.QuestionCode, m.QuestionCode));

        return new InspectorAnswerCompareItemViewModel
        {
            QuestionCode = core.QuestionCode,
            QuestionTextAr = core.QuestionTextAr,

            Driver1AnswerCode = core.Driver1AnswerCode,
            Driver2AnswerCode = core.Driver2AnswerCode,

            Driver1AnswerTextAr = core.Driver1AnswerTextAr,
            Driver2AnswerTextAr = core.Driver2AnswerTextAr,

            MirrorQuestionTextAr = mirror?.QuestionTextAr,
            MirrorDriver1AnswerTextAr = mirror?.Driver1AnswerTextAr,
            MirrorDriver2AnswerTextAr = mirror?.Driver2AnswerTextAr,

            IsEvidence = evidenceCodes.Contains(core.QuestionCode)
        };
    }).ToList(),


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
        UploadDate = i.UploadDate,

        SegmentationResultPath = i.SegmentationResultPath,
        SegmentationHasDamage = i.SegmentationHasDamage,
        SegmentationModel = i.SegmentationModel,
        SegmentationDetections = i.ImageSegmentationDetections
            .Select(d => new InspectorSegmentationDetectionItemViewModel
            {
                Label = d.DamageLabel ?? "",
                Confidence = d.Confidence
            }).ToList()
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
        UploadDate = i.UploadDate,

        SegmentationResultPath = i.SegmentationResultPath,
        SegmentationHasDamage = i.SegmentationHasDamage,
        SegmentationModel = i.SegmentationModel,
        SegmentationDetections = i.ImageSegmentationDetections
            .Select(d => new InspectorSegmentationDetectionItemViewModel
            {
                Label = d.DamageLabel ?? "",
                Confidence = d.Confidence
            }).ToList()
    }).ToList(),
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

            var driverUserIds = await _context.AccidentSessionParticipants
                .Where(p => p.AccidentId == vm.AccidentId)
                .Select(p => p.DriverUserId)
                .Distinct()
                .ToListAsync();

            if (driverUserIds.Count > 0)
            {
                if (vm.ApprovalStatus == "مقبول")
                {
                    await _notificationService.CreateForUsersAsync(
                        driverUserIds,
                        "تم اعتماد التقرير",
                        "تم اعتماد تقرير الحادث.",
                        "ReportApproved",
                        vm.AccidentId
                    );
                }
                else if (vm.ApprovalStatus == "مرفوض")
                {
                    await _notificationService.CreateForUsersAsync(
                        driverUserIds,
                        "تم رفض التقرير",
                        "تم رفض التقرير، يرجى مراجعة الملاحظات.",
                        "ReportRejected",
                        vm.AccidentId
                    );
                }
            }

            TempData["ReviewSuccess"] = "تم تحديث حالة التقرير بنجاح.";
            return RedirectToAction(nameof(Details), new { accidentId = vm.AccidentId });
        }
    }
}