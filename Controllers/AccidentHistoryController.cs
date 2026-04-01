using Aoun.Filters;
using Aoun.Models;
using Aoun.ViewModels.Accident;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Controllers
{
    public class AccidentHistoryController : Controller
    {
        private readonly AounDbContext _context;

        public AccidentHistoryController(AounDbContext context)
        {
            _context = context;
        }

        // ===============================
        // LIST PAGE
        // ===============================

        [AuthorizeUser]
        public async Task<IActionResult> Index(string searchString)
        {
            var accidentsQuery = _context.Accidents
                .Include(a => a.AccidentReport)
                .OrderByDescending(a => a.AccidentDate)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                accidentsQuery = accidentsQuery.Where(a =>
                    a.AccidentId.ToString().Contains(searchString) ||
                    a.AccidentType.Contains(searchString) ||
                    a.Status.Contains(searchString));
            }

            var accidents = await accidentsQuery.ToListAsync();

            var viewModel = new AccidentListViewModel();

            foreach (var accident in accidents)
            {
                var status = accident.AccidentReport?.ApprovalStatus ?? "قيد المراجعة";
                var fault = accident.AccidentReport?.FaultPercentDriver1 ?? 0;

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
                    FaultPercentage = fault,
                    Status = status,
                    StatusCssClass = statusClass,
                    AccidentType = accident.AccidentType
                });
            }

            ViewData["CurrentFilter"] = searchString;

            return View(viewModel);
        }

        // ===============================
        // DETAILS PAGE
        // ===============================
        public async Task<IActionResult> Details(int id)
        {
            var accident = await _context.Accidents
                .Include(a => a.AccidentReport)
                .Include(a => a.Images)
                .FirstOrDefaultAsync(a => a.AccidentId == id);

            if (accident == null)
                return NotFound();

            var status = accident.AccidentReport?.ApprovalStatus ?? "قيد المراجعة";
            var fault = accident.AccidentReport?.FaultPercentDriver1 ?? 0;

            var viewModel = new AccidentDetailsViewModel
            {
                AccidentId = accident.AccidentId,
                AccidentNumber = $"ACC-{accident.AccidentId:D6}",
                AccidentDate = accident.AccidentDate,
                AccidentTime = accident.AccidentTime,
                Location = accident.Location,
                AccidentType = accident.AccidentType,
                Status = status,
                FaultPercentage = fault,
                ImagePaths = accident.Images
                    .Select(i => i.ImagePath ?? "")
                    .ToList()
            };

            return View(viewModel);
        }
    }
}