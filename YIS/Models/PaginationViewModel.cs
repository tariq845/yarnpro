using System;

namespace YIS.Models
{
    public class PaginationViewModel
    {
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public string? BaseUrl { get; set; }

        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;

        public PaginationViewModel(int totalItems, int currentPage, int pageSize = 20, string? baseUrl = null)
        {
            TotalItems = totalItems;
            PageSize = pageSize;
            CurrentPage = currentPage;
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            BaseUrl = baseUrl;
        }
    }
}
