using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using TabTabGo.Core.Extensions;

namespace TabTabGo.Core.Entities
{
    public class Entity : IEntity
    {
        /// <summary>
        /// Date and time when entity created
        /// </summary>
        [IgnoreCopy]
        public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>
        /// Date and time when entity updated
        /// </summary>
        [IgnoreCopy]
        public DateTimeOffset UpdatedDate { get; set; }
        /// <summary>
        /// User who created the entity 
        /// </summary>
        [IgnoreCopy]
        public string CreatedBy { get; set; }
        /// <summary>
        /// Last user who updated entity
        /// </summary>
        [IgnoreCopy]
        public string UpdatedBy { get; set; }
        /// <summary>
        /// Uniqe id for object
        /// </summary>
       // public Guid? ObjectGUID { get; set; }

        public bool IsEnabled { get; set; } = true;

        [JsonExtensionData]
        public IDictionary<string, object> ExtraProperties { get; set; } = new Dictionary<string, object>();

    }
}
