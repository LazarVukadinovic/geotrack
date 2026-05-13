namespace Geotrack.Application.Services;

public class AgentException : Exception
{
    public AgentException(string code, string safeMessage, int statusCode = 400)
        : base(safeMessage)
    {
        Code = code;
        SafeMessage = safeMessage;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public string SafeMessage { get; }
    public int StatusCode { get; }
}
