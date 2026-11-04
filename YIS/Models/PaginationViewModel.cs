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

        public int StartItem => TotalItems == 0 ? 0 : (CurrentPage - 1) * PageSize + 1;
        public int EndItem => Math.Min(CurrentPage * PageSize, TotalItems);
        public int[] PageSizeOptions { get; set; } = new[] { 10, 25, 50, 100, 250, 500, 1000 };

        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;

        public PaginationViewModel(int totalItems, int currentPage, int pageSize = 10, string? baseUrl = null)
        {
            TotalItems = totalItems;
            PageSize = pageSize <= 0 ? 10 : pageSize;
            CurrentPage = currentPage <= 0 ? 1 : currentPage;
            TotalPages = (int)Math.Ceiling(totalItems / (double)PageSize);
            BaseUrl = baseUrl;
        }
    }
}
