using JosephM.Core.Extentions;
using JosephM.Core.Service;
using JosephM.Core.Utility;
using JosephM.Record.Extentions;
using JosephM.Record.IService;
using JosephM.Record.Xrm.XrmRecord;
using JosephM.Xrm;
using JosephM.Xrm.Schema;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;

namespace JosephM.Deployment.DataImport
{
    public class DataImportService
    {
        public DataImportService(XrmRecordService xrmRecordService)
        {
            XrmRecordService = xrmRecordService;
        }

        public XrmRecordService XrmRecordService { get; set; }

        protected XrmService XrmService
        {
            get
            {
                return XrmRecordService.XrmService;
            }
        }

        private Dictionary<string, Dictionary<string, Dictionary<string, List<Entity>>>> _cachedRecords = new Dictionary<string, Dictionary<string, Dictionary<string, List<Entity>>>>();

        public DataImportResponse DoImport(IEnumerable<Entity> entities, ServiceRequestController controller, bool maskEmails, MatchOption matchOption = MatchOption.PrimaryKeyThenName, IEnumerable<DataImportResponseItem> loadExistingErrorsIntoSummary = null, Dictionary<string, IEnumerable<string>> altMatchKeyDictionary = null, bool updateOnly = false, bool includeOwner = false, bool containsExportedConfigFields = true, int? executeMultipleSetSize = null, int? targetCacheLimit = null) 
        {
            var response = new DataImportResponse(entities, loadExistingErrorsIntoSummary);
            controller.AddObjectToUi(response);
            try
            {
                controller.LogLiteral("Preparing Import");
                var dataImportContainer = new DataImportContainer(response,
                    XrmRecordService,
                    altMatchKeyDictionary ?? new Dictionary<string, IEnumerable<string>>(),
                    entities,
                    controller,
                    includeOwner,
                    maskEmails,
                    matchOption,
                    updateOnly,
                    containsExportedConfigFields,
                    executeMultipleSetSize ?? 1,
                    targetCacheLimit ?? 1000);

                ImportEntities(dataImportContainer);

                RetryUnresolvedFields(dataImportContainer);

                ImportAssociations(dataImportContainer);
            }
            finally
            {
                controller.RemoveObjectFromUi(response);
            }
            return response;
        }

        private void ImportAssociations(DataImportContainer dataImportContainer)
        {
            var countToImport = dataImportContainer.AssociationTypesToImport.Count();
            var countImported = 0;
            foreach (var relationshipEntityName in dataImportContainer.AssociationTypesToImport)
            {
                var thisEntityName = relationshipEntityName;

                var relationship = XrmService.GetRelationshipMetadataForEntityName(thisEntityName);
                var type1 = relationship.Entity1LogicalName;
                var field1 = relationship.Entity1IntersectAttribute;
                var type2 = relationship.Entity2LogicalName;
                var field2 = relationship.Entity2IntersectAttribute;

                dataImportContainer.Controller.UpdateProgress(countImported++, countToImport, $"Associating {thisEntityName} Records");
                dataImportContainer.Controller.UpdateLevel2Progress(0, 1, "Loading");
                var thisTypeEntities = dataImportContainer.EntitiesToImport.Where(e => e.LogicalName == thisEntityName).ToList();
                var countRecordsToImport = thisTypeEntities.Count;
                var countRecordsImported = 0;
                var estimator = new TaskEstimator(countRecordsToImport);

                while (thisTypeEntities.Any())
                {
                    var thisSetOfEntities = thisTypeEntities
                        .Take(dataImportContainer.ExecuteMultipleSetSize)
                        .ToList();
                    var countThisSet = thisSetOfEntities.Count;

                    thisTypeEntities.RemoveRange(0, thisSetOfEntities.Count());

                    var copiesForAssociate = new List<Entity>();

                    foreach (var thisEntity in thisSetOfEntities)
                    {
                        try
                        {
                            //bit of hack
                            //when importing from csv just set the fields to the string name of the referenced record
                            //so either string when csv or guid when xml import/export
                            var value1 = thisEntity.GetField(relationship.Entity1IntersectAttribute);
                            var id1 = value1 is string
                                ? dataImportContainer.GetUniqueMatchingEntity(type1, XrmRecordService.GetPrimaryField(type1), (string)value1).Id
                                : thisEntity.GetGuidField(relationship.Entity1IntersectAttribute);

                            var value2 = thisEntity.GetField(relationship.Entity2IntersectAttribute);
                            var id2 = value2 is string
                                ? dataImportContainer.GetUniqueMatchingEntity(type2, XrmRecordService.GetPrimaryField(type2), (string)value2).Id
                                : thisEntity.GetGuidField(relationship.Entity2IntersectAttribute);

                            //add a where field lookup reference then look it up
                            if (dataImportContainer.IdSwitches.ContainsKey(type1) && dataImportContainer.IdSwitches[type1].ContainsKey(id1))
                                id1 = dataImportContainer.IdSwitches[type1][id1];
                            if (dataImportContainer.IdSwitches.ContainsKey(type2) && dataImportContainer.IdSwitches[type2].ContainsKey(id2))
                                id2 = dataImportContainer.IdSwitches[type2][id2];

                            var copyForAssociate = new Entity(thisEntity.LogicalName) { Id = thisEntity.Id };
                            copyForAssociate.SetField(field1, id1);
                            copyForAssociate.SetField(field2, id2);
                            copiesForAssociate.Add(copyForAssociate);
                        }
                        catch (Exception ex)
                        {
                            dataImportContainer.LogAssociationError(thisEntity, ex);
                        }
                        countRecordsImported++;
                        dataImportContainer.Controller.UpdateLevel2Progress(countRecordsImported, countRecordsToImport, estimator.GetProgressString(countRecordsImported));
                    }

                    var existingAssociationsQueries = copiesForAssociate.
                        Select(c =>
                        {
                            var q = new QueryByAttribute(relationship.IntersectEntityName);
                            q.AddAttributeValue(field1, c.GetGuidField(field1));
                            q.AddAttributeValue(field2, c.GetGuidField(field2));
                            return new RetrieveMultipleRequest()
                            {
                                Query = q
                            };
                        })
                        .ToArray();

                    var executeMultipleResponses = XrmService.ExecuteMultiple(existingAssociationsQueries);

                    var notYetAssociated = new List<Entity>();
                    var i = 0;
                    foreach (var queryResponse in executeMultipleResponses)
                    {
                        var associationEntity = copiesForAssociate[i];
                        if (queryResponse.Fault != null)
                        {
                            dataImportContainer.LogAssociationError(associationEntity, new FaultException<OrganizationServiceFault>(queryResponse.Fault, queryResponse.Fault.Message));
                        }
                        else if (!((RetrieveMultipleResponse)queryResponse.Response).EntityCollection.Entities.Any())
                        {
                            notYetAssociated.Add(associationEntity);
                        }
                        else
                        {
                            associationEntity.Id = Guid.NewGuid();
                            dataImportContainer.Response.AddSkippedNoChange(associationEntity);
                        }
                        i++;
                    }

                    var associateRequests = notYetAssociated.
                        Select(e =>
                        {
                            var isReferencing = relationship.Entity1IntersectAttribute == field1;

                            var r = new AssociateRequest
                            {
                                Relationship = new Relationship(relationship.SchemaName)
                                {
                                    PrimaryEntityRole =
                                    isReferencing ? EntityRole.Referencing : EntityRole.Referenced
                                },
                                Target = new EntityReference(type1, e.GetGuidField(field1)),
                                RelatedEntities = new EntityReferenceCollection(new[] { new EntityReference(type2, e.GetGuidField(field2)) })
                            };
                            return r;
                        })
                        .ToArray();

                    var associateMultipleResponses = XrmService.ExecuteMultiple(associateRequests);

                    i = 0;
                    foreach (var associateResponse in associateMultipleResponses)
                    {
                        var associationEntity = notYetAssociated[i];
                        if (associateResponse.Fault != null)
                        {
                            dataImportContainer.LogAssociationError(associationEntity, new FaultException<OrganizationServiceFault>(associateResponse.Fault, associateResponse.Fault.Message));
                        }
                        else
                        {
                            associationEntity.Id = Guid.NewGuid();
                            dataImportContainer.Response.AddCreated(associationEntity);
                        }
                        i++;
                    }
                }
            }
        }

        private void RetryUnresolvedFields(DataImportContainer dataImportContainer)
        {
            var countToImport = dataImportContainer.FieldsToRetry.Count;
            var countImported = 0;
            var estimator = new TaskEstimator(countToImport);

            dataImportContainer.Controller.UpdateProgress(countImported, countToImport, "Retrying Unresolved Fields");

            var types = dataImportContainer.FieldsToRetry.Keys.Select(e => e.LogicalName).Distinct().ToArray();

            foreach(var type in types)
            {
                var thisTypeForRetry = dataImportContainer.FieldsToRetry.Where(kv => kv.Key.LogicalName == type).ToList();

                while (thisTypeForRetry.Any())
                {
                    var thisSetOfEntities = thisTypeForRetry
                        .Take(dataImportContainer.ExecuteMultipleSetSize)
                        .ToList();
                    var countThisSet = thisSetOfEntities.Count;
                    thisTypeForRetry.RemoveRange(0, countThisSet);

                    var distinctFields = thisSetOfEntities.SelectMany(kv => kv.Value).Distinct().ToArray();

                    var indexToUpdateCopy = new Dictionary<Entity, Entity>();
                    foreach (var kv in thisSetOfEntities)
                    {
                        indexToUpdateCopy.Add(kv.Key, new Entity(kv.Key.LogicalName) { Id = kv.Key.Id });
                    }

                    foreach (var field in distinctFields)
                    {
                        var itemsWithThisFieldPopulated = thisSetOfEntities
                            .Where(e => dataImportContainer.FieldsToRetry[e.Key].Contains(field))
                            .Select(e => e.Key)
                            .ToList();
                        ParseLookupFields(dataImportContainer, itemsWithThisFieldPopulated, new[] { field }, isRetry: true, allowAddForRetry: false, doWhenResolved: (e, f) => indexToUpdateCopy[e].SetField(f, e.GetField(f)));
                    }

                    var itemsForUpdate = indexToUpdateCopy.Where(kv => kv.Value.GetFieldsInEntity().Any()).ToArray();

                    if (itemsForUpdate.Any())
                    {
                        var updateEntities = itemsForUpdate.Select(kv => kv.Value).ToArray();
                        var responses = XrmService.UpdateMultiple(updateEntities, null);

                        var i = 0;
                        foreach (var updateResponse in responses)
                        {
                            var updateEntity = updateEntities.ElementAt(i);
                            var originalEntity = itemsForUpdate.ElementAt(i).Key;
                            foreach (var updatedField in updateEntity.GetFieldsInEntity())
                                dataImportContainer.Response.RemoveFieldForRetry(originalEntity, updatedField);
                            if (updateResponse.Fault != null)
                            {
                                dataImportContainer.LogEntityError(originalEntity, new FaultException<OrganizationServiceFault>(updateResponse.Fault, updateResponse.Fault.Message));
                            }
                            else
                            {
                                dataImportContainer.Response.AddUpdated(originalEntity);
                            }
                            i++;
                        }
                    }
                    countImported += countThisSet;
                    dataImportContainer.Controller.UpdateProgress(countImported, countToImport, estimator.GetProgressString(countImported, taskName: $"Retrying Unresolved Fields"));
                }
            }
        }

        private void ImportEntities(DataImportContainer dataImportContainer)
        {
            var orderedTypes = GetEntityTypesOrderedForImport(dataImportContainer);

            var estimator = new TaskEstimator(1);
            var countToImport = orderedTypes.Count();
            var countImported = 0;
            foreach (var recordType in orderedTypes)
            {
                if (_cachedRecords.ContainsKey(recordType))
                    _cachedRecords.Remove(recordType);
                try
                {
                    dataImportContainer.LoadTargetsToCache(recordType);

                    var displayPrefix = $"Importing {recordType} Records ({countImported + 1}/{countToImport})";
                    dataImportContainer.Controller.UpdateProgress(countImported++, countToImport, string.Format("Importing {0} Records", recordType));
                    dataImportContainer.Controller.UpdateLevel2Progress(0, 1, "Loading");

                    var thisTypeEntities = dataImportContainer.EntitiesToImport.Where(e => e.LogicalName == recordType).ToList();
                    var importFieldsForEntity = dataImportContainer.GetFieldsToImport(thisTypeEntities, recordType).ToArray();

                    var orderedEntitiesForImport = OrderEntitiesForImport(dataImportContainer, thisTypeEntities, importFieldsForEntity);

                    var countRecordsToImport = orderedEntitiesForImport.Count;
                    var countRecordsImported = 0;
                    estimator = new TaskEstimator(countRecordsToImport);

                    var thisTypeCreatedDictionary = dataImportContainer.Response.GetImportForType(recordType).GetCreatedEntities();

                    //process create and updates for this type in sets
                    while (orderedEntitiesForImport.Any())
                    {
                        var thisSetOfEntities = LoadNextSetToProcess(dataImportContainer, orderedEntitiesForImport);

                        var countThisSet = thisSetOfEntities.Count;
                        var matchDictionary = new Dictionary<Entity, Entity>();

                        MatchEntitiesToTarget(dataImportContainer, thisSetOfEntities, matchDictionary);

                        var currentEntityFields = thisSetOfEntities
                            .SelectMany(e => e.GetFieldsInEntity())
                            .Distinct()
                            .Where(f => !f.Contains(".") && importFieldsForEntity.Contains(f))
                            .ToArray();

                        var lookupFields = currentEntityFields
                            .Where(f => XrmService.IsLookup(f, recordType))
                            .ToArray();

                        ParseLookupFields(dataImportContainer, thisSetOfEntities, lookupFields, isRetry: false, allowAddForRetry: true);

                        var activityPartyFields = currentEntityFields
                            .Where(f => XrmService.IsActivityParty(f, recordType))
                            .ToArray();

                        var dictionaryPartiesToParent = new Dictionary<Entity, Entity>();
                        foreach (var entity in thisSetOfEntities.ToArray())
                        {
                            foreach (var field in activityPartyFields)
                            {
                                var parties = entity.GetActivityParties(field);
                                foreach(var party in parties)
                                {
                                    if (!dictionaryPartiesToParent.ContainsKey(party))
                                        dictionaryPartiesToParent.Add(party, entity);
                                }
                            }
                        }

                        ParseLookupFields(dataImportContainer, dictionaryPartiesToParent.Keys.ToList(), new[] { Fields.activityparty_.partyid }, isRetry: false, allowAddForRetry: false,
                                doWhenNotResolved: (e, f) => thisSetOfEntities.Remove(dictionaryPartiesToParent[e]),
                                getPartyParent: (e) => dictionaryPartiesToParent[e]);

                        var forCreateEntitiesCopy = new Dictionary<Entity, Entity>();
                        var forUpdateEntitiesCopy = new Dictionary<Entity, Entity>();

                        foreach (var entity in thisSetOfEntities)
                        {
                            var fieldsToSet = new List<string>();
                            fieldsToSet.AddRange(entity.GetFieldsInEntity()
                                .Where(importFieldsForEntity.Contains));
                            if (dataImportContainer.FieldsToRetry.ContainsKey(entity))
                                fieldsToSet.RemoveAll(f => dataImportContainer.FieldsToRetry[entity].Contains(f));

                            if (dataImportContainer.MaskEmails)
                            {
                                var emailFields = new[] { "emailaddress1", "emailaddress2", "emailaddress3" };
                                foreach (var field in emailFields)
                                {
                                    var theEmail = entity.GetStringField(field);
                                    if (!string.IsNullOrWhiteSpace(theEmail))
                                    {
                                        entity.SetField(field, theEmail.Replace("@", "_AT_") + "_@fakemaskedemail.com");
                                    }
                                }
                            }

                            var isUpdate = matchDictionary.ContainsKey(entity);
                            if (!isUpdate)
                            {
                                PopulateRequiredCreateFields(dataImportContainer, entity, fieldsToSet);
                                try
                                {
                                    CheckThrowValidForCreate(entity, fieldsToSet);
                                }
                                catch (Exception ex)
                                {
                                    dataImportContainer.LogEntityError(entity, ex);
                                    thisSetOfEntities.Remove(entity);
                                }
                                var copyEntity = XrmEntity.ReplicateToNewEntity(entity);
                                copyEntity.Id = entity.Id;
                                copyEntity.RemoveFields(copyEntity.GetFieldsInEntity().Except(fieldsToSet));
                                forCreateEntitiesCopy.Add(copyEntity, entity);
                            }
                            else
                            {
                                var existingRecord = matchDictionary[entity];
                                var fieldsToSetWhichAreChanged = fieldsToSet.Where(f =>
                                {
                                    var oldValue = entity.GetField(f);
                                    var newValue = existingRecord.GetField(f);
                                    if (oldValue is EntityReference er
                                        && newValue is EntityReference erNew
                                        && er.Id == Guid.Empty && erNew.Id != Guid.Empty
                                        && er.Name == erNew.Name)
                                        return false;
                                    else
                                        return !XrmEntity.FieldsEqual(existingRecord.GetField(f), entity.GetField(f));
                                }).ToArray();
                                if (fieldsToSetWhichAreChanged.Any())
                                {
                                    var copyEntity = XrmEntity.ReplicateToNewEntity(entity);
                                    copyEntity.Id = entity.Id;
                                    copyEntity.RemoveFields(copyEntity.GetFieldsInEntity().Except(fieldsToSet));
                                    forUpdateEntitiesCopy.Add(copyEntity, entity);
                                }
                                else
                                {
                                    dataImportContainer.Response.AddSkippedNoChange(entity);
                                }
                            }
                        }

                        if (forCreateEntitiesCopy.Any())
                        {
                            //remove status on create if product or not inactive state set
                            foreach (var forCreate in forCreateEntitiesCopy)
                            {
                                if (forCreate.Key.Contains("statuscode"))
                                {
                                    if (forCreate.Key.LogicalName == Entities.product || forCreate.Key.GetOptionSetValue("statecode") > 0)
                                    {
                                        forCreate.Key.RemoveFields(new[] { "statuscode" });
                                    }
                                }
                            }
                            var responses = XrmService.CreateMultiple(forCreateEntitiesCopy.Keys);
                            var i = 0;
                            foreach (var createResponse in responses)
                            {
                                var originalEntity = forCreateEntitiesCopy.ElementAt(i).Value;
                                if (createResponse.Fault != null)
                                {
                                    dataImportContainer.LogEntityError(originalEntity, new FaultException<OrganizationServiceFault>(createResponse.Fault, createResponse.Fault.Message));
                                }
                                else
                                {
                                    originalEntity.Id = ((CreateResponse)createResponse.Response).id;
                                    dataImportContainer.AddCreated(originalEntity);
                                }
                                i++;
                            }
                        }
                        if (forUpdateEntitiesCopy.Any())
                        {
                            //if a custom set state message dont include state and status code in updates
                            foreach (var forUpdate in forUpdateEntitiesCopy)
                            {
                                if (forUpdate.Key.Contains("statecode")
                                    && _customSetStateConfigurations.ContainsKey(forUpdate.Key.LogicalName))
                                {
                                    forUpdate.Key.RemoveFields(new[] { "statuscode", "statecode" });
                                }
                            }
                            var responses = XrmService.UpdateMultiple(forUpdateEntitiesCopy.Keys, null);
                            var i = 0;
                            foreach (var updateResponse in responses)
                            {
                                var originalEntity = forUpdateEntitiesCopy.ElementAt(i).Value;
                                if (updateResponse.Fault != null)
                                {
                                    dataImportContainer.LogEntityError(originalEntity, new FaultException<OrganizationServiceFault>(updateResponse.Fault, updateResponse.Fault.Message));
                                }
                                else
                                {
                                    dataImportContainer.Response.AddUpdated(originalEntity);
                                }
                                i++;
                            }
                        }

                        var checkStateForEntities = new List<Entity>();
                        foreach (var entity in forCreateEntitiesCopy.Values.Union(forUpdateEntitiesCopy.Values).ToArray())
                        {
                            var isUpdate = matchDictionary.ContainsKey(entity);
                            if (!isUpdate)
                            {
                                if (entity.LogicalName == Entities.product || entity.GetOptionSetValue("statecode") > 0)
                                {
                                    checkStateForEntities.Add(entity);
                                }
                            }
                            else
                            {
                                var originalEntity = matchDictionary[entity];
                                if (entity.Contains("statecode") &&
                                    (entity.GetOptionSetValue("statecode") != originalEntity.GetOptionSetValue("statecode")
                                        || (entity.Contains("statuscode") && entity.GetOptionSetValue("statuscode") != originalEntity.GetOptionSetValue("statuscode"))))
                                {
                                    checkStateForEntities.Add(entity);
                                }
                            }
                        }
                        var setStateMessages = checkStateForEntities
                            .Select(GetSetStateRequest)
                            .ToArray();
                        if (setStateMessages.Any())
                        {
                            var responses = XrmService.ExecuteMultiple(setStateMessages);
                            var i = 0;
                            foreach (var updateResponse in responses)
                            {
                                var originalEntity = checkStateForEntities.ElementAt(i);
                                if (updateResponse.Fault != null)
                                {
                                    dataImportContainer.LogEntityError(originalEntity, new FaultException<OrganizationServiceFault>(updateResponse.Fault, updateResponse.Fault.Message));
                                }
                                else
                                {
                                    dataImportContainer.Response.AddUpdated(originalEntity);
                                }
                                i++;
                            }
                        }

                        countRecordsImported += countThisSet;
                        dataImportContainer.Controller.UpdateLevel2Progress(countRecordsImported, countRecordsToImport, estimator.GetProgressString(countRecordsImported));
                    }
                }
                catch (Exception ex)
                {
                    dataImportContainer.Response.AddImportError(
                        new DataImportResponseItem(recordType, null, null, null, string.Format("Error Importing Type {0}", recordType), ex));
                }
                if (_cachedRecords.ContainsKey(recordType))
                    _cachedRecords.Remove(recordType);
            }
            dataImportContainer.Controller.TurnOffLevel2();
        }

        private void ParseLookupFields(DataImportContainer dataImportContainer, IEnumerable<Entity> thisSetOfEntities, IEnumerable<string> lookupFields, bool isRetry, bool allowAddForRetry, Action<Entity, string> doWhenResolved = null,
            Action<Entity, string> doWhenNotResolved = null,
            Func<Entity, Entity> getPartyParent = null)
        {
            if (thisSetOfEntities.Any())
            {
                var recordType = thisSetOfEntities.First().LogicalName;
                var thisTypePrimaryField = XrmService.GetPrimaryNameField(recordType);
                foreach (var lookupField in lookupFields)
                {
                    var recordsNotYetResolved = thisSetOfEntities
                        .Where(e => e.GetField(lookupField) != null)
                        .ToList();

                    var targetTypes = XrmService.GetLookupTargetEntity(lookupField, recordType);
                    if (targetTypes != null)
                    {
                        var targetTypeSplit = targetTypes.Split(',');
                        foreach (var targetType in targetTypeSplit)
                        {
                            if (!recordsNotYetResolved.Any())
                                break;

                            var thisTargetPrimaryField = XrmService.GetPrimaryNameField(targetType);
                            var thisTargetPrimarykey = XrmService.GetPrimaryKeyField(targetType);

                            var recordsToTry = recordsNotYetResolved
                                .Where(e =>
                                {
                                    var referenceType = e.GetLookupType(lookupField);
                                    return referenceType == null
                                        || referenceType.Contains(",")
                                        || referenceType == targetType;
                                })
                                .ToArray();

                            var targetTypesConfig = XrmRecordService.GetTypeConfigs().GetFor(targetType);
                            var isCached = dataImportContainer.IsValidForCache(targetType);

                            //if has type config of not cached
                            //we will query the matches
                            var querySetResponses = targetTypesConfig != null || !isCached
                                ? XrmService.ExecuteMultiple(recordsToTry
                                    .Select(e => dataImportContainer.GetParseLookupQuery(e, lookupField, targetType))
                                    .Select(q => new RetrieveMultipleRequest() { Query = q })
                                    .ToArray())
                                : new ExecuteMultipleResponseItem[0];

                            var i = 0;
                            foreach (var entity in recordsToTry)
                            {
                                var thisEntity = entity;
                                var referencedName = thisEntity.GetLookupName(lookupField);
                                var referencedId = thisEntity.GetLookupGuid(lookupField) ?? Guid.Empty;
                                try
                                {
                                    IEnumerable<Entity> matchRecords = new Entity[0];
                                    if (querySetResponses.Any())
                                    {
                                        var thisOnesExecuteMultipleResponse = querySetResponses.ElementAt(i);
                                        if (thisOnesExecuteMultipleResponse.Fault != null)
                                            throw new Exception("Error Querying For Match - " + thisOnesExecuteMultipleResponse.Fault.Message);
                                        else
                                        {
                                            matchRecords = ((RetrieveMultipleResponse)thisOnesExecuteMultipleResponse.Response).EntityCollection.Entities;
                                            if (matchRecords.Any(e => e.Id == referencedId))
                                                matchRecords = matchRecords.Where(e => e.Id == referencedId).ToArray();
                                            else
                                                dataImportContainer.FilterForNameMatch(matchRecords).ToArray();
                                        }
                                    }
                                    //else the cache will be used
                                    else
                                    {
                                        matchRecords = dataImportContainer.GetMatchingEntities(targetType, new Dictionary<string, object>
                                                    {
                                                        {  thisTargetPrimarykey, referencedId }
                                                    });
                                        if (!matchRecords.Any())
                                        {
                                            matchRecords = dataImportContainer.GetMatchingEntities(targetType, new Dictionary<string, object>
                                                    {
                                                        { thisTargetPrimaryField, referencedName }
                                                    });
                                            matchRecords = dataImportContainer.FilterForNameMatch(matchRecords);
                                        }
                                    }

                                    if (matchRecords.Count() > 1)
                                    {
                                        throw new Exception($"Could Not Find Matching Target Record For The Field {lookupField} Named '{referencedName}'. This Field Is Configured As Required To Match In The Target Instance When Populated");
                                    }
                                    if (matchRecords.Count() == 1)
                                    {
                                        thisEntity.SetLookupField(lookupField, matchRecords.First());
                                        ((EntityReference)(thisEntity.GetField(lookupField))).Name = matchRecords.First().GetStringField(thisTargetPrimaryField);
                                        recordsNotYetResolved.Remove(thisEntity);
                                        doWhenResolved?.Invoke(thisEntity, lookupField);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    dataImportContainer.LogEntityError(thisEntity, ex);
                                    recordsNotYetResolved.Remove(thisEntity);
                                }
                                i++;
                            }
                        }
                    }
                    if (recordsNotYetResolved.Any())
                    {
                        foreach (var notResolved in recordsNotYetResolved)
                        {
                            doWhenNotResolved?.Invoke(notResolved, lookupField);
                            if (isRetry || !allowAddForRetry)
                            {
                                var rowNumber = notResolved.Contains("Sheet.RowNumber")
                                    ? notResolved.GetInt("Sheet.RowNumber")
                                    : (int?)null;
                                var notResolvedLogEntity = getPartyParent != null
                                    ? getPartyParent(notResolved)
                                    : notResolved;
                                var notResolvedLogEntityPrimaryField = XrmService.GetPrimaryNameField(notResolvedLogEntity.LogicalName);
                                dataImportContainer.Response.AddImportError(notResolvedLogEntity,
                                     new DataImportResponseItem(notResolvedLogEntity.LogicalName,
                                     lookupField,
                                     notResolvedLogEntity.GetStringField(notResolvedLogEntityPrimaryField) ?? notResolvedLogEntity.Id.ToString(), notResolved.GetLookupName(lookupField),
                                        "No Match Found For Lookup Field", null, rowNumber: rowNumber));
                            }
                            else
                            {
                                if (!dataImportContainer.FieldsToRetry.ContainsKey(notResolved))
                                    dataImportContainer.FieldsToRetry.Add(notResolved, new List<string>());
                                dataImportContainer.FieldsToRetry[notResolved].Add(lookupField);
                                dataImportContainer.Response.AddFieldForRetry(notResolved, lookupField);
                            }
                        }
                    }
                }
            }
        }

        private List<Entity> LoadNextSetToProcess(DataImportContainer dataImportContainer, List<Entity> orderedEntitiesForImport)
        {
            var thisSetOfEntities = new List<Entity>();
            if (orderedEntitiesForImport.Any())
            {
                var recordType = orderedEntitiesForImport.First().LogicalName;
                var primaryField = XrmService.GetPrimaryNameField(recordType);
                var takeSomeCountDown = dataImportContainer.ExecuteMultipleSetSize;
                while (takeSomeCountDown > 0 && orderedEntitiesForImport.Any())
                {
                    bool dontGetMoreThisSet = false;

                    var addToSet = orderedEntitiesForImport[0];

                    var referenceFields = addToSet
                        .Attributes
                        .Where(kv => kv.Value is EntityReference)
                        .Select(kv => kv.Value as EntityReference)
                        .ToArray();
                    foreach (var referenceField in referenceFields)
                    {
                        var logicalName = referenceField.LogicalName;
                        if (logicalName != null)
                        {
                            var targets = logicalName.Split(',');
                            foreach (var target in targets)
                            {
                                if (target == recordType)
                                {
                                    var id = referenceField.Id;
                                    var name = referenceField.Name;
                                    if (thisSetOfEntities.Any(e =>
                                        (id != Guid.Empty && e.Id == id)
                                        || (primaryField != null && e.GetStringField(primaryField) == name)))
                                    {
                                        dontGetMoreThisSet = true;
                                    }
                                }
                            }
                        }
                    }
                    if (dontGetMoreThisSet)
                        break;
                    else
                    {
                        thisSetOfEntities.Add(addToSet);
                        orderedEntitiesForImport.RemoveAt(0);
                        takeSomeCountDown--;
                    }
                }
            }
            return thisSetOfEntities;
        }

        private void MatchEntitiesToTarget(DataImportContainer dataImportContainer, List<Entity> thisSetOfEntities, Dictionary<Entity, Entity> matchDictionary)
        {
            if (!thisSetOfEntities.Any())
                return;
            var recordType = thisSetOfEntities.First().LogicalName;
            var primaryField = XrmService.GetPrimaryNameField(recordType);

            var thisTypesConfig = XrmRecordService.GetTypeConfigs().GetFor(recordType);
            var isCached = dataImportContainer.IsValidForCache(recordType);

            //if has type config of not cached
            //we will query the matches
            var querySetResponses = thisTypesConfig != null || !isCached
                ? XrmService.ExecuteMultiple(thisSetOfEntities
                    .Select(e => dataImportContainer.GetMatchQueryExpression(e))
                    .Select(q => new RetrieveMultipleRequest() { Query = q })
                    .ToArray())
                : new ExecuteMultipleResponseItem[0];

            var i = 0;
            foreach (var entity in thisSetOfEntities.ToArray())
            {
                var thisEntity = entity;
                try
                {
                    IEnumerable<Entity> matchRecords = new Entity[0];
                    if (querySetResponses.Any())
                    {
                        var thisOnesExecuteMultipleResponse = querySetResponses.ElementAt(i);
                        if (thisOnesExecuteMultipleResponse.Fault != null)
                            throw new Exception("Error Querying For Match - " + thisOnesExecuteMultipleResponse.Fault.Message);
                        else
                        {
                            matchRecords = ((RetrieveMultipleResponse)thisOnesExecuteMultipleResponse.Response).EntityCollection.Entities;
                            if (matchRecords.Any(e => e.Id == entity.Id))
                                matchRecords = matchRecords.Where(e => e.Id == entity.Id).ToArray();
                        }
                    }
                    //else the cache will be used
                    else if (dataImportContainer.AltMatchKeyDictionary.ContainsKey(thisEntity.LogicalName))
                    {
                        var matchKeyFieldDictionary = dataImportContainer.AltMatchKeyDictionary[thisEntity.LogicalName]
                            .Distinct().ToDictionary(f => f, f => thisEntity.GetField(f));
                        if (matchKeyFieldDictionary.Any(kv => XrmEntity.FieldsEqual(null, kv.Value)))
                        {
                            throw new Exception("Match Key Field Is Empty");
                        }
                        matchRecords = dataImportContainer.GetMatchingEntities(thisEntity.LogicalName, matchKeyFieldDictionary);
                    }
                    else if (dataImportContainer.MatchOption == MatchOption.PrimaryKeyThenName || thisTypesConfig != null)
                    {
                        matchRecords = dataImportContainer.GetMatchingEntities(thisEntity.LogicalName, new Dictionary<string, object>
                            {
                                {  XrmService.GetPrimaryKeyField(thisEntity.LogicalName), thisEntity.Id }
                            });
                        if (!matchRecords.Any())
                        {
                            matchRecords = dataImportContainer.GetMatchingEntities(thisEntity.LogicalName, new Dictionary<string, object>
                                        {
                                            { primaryField, thisEntity.GetStringField(primaryField) }
                                        });
                            matchRecords = dataImportContainer.FilterForNameMatch(matchRecords);
                        }
                    }
                    else if (dataImportContainer.MatchOption == MatchOption.PrimaryKeyOnly && thisEntity.Id != Guid.Empty)
                    {
                        matchRecords = dataImportContainer.GetMatchingEntities(thisEntity.LogicalName, new Dictionary<string, object>
                                    {
                                        {  XrmService.GetPrimaryKeyField(thisEntity.LogicalName), thisEntity.Id }
                                    });
                    }

                    //special case for business unit
                    if (!matchRecords.Any() && thisEntity.LogicalName == Entities.businessunit && thisEntity.GetField(Fields.businessunit_.parentbusinessunitid) == null)
                    {
                        matchRecords = new[] { dataImportContainer.GetRootBusinessUnit() };
                    }

                    //verify and process match results
                    if (!matchRecords.Any() && dataImportContainer.UpdateOnly)
                    {
                        throw new Exception("Updates Only And No Matching Record Found");
                    }
                    if (matchRecords.Count() > 1)
                    {
                        throw new Exception("Multiple Matches Were Found In The Target");
                    }
                    if (matchRecords.Any())
                    {
                        var matchRecord = matchRecords.First();
                        if (thisEntity.Id != Guid.Empty)
                            dataImportContainer.IdSwitches[recordType].Add(thisEntity.Id, matchRecord.Id);
                        thisEntity.Id = matchRecord.Id;
                        thisEntity.SetField(XrmService.GetPrimaryKeyField(thisEntity.LogicalName), thisEntity.Id);
                        if (thisTypesConfig != null)
                        {
                            if (thisTypesConfig.ParentLookupField != null)
                                thisEntity.SetField(thisTypesConfig.ParentLookupField, matchRecord.GetField(thisTypesConfig.ParentLookupField));
                            if (thisTypesConfig.UniqueChildFields != null)
                            {
                                foreach (var childField in thisTypesConfig.UniqueChildFields)
                                {
                                    var oldValue = thisEntity.GetField(childField);
                                    var newValue = matchRecord.GetField(childField);
                                    if (oldValue is EntityReference oldEr
                                        && newValue is EntityReference newEr
                                        && newEr.Name == null)
                                    {
                                        //this just fixing case on notes where the new query didnt populate trhe reference name
                                        newEr.Name = oldEr.Name;
                                    }
                                    thisEntity.SetField(childField, matchRecord.GetField(childField));
                                }
                            }
                        }
                        matchDictionary.Add(thisEntity, matchRecord);
                    }
                }
                catch (Exception ex)
                {
                    dataImportContainer.LogEntityError(thisEntity, ex);
                    thisSetOfEntities.Remove(thisEntity);
                }
                i++;
            }
        }

        private List<Entity> OrderEntitiesForImport(DataImportContainer dataImportContainer, List<Entity> thisTypeEntities, IEnumerable<string> importFieldsForEntity)
        {
            var orderedEntities = new List<Entity>();
            if (thisTypeEntities.Any())
            {
                var recordType = thisTypeEntities.First().LogicalName;
                var primaryField = XrmService.GetPrimaryNameField(recordType);
                var ignoreFields = dataImportContainer.GetIgnoreFields();
                var fieldsDontExist = dataImportContainer.GetFieldsInEntities(thisTypeEntities)
                    .Where(f => !f.Contains("."))
                    .Where(f => !XrmService.FieldExists(f, recordType))
                    .Where(f => !ignoreFields.Contains(f))
                    .Distinct()
                    .ToArray();
                foreach (var field in fieldsDontExist)
                {
                    dataImportContainer.Response.AddImportError(
                            new DataImportResponseItem(recordType, field, null, null,
                            string.Format("Field {0} On Entity {1} Doesn't Exist In Target Instance And Will Be Ignored", field, recordType),
                            new NullReferenceException(string.Format("Field {0} On Entity {1} Doesn't Exist In Target Instance And Will Be Ignored", field, recordType))));
                }

                var selfReferenceFields = importFieldsForEntity.Where(
                            f =>
                                XrmService.IsLookup(f, recordType) &&
                                XrmService.GetLookupTargetEntity(f, recordType) == recordType).ToArray();

                foreach (var entity in thisTypeEntities)
                {
                    foreach (var entity2 in orderedEntities)
                    {
                        if (selfReferenceFields.Any(f => entity2.GetLookupGuid(f) == entity.Id || (entity2.GetLookupGuid(f) == Guid.Empty && entity2.GetLookupName(f) == entity.GetStringField(primaryField))))
                        {
                            orderedEntities.Insert(orderedEntities.IndexOf(entity2), entity);
                            break;
                        }
                    }
                    if (!orderedEntities.Contains(entity))
                        orderedEntities.Add(entity);
                }
            }
            return orderedEntities;
        }

        private IEnumerable<string> GetEntityTypesOrderedForImport(DataImportContainer dataImportContainer)
        {
            var orderedTypes = new List<string>();

            var dependencyDictionary = dataImportContainer.EntityTypesToImport
                .ToDictionary(s => s, s => new List<string>());
            var dependentTo = dataImportContainer.EntityTypesToImport
                .ToDictionary(s => s, s => new List<string>());

            var toDo = dataImportContainer.EntityTypesToImport.Count();
            var done = 0;
            var fieldsToImport = new Dictionary<string, IEnumerable<string>>();
            foreach (var type in dataImportContainer.EntityTypesToImport)
            {
                dataImportContainer.Controller.LogLiteral($"Loading Fields For Import {done++}/{toDo}");
                var thatTypeEntities = dataImportContainer.EntitiesToImport.Where(e => e.LogicalName == type).ToList();
                var fields = dataImportContainer.GetFieldsToImport(thatTypeEntities, type)
                    .Where(f => XrmService.FieldExists(f, type) &&
                        (XrmService.IsLookup(f, type) || XrmService.IsActivityParty(f, type)));
                fieldsToImport.Add(type, fields.ToArray());
            }

            toDo = dataImportContainer.EntityTypesToImport.Count();
            done = 0;
            foreach (var type in dataImportContainer.EntityTypesToImport)
            {
                dataImportContainer.Controller.LogLiteral($"Ordering Types For Import {done++}/{toDo}");
                //iterate through the types and if any of them have a lookup which references this type
                //then insert this one before it for import first
                //otherwise just append to the end
                foreach (var otherType in dataImportContainer.EntityTypesToImport.Where(s => s != type))
                {
                    var fields = fieldsToImport[otherType];
                    var thatTypeEntities = dataImportContainer.EntitiesToImport.Where(e => e.LogicalName == otherType).ToList();
                    foreach (var field in fields)
                    {
                        if (thatTypeEntities.Any(e =>
                            (XrmService.IsLookup(field, otherType) && e.GetLookupType(field).Split(',').Contains(type))
                            || (XrmService.IsActivityParty(field, otherType) && e.GetActivityParties(field).Any(p => p.GetLookupType(Fields.activityparty_.partyid) == type))))
                        {
                            dependencyDictionary[type].Add(otherType);
                            dependentTo[otherType].Add(type);
                            break;
                        }
                    }
                }
            }
            foreach (var dependency in dependencyDictionary)
            {
                if (!dependentTo[dependency.Key].Any())
                    orderedTypes.Insert(0, dependency.Key);
                if (orderedTypes.Contains(dependency.Key))
                    continue;
                foreach (var otherType in orderedTypes.ToArray())
                {
                    if (dependency.Value.Contains(otherType))
                    {
                        orderedTypes.Insert(orderedTypes.IndexOf(otherType), dependency.Key);
                        break;
                    }
                }
                if (!orderedTypes.Contains(dependency.Key))
                    orderedTypes.Add(dependency.Key);
            }


            //these priorities are because when the first type gets create it creates a 'child' of the second type
            //so we need to ensure the parent created first
            var prioritiseOver = new List<KeyValuePair<string, string>>();
            prioritiseOver.Add(new KeyValuePair<string, string>(Entities.team, Entities.queue));
            prioritiseOver.Add(new KeyValuePair<string, string>(Entities.uomschedule, Entities.uom));
            foreach (var item in prioritiseOver)
            {
                //if the first item is after the second item in the list
                //then remove and insert it before the second item
                if (orderedTypes.Contains(item.Key) && orderedTypes.Contains(item.Value))
                {
                    var indexOfFirst = orderedTypes.IndexOf(item.Key);
                    var indexOfSecond = orderedTypes.IndexOf(item.Value);
                    if (indexOfFirst > indexOfSecond)
                    {
                        orderedTypes.RemoveAt(indexOfFirst);
                        orderedTypes.Insert(indexOfSecond, item.Key);
                    }
                }
            }

            return orderedTypes;
        }

        private void ParseLookup(DataImportContainer dataImportContainer, Entity thisEntity, string field, bool allowAddForRetry, bool isRetry = false)
        {
            var idNullable = thisEntity.GetLookupGuid(field);
            if (idNullable.HasValue)
            {
                var fieldResolved = false;
                var targetTypesToTry = GetTargetTypesToTry(thisEntity, field);
                var name = thisEntity.GetLookupName(field);
                foreach (var lookupEntity in targetTypesToTry)
                {
                    var targetPrimaryKey = XrmRecordService.GetPrimaryKey(lookupEntity);
                    var targetPrimaryField = XrmRecordService.GetPrimaryField(lookupEntity);
                    var idMatches = dataImportContainer.GetMatchingEntities(lookupEntity,
                            new Dictionary<string, object>
                            {
                                { targetPrimaryKey, idNullable.Value }
                            });

                    if (idMatches.Any())
                    {
                        ((EntityReference)(thisEntity.GetField(field))).Name = idMatches.First().GetStringField(targetPrimaryField);
                        fieldResolved = true;
                    }
                    else
                    {
                        var typeConfigParentOrUniqueFields = new List<string>();
                        var typeConfig = XrmRecordService.GetTypeConfigs().GetFor(thisEntity.LogicalName);
                        if (typeConfig != null)
                        {
                            if (typeConfig.ParentLookupField != null)
                                typeConfigParentOrUniqueFields.Add(typeConfig.ParentLookupField);
                            if(typeConfig.UniqueChildFields != null)
                                typeConfigParentOrUniqueFields.AddRange(typeConfig.UniqueChildFields);
                        }
                        if (dataImportContainer.ContainsExportedConfigFields && typeConfigParentOrUniqueFields.Contains(field))
                        {
                            //if the field is part of type config unique fields
                            //then we need to match the target based on the type config rather than just the name
                            //additionally if a lookup field in the config doesnt resolve then we should throw an error
                            var targetType = thisEntity.GetLookupType(field);
                            var targetName = thisEntity.GetLookupName(field);
                            var targetTypeConfig = XrmRecordService.GetTypeConfigs().GetFor(targetType);
                            var primaryField = XrmService.GetPrimaryNameField(targetType);
                            var matchQuery = XrmService.BuildQuery(targetType, null, new[]
                            {
                                new ConditionExpression(primaryField, ConditionOperator.Equal, targetName)
                            }, null);
                            var targetTypeParentOrUniqueFields = new List<string>();
                            if (targetTypeConfig != null)
                            {
                                if (targetTypeConfig.ParentLookupField != null)
                                    targetTypeParentOrUniqueFields.Add(targetTypeConfig.ParentLookupField);
                                if (targetTypeConfig.UniqueChildFields != null)
                                    targetTypeParentOrUniqueFields.AddRange(targetTypeConfig.UniqueChildFields);
                            }
                            if (targetTypeParentOrUniqueFields.Any())
                            {
                                dataImportContainer.AddUniqueFieldConfigJoins(thisEntity, matchQuery, targetTypeParentOrUniqueFields, prefixFieldInEntity: field + ".");
                            }
                            var matches = XrmService.RetrieveAll(matchQuery);
                            if (matches.Count() != 1)
                            {
                                throw new Exception($"Could Not Find Matching Target Record For The Field {field} Named '{targetName}'. This Field Is Configured As Required To Match In The Target Instance When Populated");
                            }
                            thisEntity.SetLookupField(field, matches.First());
                            fieldResolved = true;
                        }
                        else
                        {
                            var matchRecords = string.IsNullOrWhiteSpace(name) ?
                                new Entity[0] :
                                dataImportContainer.GetMatchingEntities(lookupEntity,
                                targetPrimaryField,
                                name);
                            matchRecords = dataImportContainer.FilterForNameMatch(matchRecords);
                            if (matchRecords.Count() == 1)
                            {
                                thisEntity.SetLookupField(field, matchRecords.First());
                                ((EntityReference)(thisEntity.GetField(field))).Name = name;
                                fieldResolved = true;
                            }
                        }
                    }
                }
                if (!fieldResolved)
                {
                    if (isRetry || !allowAddForRetry)
                        throw new Exception($"Could Not Resolve {field} {name}");
                    if (!dataImportContainer.FieldsToRetry.ContainsKey(thisEntity))
                        dataImportContainer.FieldsToRetry.Add(thisEntity, new List<string>());
                    dataImportContainer.FieldsToRetry[thisEntity].Add(field);
                    dataImportContainer.Response.AddFieldForRetry(thisEntity, field);
                }
            }
        }

        private OrganizationRequest GetSetStateRequest(Entity thisEntity)
        {
            if(_customSetStateConfigurations.ContainsKey(thisEntity.LogicalName))
            {
                return _customSetStateConfigurations[thisEntity.LogicalName](thisEntity);
            }
            else
            {
                var theState = thisEntity.GetOptionSetValue("statecode");
                var theStatus = thisEntity.GetOptionSetValue("statuscode");
                return new SetStateRequest()
                {
                    EntityMoniker = thisEntity.ToEntityReference(),
                    State = new OptionSetValue(theState),
                    Status = new OptionSetValue(theStatus)
                };
            }
        }

        private Dictionary<string, Func<Entity, OrganizationRequest>> _customSetStateConfigurations = new Dictionary<string, Func<Entity, OrganizationRequest>>
        {
            {
                Entities.incident,
                (e) =>
                {
                    var theState = e.GetOptionSetValue("statecode");
                    var theStatus = e.GetOptionSetValue("statuscode");
                    if (theState == OptionSets.Case.Status.Resolved)
                    {
                        var closeIt = new Entity(Entities.incidentresolution);
                        closeIt.SetLookupField(Fields.incidentresolution_.incidentid, e);
                        closeIt.SetField(Fields.incidentresolution_.subject, "Close By Data Import");
                        return new CloseIncidentRequest
                        {
                            IncidentResolution = closeIt,
                            Status = new OptionSetValue(theStatus)
                        };
                    }
                    else
                    {
                        return new SetStateRequest()
                        {
                            EntityMoniker = e.ToEntityReference(),
                            State = new OptionSetValue(theState),
                            Status = new OptionSetValue(theStatus)
                        };
                    }
                }
            }
        };

        private void PopulateRequiredCreateFields(DataImportContainer dataImportContainer, Entity thisEntity, List<string> fieldsToSet)
        {
            if (thisEntity.LogicalName == Entities.team
                && !fieldsToSet.Contains(Fields.team_.businessunitid)
                && XrmService.FieldExists(Fields.team_.businessunitid, Entities.team))
            {
                thisEntity.SetLookupField(Fields.team_.businessunitid, dataImportContainer.GetRootBusinessUnit().Id, Entities.businessunit);
                fieldsToSet.Add(Fields.team_.businessunitid);
                if (dataImportContainer.FieldsToRetry.ContainsKey(thisEntity)
                    && dataImportContainer.FieldsToRetry[thisEntity].Contains(Fields.team_.businessunitid))
                    dataImportContainer.FieldsToRetry[thisEntity].Remove(Fields.team_.businessunitid);
            }
            if (thisEntity.LogicalName == Entities.subject
                    && !fieldsToSet.Contains(Fields.subject_.featuremask)
                    && XrmService.FieldExists(Fields.subject_.featuremask, Entities.subject))
            {
                thisEntity.SetField(Fields.subject_.featuremask, 1);
                fieldsToSet.Add(Fields.subject_.featuremask);
                if (dataImportContainer.FieldsToRetry.ContainsKey(thisEntity)
                    && dataImportContainer.FieldsToRetry[thisEntity].Contains(Fields.subject_.featuremask))
                    dataImportContainer.FieldsToRetry[thisEntity].Remove(Fields.subject_.featuremask);
            }
            if (thisEntity.LogicalName == Entities.uomschedule)
            {
                fieldsToSet.Add(Fields.uomschedule_.baseuomname);
            }
            if (thisEntity.LogicalName == Entities.uom)
            {
                //var uomGroupName = thisEntity.GetLookupName(Fields.uom_.uomscheduleid);
                //var uomGroup = GetUniqueMatchingEntity(Entities.uomschedule, Fields.uomschedule_.name, uomGroupName);
                //thisEntity.SetLookupField(Fields.uom_.uomscheduleid, uomGroup);
                var unitGroupName = thisEntity.GetLookupName(Fields.uom_.uomscheduleid);
                if (string.IsNullOrWhiteSpace(unitGroupName))
                    throw new NullReferenceException($"Error The {XrmService.GetFieldLabel(Fields.uom_.uomscheduleid, Entities.uom)} Name Is Not Populated");
                fieldsToSet.Add(Fields.uom_.uomscheduleid);

                var baseUnitName = thisEntity.GetLookupName(Fields.uom_.baseuom);
                var baseUnitMatchQuery = XrmService.BuildQuery(Entities.uom, null, null, null);
                if(dataImportContainer.ContainsExportedConfigFields)
                {
                    var configUniqueFields = XrmRecordService.GetTypeConfigs().GetFor(Entities.uom).UniqueChildFields;
                    dataImportContainer.AddUniqueFieldConfigJoins(thisEntity, baseUnitMatchQuery, configUniqueFields, prefixFieldInEntity: $"{Fields.uom_.baseuom}.");
                }
                else
                {
                    if (baseUnitName == null)
                        throw new NullReferenceException("{Fields.uom_.baseuom} name is required");
                    baseUnitMatchQuery.Criteria.AddCondition(new ConditionExpression(Fields.uom_.name, ConditionOperator.Equal, baseUnitName));
                    var unitGroupLink = baseUnitMatchQuery.AddLink(Entities.uomschedule, Fields.uom_.uomscheduleid, Fields.uomschedule_.uomscheduleid);
                    unitGroupLink.LinkCriteria.AddCondition(new ConditionExpression(Fields.uomschedule_.name, ConditionOperator.Equal, unitGroupName));
                }
                var baseUnitMatches = XrmService.RetrieveAll(baseUnitMatchQuery);
                if (baseUnitMatches.Count() == 0)
                    throw new Exception($"Could Not Identify The {XrmService.GetFieldLabel(Fields.uom_.baseuom, Entities.uom)} {baseUnitName}. No Match Found For The {XrmService.GetFieldLabel(Fields.uom_.uomscheduleid, Entities.uom)}");
                if (baseUnitMatches.Count() > 1)
                    throw new Exception($"Could Not Identify The {XrmService.GetFieldLabel(Fields.uom_.baseuom, Entities.uom)} {baseUnitName}. Multiple Matches Found For The {XrmService.GetFieldLabel(Fields.uom_.uomscheduleid, Entities.uom)}");
                thisEntity.SetLookupField(Fields.uom_.baseuom, baseUnitMatches.First());
                thisEntity.SetField(Fields.uom_.uomscheduleid, baseUnitMatches.First().GetField(Fields.uom_.uomscheduleid));
                fieldsToSet.Add(Fields.uom_.baseuom);
            }
            if (thisEntity.LogicalName == Entities.product)
            {
                var unitGroupId = thisEntity.GetLookupGuid(Fields.product_.defaultuomscheduleid);
                if(unitGroupId.HasValue)
                    fieldsToSet.Add(Fields.product_.defaultuomscheduleid);
                var unitId = thisEntity.GetLookupGuid(Fields.product_.defaultuomid);
                if (unitId.HasValue)
                    fieldsToSet.Add(Fields.product_.defaultuomid);
            }
        }

        private List<string> GetTargetTypesToTry(Entity thisEntity, string field)
        {
            var targetTypesToTry = new List<string>();

            if (!string.IsNullOrWhiteSpace(thisEntity.GetLookupType(field)))
            {
                targetTypesToTry.AddRange(thisEntity.GetLookupType(field).Split(','));
            }
            else
            {
                switch (XrmRecordService.GetFieldType(field, thisEntity.LogicalName))
                {
                    case Record.Metadata.RecordFieldType.Owner:
                        targetTypesToTry.Add("systemuser");
                        targetTypesToTry.Add("team");
                        break;
                    case Record.Metadata.RecordFieldType.Customer:
                        targetTypesToTry.Add("account");
                        targetTypesToTry.Add("contact");
                        break;
                    case Record.Metadata.RecordFieldType.Lookup:
                        targetTypesToTry.Add(thisEntity.GetLookupType(field));
                        break;
                    default:
                        throw new NotImplementedException(string.Format("Could not determine target type for field {0}.{1} of type {2}", thisEntity.LogicalName, field, XrmService.GetFieldType(field, thisEntity.LogicalName)));
                }
            }

            return targetTypesToTry;
        }

        private void CheckThrowValidForCreate(Entity thisEntity, List<string> fieldsToSet)
        {
            if (thisEntity != null)
            {
                switch (thisEntity.LogicalName)
                {
                    case "annotation":
                        if (!fieldsToSet.Contains("objectid"))
                            throw new NullReferenceException(string.Format("Cannot create {0} {1} as its parent {2} does not exist"
                                , XrmService.GetEntityLabel(thisEntity.LogicalName), thisEntity.GetStringField(XrmService.GetPrimaryNameField(thisEntity.LogicalName))
                                , thisEntity.GetStringField("objecttypecode") != null ? XrmService.GetEntityLabel(thisEntity.GetStringField("objecttypecode")) : "Unknown Type"));
                        break;
                    case "productpricelevel":
                        if (!fieldsToSet.Contains("pricelevelid"))
                            throw new NullReferenceException(string.Format("Cannot create {0} {1} as its parent {2} is empty"
                                , XrmService.GetEntityLabel(thisEntity.LogicalName), thisEntity.GetStringField(XrmService.GetPrimaryNameField(thisEntity.LogicalName))
                                , XrmService.GetEntityLabel("pricelevel")));
                        break;
                }
            }
            return;
        }
    }
}
