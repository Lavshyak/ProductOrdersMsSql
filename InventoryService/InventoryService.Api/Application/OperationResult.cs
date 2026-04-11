namespace InventoryService.Api.Application;

public sealed record OperationResult(bool IsSuccess, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static OperationResult Success() => new(true);

    public static OperationResult Failure(string errorCode, string errorMessage) => new(false, errorCode, errorMessage);
}

