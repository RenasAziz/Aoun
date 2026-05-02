using Aoun.Filters;
using Aoun.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Controllers
{

/*
===============================================================================
VehiclesController
===============================================================================
Manages all vehicle-related operations for drivers.

Features:
- List vehicles (with search)
- Add new vehicle
- Edit vehicle
- Delete vehicle

Each vehicle is linked to a specific driver via DriverUserId.
===============================================================================
*/

    public class VehiclesController : Controller
    {
        private readonly AounDbContext _context;

        public VehiclesController(AounDbContext context)
        {
            _context = context;
        }

        // ======================
        // LIST
        // ======================

        /*
        - Retrieves current driver.
        - Filters vehicles belonging to that driver.
        - Applies optional search filter:
            • By License Plate
            • By Model
        - Returns filtered vehicle list to view.
         */

        [AuthorizeUser]
        public async Task<IActionResult> Index(string searchString)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");

            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var vehiclesQuery = _context.Vehicles
                .Where(v => v.DriverUserId == currentUserId);

            if (!string.IsNullOrEmpty(searchString))
            {
                vehiclesQuery = vehiclesQuery.Where(v =>
                    v.LicensePlate.Contains(searchString) ||
                    (v.Model ?? "").Contains(searchString));
            }

            var vehicles = await vehiclesQuery.ToListAsync();

            ViewData["CurrentFilter"] = searchString;

            return View(vehicles);
        }

        // ======================
        // GET ADD
        // ======================
        // Displays vehicle creation form.
        [AuthorizeUser]
        public IActionResult Add()
        {
            return View("AddVehicle");
        }

        // ======================
        // POST ADD
        // ======================

        /*
        - Validates form input.
        - Assigns vehicle to current driver.
        - Saves to database.
        - Redirects to vehicle list.
         */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(Vehicle vehicle)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");

            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            if (!ModelState.IsValid)
                return View("AddVehicle", vehicle);

            if (string.IsNullOrWhiteSpace(vehicle.LicensePlate))
            {
                ModelState.AddModelError("LicensePlate", "يرجى إدخال رقم اللوحة.");
                return View("AddVehicle", vehicle);
            }

            // Normalize plate before checking duplicate.
            // توحيد صيغة اللوحة قبل فحص التكرار.
            var normalizedPlate = vehicle.LicensePlate.Trim().ToUpper();

            bool plateExists = await _context.Vehicles
                .AnyAsync(v => v.LicensePlate != null &&
                               v.LicensePlate.Trim().ToUpper() == normalizedPlate);

            if (plateExists)
            {
                ModelState.AddModelError("LicensePlate", "رقم اللوحة مسجل مسبقًا.");
                return View("AddVehicle", vehicle);
            }

            vehicle.DriverUserId = currentUserId.Value;
            vehicle.LicensePlate = normalizedPlate;

            if (!string.IsNullOrWhiteSpace(vehicle.Model))
                vehicle.Model = vehicle.Model.Trim();

            _context.Vehicles.Add(vehicle);
            await _context.SaveChangesAsync();
            TempData["ToastSuccess"] = "تمت إضافة المركبة بنجاح.";

            return RedirectToAction(nameof(Index));
        }

        // ======================
        // GET EDIT
        // ======================

        /*
        - Retrieves vehicle by ID.
        - Displays edit form.
         */
        [AuthorizeUser]
        public async Task<IActionResult> Edit(int id)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");

            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var vehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleId == id && v.DriverUserId == currentUserId.Value);

            if (vehicle == null)
                return NotFound();

            return View("EditVehicle", vehicle);
        }

        // ======================
        // POST EDIT
        // ======================

        /*
        - Validates updated data.
        - Updates vehicle in database.
        - Handles concurrency exceptions.
        - Redirects to list after success.
         */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Vehicle vehicle)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");

            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            if (id != vehicle.VehicleId)
                return NotFound();

            if (!ModelState.IsValid)
                return View("EditVehicle", vehicle);

            if (string.IsNullOrWhiteSpace(vehicle.LicensePlate))
            {
                ModelState.AddModelError("LicensePlate", "يرجى إدخال رقم اللوحة.");
                return View("EditVehicle", vehicle);
            }

            var existingVehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleId == id && v.DriverUserId == currentUserId.Value);

            if (existingVehicle == null)
                return NotFound();

            // Normalize plate before checking duplicate.
            // توحيد صيغة اللوحة قبل فحص التكرار.
            var normalizedPlate = vehicle.LicensePlate.Trim().ToUpper();

            bool plateExists = await _context.Vehicles
                .AnyAsync(v => v.VehicleId != id &&
                               v.LicensePlate != null &&
                               v.LicensePlate.Trim().ToUpper() == normalizedPlate);

            if (plateExists)
            {
                ModelState.AddModelError("LicensePlate", "رقم اللوحة مسجل مسبقًا.");
                return View("EditVehicle", vehicle);
            }

            existingVehicle.LicensePlate = normalizedPlate;
            existingVehicle.Model = string.IsNullOrWhiteSpace(vehicle.Model) ? null : vehicle.Model.Trim();
            existingVehicle.Color = string.IsNullOrWhiteSpace(vehicle.Color) ? null : vehicle.Color.Trim();
            existingVehicle.Year = vehicle.Year;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Vehicles.Any(e => e.VehicleId == id))
                    return NotFound();

                throw;
            }

            TempData["ToastSuccess"] = "تم تعديل بيانات المركبة بنجاح.";

            return RedirectToAction(nameof(Index));
        }

        // ======================
        // GET DELETE (Confirm Page)
        // ======================

        /*
        - Retrieves vehicle.
        - Displays confirmation page.
         */
        [AuthorizeUser]
        public async Task<IActionResult> Delete(int id)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");

            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var vehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleId == id && v.DriverUserId == currentUserId.Value);

            if (vehicle == null)
                return NotFound();

            return View("DeleteVehicle", vehicle);
        }

        // ======================
        // POST DELETE (Real Delete)
        // ======================

        /*
        - Removes vehicle from database.
        - Saves changes.
        - Redirects to list page.
         */

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");

            if (currentUserId == null)
                return RedirectToAction("Login", "Auth");

            var vehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleId == id && v.DriverUserId == currentUserId.Value);

            if (vehicle == null)
                return NotFound();

            bool isUsedInAccident = await _context.AccidentSessionParticipants
                .AnyAsync(p => p.VehicleId == id);

            if (isUsedInAccident)
            {
                TempData["ToastError"] = "لا يمكن حذف هذه المركبة لأنها مرتبطة بحادث سابق.";
                return RedirectToAction(nameof(Index));
            }

            _context.Vehicles.Remove(vehicle);

            try
            {
                await _context.SaveChangesAsync();
                TempData["ToastSuccess"] = "تم حذف المركبة بنجاح.";
            }
            catch (DbUpdateException)
            {
                TempData["ToastError"] = "تعذر حذف المركبة لأنها مرتبطة ببيانات أخرى في النظام.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}


