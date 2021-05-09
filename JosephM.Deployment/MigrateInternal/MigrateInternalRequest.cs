﻿using JosephM.Core.Attributes;
using JosephM.Core.FieldType;
using JosephM.Core.Service;
using JosephM.Deployment.SpreadsheetImport;
using System.Collections.Generic;

namespace JosephM.Deployment.MigrateInternal
{
    [DisplayName("Migrate Internal")]
    [AllowSaveAndLoad]
    [Group(Sections.Main, true, 10)]
    [Group(Sections.Options, true, 20)]
    public class MigrateInternalRequest : ServiceRequestBase
    {
        public MigrateInternalRequest()
        {
            ExecuteMultipleSetSize = 50;
            TargetCacheLimit = 1000;
        }

        [Group(Sections.Options)]
        [DisplayOrder(410)]
        [RequiredProperty]
        public bool MatchRecordsByName { get; set; }

        [Group(Sections.Options)]
        [DisplayOrder(420)]
        [RequiredProperty]
        [MinimumIntValue(1)]
        [MaximumIntValue(1000)]
        public int? ExecuteMultipleSetSize { get; set; }

        [Group(Sections.Options)]
        [DisplayOrder(425)]
        [RequiredProperty]
        [MinimumIntValue(1)]
        [MaximumIntValue(5000)]
        public int? TargetCacheLimit { get; set; }

        [AllowGridFullScreen]
        [RequiredProperty]
        public IEnumerable<MigrateInternalTypeMapping> Mappings { get; set; }

        private static class Sections
        {
            public const string Main = "Main";
            public const string Options = "Options";
        }

        [DoNotAllowGridOpen]
        [Group(Sections.Main, true, 10)]
        public class MigrateInternalTypeMapping : IMapSourceImport
        {
            [Group(Sections.Main)]
            [DisplayOrder(10)]
            [RequiredProperty]
            [IncludeManyToManyIntersects]
            [RecordTypeFor(nameof(Mappings) + "." + nameof(MigrateInternalFieldMapping.SourceField))]
            public RecordType SourceType { get; set; }

            [Group(Sections.Main)]
            [DisplayOrder(20)]
            [RequiredProperty]
            [IncludeManyToManyIntersects]
            [RecordTypeFor(nameof(Mappings) + "." + nameof(MigrateInternalFieldMapping.TargetField))]
            public RecordType TargetType { get; set; }

            [AllowNestedGridEdit]
            [RequiredProperty]
            [GridWidth(800)]
            [PropertyInContextByPropertyNotNull(nameof(SourceType))]
            [PropertyInContextByPropertyNotNull(nameof(TargetType))]
            public IEnumerable<MigrateInternalFieldMapping> Mappings { get; set; }

            string IMapSourceImport.SourceType => SourceType?.Key;
            string IMapSourceImport.TargetType => TargetType?.Key;
            string IMapSourceImport.TargetTypeLabel => TargetType?.Value;
            bool IMapSourceImport.IgnoreDuplicates => false;
            IEnumerable<IMapSourceMatchKey> IMapSourceImport.AltMatchKeys => null;
            IEnumerable<IMapSourceField> IMapSourceImport.FieldMappings => Mappings;

            public override string ToString()
            {
                return (SourceType?.Value ?? "(None)") + " > " + (TargetType?.Value ?? "(None)");
            }

            private static class Sections
            {
                public const string Main = "Main";
            }

            [DoNotAllowGridOpen]
            public class MigrateInternalFieldMapping : IMapSourceField
            {
                [RequiredProperty]
                public RecordField SourceField { get; set; }

                [RequiredProperty]
                public RecordField TargetField { get; set; }

                string IMapSourceField.SourceField => SourceField?.Key;

                string IMapSourceField.TargetField => TargetField?.Key;

                bool IMapSourceField.UseAltMatchField => false;

                string IMapSourceField.AltMatchFieldType => null;

                string IMapSourceField.AltMatchField => null;

                public override string ToString()
                {
                    return (SourceField?.Value ?? "(None)") + " > " + (TargetField?.Value ?? "(None)");
                }
            }
        }
    }
}