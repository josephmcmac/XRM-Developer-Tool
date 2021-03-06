﻿using JosephM.Application.Application;
using System.Collections.Generic;
using JosephM.Core.AppConfig;
using JosephM.Xrm.Vsix.Module.PackageSettings;
using JosephM.Xrm.Vsix.Application;
using System;
using System.Linq;

namespace JosephM.Xrm.Vsix.Module.DeployIntoField
{
    public class DeployIntoFieldMenuItemVisible : MenuItemVisibleForFileTypes
    {
        public override IEnumerable<string> ValidExtentions => DeployIntoFieldService.IntoFieldTypes;

        public override bool IsVisible(IApplicationController applicationController)
        {
            var packageSettings = applicationController.ResolveType<XrmPackageSettings>();
            var visualStudioService = applicationController.ResolveType<IVisualStudioService>();
            if (visualStudioService == null)
                throw new NullReferenceException("visualStudioService");

            if (packageSettings.DeployIntoFieldProjects == null || !packageSettings.DeployIntoFieldProjects.Any())
                return base.IsVisible(applicationController);

            var selectedItems = visualStudioService.GetSelectedItems();
            return selectedItems.All(si => packageSettings.DeployIntoFieldProjects.Any(w => w.ProjectName == si.NameOfContainingProject))
                && base.IsVisible(applicationController);
        }
    }
}