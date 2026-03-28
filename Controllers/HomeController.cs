using Aoun.Models;
using Aoun.ViewModels;
using Aoun.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Aoun.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AounDbContext _context;

        public HomeController(ILogger<HomeController> logger, AounDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public IActionResult Index() => View();

        [AuthorizeUser]
        public async Task<IActionResult> HomePage()
        {
            var driverUserId = HttpContext.Session.GetInt32("UserId");

            if (driverUserId == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            var driverName = await _context.Drivers
                .Where(d => d.UserId == driverUserId.Value)
                .Select(d => d.DriverName)
                .FirstOrDefaultAsync() ?? "مستخدم";

            var recent = await
                (from a in _context.Accidents
                 join inv in _context.Involves on a.AccidentId equals inv.AccidentId
                 join v in _context.Vehicles on inv.VehicleId equals v.VehicleId
                 join ar in _context.AccidentReports on a.AccidentId equals ar.AccidentId into arJoin
                 from ar in arJoin.DefaultIfEmpty()
                 where v.DriverUserId == driverUserId.Value
                 select new { a, inv, ar })
                .Distinct()
                .OrderByDescending(x => x.a.AccidentDate)
                .ThenByDescending(x => x.a.AccidentTime)
                .Take(3)
                .Select(x => new RecentAccidentCard
                {
                    AccidentId = x.a.AccidentId,
                    AccidentDate = x.a.AccidentDate,
                    AccidentTime = x.a.AccidentTime,
                    Status = x.ar != null ? (x.ar.ApprovalStatus ?? "") : "",
                    FaultPercent =
                        x.ar == null ? null :
                        x.inv.VehicleRole == 1 ? x.ar.FaultPercentDriver1 :
                        x.inv.VehicleRole == 2 ? x.ar.FaultPercentDriver2 :
                        null
                })
                .ToListAsync();

            var vm = new HomePageViewModel
            {
                DriverId = driverUserId.Value,
                DriverName = driverName,
                RecentAccidents = recent
            };

            return View(vm);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}