using JosephM.Core.Attributes;
using JosephM.Core.FieldType;
using JosephM.Core.Service;
using JosephM.Record.Attributes;
using JosephM.Record.IService;
using JosephM.Record.Metadata;
using JosephM.Record.Query;
using JosephM.Xrm.Schema;
using System.Collections.Generic;
using System.Linq;

namespace JosephM.XrmModule.Crud.ConvertDateTimezone
{
    [Group(Sections.RecordDetails, Group.DisplayLayoutEnum.HorizontalWrap, order: 10)]
    [Group(Sections.FieldToConvert, Group.DisplayLayoutEnum.HorizontalWrap, order: 20)]
    [Group(Sections.Options, Group.DisplayLayoutEnum.HorizontalWrap, order: 30)]
    public class ConvertDateTimezoneRequest : ServiceRequestBase
    {
        private bool _allowExecuteMultiples = true;

        public ConvertDateTimezoneRequest(RecordType recordType, IEnumerable<IRecord> recordsToUpdate)
            : this()
        {
            RecordType = recordType;
            _recordsToUpdate = recordsToUpdate;
        }

        public ConvertDateTimezoneRequest()
        {
            ExecuteMultipleSetSize = 50;
        }

        private IEnumerable<IRecord> _recordsToUpdate { get; set; }

        public IEnumerable<IRecord> GetRecordsToUpdate()
        {
            return _recordsToUpdate;
        }

        [RecordTypeFor(nameof(FieldToConvert))]
        [RecordTypeFor(nameof(SetFieldWhenUpdated))]
        [Group(Sections.RecordDetails)]
        [DisplayOrder(10)]
        public RecordType RecordType { get; private set; }

        [Group(Sections.RecordDetails)]
        [DisplayOrder(20)]
        public int RecordCount { get { return _recordsToUpdate?.Count() ?? 0; } }

        [Group(Sections.FieldToConvert)]
        [DisplayOrder(30)]
        [RequiredProperty]
        [LookupCondition(nameof(IFieldMetadata.FieldType), ConditionType.Equal, RecordFieldType.Date)]
        public RecordField FieldToConvert { get; set; }

        [Group(Sections.FieldToConvert)]
        [DisplayOrder(40)]
        [RequiredProperty]
        [ReferencedType(Entities.timezonedefinition)]
        [UsePicklist(OverrideDisplayField = Fields.timezonedefinition_.userinterfacename)]
        public Lookup TargetTimeZone { get; set; }

        [Group(Sections.Options)]
        [DisplayOrder(50)]
        [LookupCondition(nameof(IFieldMetadata.FieldType), ConditionType.In, new[] { RecordFieldType.Boolean, RecordFieldType.Date})]
        public RecordField SetFieldWhenUpdated { get; set; }

        [Group(Sections.Options)]
        [DisplayOrder(40)]
        [RequiredProperty]
        [MinimumIntValue(1)]
        [MaximumIntValue(1000)]
        public int? ExecuteMultipleSetSize { get; set; }

        [Group(Sections.Options)]
        [DisplayOrder(50)]
        [RequiredProperty]
        [MinimumIntValue(0)]
        public int WaitPerMessage { get; set; }

        [Hidden]
        public bool AllowExecuteMultiples
        {
            get => _allowExecuteMultiples; set
            {
                _allowExecuteMultiples = value;
                if (!value)
                    ExecuteMultipleSetSize = 1;
            }
        }

        private static class Sections
        {
            public const string RecordDetails = "Selected Update Details";
            public const string FieldToConvert = "Field To Convert";
            public const string Options = "Options";
        }
    }
}