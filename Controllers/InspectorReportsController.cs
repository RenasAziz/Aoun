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

        private static bool IsYesNoConflict(string? a, string? b)
        {
            return (a == "CQ2_YES" && b == "M2_NO") ||
                   (a == "CQ2_NO" && b == "M2_YES") ||
                   (a == "CQ10_YES" && b == "M5_NO") ||
                   (a == "CQ10_NO" && b == "M5_YES");
        }

        private static bool IsLaneChangeConflict(string? cq1, string? m1)
        {
            bool? claim = cq1 switch
            {
                "CQ1_LEFT" => true,
                "CQ1_RIGHT" => true,
                "CQ1_NO" => false,
                _ => null
            };

            bool? obs = m1 switch
            {
                "M1_YES" => true,
                "M1_NO" => false,
                _ => null
            };

            return claim.HasValue && obs.HasValue && claim.Value != obs.Value;
        }

        private static bool IsSpecialMoveConflict(string? cq3, string? m3)
        {
            bool? claim = cq3 switch
            {
                "CQ3_REVERSING" => true,
                "CQ3_UTURN" => true,
                "CQ3_NORMAL" => false,
                "CQ3_SLOW" => false,
                _ => null
            };

            bool? obs = m3 switch
            {
                "M3_YES" => true,
                "M3_NO" => false,
                _ => null
            };

            return claim.HasValue && obs.HasValue && claim.Value != obs.Value;
        }

        private static bool IsIntersectionControlConflict(string? d1Code, string? d2Code)
        {
            return (d1Code == "CQ7_LIGHT" && d2Code == "CQ7_NONE") ||
                   (d1Code == "CQ7_NONE" && d2Code == "CQ7_LIGHT");
        }

        private static bool IsPositionConflict(string? d1Code, string? d2Code)
        {
            return (d1Code == "CQ5_BEHIND" && d2Code == "CQ5_BEHIND") ||
                   (d1Code == "CQ5_AHEAD" && d2Code == "CQ5_AHEAD");
        }

        private static bool IsIntersectionEntryFirstConflict(string? d1Code, string? d2Code)
        {
            return (d1Code == "CQ9_ME" && d2Code == "CQ9_ME") ||
                   (d1Code == "CQ9_OTHER" && d2Code == "CQ9_OTHER");
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

            var cq6Driver1Code = role1UserId > 0 ? GetAnswerCode("CQ6", role1UserId) : "—";
            var cq6Driver2Code = role2UserId > 0 ? GetAnswerCode("CQ6", role2UserId) : "—";

            bool intersectionActive =
                string.Equals(cq6Driver1Code, "CQ6_YES", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cq6Driver2Code, "CQ6_YES", StringComparison.OrdinalIgnoreCase);

            bool hasIntersectionDetailAnswers =
                (role1UserId > 0 && (
                    GetAnswerCode("CQ7", role1UserId) != "—" ||
                    GetAnswerCode("CQ8", role1UserId) != "—" ||
                    GetAnswerCode("CQ9", role1UserId) != "—" ||
                    GetAnswerCode("M4", role1UserId) != "—")) ||
                (role2UserId > 0 && (
                    GetAnswerCode("CQ7", role2UserId) != "—" ||
                    GetAnswerCode("CQ8", role2UserId) != "—" ||
                    GetAnswerCode("CQ9", role2UserId) != "—" ||
                    GetAnswerCode("M4", role2UserId) != "—"));

            var cq10Driver1Code = role1UserId > 0 ? GetAnswerCode("CQ10", role1UserId) : "—";
            var cq10Driver2Code = role2UserId > 0 ? GetAnswerCode("CQ10", role2UserId) : "—";

            bool overtakeActive =
                string.Equals(cq10Driver1Code, "CQ10_YES", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cq10Driver2Code, "CQ10_YES", StringComparison.OrdinalIgnoreCase);

            bool hasOvertakeMirrorAnswers =
                (role1UserId > 0 && GetAnswerCode("M5", role1UserId) != "—") ||
                (role2UserId > 0 && GetAnswerCode("M5", role2UserId) != "—");

            bool ShouldDisplayQuestion(string? questionCode)
            {
                if (string.IsNullOrWhiteSpace(questionCode))
                    return false;

                if (questionCode.Equals("CQ7", StringComparison.OrdinalIgnoreCase) ||
                    questionCode.Equals("CQ8", StringComparison.OrdinalIgnoreCase) ||
                    questionCode.Equals("CQ9", StringComparison.OrdinalIgnoreCase) ||
                    questionCode.Equals("M4", StringComparison.OrdinalIgnoreCase))
                {
                    return intersectionActive || hasIntersectionDetailAnswers;
                }

                if (questionCode.Equals("M5", StringComparison.OrdinalIgnoreCase))
                {
                    return overtakeActive || hasOvertakeMirrorAnswers;
                }

                return true;
            }

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
               .Where(q => ShouldDisplayQuestion(q.QuestionCode))
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


            var packSeverityMap = conflicts
    .Where(c => Enum.TryParse<ConflictType>(c.ConflictType, out _))
    .Select(c =>
    {
        Enum.TryParse<ConflictType>(c.ConflictType, out var parsedType);
        var packName = MapConflictTypeToPackName(parsedType);
        return new
        {
            PackName = packName,
            Severity = c.Severity
        };
    })
    .Where(x => !string.IsNullOrWhiteSpace(x.PackName))
    .GroupBy(x => x.PackName!)
    .ToDictionary(
        g => g.Key,
        g =>
        {
            if (g.Any(x => string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase)))
                return "Critical";

            if (g.Any(x => string.Equals(x.Severity, "High", StringComparison.OrdinalIgnoreCase)))
                return "High";

            if (g.Any(x => string.Equals(x.Severity, "Medium", StringComparison.OrdinalIgnoreCase)))
                return "Medium";

            return "Low";
        },
        StringComparer.OrdinalIgnoreCase
    );

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

            var conflictBackAnswers = allAnswers
             .Where(a =>
                 a.QuestionType == "ConflictBack" &&
                 !string.IsNullOrWhiteSpace(a.PackName) &&
                 relevantPackNames.Contains(a.PackName))
             .OrderBy(a => a.PackName)
             .ThenBy(a => a.QuestionCode)
             .ToList();

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
                ConflictBackAnswers = conflictBackAnswers,
                PackSeverityMap = packSeverityMap,

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

        var item = new InspectorAnswerCompareItemViewModel
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

        switch (core.QuestionCode)
        {
            case "CQ1":
                item.Driver1CoreConflict = IsLaneChangeConflict(core.Driver1AnswerCode, mirror?.Driver2AnswerCode);
                item.Driver2MirrorConflict = IsLaneChangeConflict(core.Driver1AnswerCode, mirror?.Driver2AnswerCode);

                item.Driver2CoreConflict = IsLaneChangeConflict(core.Driver2AnswerCode, mirror?.Driver1AnswerCode);
                item.Driver1MirrorConflict = IsLaneChangeConflict(core.Driver2AnswerCode, mirror?.Driver1AnswerCode);

                if (item.Driver1CoreConflict || item.Driver2CoreConflict ||
                    item.Driver1MirrorConflict || item.Driver2MirrorConflict)
                {
                    item.ConflictHintAr = "يوجد تناقض بين إجابة السائق عن نفسه وإجابة الطرف الآخر عنه.";
                }
                break;

            case "CQ2":
                item.Driver1CoreConflict =
                    (core.Driver1AnswerCode == "CQ2_YES" && mirror?.Driver2AnswerCode == "M2_NO") ||
                    (core.Driver1AnswerCode == "CQ2_NO" && mirror?.Driver2AnswerCode == "M2_YES");
                item.Driver2MirrorConflict = item.Driver1CoreConflict;

                item.Driver2CoreConflict =
                    (core.Driver2AnswerCode == "CQ2_YES" && mirror?.Driver1AnswerCode == "M2_NO") ||
                    (core.Driver2AnswerCode == "CQ2_NO" && mirror?.Driver1AnswerCode == "M2_YES");
                item.Driver1MirrorConflict = item.Driver2CoreConflict;

                if (item.Driver1CoreConflict || item.Driver2CoreConflict ||
                    item.Driver1MirrorConflict || item.Driver2MirrorConflict)
                {
                    item.ConflictHintAr = "يوجد تناقض بين الادعاء بالدخول للطريق الرئيسي وملاحظة الطرف الآخر.";
                }
                break;

            case "CQ3":
                item.Driver1CoreConflict = IsSpecialMoveConflict(core.Driver1AnswerCode, mirror?.Driver2AnswerCode);
                item.Driver2MirrorConflict = IsSpecialMoveConflict(core.Driver1AnswerCode, mirror?.Driver2AnswerCode);

                item.Driver2CoreConflict = IsSpecialMoveConflict(core.Driver2AnswerCode, mirror?.Driver1AnswerCode);
                item.Driver1MirrorConflict = IsSpecialMoveConflict(core.Driver2AnswerCode, mirror?.Driver1AnswerCode);

                if (item.Driver1CoreConflict || item.Driver2CoreConflict ||
                    item.Driver1MirrorConflict || item.Driver2MirrorConflict)
                {
                    item.ConflictHintAr = "يوجد تناقض بخصوص الحركة الخاصة قبل الاصطدام.";
                }
                break;

            case "CQ5":
                item.Driver1CoreConflict = IsPositionConflict(core.Driver1AnswerCode, core.Driver2AnswerCode);
                item.Driver2CoreConflict = IsPositionConflict(core.Driver1AnswerCode, core.Driver2AnswerCode);

                if (item.Driver1CoreConflict || item.Driver2CoreConflict)
                {
                    item.ConflictHintAr = "الطرفان قدّما تموضعًا نسبيًا غير منطقي.";
                }
                break;

            case "CQ6":
                item.Driver1CoreConflict =
                    (core.Driver1AnswerCode == "CQ6_YES" && mirror?.Driver2AnswerCode == "M4_NO") ||
                    (core.Driver1AnswerCode == "CQ6_NO" && mirror?.Driver2AnswerCode == "M4_YES");
                item.Driver2MirrorConflict = item.Driver1CoreConflict;

                item.Driver2CoreConflict =
                    (core.Driver2AnswerCode == "CQ6_YES" && mirror?.Driver1AnswerCode == "M4_NO") ||
                    (core.Driver2AnswerCode == "CQ6_NO" && mirror?.Driver1AnswerCode == "M4_YES");
                item.Driver1MirrorConflict = item.Driver2CoreConflict;

                if (item.Driver1CoreConflict || item.Driver2CoreConflict ||
                    item.Driver1MirrorConflict || item.Driver2MirrorConflict)
                {
                    item.ConflictHintAr = "يوجد تناقض بين تحديد وقوع الحادث عند تقاطع وملاحظة الطرف الآخر.";
                }
                break;

            case "CQ7":
                item.Driver1CoreConflict = IsIntersectionControlConflict(core.Driver1AnswerCode, core.Driver2AnswerCode);
                item.Driver2CoreConflict = IsIntersectionControlConflict(core.Driver1AnswerCode, core.Driver2AnswerCode);

                if (item.Driver1CoreConflict || item.Driver2CoreConflict)
                {
                    item.ConflictHintAr = "يوجد اختلاف جوهري في وصف تنظيم التقاطع.";
                }
                break;

            case "CQ9":
                item.Driver1CoreConflict = IsIntersectionEntryFirstConflict(core.Driver1AnswerCode, core.Driver2AnswerCode);
                item.Driver2CoreConflict = IsIntersectionEntryFirstConflict(core.Driver1AnswerCode, core.Driver2AnswerCode);

                if (item.Driver1CoreConflict || item.Driver2CoreConflict)
                {
                    item.ConflictHintAr = "كلا الطرفين يدّعي أولوية دخول غير منطقية.";
                }
                break;

            case "CQ10":
                item.Driver1CoreConflict =
                    (core.Driver1AnswerCode == "CQ10_YES" && mirror?.Driver2AnswerCode == "M5_NO") ||
                    (core.Driver1AnswerCode == "CQ10_NO" && mirror?.Driver2AnswerCode == "M5_YES");
                item.Driver2MirrorConflict = item.Driver1CoreConflict;

                item.Driver2CoreConflict =
                    (core.Driver2AnswerCode == "CQ10_YES" && mirror?.Driver1AnswerCode == "M5_NO") ||
                    (core.Driver2AnswerCode == "CQ10_NO" && mirror?.Driver1AnswerCode == "M5_YES");
                item.Driver1MirrorConflict = item.Driver2CoreConflict;

                if (item.Driver1CoreConflict || item.Driver2CoreConflict ||
                    item.Driver1MirrorConflict || item.Driver2MirrorConflict)
                {
                    item.ConflictHintAr = "يوجد تناقض بين ادعاء التجاوز وملاحظة الطرف الآخر.";
                }
                break;
        }

        return item;
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