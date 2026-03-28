using System.Collections.Generic;

namespace Aoun.ViewModels.Accident
{
    public class AccidentListViewModel
    {
        public List<AccidentListItemViewModel> Accidents { get; set; }
            = new();
    }
}
