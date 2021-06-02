using JosephM.Core.Service;
using System;

namespace JosephM.XrmModule.Crud.ConvertDateTimezone
{
    public class ConvertDateTimezoneResponseItem : ServiceResponseItem
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public ConvertDateTimezoneResponseItem(string id, string name, Exception ex)
        {
            Id = id;
            Name = name;
            Exception = ex;
        }
    }
}