﻿#region

using JosephM.Application.ViewModel.Navigation;
using JosephM.Application.ViewModel.RecordEntry.Field;
using JosephM.Application.ViewModel.RecordEntry.Metadata;
using JosephM.Application.ViewModel.RecordEntry.Section;
using JosephM.Application.ViewModel.Shared;
using JosephM.Application.ViewModel.Validation;
using JosephM.Core.Constants;
using JosephM.Core.Extentions;
using JosephM.Core.Service;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

#endregion

namespace JosephM.Application.ViewModel.RecordEntry.Form
{
    public abstract class RecordEntryFormViewModel : RecordEntryViewModelBase
    {
        public virtual int GridPageSize { get { return StandardPageSize; } }

        private ObservableCollection<SectionViewModelBase> _formSections;

        private List<FieldViewModelBase> _recordFields;
        private string _recordType;

        protected RecordEntryFormViewModel(FormController formController, RecordEntryViewModelBase parentForm, string parentFormReference)
            : this(formController)
        {
            _parentForm = parentForm;
            _parentFormReference = parentFormReference;
        }

        protected RecordEntryFormViewModel(FormController formController)
            : base(formController)
        {
            SaveButtonViewModel = new XrmButtonViewModel(SaveButtonLabel, DoOnSave, ApplicationController)
            {
                IsVisible = false
            };
            CancelButtonViewModel = new XrmButtonViewModel(CancelButtonLabel, () => OnCancel(), ApplicationController)
            {
                IsVisible = false
            };
            LoadRequestButtonViewModel = new XrmButtonViewModel("Load Saved Details", LoadObject, ApplicationController)
            {
                IsVisible = false
            };
            SaveRequestButtonViewModel = new XrmButtonViewModel("Save Details", SaveObject, ApplicationController)
            {
                IsVisible = false
            };
            ChangedPersistentFields = new List<string>();
            LoadingViewModel.IsLoading = true;
        }

        public List<string> ChangedPersistentFields { get; private set; }

        /// <summary>
        ///     WARNING!!! Populates itself asyncronously the first time its requested
        /// </summary>
        public virtual ObservableCollection<SectionViewModelBase> FormSectionsAsync
        {
            get
            {
                if (_formSections == null)
                {
                    //Note this return an empty collection and spawns a new thread to load the sections
                    //this is to free up the ui
                    //once loaded it raises the property updated event
                    Reload();
                }
                return _formSections;
            }
            set
            {
                _formSections = value;
                foreach (var item in _formSections)
                {
                    item.IsVisible = FormService.IsSectionInContext(item.SectionIdentifier, GetRecord());
                }
                OnPropertyChanged("FormSectionsAsync");
            }
        }

        protected void Reload()
        {
            _formSections = new ObservableCollection<SectionViewModelBase>();
            StartNewAction(LoadFormSections);
        }

        public XrmButtonViewModel SaveButtonViewModel { get; private set; }

        public XrmButtonViewModel CancelButtonViewModel { get; private set; }

        public XrmButtonViewModel SaveRequestButtonViewModel { get; private set; }

        public XrmButtonViewModel LoadRequestButtonViewModel { get; private set; }

        public string RecordIdName { get; set; }

        public override string TabLabel
        {
            get { return "Create"; }
        }

        public virtual string SaveButtonLabel
        {
            get { return "Save"; }
        }

        public virtual string CancelButtonLabel
        {
            get { return "Cancel"; }
        }

        protected bool HasChangedPersistentFields
        {
            get { return ChangedPersistentFields.Any(); }
        }

        public string RecordType
        {
            get { return _recordType; }
            set
            {
                _recordType = value;
                OnPropertyChanged("TabLabel");
            }
        }

        public string RecordId { get; set; }

        public override Action<FieldViewModelBase> GetOnFieldChangeDelegate()
        {
            return f =>
            {
                AddChangedField(f);
                foreach (var action in FormService.GetOnChanges(f.FieldName))
                    action(this);
            };
        }
        //
        public IEnumerable<GridSectionViewModel> SubGrids
        {
            get { return FormSectionsAsync.Where(s => s is GridSectionViewModel).Cast<GridSectionViewModel>(); }
        }

        public void UserMessage(string message)
        {
            ApplicationController.UserMessage(message);
        }

        private void AddChangedField(FieldViewModelBase fieldViewModel)
        {
            if (fieldViewModel.IsRecordServiceField)
                if (!ChangedPersistentFields.Contains(fieldViewModel.FieldName))
                    ChangedPersistentFields.Add(fieldViewModel.FieldName);
        }

        public Action OnCancel { get; set; }

        public virtual void SaveObject()
        {
            var fileName = ApplicationController.GetSaveFileName("*", ".xml");
            if (!fileName.IsNullOrWhiteSpace())
                SaveObject(fileName);
        }

        public virtual void SaveObject(string fileName)
        {
            throw new NotImplementedException();
        }

        public virtual void LoadObject()
        {
            var selectFileDialog = new OpenFileDialog { Filter = FileMasks.XmlFile };
            selectFileDialog.Title = "Select A Saved XML File";
            var selected = selectFileDialog.ShowDialog();
            if (selected ?? false)
            {
                LoadObject(selectFileDialog.FileName);
            }
        }

        public virtual void LoadObject(string fileName)
        {
            throw new NotImplementedException();
        }

        private bool ConfirmClose()
        {
            var continueCancel = true;
            if (HasChangedPersistentFields)
            {
                continueCancel =
                    ApplicationController.UserConfirmation(
                        "The form has pending changes are you sure you want to cancel");
            }
            return continueCancel;
        }

        private void DoOnSave()
        {
            DoOnAsynchThread(() =>
            {
                try
                {
                    if (Validate())
                    {
                        OnSave();
                        ApplicationController.Remove(RegionNames.MainTabRegion, this);
                    }
                }
                catch (Exception ex)
                {
                    ApplicationController.ThrowException(ex);
                }
            });
        }

        public Action OnSave { get; set; }

        protected virtual void PreValidateExtention()
        {
        }

        public override bool Validate()
        {
            try
            {
                LoadingViewModel.IsLoading = true;
                PreValidateExtention();
                ValidationPrompt = null;
                var isValid = base.Validate();

                foreach (var section in FormSectionsAsync)
                {
                    if (!section.Validate())
                        isValid = false;
                }
                if (!isValid)
                {
                    ValidationPrompt = "There Were Validation Errors - Please Review Your Input And Retry";
                }
                else
                {
                    var finalResponse = ValidateFinal();
                    isValid = finalResponse.IsValid;
                    if (!isValid)
                        ValidationPrompt = finalResponse.GetErrorString();
                }
                return isValid;
            }
            finally
            {
                LoadingViewModel.IsLoading = false;
            }

        }

        public virtual IsValidResponse ValidateFinal()
        {
            return new IsValidResponse();
        }

        private string _validationPrompt;
        public string ValidationPrompt
        {
            get { return _validationPrompt; }
            set
            {
                _validationPrompt = value;
                OnPropertyChanged("ValidationPrompt");
            }
        }

        public void LoadFormSections()
        {
            //forcing enumeration up front
            var sections = FormService.GetFormMetadata(RecordType).FormSections.ToArray();
            var sectionViewModels = new List<SectionViewModelBase>();
            //Create the section view models

            foreach (var section in sections)
            {
                if (section is FormFieldSection)
                {
                    sectionViewModels.Add(new FieldSectionViewModel(
                        (FormFieldSection)section,
                        this
                        ));
                }
                else if (section is SubGridSection)
                {
                    sectionViewModels.Add(new GridSectionViewModel(
                        (SubGridSection)section,
                        this
                        ));
                }
            }
            //we need to populate the RecordFields property with the generated field view models
            _recordFields = new List<FieldViewModelBase>();
            foreach (
                var formSection in
                    sectionViewModels.Where(fs => fs is FieldSectionViewModel).Cast<FieldSectionViewModel>()
                )
            {
                _recordFields.AddRange(formSection.Fields);
            }
            //now set the section view model property in the ui thread which will notify the ui with the sections
            DoOnMainThread(
                () =>
                {
                    FormSectionsAsync = new ObservableCollection<SectionViewModelBase>(sectionViewModels);
                    OnSectionLoaded();
                });
        }

        protected virtual bool AllowSaveAndLoad
        {
            get { return false; }
        }

        protected override bool ConfirmTabClose()
        {
            return ConfirmClose();
        }

        public override IEnumerable<FieldViewModelBase> FieldViewModels
        {
            get
            {
                if (_recordFields == null)
                    throw new NullReferenceException("The Field Sections Are Not Loaded Yet. The Reload Method Needs To Have Been Called And Completed To Initialise It");
                return _recordFields;
            }
        }

        protected internal override IEnumerable<ValidationRuleBase> GetValidationRules(string fieldName)
        {
            return FormService.GetValidationRules(fieldName);
        }

        public void OnNavigatedTo(INavigationProvider navigationProvider)
        {
            if (!navigationProvider.GetValue(NavigationParameters.RecordType).IsNullOrWhiteSpace())
                RecordType = navigationProvider.GetValue(NavigationParameters.RecordType);
            if (!navigationProvider.GetValue(NavigationParameters.RecordIdName).IsNullOrWhiteSpace())
                RecordIdName = navigationProvider.GetValue(NavigationParameters.RecordIdName);
            if (!navigationProvider.GetValue(NavigationParameters.RecordId).IsNullOrWhiteSpace())
                RecordId = navigationProvider.GetValue(NavigationParameters.RecordId);
        }

        public string GetValidationSummary()
        {
            var validationBuilder = new StringBuilder();
            foreach (var fieldViewModelBase in FieldViewModels)
            {
                var message = fieldViewModelBase.GetErrorsString();
                if (!message.IsNullOrWhiteSpace())
                    validationBuilder.AppendLine(string.Format("{0}: {1}", fieldViewModelBase.Label, message));
            }
            foreach (var subGrid in SubGrids)
            {
                foreach (var gridRecord in subGrid.DynamicGridViewModel.GridRecords)
                {
                    foreach (var fieldViewModelBase in gridRecord.FieldViewModels)
                    {
                        var message = fieldViewModelBase.GetErrorsString();
                        if (!message.IsNullOrWhiteSpace())
                            validationBuilder.AppendLine(string.Format("{0} - {1}: {2}", subGrid.SectionLabel, fieldViewModelBase.Label, message));
                    }
                }
            }
            return validationBuilder.ToString();
        }

        protected override void RefreshVisibilityExtention()
        {
            if (FormSectionsAsync != null)
            {
                foreach (var section in SubGrids)
                {
                    section.IsVisible = FormService.IsSectionInContext(section.ReferenceName, GetRecord());
                }
            }
        }

        public GridSectionViewModel GetSubGridViewModel(string subgridName)
        {
            if (SubGrids.Any(g => g.SectionIdentifier == subgridName))
            {
                return SubGrids.First(g => g.SectionIdentifier == subgridName);
            }
            throw new ArgumentOutOfRangeException("subgridName", "No SubGrid In Has The SectionIdentifier: " + subgridName);
        }


        internal void LoadChildForm(RecordEntryFormViewModel viewModel)
        {
            ApplicationController.DoOnMainThread(() =>
            {
                ChildForms.Add(viewModel);
                OnPropertyChanged("MainFormInContext");
            });
        }

        internal void ClearChildForm()
        {
            ApplicationController.DoOnMainThread(() =>
            {
                ChildForms.Clear();
                OnPropertyChanged("MainFormInContext");
            });
        }

        private ObservableCollection<RecordEntryFormViewModel> _childForms = new ObservableCollection<RecordEntryFormViewModel>();

        /// <summary>
        /// DONT USE CLEAR USER ClearChildForm()
        /// </summary>
        public ObservableCollection<RecordEntryFormViewModel> ChildForms
        {
            get { return _childForms; }
            set
            {
                _childForms = value;
                OnPropertyChanged("ChildForms");
                OnPropertyChanged("MainFormInContext");
            }
        }

        public bool MainFormInContext
        {
            get
            {
                return !ChildForms.Any();
            }
        }

        private readonly RecordEntryViewModelBase _parentForm;
        internal override RecordEntryViewModelBase ParentForm
        {
            get { return _parentForm; }
        }

        private readonly string _parentFormReference;
        internal override string ParentFormReference
        {
            get { return _parentFormReference; }
        }

        internal void OnSectionLoaded()
        {
            if (FormSectionsAsync.All(s => s.IsLoaded))
            {
                OnLoad();
                foreach (var section in SubGrids)
                {
                    if (section.GridRecords != null)
                        foreach (var record in section.GridRecords)
                            record.OnLoad();
                }

                SaveButtonViewModel.IsVisible = OnSave != null;
                CancelButtonViewModel.IsVisible = OnCancel != null;
                if (AllowSaveAndLoad)
                    SaveRequestButtonViewModel.IsVisible = true;
                if (AllowSaveAndLoad)
                    LoadRequestButtonViewModel.IsVisible = true;

                LoadingViewModel.IsLoading = false;
            }
        }
    }
}