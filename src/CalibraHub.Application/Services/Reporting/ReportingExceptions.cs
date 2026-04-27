namespace CalibraHub.Application.Services.Reporting;

public sealed class ReportValidationException : Exception
{
    public ReportValidationException(string message) : base(message) { }
    public ReportValidationException(string message, Exception inner) : base(message, inner) { }
}

public sealed class ReportAuthorizationException : Exception
{
    public ReportAuthorizationException(string message) : base(message) { }
}

public sealed class ReportNotFoundException : Exception
{
    public ReportNotFoundException(string message) : base(message) { }
}
