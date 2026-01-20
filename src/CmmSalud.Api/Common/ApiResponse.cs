namespace CmmSalud.Api.Common;

public sealed record ApiResponse<T>(int StatusCode, string Message, T? Data = default);
