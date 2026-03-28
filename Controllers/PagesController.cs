using Microsoft.AspNetCore.Mvc;
using Aoun.Models;
using Aoun.Filters;
using System.Linq;


/*
===============================================================================
PagesController
===============================================================================
This controller handles static and general pages in the system.

Includes:
- Public pages (About, Contact, Help)
- Protected pages (Profile, Settings)

Protected pages require the user to be logged in using the
custom [AuthorizeUser] filter.
===============================================================================
*/

public class PagesController : Controller
{
    private readonly AounDbContext _context;

    public PagesController(AounDbContext context)
    {
        _context = context;
    }

    // =========================
    // PUBLIC PAGES
    // =========================

    // These pages are accessible to all users without authentication.

    public IActionResult About()
    {
        return View();
    }

    public IActionResult Contact()
    {
        return View();
    }

    public IActionResult Help()
    {
        return View();
    }

    // =========================
    // PROTECTED PAGES
    // =========================

    /*
    These pages require a logged-in user.
    
    [AuthorizeUser] filter checks:
        - If UserId exists in Session.
        - If not → redirects to Login.

    Profile page:
        - Retrieves logged-in user.
        - Retrieves associated driver record.
        - Passes data to the view.
     */

    [AuthorizeUser]
    public IActionResult Profile()
    {
        var userId = HttpContext.Session.GetInt32("UserId");

        if (userId == null)
            return RedirectToAction("Login", "Auth");

        var user = _context.Users.Find(userId);
        if (user == null)
            return RedirectToAction("Login", "Auth");

        var driver = _context.Drivers
            .FirstOrDefault(d => d.UserId == userId);

        ViewBag.Driver = driver;

        return View(user);
    }

    [AuthorizeUser]
    public IActionResult Settings()
    {
        return View();
    }
}

