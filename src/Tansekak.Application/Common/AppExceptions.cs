using System.Net;

namespace Tansekak.Application.Common;

public sealed class NotFoundException : AppException
{
    public NotFoundException(string errorCode = ApiErrorCodes.NotFound)
        : base(errorCode, HttpStatusCode.NotFound)
    {
    }

    public NotFoundException(string errorCode, string userMessage)
        : base(errorCode, HttpStatusCode.NotFound, userMessage)
    {
    }
}

public sealed class ServiceUnavailableException : AppException
{
    public ServiceUnavailableException(string errorCode = ApiErrorCodes.ServiceUnavailable)
        : base(errorCode, HttpStatusCode.ServiceUnavailable)
    {
    }
}

public sealed class ValidationException : AppException
{
    public ValidationException(string errorCode, string? userMessage = null)
        : base(errorCode, HttpStatusCode.BadRequest, userMessage)
    {
    }
}

public sealed class ConflictException : AppException
{
    public ConflictException(string errorCode, string? userMessage = null)
        : base(errorCode, HttpStatusCode.Conflict, userMessage)
    {
    }
}
