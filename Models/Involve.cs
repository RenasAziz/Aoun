using System.ComponentModel.DataAnnotations.Schema;

namespace Aoun.Models;

[Table("Involves")]
public partial class Involve
{
    [Column("accident_id")]
    public int AccidentId { get; set; }

    [Column("vehicle_id")]
    public int VehicleId { get; set; }

    [Column("vehicle_role")]
    public int VehicleRole { get; set; }

    // ✅ Navigation
    public virtual Accident Accident { get; set; } = null!;
    public virtual Vehicle Vehicle { get; set; } = null!;
}
