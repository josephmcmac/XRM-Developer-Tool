using JosephM.Application.ViewModel.Extentions;
using JosephM.Application.ViewModel.Grid;
using JosephM.Application.ViewModel.RecordEntry.Form;
using JosephM.Application.ViewModel.RecordEntry.Metadata;
using JosephM.Application.ViewModel.Shared;
using JosephM.Core.Extentions;
using JosephM.Core.Utility;
using JosephM.Record.Extentions;
using JosephM.Record.IService;
using JosephM.Record.Metadata;
using JosephM.Record.Query;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace JosephM.Application.ViewModel.RecordEntry.Field
{
    public class EnumerableFieldViewModel : FieldViewModel<IEnumerable>
    {
        public EnumerableFieldViewModel(string fieldName, string label, RecordEntryViewModelBase recordForm, string linkedRecordType)
            : base(fieldName, label, recordForm)
        {
            if (recordForm is RecordEntryFormViewModel)
            {
                RecordForm = (RecordEntryFormViewModel)recordForm;
                LinkedRecordType = linkedRecordType;

                DynamicGridViewModel = new DynamicGridViewModel(ApplicationController)
                {
                    PageSize = RecordForm.GridPageSize,
                    ViewType = ViewType.AssociatedView,
                    DeleteRow = !recordForm.IsReadOnly && FormService.AllowDelete(ReferenceName, GetRecordType()) ? RemoveRow :(Action<GridRowViewModel>)null,
                    EditRow = EditRow,
                    AddRow = !recordForm.IsReadOnly && FormService.AllowAddNew(ReferenceName, GetRecordType()) ? AddRow : (Action)null,
                    AddMultipleRow = FormService.GetBulkAddFunctionFor(ReferenceName, RecordEntryViewModel),
                    IsReadOnly = recordForm.IsReadOnly,
                    ParentForm = recordForm,
                    ReferenceName = ReferenceName,
                    RecordType = linkedRecordType,
                    RecordService = recordForm.RecordService,
                    GetGridRecords = GetGridRecords,
                    LoadRecordsAsync = true,
                    FormController = recordForm.FormController,
                    OnReloading = () =>
                    {
                        _isLoaded = false;
                    },
                    LoadedCallback = () =>
                    {
                        _isLoaded = true;
                        RecordForm.OnSectionLoaded();
                    },
                    OnlyValidate = recordForm.OnlyValidate,
                    MaxHeight = 600,
                    LoadDialog = (d) => { RecordEntryViewModel.LoadChildForm(d); }
                };
                DynamicGridViewModel.AddMultipleRow = FormService.GetBulkAddFunctionFor(ReferenceName, RecordEntryViewModel);
            }
            else
            {
                var bulkAddFunction = FormService.GetBulkAddFunctionFor(ReferenceName, RecordEntryViewModel);
                if (bulkAddFunction != null)
                    BulkAddButton = new XrmButtonViewModel("BULKADD", "BULK ADD", bulkAddFunction, ApplicationController);
            }
        }

        public XrmButtonViewModel BulkAddButton { get; set; }

        private bool _isLoaded;
        public override bool IsLoaded { get { return _isLoaded; } }

        private string LinkedRecordLookup {  get { return FieldName; } }
        private string LinkedRecordType { get; set; }

        private RecordEntryFormViewModel RecordForm { get; set; }

        public void AddRow()
        {
            LoadingViewModel.IsLoading = true;
            try
            {
                var viewModel = FormService.GetLoadRowViewModel(SectionIdentifier, RecordForm, (record) =>
                    DoOnMainThread(() =>
                    {
                        InsertRecord(record, 0);
                        RecordForm.ClearChildForm();
                    }), () => RecordForm.ClearChildForm());
                if (viewModel == null)
                {
                    InsertRecord(GetRecordService().NewRecord(RecordType), 0);
                }
                else
                {
                    RecordForm.LoadChildForm(viewModel);
                }
            }
            catch (Exception ex)
            {
                ApplicationController.UserMessage(string.Format("Error Adding Row: {0}", ex.DisplayString()));
            }
            finally
            {
                LoadingViewModel.IsLoading = false;
            }
        }

        private void RemoveRow(GridRowViewModel row)
        {
            DynamicGridViewModel.GridRecords.Remove(row);
        }

        private void EditRow(GridRowViewModel row)
        {
            DoOnAsynchThread(() =>
            {
                LoadingViewModel.IsLoading = true;
                LoadingViewModel.LoadingMessage = "Please Wait While Loading";
                try
                {

                    var viewModel = GetEditRowViewModel(row);
                    if (viewModel == null)
                    {
                        throw new NotImplementedException("No Form For Type");
                    }
                    else
                    {
                        viewModel.IsReadOnly = IsReadOnly;
                        RecordForm.LoadChildForm(viewModel);
                    }
                }
                catch (Exception ex)
                {
                    ApplicationController.UserMessage(string.Format("Error Adding Row: {0}", ex.DisplayString()));
                }
                finally
                {
                    LoadingViewModel.IsLoading = false;
                }
            });
        }

        public RecordEntryFormViewModel GetEditRowViewModel(GridRowViewModel row)
        {
            var viewModel = FormService.GetEditRowViewModel(SectionIdentifier, RecordForm, (record) =>
            {
                RecordForm.ClearChildForm();
                var index = DynamicGridViewModel.GridRecords.IndexOf(row);
                DoOnMainThread(() =>
                {
                    DynamicGridViewModel.GridRecords.Remove(row);
                    InsertRecord(record, index == -1 ? 0 : index);
                });
            }, () => RecordForm.ClearChildForm(), row);
            return viewModel;
        }

        public IEnumerable<GridFieldMetadata> GridFieldMetadata
        {
            get; set;
        }

        public GetGridRecordsResponse GetGridRecords(bool ignorePages)
        {
            if (DynamicGridViewModel.HasPaging && !ignorePages)
            {
                var conditions = new[]
                {new Condition(LinkedRecordLookup, ConditionType.Equal, RecordForm.RecordId)};
                return DynamicGridViewModel.GetGridRecordPage(conditions, null);
            }
            else
                return new GetGridRecordsResponse(GetRecordService().GetLinkedRecords(LinkedRecordType, RecordForm.RecordType,
                    LinkedRecordLookup, RecordForm.RecordId));
        }

        public void InsertRecord(IRecord record, int index)
        {
            DoOnAsynchThread(() =>
            {
                RecordForm.LoadingViewModel.IsLoading = true;
                try
                {
                    //this part may take a while to load/create (e.g. get record types for record type field)
                    //so create on asynch thread while loading so doesn't freeze ui
                    //then add to observable collection on main thread
                    var rowItem = new GridRowViewModel(record, DynamicGridViewModel);
                    DoOnMainThread(() =>
                    {
                        try
                        {

                            DynamicGridViewModel.GridRecords.Insert(index, rowItem);
                            rowItem.OnLoad();
                            rowItem.RunOnChanges();
                        }
                        finally
                        {
                            RecordForm.LoadingViewModel.IsLoading = false;
                        }
                    });
                }
                catch (Exception)
                {
                    RecordForm.LoadingViewModel.IsLoading = false;
                    throw;
                }
            });
        }

        public string RecordType
        {
            get { return LinkedRecordType; }
        }

        public DynamicGridViewModel DynamicGridViewModel { get; set; }

        public override bool Validate()
        {
            ErrorMessage = null;
            var isValid = true;
            if (IsVisible)
            {
                if (DynamicGridViewModel != null)
                {
                    foreach (var gridRowViewModel in DynamicGridViewModel.GridRecords)
                    {
                        if (!gridRowViewModel.Validate())
                            isValid = false;
                    }
                }
                else
                {
                    isValid = base.Validate();
                }
                var onlyValidate = GetRecordForm().OnlyValidate;
                if (onlyValidate == null ||
                    (RecordType != null && onlyValidate.ContainsKey(RecordType) && onlyValidate[RecordType].Contains(FieldName)))
                {
                    var thisValidators = FormService.GetSectionValidationRules(SectionIdentifier);

                    foreach (var validator in thisValidators)
                    {
                        var response = validator.Validate(this);
                        if (!response.IsValid)
                        {
                            ErrorMessage = response.ErrorContent != null ? response.ErrorContent.ToString() : "No Error Content";
                            //need to somehow get the error message into the grid
                            isValid = false;
                        }
                    }
                }
            }

            return isValid;
        }

        private bool _hasError;
        public bool HasError
        {
            get { return _hasError; }
            set
            {
                _hasError = value;
                OnPropertyChanged("HasError");
            }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get { return _errorMessage; }
            set
            {
                _errorMessage = value;
                HasError = !_errorMessage.IsNullOrWhiteSpace();
                OnPropertyChanged("ErrorMessage");
            }
        }

        public string SectionIdentifier
        {
            get { return ReferenceName; }
        }

        public ObservableCollection<GridRowViewModel> GridRecords
        {
            get { return DynamicGridViewModel.GridRecords; }
        }

        public void ClearRows()
        {
            ApplicationController.DoOnMainThread(() => DynamicGridViewModel.GridRecords.Clear());
        }

        public void DownloadCsv()
        {
            try
            {
                var fileName = ApplicationController.GetSaveFileName(RecordType, ".csv");
                if (!fileName.IsNullOrWhiteSpace())
                {
                    RecordForm.LoadingViewModel.IsLoading = true;
                    DoOnAsynchThread(() =>
                    {
                        try
                        {
                            Thread.Sleep(5000);
                            var records = GetGridRecords(true);
                            CsvUtility.CreateCsv(Path.GetDirectoryName(fileName), Path.GetFileName(fileName),
                                records.Records
                                , GetRecordService().GetFields(RecordType),
                                (f) => GetRecordService().GetFieldLabel(f, RecordType),
                                (r, f) => GetRecordService().GetFieldAsDisplayString((IRecord)r, f));
                            ApplicationController.StartProcess(fileName);
                        }
                        catch (Exception ex)
                        {
                            ApplicationController.ThrowException(ex);
                        }
                        finally
                        {
                            RecordForm.LoadingViewModel.IsLoading = false;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                ApplicationController.ThrowException(ex);
            }
        }

        public string StringDisplay
        {
            get
            {
                var list = new List<string>();
                if (Enumerable != null)
                {
                    foreach (var item in Enumerable)
                        list.Add(item == null ? "" : item.ToString());
                }
                return string.Join(",", list.ToArray());
            }
        }

        public IEnumerable Enumerable
        {
            get { return ValueObject == null ? null : (IEnumerable) ValueObject; }
        }

        public override IEnumerable Value
        {
            get
            {
                return base.Value;
            }
            set
            {
                base.Value = value;
                OnPropertyChanged(nameof(StringDisplay));
            }
        }
    }
}
