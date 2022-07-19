using System;

namespace TabTabGo.Core.Enums
{
    [Flags]
    public enum QueryFlags
    {
        DisableTracking = 1,
        IgnoreFixQuery = 2,
        IsExpandable = 4,
        UseLocal = 8
    }
}
