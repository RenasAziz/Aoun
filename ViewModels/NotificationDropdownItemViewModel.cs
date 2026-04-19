namespace Aoun.ViewModels
{
    public class NotificationDropdownItemViewModel
    {
        public int NotificationId { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Type { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? ReferenceId { get; set; }
    }
}