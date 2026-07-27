namespace Bastet.Models.ViewModels;

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    // Base error properties
    public string? ErrorMessage { get; set; }
    public int StatusCode { get; set; }
    public string? Title { get; set; } // Display title
}
