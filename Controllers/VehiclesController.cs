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
            if (ModelState.IsValid)
            {
                var currentUserId = HttpContext.Session.GetInt32("UserId");

                if (currentUserId == null)
                    return RedirectToAction("Login", "Auth");

                vehicle.DriverUserId = currentUserId.Value;

                _context.Vehicles.Add(vehicle);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("AddVehicle", vehicle);
        }

        // ======================
        // GET EDIT
        // ======================

        /*
        - Retrieves vehicle by ID.
        - Displays edit form.
         */

        public async Task<IActionResult> Edit(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);

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
            if (id != vehicle.VehicleId)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(vehicle);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Vehicles.Any(e => e.VehicleId == id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View("EditVehicle", vehicle);
        }

        // ======================
        // GET DELETE (Confirm Page)
        // ======================

        /*
        - Retrieves vehicle.
        - Displays confirmation page.
         */

        public async Task<IActionResult> Delete(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);

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
            var vehicle = await _context.Vehicles.FindAsync(id);

            if (vehicle != null)
            {
                _context.Vehicles.Remove(vehicle);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}


