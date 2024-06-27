namespace TabTabGo.Core
{
    public abstract class User<TKey> where TKey : struct
    {
        public string Username { get; set; }
        public TKey UserId { get; set; }
    }
}