using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace TabTabGo.Core
{
    public class Instance
    {
        public string Name { get; set; }
        public int Id { get; set; }

        public dynamic Settings { get; set; }
        [JsonExtensionData]
        public IDictionary<string, object> ExtraProperties { get; set; } = new Dictionary<string, object>();
    }
}
