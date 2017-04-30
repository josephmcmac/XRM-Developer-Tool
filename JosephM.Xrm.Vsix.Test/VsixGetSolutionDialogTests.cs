﻿using JosephM.Application.ViewModel.RecordEntry.Form;
using JosephM.ObjectMapping;
using JosephM.Record.Xrm.Mappers;
using JosephM.XRM.VSIX.Commands.GetSolution;
using JosephM.XRM.VSIX.Commands.RefreshConnection;
using JosephM.XRM.VSIX.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace JosephM.Xrm.Vsix.Test
{
    [TestClass]
    public class VsixGetSolutionDialogTests : JosephMVsixTests
    {
        [TestMethod]
        public void VsixGetSolutionDialogTest()
        {
            var fakeVisualStudioService = CreateVisualStudioService();
            
            var settings = GetPackageSettingsAddToSolution(); ;
            //var dialog = new GetSolutionDialog(CreateDialogController(), XrmRecordService, settings, fakeVisualStudioService);
            //dialog.Controller.BeginDialog();
        }
    }
}