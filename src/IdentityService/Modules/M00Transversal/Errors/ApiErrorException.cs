namespace IdentityService.Modules.M00Transversal.Errors;

public class ApiErrorException : Exception
{
    public string Code { get; }

    public ApiErrorException(string code) : base(code)
    {
        Code = code;
    }

    public ApiErrorException(string code, string message) : base(message)
    {
        Code = code;
    }
}
