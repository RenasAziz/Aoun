using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Aoun.ViewModels
{
    public class SelectVehicleViewModel
    {
        public int AccidentId { get; set; }
        public int Role { get; set; } // 1 or 2

        [Required(ErrorMessage = "يرجى اختيار مركبة.")]
        public int? SelectedVehicleId { get; set; }

        public List<VehicleOption> Vehicles { get; set; } = new();

        // Add New Vehicle (Modal)
        public string? NewLicensePlate { get; set; }
        public string? NewModel { get; set; }
        public int? NewYear { get; set; }
    }

    public class VehicleOption
    {
        public int VehicleId { get; set; }
        public string Title { get; set; } = "";
        public string Sub { get; set; } = "";
    }
}
