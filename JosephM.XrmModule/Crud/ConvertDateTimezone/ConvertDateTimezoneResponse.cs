using JosephM.Core.Attributes;
using JosephM.Core.Service;
using System.ComponentModel;

namespace JosephM.XrmModule.Crud.ConvertDateTimezone
{
    [Group(Sections.Counts, true, 0, displayLabel: false)]
    public class ConvertDateTimezoneResponse : ServiceResponseBase<ConvertDateTimezoneResponseItem>, INotifyPropertyChanged
    {
        private int _totalRecordsProcessed;
        private int _numberOfErrors;

        [DisplayOrder(40)]
        [Group(Sections.Counts)]
        public int TotalRecordsProcessed
        {
            get { return _totalRecordsProcessed; }
            set
            {
                _totalRecordsProcessed = value;
                OnPropertyChanged(nameof(TotalRecordsProcessed));
            }
        }

        [DisplayOrder(50)]
        [Group(Sections.Counts)]
        [Core.Attributes.DisplayName("Number of Errors")]
        public int NumberOfErrors
        {
            get { return _numberOfErrors; }
            set
            {
                _numberOfErrors = value;
                OnPropertyChanged(nameof(NumberOfErrors));
            }
        }
        private static class Sections
        {
            public const string Counts = "Counts";
        }
    }
}