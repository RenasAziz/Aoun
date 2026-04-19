using Aoun.Models;
using Aoun.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aoun.ViewComponents
{
    public class NotificationsViewComponent : ViewComponent
    {
        private readonly AounDbContext _context;

        public NotificationsViewComponent(AounDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            if (userId == null)
            {
                return View(new List<NotificationDropdownItemViewModel>());
            }

            var notifications = await _context.Notifications
       .AsNoTracking()
       .Where(n => n.UserId == userId.Value && !n.IsRead)
       .OrderByDescending(n => n.CreatedAt)
       .Take(6)
       .Select(n => new NotificationDropdownItemViewModel
       {
           NotificationId = n.NotificationId,
           Title = n.Title,
           Message = n.Message,
           Type = n.Type,
           IsRead = n.IsRead,
           CreatedAt = n.CreatedAt,
           ReferenceId = n.ReferenceId
       })
       .ToListAsync();

            return View(notifications);
        }
    }
}