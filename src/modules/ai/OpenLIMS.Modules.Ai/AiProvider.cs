using OpenLIMS.Contracts.Ai;

namespace OpenLIMS.Modules.Ai;

internal sealed class DisabledAiProviderPort : IAiProviderPort
{
    public ValueTask<AiProviderResponse> ExecuteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AiProviderResponse(AiProviderStatuses.Disabled));
    }
}
