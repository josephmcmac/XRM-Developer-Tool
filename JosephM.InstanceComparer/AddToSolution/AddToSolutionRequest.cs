﻿using JosephM.Core.Attributes;
using JosephM.Core.FieldType;
using JosephM.Core.Service;
using JosephM.Record.Attributes;
using JosephM.Record.Extentions;
using JosephM.Record.Query;
using JosephM.Record.Xrm.XrmRecord;
using JosephM.Xrm.Schema;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JosephM.InstanceComparer.AddToSolution
{
    [Group(Sections.Solution, true, 10)]
    [Group(Sections.Types, true, order: 20, selectAll: true)]
    public class AddToSolutionRequest : ServiceRequestBase
    {
        public AddToSolutionRequest(IEnumerable<AddToSolutionItem> items, XrmRecordService xrmRecordService)
        {
            Items = items
                .GroupBy(i => i.ComponentType)
                .Select(g => new AddToSolutionComponent(g.Key, g.Select(c => c.ComponentId).Distinct().ToArray(), xrmRecordService))
                .ToArray();
        }

        public AddToSolutionRequest()
        {
            Items = new AddToSolutionComponent[0];
        }

        [Group(Sections.Solution)]
        [RequiredProperty]
        [ReferencedType(Entities.solution)]
        [UsePicklist(Fields.solution_.uniquename)]
        [LookupCondition(Fields.solution_.ismanaged, false)]
        [LookupCondition(Fields.solution_.isvisible, true)]
        [LookupCondition(Fields.solution_.uniquename, ConditionType.NotEqual, "default")]
        public Lookup SolutionAddTo { get; set; }

        public IEnumerable<AddToSolutionComponent> GetItemsToInclude()
        {
            return Items.Where(i => i.Selected);
        }

        [DisplayName("Components For Inclusion")]
        [DoNotAllowAdd]
        [DoNotAllowDelete]
        [Group(Sections.Types)]
        [RequiredProperty]
        public IEnumerable<AddToSolutionComponent> Items { get; set; }

        private static class Sections
        {
            public const string Solution = "Solution";
            public const string Types = "Types";
        }

        [Group(Sections.Main, true)]
        public class AddToSolutionComponent : ISelectable
        {
            [DisplayName("Include")]
            [Group(Sections.Main)]
            [GridWidth(75)]
            public bool Selected { get; set; }

            [Group(Sections.Main)]
            [GridWidth(225)]
            public string ComponentType { get; private set; }
            [Hidden]
            public int ComponentTypeKey { get; set; }
            [Group(Sections.Main)]
            [GridWidth(75)]
            public int Count
            {
                get
                {
                    return Items.Count();
                }
            }

            [DoNotAllowAdd]
            [DoNotAllowDelete]
            [DoNotAllowGridEdit]
            [GridWidth(400)]
            public IEnumerable<AddToSolutionComponentItem> Items { get; set; }

            public AddToSolutionComponent(int componentType, IEnumerable<string> ids, XrmRecordService xrmRecordService)
            {
                ComponentTypeKey = componentType;
                ComponentType = xrmRecordService.GetPicklistLabel(Fields.solutioncomponent_.componenttype, Entities.solutioncomponent, componentType.ToString());

                LoadComponentItems(ids, xrmRecordService);
            }

            private static class Sections
            {
                public const string Main = "Main";
            }

            public class AddToSolutionComponentItem
            {
                public AddToSolutionComponentItem(string id, string name)
                {
                    Id = id;
                    Name = name;
                }
                [Hidden]
                public string Id { get; set; }

                [GridWidth(400)]
                public string Name { get; set; }

                public override string ToString()
                {
                    return Name;
                }
            }

            private void LoadComponentItems(IEnumerable<string> ids, XrmRecordService xrmRecordService)
            {
                if(ComponentTypeKey == OptionSets.SolutionComponent.ObjectTypeCode.Entity)
                {
                    Items = xrmRecordService
                        .GetAllRecordTypes()
                        .Select(r => xrmRecordService.GetRecordTypeMetadata(r))
                        .Where(m => ids.Contains(m.MetadataId))
                        .Select(m => new AddToSolutionComponentItem(m.MetadataId, m.DisplayName))
                        .ToArray();
                }
                else if (ComponentTypeKey == OptionSets.SolutionComponent.ObjectTypeCode.OptionSet)
                {
                    Items = xrmRecordService
                        .GetSharedPicklists()
                        .Where(m => ids.Contains(m.MetadataId))
                        .Select(m => new AddToSolutionComponentItem(m.MetadataId, m.DisplayName))
                        .ToArray();
                }
                else
                {
                    var propTypeMaps = new Dictionary<int, string>();
                    propTypeMaps.Add(OptionSets.SolutionComponent.ObjectTypeCode.SystemForm, Entities.systemform);
                    propTypeMaps.Add(OptionSets.SolutionComponent.ObjectTypeCode.EmailTemplate, Entities.template);
                    propTypeMaps.Add(OptionSets.SolutionComponent.ObjectTypeCode.PluginAssembly, Entities.pluginassembly);
                    propTypeMaps.Add(OptionSets.SolutionComponent.ObjectTypeCode.SDKMessageProcessingStep, Entities.sdkmessageprocessingstep);
                    propTypeMaps.Add(OptionSets.SolutionComponent.ObjectTypeCode.Report, Entities.report);
                    propTypeMaps.Add(OptionSets.SolutionComponent.ObjectTypeCode.Role, Entities.role);
                    propTypeMaps.Add(OptionSets.SolutionComponent.ObjectTypeCode.WebResource, Entities.webresource);
                    propTypeMaps.Add(OptionSets.SolutionComponent.ObjectTypeCode.Workflow, Entities.workflow);
                    if (!propTypeMaps.ContainsKey(ComponentTypeKey))
                        throw new NotImplementedException($"Component Type {ComponentTypeKey} Is Not Implemented");

                    var recordType = propTypeMaps[ComponentTypeKey];
                    var primaryKeyField = xrmRecordService.GetPrimaryKey(recordType);
                    var nameField = xrmRecordService.GetPrimaryField(recordType);

                    Items = xrmRecordService
                        .RetrieveAllOrClauses(recordType, ids.Select(i => new Condition(primaryKeyField, ConditionType.Equal, i)))
                        .Select(e => new AddToSolutionComponentItem(e.Id, e.GetStringField(nameField)))
                        .OrderBy(c => c.Name)
                        .ToArray();
                }
            }
        }
    }
}