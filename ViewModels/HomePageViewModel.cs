namespace Aoun.ViewModels
{
    public class HomePageViewModel
    {
        public int DriverId { get; set; }
        public string DriverName { get; set; } = "مستخدم";

        public List<RecentAccidentCard> RecentAccidents { get; set; } = new();
    }

    public class RecentAccidentCard
    {
        public int AccidentId { get; set; }
        public DateOnly? AccidentDate { get; set; }   // من accident_date
        public TimeOnly? AccidentTime { get; set; }   // من accident_time
        public string Status { get; set; } = "";
        public int? FaultPercent { get; set; }        // من Accident_Report حسب vehicle_role
    }
}
