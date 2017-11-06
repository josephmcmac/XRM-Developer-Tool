﻿using JosephM.Application.ViewModel.Grid;
using JosephM.Application.ViewModel.RecordEntry.Field;
using JosephM.Application.ViewModel.RecordEntry.Form;
using JosephM.Record.Extentions;
using JosephM.Xrm.Vsix.Module.PluginTriggers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Entities = JosephM.Xrm.Schema.Entities;
using Fields = JosephM.Xrm.Schema.Fields;

namespace JosephM.Xrm.Vsix.Test
{
    [TestClass]
    public class VsixManagePluginTriggersTests : JosephMVsixTests
    {
        [TestMethod]
        public void VsixManagePluginTriggersTest()
        {
            var packageSettings = GetTestPackageSettings();
            DeployAssembly(packageSettings);

            var assemblyRecord = GetTestPluginAssemblyRecords().First();

            DeletePluginTriggers(assemblyRecord);

            //add one update trigger
            RunDialogAndAddMessage("Update");

            //verify trigger created
            var triggers = GetPluginTriggers(assemblyRecord);
            Assert.AreEqual(1, triggers.Count());
            Assert.IsTrue(triggers.First().GetBoolField(Fields.sdkmessageprocessingstep_.asyncautodelete));
            Assert.IsNull(triggers.First().GetStringField(Fields.sdkmessageprocessingstep_.filteringattributes));

            //verify preimage created for update with all fields
            var image = XrmRecordService.GetFirst(Entities.sdkmessageprocessingstepimage,
                Fields.sdkmessageprocessingstepimage_.sdkmessageprocessingstepid, triggers.First().Id);
            Assert.IsNotNull(image);
            Assert.IsNull(image.GetStringField(Fields.sdkmessageprocessingstepimage_.attributes));
            Assert.AreEqual("PreImage", image.GetStringField(Fields.sdkmessageprocessingstepimage_.entityalias));

            //add one create trigger
            RunDialogAndAddMessage("Create");
            
            //verify created
            triggers = GetPluginTriggers(assemblyRecord);
            Assert.AreEqual(2, triggers.Count());

            //delete a trigger
            var dialog = new ManagePluginTriggersDialog(CreateDialogController(), new FakeVisualStudioService(), XrmRecordService, packageSettings);
            dialog.Controller.BeginDialog();

            var entryViewModel = (ObjectEntryViewModel)dialog.Controller.UiItems.First();
            var triggersSubGrid = entryViewModel.SubGrids.First();

            triggersSubGrid.GridRecords.First().DeleteRow();
            Assert.IsTrue(entryViewModel.Validate());
            entryViewModel.OnSave();

            //verify deleted
            triggers = GetPluginTriggers(assemblyRecord);
            Assert.AreEqual(1, triggers.Count());


            //add 2 update triggers
            RunDialogAndAddMessage("Update");
            RunDialogAndAddMessage("Update");
            triggers = GetPluginTriggers(assemblyRecord);
            Assert.AreEqual(3, triggers.Count());

            //okay now lets inspect and adjust the filtering attributes and preimages in one of the update messages
            dialog = new ManagePluginTriggersDialog(CreateDialogController(), new FakeVisualStudioService(), XrmRecordService, packageSettings);
            dialog.Controller.BeginDialog();
            entryViewModel = (ObjectEntryViewModel)dialog.Controller.UiItems.First();
            triggersSubGrid = entryViewModel.SubGrids.First();

            var updateRows = triggersSubGrid.GridRecords.Where(r => r.GetLookupFieldFieldViewModel(nameof(PluginTrigger.Message)).Value.Name == "Update");
            var letsAdjustThisOne = updateRows.First();
            //set no not all preimage fields
            letsAdjustThisOne.GetBooleanFieldFieldViewModel(nameof(PluginTrigger.PreImageAllFields)).Value = false;
            //set some arbitrary other image name
            letsAdjustThisOne.GetStringFieldFieldViewModel(nameof(PluginTrigger.PreImageName)).Value = "FooOthername";
            //set some specific fields in the preimage
            var preImageFieldsField = letsAdjustThisOne.GetFieldViewModel<RecordFieldMultiSelectFieldViewModel>(nameof(PluginTrigger.PreImageFields));
            preImageFieldsField.MultiSelectsVisible = true;
            preImageFieldsField.DynamicGridViewModel.GridRecords.ElementAt(1).GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = true;
            preImageFieldsField.DynamicGridViewModel.GridRecords.ElementAt(3).GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = true;
            //set some specific filtering attributes
            var filteringAttributesField = letsAdjustThisOne.GetFieldViewModel<RecordFieldMultiSelectFieldViewModel>(nameof(PluginTrigger.FilteringFields));
            filteringAttributesField.MultiSelectsVisible = true;
            filteringAttributesField.DynamicGridViewModel.GridRecords.ElementAt(1).GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = true;
            filteringAttributesField.DynamicGridViewModel.GridRecords.ElementAt(3).GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = true;

            //save
            Assert.IsTrue(entryViewModel.Validate());
            entryViewModel.OnSave();

            //verify still 3 triggers
            triggers = GetPluginTriggers(assemblyRecord);
            Assert.AreEqual(3, triggers.Count());

            //get the record we updated
            var updatedTriggerMatches = triggers.Where(t => t.GetStringField(Fields.sdkmessageprocessingstep_.filteringattributes) != null);
            Assert.AreEqual(1, updatedTriggerMatches.Count());
            var updatedTrigger = updatedTriggerMatches.First();
            //verify the filtering and image fields we set got saved correctly
            Assert.IsNotNull(updatedTrigger.GetStringField(Fields.sdkmessageprocessingstep_.filteringattributes));
            image = XrmRecordService.GetFirst(Entities.sdkmessageprocessingstepimage,
                Fields.sdkmessageprocessingstepimage_.sdkmessageprocessingstepid, updatedTrigger.Id);
            Assert.IsNotNull(image);
            Assert.IsNotNull(image.GetStringField(Fields.sdkmessageprocessingstepimage_.attributes));
            Assert.AreEqual("FooOthername", image.GetStringField(Fields.sdkmessageprocessingstepimage_.entityalias));

            //lets just verify if we go through te dialog without touching the record we adjusted that it is still the same after the save
            dialog = new ManagePluginTriggersDialog(CreateDialogController(), new FakeVisualStudioService(), XrmRecordService, packageSettings);
            dialog.Controller.BeginDialog();
            entryViewModel = (ObjectEntryViewModel)dialog.Controller.UiItems.First();
            Assert.IsTrue(entryViewModel.Validate());
            entryViewModel.OnSave();

            updatedTrigger = XrmRecordService.Get(updatedTrigger.Type, updatedTrigger.Id);
            Assert.IsNotNull(updatedTrigger.GetStringField(Fields.sdkmessageprocessingstep_.filteringattributes));
            XrmRecordService.GetFirst(Entities.sdkmessageprocessingstepimage, Fields.sdkmessageprocessingstepimage_.sdkmessageprocessingstepid, triggers.First().Id);
            Assert.IsNotNull(image);
            Assert.IsNotNull(image.GetStringField(Fields.sdkmessageprocessingstepimage_.attributes));
            Assert.AreEqual("FooOthername", image.GetStringField(Fields.sdkmessageprocessingstepimage_.entityalias));
        }

        private void RunDialogAndAddMessage(string message)
        {
            var packageSettings = GetTestPackageSettings(); ;
            var dialog = new ManagePluginTriggersDialog(CreateDialogController(), new FakeVisualStudioService(), XrmRecordService, packageSettings);
            dialog.Controller.BeginDialog();
            var entryViewModel = (ObjectEntryViewModel)dialog.Controller.UiItems.First();
            var triggersSubGrid = entryViewModel.SubGrids.First();
            var newRow = AddtriggerForMessage(triggersSubGrid, message);
            Assert.IsTrue(entryViewModel.Validate());
            entryViewModel.OnSave();
        }

        private static GridRowViewModel AddtriggerForMessage(EnumerableFieldViewModel triggersSubGrid, string message)
        {
            triggersSubGrid.AddRow();
            var newRow = triggersSubGrid.GridRecords.First();
            PopulateRowForMessage(newRow, message);
            return newRow;
        }

        private static void PopulateRowForMessage(GridRowViewModel newRow, string message)
        {
            foreach (var field in newRow.FieldViewModels)
            {
                if (field.ValueObject == null)
                {
                    if (field is LookupFieldViewModel)
                    {
                        var typeFieldViewModel = (LookupFieldViewModel) field;
                        if (field.FieldName == "Message")
                        {
                            typeFieldViewModel.Value = typeFieldViewModel.LookupService.ToLookup(typeFieldViewModel.ItemsSource.First(m => m.Name == message).Record);
                        }
                        else if (typeFieldViewModel.UsePicklist)
                            typeFieldViewModel.Value = typeFieldViewModel.LookupService.ToLookup(typeFieldViewModel.ItemsSource.First().Record); ;
                    }
                    if (field is PicklistFieldViewModel)
                    {
                        var typeFieldViewModel = (PicklistFieldViewModel) field;
                        typeFieldViewModel.Value = typeFieldViewModel.ItemsSource.First();
                    }
                    if (field is RecordTypeFieldViewModel)
                    {
                        var typeFieldViewModel = (RecordTypeFieldViewModel) field;
                        typeFieldViewModel.Value = typeFieldViewModel.ItemsSource.First();
                    }
                    if (field.FieldName == nameof(PluginTrigger.FilteringFields) && message == "Update")
                    {
                        var multiSelectField = newRow.GetFieldViewModel<RecordFieldMultiSelectFieldViewModel>(nameof(PluginTrigger.FilteringFields));
                        multiSelectField.MultiSelectsVisible = true;
                        multiSelectField.DynamicGridViewModel.GridRecords.ElementAt(1).GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = true;
                        multiSelectField.DynamicGridViewModel.GridRecords.ElementAt(2).GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = true;
                    }
                    if (field.FieldName == nameof(PluginTrigger.PreImageAllFields) && message == "Update")
                    {
                        newRow.GetFieldViewModel<BooleanFieldViewModel>(nameof(PluginTrigger.PreImageAllFields)).Value = false;
                    }
                    if (field.FieldName == nameof(PluginTrigger.PreImageFields) && message == "Update")
                    {
                        var multiSelectField = newRow.GetFieldViewModel<RecordFieldMultiSelectFieldViewModel>(nameof(PluginTrigger.PreImageFields));
                        multiSelectField.MultiSelectsVisible = true;
                        multiSelectField.DynamicGridViewModel.GridRecords.ElementAt(1).GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = true;
                        multiSelectField.DynamicGridViewModel.GridRecords.ElementAt(2).GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = true;
                    }
                }
            }
            newRow.GetPicklistFieldFieldViewModel(nameof(PluginTrigger.Mode)).ValueObject = PluginTrigger.PluginMode.Asynchronous;
            newRow.GetPicklistFieldFieldViewModel(nameof(PluginTrigger.Stage)).ValueObject = PluginTrigger.PluginStage.PostEvent;
            newRow.GetBooleanFieldFieldViewModel(nameof(PluginTrigger.PreImageAllFields)).Value = true;
            var filteringAttributesField = newRow.GetFieldViewModel<RecordFieldMultiSelectFieldViewModel>(nameof(PluginTrigger.FilteringFields));
            filteringAttributesField.MultiSelectsVisible = true;
            foreach (var field in filteringAttributesField.DynamicGridViewModel.GridRecords)
            {
                field.GetBooleanFieldFieldViewModel(nameof(RecordFieldMultiSelectFieldViewModel.SelectablePicklistOption.Select)).Value = false;
            }
        }
    }
}
