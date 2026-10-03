using SPTInstaller.Interfaces;

namespace SPTInstaller.Models;

public class Result : IResult
{
    public bool Succeeded { get; private set; }
    
    public string Message { get; private set; }
    
    public string? RetryText { get; private set; }
    
    protected Result(string message, bool succeeded, string? retryText = null)
    {
        Message = message;
        Succeeded = succeeded;
        RetryText = retryText;
    }
    
    public static Result FromSuccess(string message = "") => new(message, true);
    public static Result FromError(string message) => new(message, false);
    public static Result FromRetryableError(string message, string retryText) => new(message, false, retryText);
}