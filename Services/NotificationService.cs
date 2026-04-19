using Aoun.Models;
using Microsoft.EntityFrameworkCore;

namespace Aoun.Services
{
    public class NotificationService
    {
        private readonly AounDbContext _db;

        public NotificationService(AounDbContext db)
        {
            _db = db;
        }

        public async Task CreateAsync(int userId, string title, string message, string? type = null, int? referenceId = null)
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                ReferenceId = referenceId,
                IsRead = false,
                CreatedAt = DateTime.Now
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();
        }

        public async Task CreateForUsersAsync(IEnumerable<int> userIds, string title, string message, string? type = null, int? referenceId = null)
        {
            var distinctUserIds = userIds.Distinct().ToList();

            foreach (var userId in distinctUserIds)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = userId,
                    Title = title,
                    Message = message,
                    Type = type,
                    ReferenceId = referenceId,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();
        }

        public async Task<List<Notification>> GetUserNotificationsAsync(int userId, int count = 10)
        {
            return await _db.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync(int userId)
        {
            return await _db.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);
        }

        public async Task MarkAllAsReadAsync(int userId)
        {
            var items = await _db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var item in items)
            {
                item.IsRead = true;
            }

            await _db.SaveChangesAsync();
        }
    }
}