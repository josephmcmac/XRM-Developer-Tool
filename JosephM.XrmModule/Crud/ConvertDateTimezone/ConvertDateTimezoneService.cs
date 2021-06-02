using JosephM.Core.Service;
using JosephM.Core.Utility;
using JosephM.Record.Extentions;
using JosephM.Record.Metadata;
using JosephM.Record.Xrm.XrmRecord;
using JosephM.Xrm.Schema;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;

namespace JosephM.XrmModule.Crud.ConvertDateTimezone
{
    public class ConvertDateTimezoneService :
        ServiceBase<ConvertDateTimezoneRequest, ConvertDateTimezoneResponse, ConvertDateTimezoneResponseItem>
    {
        public XrmRecordService RecordService { get; set; }
        public ConvertDateTimezoneService(XrmRecordService recordService)
        {
            RecordService = recordService;
        }

        public override void ExecuteExtention(ConvertDateTimezoneRequest request, ConvertDateTimezoneResponse response,
            ServiceRequestController controller)
        {
            var countToUpdate = request.RecordCount;
            var countUpdated = 0;
            controller.UpdateProgress(0, countToUpdate, "Executing Conversions");
            var estimator = new TaskEstimator(countToUpdate);
            var recordsRemaining = request.GetRecordsToUpdate().ToList();

            var timeZone = RecordService.Get(Entities.timezonedefinition, request.TargetTimeZone.Id);
            var timezoneStandardname = timeZone.GetStringField(Fields.timezonedefinition_.standardname);
            if (string.IsNullOrWhiteSpace(timezoneStandardname))
            {
                throw new NullReferenceException($"{Fields.timezonedefinition_.standardname} was not found in the {Entities.timezonedefinition} record");
            }
            var targetTimezoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezoneStandardname);

            var fieldsForLoading = new List<string>();
            fieldsForLoading.Add(RecordService.GetPrimaryKey(request.RecordType.Key));
            fieldsForLoading.Add(RecordService.GetPrimaryField(request.RecordType.Key));
            fieldsForLoading.Add(request.FieldToConvert.Key);

            while (recordsRemaining.Any())
            {
                controller.UpdateProgress(countUpdated, countToUpdate, estimator.GetProgressString(countUpdated, taskName: "Executing Conversions"));

                var thisSetOfRecords = recordsRemaining
                    .Take(request.ExecuteMultipleSetSize ?? 50)
                    .ToList();

                recordsRemaining.RemoveRange(0, thisSetOfRecords.Count);

                var reloadThisSet = RecordService.GetMultiple(request.RecordType.Key, thisSetOfRecords.Select(r => r.Id).ToArray(), fieldsForLoading);

                var errorsThisIteration = 0;

                //old versions dont have execute multiple so if 1 then do each request
                if (reloadThisSet.Count() == 1)
                {
                    var record = (XrmRecord)reloadThisSet.First();
                    try
                    {
                        var dateValue = record.GetDateTime(request.FieldToConvert.Key);
                        if(dateValue.HasValue)
                        {
                            var updateRecord = CreateRecordForUpdate(request, targetTimezoneInfo, record, dateValue);
                            RecordService.Update(updateRecord);
                        }
                    }
                    catch (Exception ex)
                    {
                        response.AddResponseItem(new ConvertDateTimezoneResponseItem(record.Id, record.GetStringField(RecordService.GetPrimaryField(record.Type)), ex));
                        errorsThisIteration++;
                    }
                }
                else
                {
                    var requests = new List<UpdateRequest>();
                    foreach(var record in reloadThisSet)
                    {
                        var dateValue = record.GetDateTime(request.FieldToConvert.Key);
                        if (dateValue.HasValue)
                        {
                            var updateRecord = CreateRecordForUpdate(request, targetTimezoneInfo, record, dateValue);
                            requests.Add(new UpdateRequest { Target = RecordService.ToEntity(updateRecord) });
                        }
                    }
                    var multipleResponse = RecordService.XrmService.ExecuteMultiple(requests);
                    var key = 0;
                    foreach (var item in multipleResponse)
                    {
                        var originalRecord = thisSetOfRecords[key];
                        if (item.Fault != null)
                        {
                            response.AddResponseItem(new ConvertDateTimezoneResponseItem(originalRecord.Id, originalRecord.GetStringField(RecordService.GetPrimaryField(originalRecord.Type)), new FaultException<OrganizationServiceFault>(item.Fault, item.Fault.Message)));
                            errorsThisIteration++;
                        }
                        key++;
                    }
                }

                countUpdated += thisSetOfRecords.Count();
                response.NumberOfErrors += errorsThisIteration;
                response.TotalRecordsProcessed = countUpdated;

                Thread.Sleep(request.WaitPerMessage * 1000);
            }
            controller.UpdateProgress(1, 1, "All Conversions Have Completed");
            response.Message = "Conversions Completed";
        }

        private Record.IService.IRecord CreateRecordForUpdate(ConvertDateTimezoneRequest request, TimeZoneInfo targetTimezoneInfo, Record.IService.IRecord record, DateTime? dateValue)
        {
            var convertedDate = TimeZoneInfo.ConvertTimeFromUtc(dateValue.Value, targetTimezoneInfo);
            convertedDate = new DateTime(convertedDate.Year, convertedDate.Month, convertedDate.Day, convertedDate.Hour, convertedDate.Minute, convertedDate.Second, DateTimeKind.Utc);
            var updateRecord = RecordService.NewRecord(request.RecordType.Key);
            updateRecord.Id = record.Id;
            updateRecord.SetField(request.FieldToConvert.Key, convertedDate, RecordService);
            if (request.SetFieldWhenUpdated != null)
            {
                var fieldType = RecordService.GetFieldType(request.SetFieldWhenUpdated.Key, request.RecordType.Key);
                if (fieldType == RecordFieldType.Boolean)
                {
                    updateRecord.SetField(request.SetFieldWhenUpdated.Key, true, RecordService);
                }
                else if (fieldType == RecordFieldType.Date)
                {
                    updateRecord.SetField(request.SetFieldWhenUpdated.Key, DateTime.UtcNow, RecordService);
                }
                else
                {
                    throw new NotImplementedException($"Expected {nameof(ConvertDateTimezoneRequest.SetFieldWhenUpdated)} of type {RecordFieldType.Boolean} or {RecordFieldType.Date}. Actual type is {fieldType}");
                }
            }

            return updateRecord;
        }
    }
}