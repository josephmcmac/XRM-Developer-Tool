﻿using JosephM.Core.Attributes;
using JosephM.Core.Service;
using JosephM.Deployment.SolutionImport;
using JosephM.XrmModule.SavedXrmConnections;
using System.Linq;

namespace JosephM.Deployment.DeploySolution
{
    [Group(Sections.Summary, false, 0)]
    public class DeploySolutionResponse : ServiceResponseBase<DeploySolutionResponseItem>
    {
        [Hidden]
        public SavedXrmRecordConfiguration ConnectionDeployedInto { get; set; }

        [DisplayOrder(10)]
        [Group(Sections.Summary)]
        [PropertyInContextByPropertyNotNull(nameof(FailedSolution))]
        public string FailedSolution { get; private set; }
        [Group(Sections.Summary)]
        [DisplayOrder(20)]
        [PropertyInContextByPropertyNotNull(nameof(FailedSolutionXml))]
        public string FailedSolutionXml { get; private set; }

        public void LoadImportSolutionsResponse(ImportSolutionsResponse importSolutionResponse)
        {
            AddResponseItems(importSolutionResponse.ImportedSolutionResults.Select(i => new DeploySolutionResponseItem(i)).ToArray());
            FailedSolution = importSolutionResponse.FailedSolution;
            FailedSolutionXml = importSolutionResponse.FailedSolutionXml;
        }

        private static class Sections
        {
            public const string Summary = "Summary";
        }
    }
}