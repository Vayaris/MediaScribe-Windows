namespace MediaScribeRecorder.Services;

public sealed class UserFacingException : Exception
{
    public UserFacingException(string code, string message, Exception? innerException = null)
        : base($"{code} - {message}", innerException)
    {
        Code = code;
        UserMessage = message;
    }

    public string Code { get; }
    public string UserMessage { get; }
}
