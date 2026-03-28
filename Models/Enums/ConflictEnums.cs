namespace Aoun.Models
{
    // Arabic: أنواع التعارض الممكنة في النظام
    // English: Supported conflict types in the system
    public enum ConflictType
    {
        LaneChange = 1,
        EnteringRoad = 2,
        SpecialMove = 3,
        Position = 4,

        // Intersection-related conflicts
        IntersectionControl = 10,
        IntersectionCompliance = 11,
        IntersectionEntryFirst = 12,

        Overtake = 20
    }

    // Arabic: شدة التعارض
    // English: Conflict severity level
    public enum ConflictSeverity
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

}