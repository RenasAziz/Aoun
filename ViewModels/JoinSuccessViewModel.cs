namespace Aoun.ViewModels
{
    public class JoinSuccessViewModel
    {
        public int AccidentId { get; set; }
        public string AccidentCode { get; set; } = "";

        public string? Location { get; set; }
        public string? AccidentDate { get; set; }   // للعرض
        public string? AccidentTime { get; set; }   // للعرض

        public int Role { get; set; }               // 1 or 2
        public string RoleText => Role == 1 ? "سائق 1" : "سائق 2";
    }
}
