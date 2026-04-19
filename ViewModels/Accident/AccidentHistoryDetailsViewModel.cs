using Aoun.ViewModels;

namespace Aoun.ViewModels.Accident
{
    public class AccidentHistoryDetailsViewModel : FinalResultViewModel
    {
        public string? InspectorNote { get; set; }
        public string ApprovalStatus { get; set; } = "قيد المراجعة";

        public string ReportTitle { get; set; } = "تقرير حادث رسمي";
        public string ReportSource { get; set; } = "منصة عون - نظام تحليل الحوادث";
        public string ReportReference { get; set; } = "";
        public string GeneratedOnText { get; set; } = "";

        public string DriverName { get; set; } = "—";
        public string DriverRoleText { get; set; } = "—";

        public string VehiclePlate { get; set; } = "—";
        public string VehicleModel { get; set; } = "—";
        public string VehicleColor { get; set; } = "—";
        public string VehicleYearText { get; set; } = "—";
    }
}