
namespace TabTabGo.Core
{
    public interface ITenant<TKey> where TKey : struct
    { 
        string Name { get; set; } 
        string Code { get; set; }
        TKey Id { get; set; }
        dynamic Settings { get; set; }
    }
}
