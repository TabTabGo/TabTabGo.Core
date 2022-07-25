namespace TabTabGo.Core.ViewModels;

public class PagingOptionRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? OrderBy { get; set; }
    public string? OrderDirection { get; set; }
    public string? Extends {get; set;}
}