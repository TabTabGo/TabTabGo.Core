using System;
using System.Collections.Generic;

namespace TabTabGo.Core.Entities
{
    public interface IEntity
    {
        string CreatedBy { get; set; }
        DateTimeOffset CreatedDate { get; set; }
        bool IsEnabled { get; set; }
        IDictionary<string, object> Properties { get; set; }
        string UpdatedBy { get; set; }
        DateTimeOffset UpdatedDate { get; set; }
    }
}