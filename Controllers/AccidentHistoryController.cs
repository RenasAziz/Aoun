using Aoun.Filters;
using Aoun.Models;
using Aoun.Services;
using Aoun.ViewModels;
using Aoun.ViewModels.Accident;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Controllers
{
    public class AccidentHistoryController : Controller
    {
        private readonly AounDbContext _context;
        private readonly AccidentHistoryPdfService _pdfService;

        public AccidentHistoryController(AounDbContext context, AccidentHistoryPdfService pdfService)
        {
            _context = context;
            _pdfService = pdfService;
        }

        [AuthorizeUser]
        public async Task<IActionResult> Index(string searchString)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var userAccidentIds = await _context.AccidentSessionParticipants
                .Where(p => p.DriverUserId == currentUserId.Value)
                .Select(p => p.AccidentId)
                .Distinct()
                .ToListAsync();

            var accidentsQuery = _context.Accidents
                .Include(a => a.AccidentReport)
                .Where(a => userAccidentIds.Contains(a.AccidentId))
                .OrderByDescending(a => a.AccidentDate)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                accidentsQuery = accidentsQuery.Where(a =>
                    a.AccidentId.ToString().Contains(searchString) ||
                    (a.AccidentType != null && a.AccidentType.Contains(searchString)) ||
                    (a.Status != null && a.Status.Contains(searchString)));
            }

            var accidents = await accidentsQuery.ToListAsync();

            var viewModel = new AccidentListViewModel();

            foreach (var accident in accidents)
            {
                var participant = await _context.AccidentSessionParticipants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.AccidentId == accident.AccidentId && p.DriverUserId == currentUserId.Value);

                var role = participant?.Role ?? (byte)1;
                var status = accident.AccidentReport?.ApprovalStatus ?? "قيد المراجعة";

                var currentFault = role == 2
                    ? (accident.AccidentReport?.FaultPercentDriver2 ?? 0)
                    : (accident.AccidentReport?.FaultPercentDriver1 ?? 0);

                string statusClass = status switch
                {
                    "مقبول" => "st-accepted",
                    "مرفوض" => "st-rejected",
                    _ => "st-pending"
                };

                viewModel.Accidents.Add(new AccidentListItemViewModel
                {
                    AccidentId = accident.AccidentId,
                    AccidentNumber = $"ACC-{accident.AccidentId:D6}",
                    AccidentDate = accident.AccidentDate,
                    FaultPercentage = currentFault,
                    Status = status,
                    StatusCssClass = statusClass,
                    AccidentClassification = accident.AccidentReport?.AccidentClassification ?? "—"
                });
            }

            ViewData["CurrentFilter"] = searchString;

            return View(viewModel);
        }

        [AuthorizeUser]
        public async Task<IActionResult> Details(int id)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var vm = await BuildDetailsViewModelAsync(id, currentUserId.Value);
            if (vm == null)
                return NotFound();

            return View(vm);
        }

        [AuthorizeUser]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var vm = await BuildDetailsViewModelAsync(id, currentUserId.Value);
            if (vm == null)
                return NotFound();

            var pdfBytes = _pdfService.Generate(vm);

            return File(
                pdfBytes,
                "application/pdf",
                $"تقرير-حادث-{vm.AccidentCode}.pdf");
        }

        private async Task<AccidentHistoryDetailsViewModel?> BuildDetailsViewModelAsync(int id, int currentUserId)
        {
            var participant = await _context.AccidentSessionParticipants
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.AccidentId == id && p.DriverUserId == currentUserId);

            if (participant == null)
                return null;

            var accident = await _context.Accidents
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccidentId == id);

            if (accident == null)
                return null;

            var report = await _context.AccidentReports
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.AccidentId == id);

            if (report == null)
                return null;

            var damageImages = await _context.Images
                .AsNoTracking()
                .Include(i => i.ImageSegmentationDetections)
                .Where(i => i.AccidentId == id
                         && i.DriverUserId == currentUserId
                         && (i.Label == "Damage1" || i.Label == "Damage2"))
                .OrderBy(i => i.ImageId)
                .ToListAsync();

            var damage1 = damageImages.FirstOrDefault(i => i.Label == "Damage1");
            var damage2 = damageImages.FirstOrDefault(i => i.Label == "Damage2");

            var driver = await _context.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == currentUserId);

            Vehicle? vehicle = null;

            if (participant.VehicleId.HasValue)
            {
                vehicle = await _context.Vehicles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.VehicleId == participant.VehicleId.Value);
            }

            return new AccidentHistoryDetailsViewModel
            {
                AccidentId = id,
                Role = participant.Role,
                AccidentCode = $"ACC-{id:000000}",
                AccidentDate = accident.AccidentDate,
                AccidentTime = accident.AccidentTime,
                Location = accident.Location ?? "—",

                ApprovalStatus = report.ApprovalStatus ?? "قيد المراجعة",
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

                HasConflicts = await _context.AccidentConflicts.AnyAsync(c => c.AccidentId == id),
                InspectorNote = report.InspectorNote,

                ReportTitle = "تقرير حادث رسمي",
                ReportSource = "منصة عون - نظام تحليل الحوادث",
                ReportReference = $"AOUN-{id:000000}",
                GeneratedOnText = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),

                DriverName = driver?.DriverName ?? "—",
                DriverRoleText = participant.Role == 2 ? "الطرف الثاني (أنت)" : "الطرف الأول (أنت)",

                VehiclePlate = vehicle?.LicensePlate ?? "—",
                VehicleModel = vehicle?.Model ?? "—",
                VehicleColor = vehicle?.Color ?? "—",
                VehicleYearText = vehicle?.Year.HasValue == true ? vehicle.Year.Value.ToString() : "—"
            };
        }
    }
}