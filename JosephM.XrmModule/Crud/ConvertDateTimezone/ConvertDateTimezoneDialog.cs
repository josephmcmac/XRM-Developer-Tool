using JosephM.Application.Desktop.Module.ServiceRequest;
using JosephM.Application.ViewModel.Dialog;
using JosephM.Record.Xrm.XrmRecord;
using System;

namespace JosephM.XrmModule.Crud.ConvertDateTimezone
{
    public class ConvertDateTimezoneDialog :
        ServiceRequestDialog<ConvertDateTimezoneService, ConvertDateTimezoneRequest, ConvertDateTimezoneResponse, ConvertDateTimezoneResponseItem>
    {
        public ConvertDateTimezoneDialog(XrmRecordService recordService, IDialogController dialogController, ConvertDateTimezoneRequest request, Action onClose)
            : base(new ConvertDateTimezoneService(recordService), dialogController, recordService, request, onClose)
        {
            
        }

        public override bool DisplayResponseDuringServiceRequestExecution => true;
    }
}