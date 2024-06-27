namespace TabTabGo.Core
{
    public interface IUser<TKey> where TKey : struct
    {
        public string Username { get; set; }
        public TKey UserId { get; set; }
    }
}