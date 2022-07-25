using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TabTabGo.Core.WebApi.Middlewares.Logging.NLog
{
    /// <summary>
    /// https://github.com/NLog/NLog.Extensions.Logging/wiki/NLog-properties-with-Microsoft-Extension-Logging
    /// mapps keyvalue pair args passed to ILogger.Log<TState>() to NLog's LogEventInfo.Properties collection. which can be used with ${event-properties} nlog's layoutrendrer
    /// </summary>
    class TStatePropertiesMapperEventInfo : IEnumerable<KeyValuePair<string, object>>
    {
        List<KeyValuePair<string, object>> _properties = new List<KeyValuePair<string, object>>();

        public string Message { get; }

        public TStatePropertiesMapperEventInfo(string message)
        {
            Message = message;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return _properties.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public TStatePropertiesMapperEventInfo AddProp(string name, object value)
        {
            _properties.Add(new KeyValuePair<string, object>(name, value));
            return this;
        }

        public static Func<TStatePropertiesMapperEventInfo, Exception, string> Formatter { get; } = (l, e) => l.Message;
    }
}
