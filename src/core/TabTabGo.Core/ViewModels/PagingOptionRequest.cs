namespace TabTabGo.Core.ViewModels;

public class PagingOptionRequest
{
    /// <summary>
    /// Current page number index start from 1
    /// </summary>
    public int Page { get; set; } = 1;
    /// <summary>
    /// Number of Items per page
    /// </summary>
    public int PageSize { get; set; } = 20;
    /// <summary>
    /// Property name to sort 
    /// </summary>
    public string? OrderBy { get; set; }
    /// <summary>
    /// Sort direction (asc or desc)
    /// </summary>
    public string? OrderDirection { get; set; }
    /// <summary>
    /// Extended properties to be included in the response
    /// </summary>
    public string? Extends {get; set;}
}